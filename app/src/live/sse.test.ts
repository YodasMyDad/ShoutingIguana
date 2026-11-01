import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { SseParser, backoffDelay, startEventStream, type SseMessage, type StreamState } from './sse';

describe('SseParser', () => {
  it('reads events with a name, data and id', () => {
    const parser = new SseParser();
    expect(parser.push('event: progress\ndata: {"a":1}\nid: 7\n\n')).toEqual([
      { event: 'progress', data: '{"a":1}', id: '7' },
    ]);
  });

  it('joins multi-line data with newlines and defaults the name to message', () => {
    expect(new SseParser().push('data: one\ndata: two\n\n')).toEqual([{ event: 'message', data: 'one\ntwo', id: '' }]);
  });

  it('accepts CRLF, LF and CR line endings, even a CRLF split across chunks', () => {
    const parser = new SseParser();
    expect(parser.push('event: a\r\ndata: 1\r')).toEqual([]);
    expect(parser.push('\n\r\n')).toEqual([{ event: 'a', data: '1', id: '' }]);
    // A CR at the very end might be half a CRLF: it waits for the next chunk to say.
    expect(parser.push('data: 2\r\r')).toEqual([]);
    expect(parser.push('data: 3\n\n')).toEqual([
      { event: 'message', data: '2', id: '' },
      { event: 'message', data: '3', id: '' },
    ]);
  });

  it('waits for the rest of a message split across chunks', () => {
    const parser = new SseParser();
    expect(parser.push('event: hel')).toEqual([]);
    expect(parser.push('lo\ndata: {"live":[]')).toEqual([]);
    expect(parser.push('}\n\n')).toEqual([{ event: 'hello', data: '{"live":[]}', id: '' }]);
  });

  it('ignores comments (the keep-alive), retry and unknown fields', () => {
    expect(new SseParser().push(': keep-alive\n\nretry: 100\nfoo: bar\ndata: x\n\n')).toEqual([
      { event: 'message', data: 'x', id: '' },
    ]);
  });

  it('dispatches nothing for an event with no data, and drops a leading byte order mark', () => {
    expect(new SseParser().push('﻿event: empty\n\ndata: y\n\n')).toEqual([{ event: 'message', data: 'y', id: '' }]);
  });

  it('keeps only the space after the colon, not the rest', () => {
    expect(new SseParser().push('data:  two spaces\ndata:none\n\n')[0]?.data).toBe(' two spaces\nnone');
  });
});

describe('backoffDelay', () => {
  it('doubles from 250 ms to a cap of 4 s', () => {
    expect([1, 2, 3, 4, 5, 6, 10].map((attempt) => backoffDelay(attempt))).toEqual([
      250, 500, 1_000, 2_000, 4_000, 4_000, 4_000,
    ]);
  });
});

/** A controllable server side: each open() gets a stream the test can write to, end or fail. */
function fakeServer() {
  const encoder = new TextEncoder();
  const opened: { controller: ReadableStreamDefaultController<Uint8Array>; signal: AbortSignal }[] = [];
  let status = 200;
  let refuse = false;
  const open = vi.fn((signal: AbortSignal) => {
    if (refuse) return Promise.reject(new TypeError('Failed to fetch'));
    let controller!: ReadableStreamDefaultController<Uint8Array>;
    const body = new ReadableStream<Uint8Array>({
      start(c) {
        controller = c;
      },
    });
    signal.addEventListener('abort', () => controller.error(new DOMException('Aborted', 'AbortError')));
    opened.push({ controller, signal });
    return Promise.resolve(new Response(body, { status }));
  });
  return {
    open,
    opened,
    send: (index: number, text: string) => opened[index]?.controller.enqueue(encoder.encode(text)),
    end: (index: number) => opened[index]?.controller.close(),
    setStatus: (value: number) => {
      status = value;
    },
    setRefuse: (value: boolean) => {
      refuse = value;
    },
  };
}

function connection(initial: number | null) {
  let generation = initial;
  const listeners = new Set<() => void>();
  return {
    generation: () => generation,
    subscribe: (listener: () => void) => {
      listeners.add(listener);
      return () => listeners.delete(listener);
    },
    set: (next: number | null) => {
      generation = next;
      listeners.forEach((listener) => listener());
    },
  };
}

/** Lets the stream's promise chain run between fake-timer steps. */
const flush = () => vi.advanceTimersByTimeAsync(0);

