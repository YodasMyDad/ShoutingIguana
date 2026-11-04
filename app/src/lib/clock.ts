// One clock for the app's relative times ("3 minutes ago"): a tick a minute, shared by every one on show, and none
// while the window is hidden (in the tray), as the live store stops then. Shown again, it ticks at once.
import { useSyncExternalStore } from 'react';

export const CLOCK_TICK_MS = 60_000;

let now = Date.now();
let timer: ReturnType<typeof setInterval> | undefined;
const listeners = new Set<() => void>();

const hidden = () => typeof document !== 'undefined' && document.hidden;

function tick(): void {
  now = Date.now();
  listeners.forEach((listener) => listener());
}

function start(): void {
  if (timer === undefined && !hidden()) timer = setInterval(tick, CLOCK_TICK_MS);
}

function stop(): void {
  clearInterval(timer);
  timer = undefined;
}

function visibilityChanged(): void {
  if (hidden()) {
    stop();
    return;
  }
  tick();
  start();
}

function subscribe(listener: () => void): () => void {
  listeners.add(listener);
  if (listeners.size === 1) {
    // The first one on show: the time is now, not when the last one went.
    now = Date.now();
    document.addEventListener('visibilitychange', visibilityChanged);
    start();
  }
  return () => {
    listeners.delete(listener);
    if (listeners.size > 0) return;
    stop();
    document.removeEventListener('visibilitychange', visibilityChanged);
  };
}

const getNow = () => now;

/** The time, in ms, as of the clock's last tick: a component using it renders again each minute. */
export function useMinuteClock(): number {
  return useSyncExternalStore(subscribe, getNow, getNow);
}
