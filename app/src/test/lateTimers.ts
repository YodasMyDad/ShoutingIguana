import { vi } from 'vitest';

export interface LateTimers {
  /** How many late timers have yet to come. */
  pending: () => number;
  /** Timers asked for from here on are on time again. */
  stop: () => void;
  /** Stops, and resolves once every late timer has come (or was cleared). */
  over: () => Promise<void>;
}

/**
 * Makes an overlay's close take `ms` longer than it does. Radix gives focus back (`onCloseAutoFocus`) from a
 * zero-delay timer it starts once the overlay's content has gone; while this is on, every zero-delay timer asked for
 * comes `ms` late, so whatever was meant to follow the close (a frame, another timer) comes before it. Testing
 * Library and user-event wait on zero-delay timers too: turn it on just before the key that closes, and `stop` or
 * `over` once the overlay has gone.
 */
export function lateZeroTimers(ms: number): LateTimers {
  const onTime = globalThis.setTimeout;
  const clear = globalThis.clearTimeout;
  /** Each late timer still to come, with what says it has. */
  const waiting = new Map<unknown, () => void>();
  const came: Promise<void>[] = [];
  const settle = (timer: unknown) => {
    waiting.get(timer)?.();
    waiting.delete(timer);
  };
  const asked = vi.spyOn(globalThis, 'setTimeout').mockImplementation(((
    handler: (...args: unknown[]) => void,
    delay?: number,
    ...args: unknown[]
  ) => {
    if (delay) return onTime(handler, delay, ...args);
    const timer = onTime(() => {
      try {
        handler(...args);
      } finally {
        settle(timer);
      }
    }, ms);
    came.push(new Promise<void>((resolve) => waiting.set(timer, resolve)));
    return timer;
  }) as typeof globalThis.setTimeout);
  // A timer that's cleared never comes: it isn't waited for.
  const cleared = vi.spyOn(globalThis, 'clearTimeout').mockImplementation((timer) => {
    clear(timer);
    settle(timer);
  });
  const stop = () => asked.mockRestore();
  return {
    pending: () => waiting.size,
    stop,
    over: async () => {
      stop();
      await Promise.all(came);
      cleared.mockRestore();
    },
  };
}
