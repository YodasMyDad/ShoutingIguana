// Live crawl state, outside React. Events are applied as they arrive, but React hears about them at most 4 times a
// second, and not at all while the window is hidden (in the tray), so a 2,900 URLs/s crawl never re-renders the grid
// and a hidden window stays idle. Only components that select live data subscribe.
import { useSyncExternalStore } from 'react';
import { isLive } from '../api/types';
import type {
  AlertEvent,
  AlertLevel,
  AnalysisEvent,
  CrawlEvent,
  CrawlStatus,
  ProgressEvent,
  SidecarEvent,
} from '../api/types';
import type { SseMessage, StreamState } from './sse';

/** Five minutes of rate samples at one per progress event (every 500 ms). */
export const HISTORY_POINTS = 600;
export const MAX_ALERTS = 200;
/** At most 4 renders a second. */
export const PUBLISH_INTERVAL_MS = 250;

export interface LiveCrawl {
  id: string;
  status: CrawlStatus | null;
  progress: ProgressEvent | null;
  /** URLs per second, oldest first, for the sparkline. */
  history: readonly number[];
  /** Newest last. */
  alerts: readonly AlertEvent[];
  analysis: AnalysisEvent | null;
  error: string | null;
  errorType: string | null;
}

export interface LiveState {
  stream: StreamState;
  /** The crawls this sidecar is crawling or analysing, in the order they went live. */
  live: readonly string[];
  crawls: Readonly<Record<string, LiveCrawl>>;
}

const INITIAL: LiveState = { stream: 'waiting', live: [], crawls: {} };

export interface LiveStoreClock {
  now: () => number;
  setTimeout: (callback: () => void, ms: number) => unknown;
  clearTimeout: (handle: unknown) => void;
  isHidden: () => boolean;
}

const browserClock: LiveStoreClock = {
  now: () => performance.now(),
  setTimeout: (callback, ms) => window.setTimeout(callback, ms),
  clearTimeout: (handle) => window.clearTimeout(handle as number),
  isHidden: () => typeof document !== 'undefined' && document.hidden,
};

export class LiveStore {
  /** What React reads: replaced at most every PUBLISH_INTERVAL_MS. */
  private published: LiveState = INITIAL;
  /** Every event applied, immutably, so publishing is one assignment. */
  private draft: LiveState = INITIAL;
  private timer: unknown = null;
  private lastPublishAt = Number.NEGATIVE_INFINITY;
  private missedWhileHidden = false;
  private readonly listeners = new Set<() => void>();
  private readonly eventListeners = new Set<(event: SidecarEvent) => void>();

  constructor(private readonly clock: LiveStoreClock = browserClock) {}

  readonly getState = (): LiveState => this.published;

  readonly subscribe = (listener: () => void): (() => void) => {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  };

  /** Every event as it arrives, unbatched: for side effects such as refetching, never for rendering. */
  onEvent(listener: (event: SidecarEvent) => void): () => void {
    this.eventListeners.add(listener);
    return () => this.eventListeners.delete(listener);
  }

  ingest(message: SseMessage): void {
    const event = parseSidecarEvent(message);
    if (!event) return;
    this.draft = applyEvent(this.draft, event);
    this.eventListeners.forEach((listener) => listener(event));
    this.schedule();
  }

  setStreamState(stream: StreamState): void {
    if (this.draft.stream === stream) return;
    this.draft = { ...this.draft, stream };
    this.schedule();
  }

  /** The window was shown again: render what changed while it was hidden, at once. */
  becameVisible(): void {
    if (this.missedWhileHidden) {
      this.missedWhileHidden = false;
      this.listeners.forEach((listener) => listener());
    }
  }

  reset(): void {
    if (this.timer !== null) this.clock.clearTimeout(this.timer);
    this.timer = null;
    this.draft = INITIAL;
    this.published = INITIAL;
    this.lastPublishAt = Number.NEGATIVE_INFINITY;
    this.listeners.forEach((listener) => listener());
  }

  private schedule(): void {
    if (this.timer !== null) return;
    const wait = Math.max(0, this.lastPublishAt + PUBLISH_INTERVAL_MS - this.clock.now());
    this.timer = this.clock.setTimeout(() => this.publish(), wait);
  }

  private publish(): void {
    this.timer = null;
    this.lastPublishAt = this.clock.now();
    if (this.published === this.draft) return;
    this.published = this.draft;
    if (this.clock.isHidden()) {
      this.missedWhileHidden = true;
      return;
    }
    this.listeners.forEach((listener) => listener());
  }
}

export const liveStore = new LiveStore();

if (typeof document !== 'undefined') {
  document.addEventListener('visibilitychange', () => {
    if (!document.hidden) liveStore.becameVisible();
  });
}

/**
 * Subscribes to a slice of the live state. The selector must return stored values (a crawl, the live list) or
 * primitives, never a new array or object, or React would see a change on every read.
 */
export function useLive<T>(selector: (state: LiveState) => T, store: LiveStore = liveStore): T {
  const read = () => selector(store.getState());
  return useSyncExternalStore(store.subscribe, read, read);
}