describe('startEventStream', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it('connects when a sidecar is ready and passes its messages on', async () => {
    const server = fakeServer();
    const sidecar = connection(1);
    const messages: SseMessage[] = [];
    const states: StreamState[] = [];
    const stream = startEventStream({
      ...sidecar,
      open: server.open,
      onMessage: (m) => messages.push(m),
      onState: (s) => states.push(s),
    });
    await flush();
    server.send(0, 'event: hello\ndata: {"live":[]}\n\n');
    await flush();
    expect(messages).toEqual([{ event: 'hello', data: '{"live":[]}', id: '' }]);
    expect(states).toEqual(['connecting', 'open']);
    stream.stop();
  });

  it('waits while no sidecar is ready, then connects at once', async () => {
    const server = fakeServer();
    const sidecar = connection(null);
    const states: StreamState[] = [];
    const stream = startEventStream({
      ...sidecar,
      open: server.open,
      onMessage: () => undefined,
      onState: (s) => states.push(s),
    });
    await flush();
    expect(server.open).not.toHaveBeenCalled();
    expect(states).toEqual(['waiting']);
    sidecar.set(1);
    await flush();
    expect(server.open).toHaveBeenCalledOnce();
    stream.stop();
  });

  it('reconnects with backoff when the stream ends, sooner after a stream that worked', async () => {
    const server = fakeServer();
    const sidecar = connection(1);
    const stream = startEventStream({ ...sidecar, open: server.open, onMessage: () => undefined });
    await flush();
    server.end(0);
    await flush();
    expect(server.open).toHaveBeenCalledTimes(1);
    await vi.advanceTimersByTimeAsync(249);
    expect(server.open).toHaveBeenCalledTimes(1);
    await vi.advanceTimersByTimeAsync(1);
    expect(server.open).toHaveBeenCalledTimes(2);
    stream.stop();
  });

  it('backs off further after each failed attempt in a row', async () => {
    const server = fakeServer();
    server.setRefuse(true);
    const sidecar = connection(1);
    const stream = startEventStream({ ...sidecar, open: server.open, onMessage: () => undefined });
    await flush();
    expect(server.open).toHaveBeenCalledTimes(1);
    await vi.advanceTimersByTimeAsync(250);
    expect(server.open).toHaveBeenCalledTimes(2);
    await vi.advanceTimersByTimeAsync(499);
    expect(server.open).toHaveBeenCalledTimes(2);
    await vi.advanceTimersByTimeAsync(1);
    expect(server.open).toHaveBeenCalledTimes(3);
    stream.stop();
  });

  it('treats an error status as a failed attempt', async () => {
    const server = fakeServer();
    server.setStatus(401);
    const sidecar = connection(1);
    const states: StreamState[] = [];
    const stream = startEventStream({
      ...sidecar,
      open: server.open,
      onMessage: () => undefined,
      onState: (s) => states.push(s),
    });
    await flush();
    expect(states).toEqual(['connecting', 'reconnecting']);
    stream.stop();
  });

  it('drops a stream that has gone quiet', async () => {
    const server = fakeServer();
    const sidecar = connection(1);
    const stream = startEventStream({
      ...sidecar,
      open: server.open,
      onMessage: () => undefined,
      idleTimeoutMs: 5_000,
    });
    await flush();
    await vi.advanceTimersByTimeAsync(4_000);
    server.send(0, ': keep-alive\n\n');
    await vi.advanceTimersByTimeAsync(4_000);
    expect(server.opened[0]?.signal.aborted).toBe(false);
    await vi.advanceTimersByTimeAsync(2_000);
    expect(server.opened[0]?.signal.aborted).toBe(true);
    stream.stop();
  });

  it('moves to a new sidecar at once, ending any backoff', async () => {
    const server = fakeServer();
    server.setRefuse(true);
    const sidecar = connection(1);
    const stream = startEventStream({ ...sidecar, open: server.open, onMessage: () => undefined });
    await flush();
    await vi.advanceTimersByTimeAsync(250 + 500 + 1_000);
    const before = server.open.mock.calls.length;
    server.setRefuse(false);
    sidecar.set(2);
    await flush();
    expect(server.open.mock.calls.length).toBe(before + 1);
    stream.stop();
  });

  it('drops the old stream when the sidecar is replaced', async () => {
    const server = fakeServer();
    const sidecar = connection(1);
    const stream = startEventStream({ ...sidecar, open: server.open, onMessage: () => undefined });
    await flush();
    sidecar.set(2);
    await flush();
    expect(server.opened[0]?.signal.aborted).toBe(true);
    expect(server.open).toHaveBeenCalledTimes(2);
    stream.stop();
  });

  it('stops for good', async () => {
    const server = fakeServer();
    const sidecar = connection(1);
    const states: StreamState[] = [];
    const stream = startEventStream({
      ...sidecar,
      open: server.open,
      onMessage: () => undefined,
      onState: (s) => states.push(s),
    });
    await flush();
    stream.stop();
    await vi.advanceTimersByTimeAsync(10_000);
    expect(server.open).toHaveBeenCalledOnce();
    expect(states.at(-1)).toBe('stopped');
  });
});
