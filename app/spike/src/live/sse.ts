// Server-sent events over fetch streaming. EventSource cannot send an Authorization header, so we parse the
// text/event-stream format ourselves and reconnect with backoff, picking up a new port/token after a sidecar restart.
import { getConnection, subscribeConnection } from '../connection';

export type SseMessage = { event: string; data: string; id: string };

export type StreamStats = {
  connects: number;
  reconnects: number;
  messages: number;
  lastMessageAt: number;
  lastChunkAt: number;
  errors: string[];
};

const IDLE_TIMEOUT_MS = 5_000; // progress arrives every 500 ms, so 5 s of silence means the stream is dead

export async function readSse(body: ReadableStream<BufferSource>, onMessage: (message: SseMessage) => void, onChunk: () => void) {
  const reader = body.pipeThrough(new TextDecoderStream()).getReader();
  let buffer = '';
  let event = '';
  let data: string[] = [];
  let id = '';
  for (;;) {
    const { value, done } = await reader.read();
    if (done) return;
    onChunk();
    buffer += value;
    for (;;) {
      const newline = buffer.search(/\r\n|\r|\n/);
      if (newline < 0 || (buffer[newline] === '\r' && newline === buffer.length - 1)) break; // wait for a possible \n
      const line = buffer.slice(0, newline);
      buffer = buffer.slice(newline + (buffer[newline] === '\r' && buffer[newline + 1] === '\n' ? 2 : 1));
      if (line === '') {
        if (data.length) onMessage({ event: event || 'message', data: data.join('\n'), id });
        event = '';
        data = [];
        continue;
      }
      if (line.startsWith(':')) continue;
      const colon = line.indexOf(':');
      const field = colon < 0 ? line : line.slice(0, colon);
      let fieldValue = colon < 0 ? '' : line.slice(colon + 1);
      if (fieldValue.startsWith(' ')) fieldValue = fieldValue.slice(1);
      if (field === 'event') event = fieldValue;
      else if (field === 'data') data.push(fieldValue);
      else if (field === 'id') id = fieldValue;
    }
  }
}

export function startProgressStream(
  onMessage: (message: SseMessage) => void,
  onStatus: (status: string) => void,
  intervalMs = 500,
) {
  const stats: StreamStats = { connects: 0, reconnects: 0, messages: 0, lastMessageAt: 0, lastChunkAt: 0, errors: [] };
  let stopped = false;
  let controller: AbortController | null = null;
  let wake: (() => void) | null = null;
  // A new connection (sidecar restarted on a new port) ends any backoff at once.
  const unsubscribe = subscribeConnection(() => wake?.());

  const sleep = (ms: number) =>
    new Promise<void>((resolve) => {
      const done = () => {
        clearTimeout(timer);
        wake = null;
        resolve();
      };
      const timer = setTimeout(done, ms);
      wake = done;
    });

  void (async () => {
    let attempt = 0;
    let lastEventId = '';
    let generation = 0;
    while (!stopped) {
      const connection = getConnection();
      if (!connection) {
        onStatus('waiting');
        await sleep(5_000);
        continue;
      }
      if (connection.generation !== generation) {
        generation = connection.generation;
        lastEventId = ''; // event ids belong to one sidecar process
      }
      controller = new AbortController();
      const current = controller;
      const watchdog = setInterval(() => {
        if (stats.lastChunkAt && performance.now() - stats.lastChunkAt > IDLE_TIMEOUT_MS) current.abort();
      }, 1_000);
      try {
        onStatus('connecting');
        const headers: Record<string, string> = { Authorization: `Bearer ${connection.token}`, Accept: 'text/event-stream' };
        if (lastEventId) headers['Last-Event-ID'] = lastEventId;
        const response = await fetch(`${connection.baseUrl}/api/progress/stream?intervalMs=${intervalMs}`, {
          headers,
          signal: current.signal,
        });
        if (!response.ok || !response.body) throw new Error(`HTTP ${response.status}`);
        stats.connects++;
        attempt = 0;
        stats.lastChunkAt = performance.now();
        onStatus('open');
        await readSse(
          response.body,
          (message) => {
            if (message.id) lastEventId = message.id;
            stats.messages++;
            stats.lastMessageAt = performance.now();
            onMessage(message);
          },
          () => {
            stats.lastChunkAt = performance.now();
          },
        );
        stats.errors.push('stream ended');
      } catch (error) {
        if (!stopped) stats.errors.push(String(error));
      } finally {
        clearInterval(watchdog);
        stats.lastChunkAt = 0;
      }
      if (stopped) break;
      stats.reconnects++;
      onStatus('reconnecting');
      attempt++;
      await sleep(Math.min(5_000, 250 * 2 ** Math.min(attempt - 1, 4)));
    }
  })();

  return {
    stats,
    stop() {
      stopped = true;
      controller?.abort();
      unsubscribe();
      wake?.();
    },
  };
}