export function useLiveCrawl(id: string | undefined, store: LiveStore = liveStore): LiveCrawl | undefined {
  return useLive((state) => (id === undefined ? undefined : state.crawls[id]), store);
}

export function useLiveIds(store: LiveStore = liveStore): readonly string[] {
  return useLive((state) => state.live, store);
}

// ------------------------------------------------------------------------------------------------ Event handling

/** Reads one stream message at the boundary; anything that doesn't match the contract is dropped. */
export function parseSidecarEvent(message: SseMessage): SidecarEvent | null {
  let data: unknown;
  try {
    data = JSON.parse(message.data);
  } catch {
    return null;
  }
  if (typeof data !== 'object' || data === null) return null;
  const record = data as Record<string, unknown>;
  switch (message.event) {
    case 'hello':
      return Array.isArray(record.live)
        ? { type: 'hello', data: { live: record.live.filter((id): id is string => typeof id === 'string') } }
        : null;
    case 'library':
      return { type: 'library', data: {} };
    case 'progress':
      return typeof record.crawlId === 'string' && typeof record.status === 'string'
        ? { type: 'progress', data: withNumbers(record as unknown as ProgressEvent) }
        : null;
    case 'crawl':
      return typeof record.crawlId === 'string' && typeof record.status === 'string'
        ? { type: 'crawl', data: record as unknown as CrawlEvent }
        : null;
    case 'alert':
      return typeof record.crawlId === 'string' && typeof record.message === 'string'
        ? {
            type: 'alert',
            data: {
              crawlId: record.crawlId,
              level: (['info', 'warning', 'error'].includes(String(record.level))
                ? record.level
                : 'info') as AlertLevel,
              code: typeof record.code === 'string' ? record.code : '',
              message: record.message,
              at: typeof record.at === 'string' ? record.at : new Date().toISOString(),
            },
          }
        : null;
    case 'analysis':
      return typeof record.crawlId === 'string' ? { type: 'analysis', data: record as unknown as AnalysisEvent } : null;
    default:
      return null;
  }
}

const PROGRESS_NUMBERS = [
  'elapsedMs',
  'discovered',
  'queued',
  'inFlight',
  'fetched',
  'htmlPages',
  'redirects',
  'errors',
  'blocked',
  'robotsDisallowed',
  'skipped',
  'bytesDownloaded',
  'urlsPerSecond',
] as const;

/** Missing or malformed counts read as 0, so one bad field can't put NaN on screen. */
function withNumbers(progress: ProgressEvent): ProgressEvent {
  const fixed = { ...progress };
  for (const key of PROGRESS_NUMBERS) {
    const value = fixed[key] as unknown;
    if (typeof value !== 'number' || !Number.isFinite(value)) fixed[key] = 0;
  }
  return fixed;
}

function blankCrawl(id: string): LiveCrawl {
  return { id, status: null, progress: null, history: [], alerts: [], analysis: null, error: null, errorType: null };
}

function withLive(live: readonly string[], id: string, status: CrawlStatus): readonly string[] {
  const has = live.includes(id);
  if (isLive(status)) return has ? live : [...live, id];
  return has ? live.filter((other) => other !== id) : live;
}

function updateCrawl(state: LiveState, id: string, update: (crawl: LiveCrawl) => LiveCrawl): LiveState {
  const crawl = update(state.crawls[id] ?? blankCrawl(id));
  return { ...state, crawls: { ...state.crawls, [id]: crawl } };
}

export function applyEvent(state: LiveState, event: SidecarEvent): LiveState {
  switch (event.type) {
    case 'hello':
      // A (re)connected stream: exactly these are live now. Their latest progress follows.
      return { ...state, live: [...new Set(event.data.live)] };
    case 'progress': {
      const progress = event.data;
      const next = updateCrawl(state, progress.crawlId, (crawl) => ({
        ...crawl,
        status: progress.status,
        progress,
        history:
          crawl.history.length >= HISTORY_POINTS
            ? [...crawl.history.slice(crawl.history.length - HISTORY_POINTS + 1), progress.urlsPerSecond]
            : [...crawl.history, progress.urlsPerSecond],
      }));
      return { ...next, live: withLive(next.live, progress.crawlId, progress.status) };
    }
    case 'crawl': {
      const { crawlId, status, error, errorType } = event.data;
      const next = updateCrawl(state, crawlId, (crawl) => ({
        ...crawl,
        status,
        error: error ?? null,
        errorType: errorType ?? null,
        analysis: status === 'analyzing' ? crawl.analysis : null,
      }));
      return { ...next, live: withLive(next.live, crawlId, status) };
    }
    case 'alert':
      return updateCrawl(state, event.data.crawlId, (crawl) => ({
        ...crawl,
        alerts:
          crawl.alerts.length >= MAX_ALERTS
            ? [...crawl.alerts.slice(crawl.alerts.length - MAX_ALERTS + 1), event.data]
            : [...crawl.alerts, event.data],
      }));
    case 'analysis':
      return updateCrawl(state, event.data.crawlId, (crawl) => ({ ...crawl, analysis: event.data }));
    case 'library':
      return state;
  }
}
