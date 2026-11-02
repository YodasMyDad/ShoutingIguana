// Server-sent events over fetch. EventSource can't send the bearer token, so the stream is read with fetch and parsed
// here, as the HTML spec's event-stream format: fields `event`, `data`, `id`, `retry`, comments, and any line ending
// (CRLF, LF or CR, even split across chunks).

export interface SseMessage {
  event: string;
  data: string;
  id: string;
}

/** An incremental parser: push decoded text as it arrives, get back the messages it completes. */
export class SseParser {
  private buffer = '';
  private event = '';
  private data: string[] = [];
  private id = '';
  private started = false;

  push(text: string): SseMessage[] {
    let chunk = text;
    if (!this.started && chunk.length > 0) {
      this.started = true;
      if (chunk.startsWith('﻿')) chunk = chunk.slice(1);
    }
    this.buffer += chunk;
    const messages: SseMessage[] = [];
    let start = 0;
    for (;;) {
      const end = findLineEnd(this.buffer, start);
      if (end === null) break;
      this.line(this.buffer.slice(start, end.index), messages);
      start = end.next;
    }
    this.buffer = this.buffer.slice(start);
    return messages;
  }

  private line(line: string, messages: SseMessage[]): void {
    if (line === '') {
      if (this.data.length > 0)
        messages.push({ event: this.event || 'message', data: this.data.join('\n'), id: this.id });
      this.event = '';
      this.data = [];
      return;
    }
    if (line.startsWith(':')) return; // a comment: the keep-alive
    const colon = line.indexOf(':');
    const field = colon < 0 ? line : line.slice(0, colon);
    let value = colon < 0 ? '' : line.slice(colon + 1);
    if (value.startsWith(' ')) value = value.slice(1);
    if (field === 'event') this.event = value;
    else if (field === 'data') this.data.push(value);
    else if (field === 'id' && !value.includes('\0')) this.id = value;
    // `retry` and unknown fields are ignored: the client keeps its own backoff.
  }
}

/** The next line ending at or after `from`, or null when the buffer might still be mid-line (or mid-CRLF). */
function findLineEnd(buffer: string, from: number): { index: number; next: number } | null {
  for (let i = from; i < buffer.length; i++) {
    const char = buffer.charCodeAt(i);
    if (char === 10) return { index: i, next: i + 1 };
    if (char === 13) {
      if (i + 1 === buffer.length) return null; // a CR at the end: wait to see whether an LF follows
      return { index: i, next: buffer.charCodeAt(i + 1) === 10 ? i + 2 : i + 1 };
    }
  }
  return null;
}

export type StreamState = 'waiting' | 'connecting' | 'open' | 'reconnecting' | 'stopped';

export interface EventStreamDeps {
  /** Opens the stream: resolves with the response (any status), rejects when no response came. */
  open: (signal: AbortSignal) => Promise<Response>;
  /** The current sidecar generation, or null while no sidecar is ready. */
  generation: () => number | null;
  /** Called whenever the sidecar connection may have changed. Returns an unsubscribe. */
  subscribe: (listener: () => void) => () => void;
  onMessage: (message: SseMessage) => void;
  onState?: (state: StreamState) => void;
  /** No byte for this long means the stream is dead. The sidecar sends a keep-alive comment every 15 s. */
  idleTimeoutMs?: number;
  /** Backoff between attempts, doubling from the first to the cap. */
  backoffMs?: { initial: number; max: number };
}

export const DEFAULT_IDLE_TIMEOUT_MS = 35_000;
export const DEFAULT_BACKOFF_MS = { initial: 250, max: 4_000 };

/** Delay before reconnect attempt `attempt` (1-based): 250 ms, 500 ms, 1 s, 2 s, then 4 s. */
export function backoffDelay(attempt: number, backoff = DEFAULT_BACKOFF_MS): number {
  return Math.min(backoff.max, backoff.initial * 2 ** Math.max(0, attempt - 1));
}

