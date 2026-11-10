import { vi } from 'vitest';

export interface LateTimers {
  /** How many late timers have yet to come. */
  pending: () => number;
  /** Timers asked for from here on are on time again. */
  stop: () => void;
  /** Stops, and resolves once every late timer has come (or was cleared). Held timers come now. */
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
  return zeroTimers(ms);
}

/**
 * The same, with no clock in it: every zero-delay timer asked for while this is on is held, and comes when `over` is
 * called. For what must happen while a close hasn't been said yet, however slow the machine: nothing races the
 * test. `stop` before waiting on anything (user-event and Testing Library's waits would be held too).
 */
export function heldZeroTimers(): LateTimers {
  return zeroTimers(null);
}

function zeroTimers(ms: number | null): LateTimers {
  const onTime = globalThis.setTimeout;
  const clear = globalThis.clearTimeout;
  /** Each late timer still to come, with what says it has. */
  const waiting = new Map<unknown, () => void>();
  /** What the held timers do, in the order they were asked for. */
  const held = new Map<unknown, () => void>();
  const came: Promise<void>[] = [];
  const settle = (timer: unknown) => {
    held.delete(timer);
    waiting.get(timer)?.();
    waiting.delete(timer);
  };
  const asked = vi.spyOn(globalThis, 'setTimeout').mockImplementation(((
    handler: (...args: unknown[]) => void,
    delay?: number,
    ...args: unknown[]
  ) => {
    if (delay) return onTime(handler, delay, ...args);
    const run = () => {
      try {
        handler(...args);
      } finally {
        settle(timer);
      }
    };
    // A held timer has a handle of its own to be cleared by: a timer far off, that never comes.
    const timer = ms === null ? onTime(() => undefined, 2 ** 31 - 1) : onTime(run, ms);
    if (ms === null) {
      held.set(timer, run);
      // It keeps nothing alive if a test fails before `over`.
      (timer as { unref?: () => void }).unref?.();
    }
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
      for (const [timer, run] of [...held]) {
        clear(timer as Parameters<typeof clear>[0]);
        onTime(run, 0);
      }
      await Promise.all(came);
      cleared.mockRestore();
    },
  };
}
