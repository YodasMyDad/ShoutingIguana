// Live crawl state outside React. Messages are coalesced and published at most 4 times a second, and only the live
// panel subscribes, so progress never re-renders the grid.
import { useSyncExternalStore } from 'react';
import type { SseMessage } from './sse';

export type StatusBreakdown = { s2xx: number; s3xx: number; s4xx: number; s5xx: number };
export type Progress = {
  t: number;
  crawled: number;
  found: number;
  queued: number;
  errors: number;
  pagesPerSecond: number;
  status: StatusBreakdown;
};
export type Notice = { index: number; code: string; message: string };
export type LiveState = { status: string; progress: Progress | null; history: number[]; notices: Notice[] };

export const HISTORY_POINTS = 600; // 5 minutes at one point per 500 ms
const MIN_PUBLISH_INTERVAL_MS = 250; // at most 4 renders a second

let state: LiveState = { status: 'idle', progress: null, history: [], notices: [] };
let pendingProgress: Progress | null = null;
let pendingStatus: string | null = null;
const pendingHistory: number[] = [];
const pendingNotices: Notice[] = [];
let timer = 0;
let lastPublishAt = 0;
const listeners = new Set<() => void>();

export const liveStats = { received: 0, published: 0 };

function schedule() {
  if (timer) return;
  const wait = Math.max(0, lastPublishAt + MIN_PUBLISH_INTERVAL_MS - performance.now());
  timer = window.setTimeout(publish, wait);
}

function publish() {
  timer = 0;
  lastPublishAt = performance.now();
  let history = state.history;
  if (pendingHistory.length) {
    history = history.concat(pendingHistory);
    if (history.length > HISTORY_POINTS) history = history.slice(history.length - HISTORY_POINTS);
    pendingHistory.length = 0;
  }
  const notices = pendingNotices.length ? [...pendingNotices.reverse(), ...state.notices].slice(0, 5) : state.notices;
  pendingNotices.length = 0;
  state = { status: pendingStatus ?? state.status, progress: pendingProgress ?? state.progress, history, notices };
  pendingProgress = null;
  pendingStatus = null;
  // While the window is hidden (in the tray) the state keeps up but nothing re-renders, so the renderer stays idle
  // and the memory WebView2 trims stays paged out. Showing the window renders the latest state at once.
  if (document.hidden) return;
  liveStats.published++;
  listeners.forEach((listener) => listener());
}

document.addEventListener('visibilitychange', () => {
  if (!document.hidden) listeners.forEach((listener) => listener());
});

export function pushMessage(message: SseMessage) {
  liveStats.received++;
  if (message.event === 'progress') {
    const progress = JSON.parse(message.data) as Progress;
    pendingProgress = progress;
    pendingHistory.push(progress.pagesPerSecond);
  } else if (message.event === 'notice') {
    pendingNotices.push(JSON.parse(message.data) as Notice);
  }
  schedule();
}

export function pushStatus(status: string) {
  pendingStatus = status;
  schedule();
}

function subscribe(listener: () => void) {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}

export function useLive<T>(select: (state: LiveState) => T): T {
  return useSyncExternalStore(subscribe, () => select(state));
}