/**
 * One event stream for the app's lifetime: connects when a sidecar is ready, reconnects with backoff when the stream
 * ends, fails or goes quiet, and moves to a new sidecar at once (a new generation ends any backoff).
 */
export function startEventStream(deps: EventStreamDeps): { stop: () => void } {
  const idleTimeoutMs = deps.idleTimeoutMs ?? DEFAULT_IDLE_TIMEOUT_MS;
  const backoff = deps.backoffMs ?? DEFAULT_BACKOFF_MS;
  // Shared with stop() and the connection listener, which change it from outside the loop.
  const run: {
    stopped: boolean;
    controller: AbortController | null;
    connectedGeneration: number | null;
    wake: (() => void) | null;
  } = {
    stopped: false,
    controller: null,
    connectedGeneration: null,
    wake: null,
  };

  const setState = (state: StreamState) => deps.onState?.(state);

  let lastSeen = deps.generation();
  const unsubscribe = deps.subscribe(() => {
    const generation = deps.generation();
    if (generation === lastSeen) return;
    lastSeen = generation;
    // A new sidecar, or the old one gone: drop the stream we have and end any wait.
    if (generation !== run.connectedGeneration) run.controller?.abort();
    run.wake?.();
  });

  const sleep = (ms: number | null) =>
    new Promise<void>((resolve) => {
      let timer: ReturnType<typeof setTimeout> | undefined;
      const done = () => {
        if (timer !== undefined) clearTimeout(timer);
        run.wake = null;
        resolve();
      };
      if (ms !== null) timer = setTimeout(done, ms);
      run.wake = done;
    });

  // Read through a function: the loop awaits, and stop() can change it meanwhile.
  const isStopped = () => run.stopped;

  void (async () => {
    let attempt = 0;
    while (!run.stopped) {
      const generation = deps.generation();
      if (generation === null) {
        setState('waiting');
        await sleep(null);
        continue;
      }
      const current = new AbortController();
      run.controller = current;
      run.connectedGeneration = generation;
      let lastByteAt = Date.now();
      const watchdog = setInterval(
        () => {
          if (Date.now() - lastByteAt > idleTimeoutMs) current.abort();
        },
        Math.min(1_000, idleTimeoutMs),
      );
      let opened = false;
      try {
        setState(attempt === 0 ? 'connecting' : 'reconnecting');
        const response = await deps.open(current.signal);
        if (!response.ok || !response.body) {
          await response.body?.cancel();
          throw new Error(`HTTP ${response.status}`);
        }
        opened = true;
        attempt = 0;
        lastByteAt = Date.now();
        setState('open');
        const parser = new SseParser();
        const reader = response.body.pipeThrough(new TextDecoderStream()).getReader();
        for (;;) {
          const { value, done } = await reader.read();
          if (done) break;
          lastByteAt = Date.now();
          for (const message of parser.push(value)) deps.onMessage(message);
        }
      } catch {
        // Aborted (new sidecar, idle, stop) or failed, a message handler that threw included: either way, reconnect
        // below unless stopped.
      } finally {
        clearInterval(watchdog);
        // However the read ended, the connection goes with it: the browser allows the sidecar 6, and a stream left
        // open after a failure would hold one for good.
        current.abort();
        if (run.controller === current) run.controller = null;
      }
      if (isStopped()) break;
      // A new generation reconnects at once; otherwise back off, longer each failed attempt in a row.
      if (deps.generation() !== run.connectedGeneration) {
        attempt = 0;
        continue;
      }
      attempt = opened ? 1 : attempt + 1;
      setState('reconnecting');
      await sleep(backoffDelay(attempt, backoff));
    }
    setState('stopped');
  })();

  return {
    stop() {
      run.stopped = true;
      unsubscribe();
      run.controller?.abort();
      run.wake?.();
    },
  };
}
