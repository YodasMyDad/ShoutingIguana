// Live crawl state, outside React. Events are applied as they arrive, but React hears about them at most 4 times a
// second, and not at all while the window is hidden (in the tray), so a 2,900 URLs/s crawl never re-renders the grid
// and a hidden window stays idle. Only components that select live data subscribe.
import { useSyncExternalStore } from 'react';
import { parseExportJob } from '../api/exports';
import { CRAWL_PHASES, isCrawlStatus, isLive } from '../api/types';
import type {
  AlertData,
  AlertEvent,
  AlertLevel,
  AnalysisEvent,
  CrawlEvent,
  CrawlPhase,
  CrawlStatus,
  CrawlWait,
  CrawlWaitReason,
  NoResponseCounts,
  ProgressEvent,
  SidecarEvent,
  SiteVersionCounts,
  StatusClassCounts,
} from '../api/types';
import type { SseMessage, StreamState } from './sse';

/** Five minutes of rate samples at one per progress event (every 500 ms). */
export const HISTORY_POINTS = 600;
export const MAX_ALERTS = 200;
/** At most 4 renders a second. */
export const PUBLISH_INTERVAL_MS = 250;
/** The found-and-crawled trail: at most this many points however long the crawl runs, as the sidecar's own history. */
export const TRAIL_POINTS = 600;
/** The pace is read over this much of the time the run has spent crawling: its samples go back that far, and no further. */
export const PACE_WINDOW_MS = 60_000;
/** At most this many pace samples are kept: the window at four events a second, twice the engine's pace. */
export const PACE_SAMPLES = 240;

const PROGRESS_COUNTS = [
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

type ProgressCount = (typeof PROGRESS_COUNTS)[number];

/**
 * E4's part of a progress event (docs/v2-sidecar-api.md, Progress), as the store keeps it. An engine from before E4
 * sends none of it, and a part the stream sent malformed isn't guessed at: each is null (or empty) then, so the screen
 * says "–" or leaves the figure out, never 0.
 */
export interface LiveProgressDetail {
  /** When the numbers were taken, in ms since the epoch. */
  at: number | null;
  phase: CrawlPhase | null;
  /** The site's own URLs the crawl means to fetch, and those it has fetched: what a URL limit counts. */
  internalFound: number | null;
  internalFetched: number | null;
  /** The URL limit; null when the crawl has none; undefined when the stream didn't say. */
  maxUrls: number | null | undefined;
  /** Every URL fetched, once, by class. Null unless every class is a count. */
  statusClasses: StatusClassCounts | null;
  /** URLs with a status that weren't blocked, by code. */
  statusCodes: Readonly<Record<string, number>> | null;
  /** Why URLs got no response. Null unless every reason is a count. */
  noResponse: NoResponseCounts | null;
  /** What holds part of the crawl up now, the longest first; usually nothing. */
  waits: readonly CrawlWait[];
  /** How many more waits there are than `waits` lists, by reason. */
  moreWaits: Readonly<Partial<Record<CrawlWaitReason, number>>>;
  /** E3c: the URLs crawled so far, as the URL grid counts them (`fetched` is true). Null from an earlier engine. */
  crawled: number | null;
  /**
   * E3c: the part of the figures that is the crawler's own probes of the site's other versions. Null when the stream
   * didn't say (an earlier engine) or said it malformed: then nothing is taken out, as nothing is known to be a probe.
   */
  siteVersions: SiteVersionCounts | null;
  /**
   * E3c: the time spent crawling over every run, standing still while paused. Null when an earlier run's time wasn't
   * kept; undefined when the stream didn't say (an earlier engine).
   */
  crawlingMs: number | null | undefined;
}

/** A progress event as the store keeps it: a count the stream sent malformed is null, shown as "–", never 0. */
export type LiveProgress = Omit<ProgressEvent, ProgressCount | keyof LiveProgressDetail> &
  Record<ProgressCount, number | null> &
  LiveProgressDetail;

/** A stream event as the store reads it at the boundary. */
export type LiveEvent = Exclude<SidecarEvent, { type: 'progress' }> | { type: 'progress'; data: LiveProgress };

/**
 * How long the run being watched has spent crawling: the one clock the live screen's times go by (the pace, Elapsed,
 * the chart's time axis). It runs while the crawl's status is `running` and stands still while it's paused or
 * stopping, and it starts again with each run (a continued crawl). The engine has no such figure: its `elapsedMs` is
 * the wall time since the run began, time paused included, so the page counts from there, by the progress events it
 * hears. What was paused before the page first heard of the run can't be told, and counts.
 */
export interface RunClock {
  /** Ms this run has spent crawling, as far as the page can tell. */
  runMs: number;
  /** The progress event it was last advanced by: when it was taken, its `elapsedMs`, and the crawl's status then. */
  at: number;
  elapsedMs: number | null;
  status: CrawlStatus;
}

/** One point of the found-and-crawled chart, taken from a progress event at its `at`. */
export interface TrailPoint {
  /** Ms since the epoch. */
  at: number;
  /** Ms the run had spent crawling by then (`RunClock`): where the point sits on the chart's time axis. */
  runMs: number;
  /**
   * The engine's time spent crawling by then, over every run (E3c's `crawlingMs`), when the event said it: what Elapsed
   * shows, and where the chart places the point. The run's clock can't know of a pause from before the page first
   * heard of the run (a page loaded again part-way, round 5's polish); this does.
   */
  crawlingMs?: number | undefined;
  fetched: number | null;
  internalFound: number;
  internalFetched: number;
}

/** The site's URLs fetched after so much of the run's time spent crawling: what the pace is worked out from. */
export interface PaceSample {
  runMs: number;
  internalFetched: number;
}

/**
 * The progress of the run being watched since this page first heard of it: at most TRAIL_POINTS points however long it
 * runs, evenly spread, as the sidecar keeps its own history. `GET /history` gives what came before; this carries the
 * chart on. It starts again when the crawl is continued, as the run's clock does.
 */
export interface LiveTrail {
  /** Oldest first. The last is always the newest progress, so the chart ends where the headline figures are. */
  points: readonly TrailPoint[];
  /** One point is kept for every this many progress events: 1 at first, doubling each time the trail fills. */
  every: number;
  /** Progress events since the last kept point: above 0, the last point only stands in for the newest. */
  since: number;
}

export const EMPTY_TRAIL: LiveTrail = { points: [], every: 1, since: 0 };

export interface LiveCrawl {
  id: string;
  /** This sidecar's live status for the crawl; null once a stream's `hello` no longer lists it as live. */
  status: CrawlStatus | null;
  progress: LiveProgress | null;
  /** URLs per second, oldest first, for the sparkline. */
  history: readonly number[];
  /** The run's progress since this page first heard of it, for the found-and-crawled chart. */
  trail: LiveTrail;
  /** How long the run has spent crawling; null until a progress event says when it was taken. */
  clock: RunClock | null;
  /**
   * The newest samples for the pace, oldest first: one per progress event taken while the crawl was running, as far
   * back as PACE_WINDOW_MS of the run's time. Every event, never thinned as the trail is, so a long crawl's pace is as
   * exact as a short one's; and by the run's clock, so time paused is no part of it.
   */
  pace: readonly PaceSample[];
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
  private readonly eventListeners = new Set<(event: LiveEvent, previous: LiveState) => void>();

  constructor(private readonly clock: LiveStoreClock = browserClock) {}

  readonly getState = (): LiveState => this.published;

  readonly subscribe = (listener: () => void): (() => void) => {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  };

  /**
   * Every event as it arrives, unbatched, with the state from before it: for side effects such as refetching, never
   * for rendering.
   */
  onEvent(listener: (event: LiveEvent, previous: LiveState) => void): () => void {
    this.eventListeners.add(listener);
    return () => this.eventListeners.delete(listener);
  }

  ingest(message: SseMessage): void {
    const event = parseSidecarEvent(message);
    if (!event) return;
    const previous = this.draft;
    this.draft = applyEvent(previous, event);
    this.eventListeners.forEach((listener) => listener(event, previous));
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
export function parseSidecarEvent(message: SseMessage): LiveEvent | null {
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
        ? {
            type: 'hello',
            data: {
              live: strings(record.live),
              analyzing: strings(record.analyzing),
              exports: strings(record.exports),
            },
          }
        : null;
    case 'library':
      return { type: 'library', data: {} };
    case 'progress':
      return typeof record.crawlId === 'string' && isCrawlStatus(record.status)
        ? { type: 'progress', data: withCounts(record.crawlId, record.status, record) }
        : null;
    case 'crawl':
      return typeof record.crawlId === 'string' && isCrawlStatus(record.status)
        ? {
            type: 'crawl',
            data: {
              crawlId: record.crawlId,
              status: record.status,
              ...(typeof record.error === 'string' ? { error: record.error } : {}),
              ...(typeof record.errorType === 'string' ? { errorType: record.errorType } : {}),
            } satisfies CrawlEvent,
          }
        : null;
    case 'alert':
      return typeof record.crawlId === 'string' && typeof record.message === 'string'
        ? {
            type: 'alert',
            data: {
              crawlId: record.crawlId,
              ...(isCount(record.id) && Number.isInteger(record.id) ? { id: record.id } : {}),
              level: (['info', 'warning', 'error'].includes(String(record.level))
                ? record.level
                : 'info') as AlertLevel,
              code: typeof record.code === 'string' ? record.code : '',
              message: record.message,
              at: typeof record.at === 'string' ? record.at : new Date().toISOString(),
              data: alertData(record.data),
            },
          }
        : null;
    case 'analysis':
      return typeof record.crawlId === 'string' ? { type: 'analysis', data: record as unknown as AnalysisEvent } : null;
    case 'export': {
      const job = parseExportJob(record);
      return job ? { type: 'export', data: job } : null;
    }
    default:
      return null;
  }
}

/**
 * A missing or malformed count reads as null, so it shows as "–": never NaN, and never 0, which would claim the crawl
 * did nothing.
 */
function withCounts(crawlId: string, status: CrawlStatus, record: Record<string, unknown>): LiveProgress {
  const counts = {} as Record<ProgressCount, number | null>;
  for (const key of PROGRESS_COUNTS) {
    const value = record[key];
    counts[key] = typeof value === 'number' && Number.isFinite(value) ? value : null;
  }
  return { crawlId, status, ...counts, ...progressDetail(record) };
}

/** A progress event as the stream sends it, read as the store keeps it: for tests and stories that make their own. */
export function liveProgressOf(event: ProgressEvent): LiveProgress {
  return withCounts(event.crawlId, event.status, event as unknown as Record<string, unknown>);
}

const isCount = (value: unknown): value is number => typeof value === 'number' && Number.isFinite(value) && value >= 0;

const isRecord = (value: unknown): value is Record<string, unknown> =>
  typeof value === 'object' && value !== null && !Array.isArray(value);

const strings = (value: unknown): string[] =>
  Array.isArray(value) ? value.filter((entry): entry is string => typeof entry === 'string') : [];

const STATUS_CLASS_KEYS = ['2xx', '3xx', '4xx', '5xx', 'other', 'noResponse', 'blocked'] as const;
const NO_RESPONSE_KEYS = ['robotsDisallowed', 'timeout', 'networkError', 'notChecked', 'processingError'] as const;
const WAIT_REASONS: readonly CrawlWaitReason[] = [
  'robotsTxt',
  'otherSitesRobotsTxt',
  'crawlDelay',
  'backoff',
  'heldBack',
  'siteDown',
  'robotsTxtCoolDown',
  'sectionLimited',
];
/** Shared, so a progress event without waits (most of them) keeps the same value from one event to the next. */
const NO_WAITS: readonly CrawlWait[] = [];
const NO_MORE_WAITS: Readonly<Partial<Record<CrawlWaitReason, number>>> = {};

/** A breakdown whose every part is a count, or null: a part missing would make the parts add up to less than all. */
function countsOf<Key extends string>(value: unknown, keys: readonly Key[]): Record<Key, number> | null {
  if (!isRecord(value)) return null;
  const counts = {} as Record<Key, number>;
  for (const key of keys) {
    const count = value[key];
    if (!isCount(count)) return null;
    counts[key] = count;
  }
  return counts;
}

function statusCodesOf(value: unknown): Record<string, number> | null {
  if (!isRecord(value)) return null;
  const codes: Record<string, number> = {};
  for (const [code, count] of Object.entries(value)) {
    if (/^\d{1,3}$/.test(code) && isCount(count)) codes[code] = count;
  }
  return codes;
}

function waitsOf(value: unknown): readonly CrawlWait[] {
  if (!Array.isArray(value) || value.length === 0) return NO_WAITS;
  const waits: CrawlWait[] = [];
  for (const entry of value) {
    if (!isRecord(entry)) continue;
    const reason = WAIT_REASONS.find((candidate) => candidate === entry.reason);
    if (!reason) continue;
    waits.push({
      reason,
      ...(typeof entry.host === 'string' ? { host: entry.host } : {}),
      ...(isCount(entry.urls) ? { urls: entry.urls } : {}),
      ...(isCount(entry.durationMs) ? { durationMs: entry.durationMs } : {}),
      ...(typeof entry.section === 'string' ? { section: entry.section } : {}),
    });
  }
  return waits.length > 0 ? waits : NO_WAITS;
}

function moreWaitsOf(value: unknown): Readonly<Partial<Record<CrawlWaitReason, number>>> {
  if (!isRecord(value)) return NO_MORE_WAITS;
  const more: Partial<Record<CrawlWaitReason, number>> = {};
  for (const reason of WAIT_REASONS) {
    const count = value[reason];
    if (isCount(count) && count > 0) more[reason] = count;
  }
  return Object.keys(more).length > 0 ? more : NO_MORE_WAITS;
}

function progressDetail(record: Record<string, unknown>): LiveProgressDetail {
  const at = typeof record.at === 'string' ? Date.parse(record.at) : Number.NaN;
  return {
    at: Number.isNaN(at) ? null : at,
    phase: CRAWL_PHASES.find((phase) => phase === record.phase) ?? null,
    internalFound: isCount(record.internalFound) ? record.internalFound : null,
    internalFetched: isCount(record.internalFetched) ? record.internalFetched : null,
    maxUrls: record.maxUrls === null ? null : isCount(record.maxUrls) ? record.maxUrls : undefined,
    statusClasses: countsOf(record.statusClasses, STATUS_CLASS_KEYS),
    statusCodes: statusCodesOf(record.statusCodes),
    noResponse: countsOf(record.noResponse, NO_RESPONSE_KEYS),
    waits: waitsOf(record.waits),
    moreWaits: moreWaitsOf(record.moreWaits),
    crawled: isCount(record.crawled) ? record.crawled : null,
    siteVersions: siteVersionsOf(record.siteVersions),
    crawlingMs: record.crawlingMs === null ? null : isCount(record.crawlingMs) ? record.crawlingMs : undefined,
  };
}

/** The probes' part of the figures, whole or not at all: a part missing would take too little out. */
function siteVersionsOf(value: unknown): SiteVersionCounts | null {
  if (!isRecord(value) || !isCount(value.fetched)) return null;
  const statusClasses = countsOf(value.statusClasses, STATUS_CLASS_KEYS);
  const statusCodes = statusCodesOf(value.statusCodes);
  const noResponse = countsOf(value.noResponse, NO_RESPONSE_KEYS);
  if (!statusClasses || !statusCodes || !noResponse) return null;
  return { fetched: value.fetched, statusClasses, statusCodes, noResponse };
}

/** An alert's facts: only the contract's fields, each of its own type; null when it has none. */
function alertData(value: unknown): AlertData | null {
  if (!isRecord(value)) return null;
  const data: AlertData = {};
  for (const key of ['host', 'origin', 'url', 'reason'] as const) {
    const text = value[key];
    if (typeof text === 'string') data[key] = text;
  }
  for (const key of ['count', 'sampled', 'urlCount', 'durationMs'] as const) {
    const count = value[key];
    if (isCount(count)) data[key] = count;
  }
  return Object.keys(data).length > 0 ? data : null;
}

/**
 * Adds a progress event's point to a crawl's trail, keeping it to `capacity` points however long the crawl runs: every
 * event is a point at first; once full, every other point goes, and from then on every second event is kept, then
 * every fourth, and so on. Between kept points the newest event stands in as the last, so the chart always ends at now.
 */
export function addTrailPoint(trail: LiveTrail, point: TrailPoint, capacity: number = TRAIL_POINTS): LiveTrail {
  const newest = trail.points.at(-1);
  // Out of order (a reconnect's replayed progress): what's kept is newer.
  if (newest && point.at <= newest.at) return trail;
  const points = trail.since > 0 ? [...trail.points.slice(0, -1), point] : [...trail.points, point];
  if (points.length <= capacity) return { points, every: trail.every, since: (trail.since + 1) % trail.every };
  // Full: the points at even places stay, a kept point for every `2 × every` events, and the one just added stands in.
  const last = points.length - 1;
  const every = trail.every * 2;
  return {
    points: points.filter((_, index) => index % 2 === 0 || index === last),
    every,
    since: (last % 2 === 0 ? trail.every + 1 : 1) % every,
  };
}

/**
 * The run's clock after a progress event. It advances by the time since the last event when the crawl was running
 * then, and not at all when it was paused or stopping. A new run (the crawl was continued: the engine's `elapsedMs`
 * starts again, or the last event was a finished run's) starts it again from the engine's `elapsedMs`, which is all
 * that's known of a run the page comes to part-way. An event that isn't newer than the last (a reconnect replays the
 * latest) changes nothing.
 */
export function advanceRunClock(clock: RunClock | null, progress: LiveProgress): RunClock | null {
  const { at, elapsedMs, status } = progress;
  if (at === null) return clock;
  if (clock === null || startsNewRun(clock, progress))
    return { runMs: Math.max(0, elapsedMs ?? 0), at, elapsedMs, status };
  if (at <= clock.at) return clock;
  return { runMs: clock.runMs + (clock.status === 'running' ? at - clock.at : 0), at, elapsedMs, status };
}

function startsNewRun(clock: RunClock, progress: LiveProgress): boolean {
  if (progress.at === null || progress.at <= clock.at) return false;
  if (progress.elapsedMs !== null && clock.elapsedMs !== null && progress.elapsedMs < clock.elapsedMs) return true;
  // The last event was of a run that had ended, and this one is of a crawl that's live.
  return !isLive(clock.status) && isLive(progress.status);
}

/** Adds a sample for the pace, and lets go of the ones older than the window (but for the one that spans it). */
export function addPaceSample(samples: readonly PaceSample[], sample: PaceSample): readonly PaceSample[] {
  const from = sample.runMs - PACE_WINDOW_MS;
  // The last sample at or before the window's start stays, so the window is spanned in full.
  let keep = 0;
  while (keep + 1 < samples.length && (samples[keep + 1]?.runMs ?? Number.POSITIVE_INFINITY) <= from) keep++;
  const kept = [...samples.slice(Math.max(keep, samples.length + 1 - PACE_SAMPLES)), sample];
  return kept;
}

function blankCrawl(id: string): LiveCrawl {
  return {
    id,
    status: null,
    progress: null,
    history: [],
    trail: EMPTY_TRAIL,
    clock: null,
    pace: [],
    alerts: [],
    analysis: null,
    error: null,
    errorType: null,
  };
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

export function applyEvent(state: LiveState, event: LiveEvent): LiveState {
  switch (event.type) {
    case 'hello': {
      // A (re)connected stream, maybe to a new sidecar after a crash: exactly these are live now, and their latest
      // progress follows. Any other crawl stops looking live, so its status is the library's again (a crawl the old
      // sidecar was running reads `interrupted`, with Continue).
      // E4: the crawls being analysed here are live too, and their latest `analysis` event follows, though this
      // stream missed their `analyzing` status.
      const analyzing = (event.data.analyzing ?? []).filter((id) => !event.data.live.includes(id));
      const live = [...new Set([...event.data.live, ...analyzing])];
      let crawls = state.crawls;
      for (const crawl of Object.values(state.crawls)) {
        if (crawl.status !== null && isLive(crawl.status) && !live.includes(crawl.id)) {
          crawls = { ...crawls, [crawl.id]: { ...crawl, status: null, analysis: null } };
        }
      }
      for (const id of analyzing) {
        const crawl = crawls[id] ?? blankCrawl(id);
        if (crawl.status !== 'analyzing') crawls = { ...crawls, [id]: { ...crawl, status: 'analyzing' } };
      }
      return { ...state, live, crawls };
    }
    case 'progress': {
      const progress = event.data;
      const rate = progress.urlsPerSecond;
      const next = updateCrawl(state, progress.crawlId, (crawl) => {
        const { at, internalFound, internalFetched } = progress;
        const clock = advanceRunClock(crawl.clock, progress);
        // A new run: its chart and its pace start with it, as its clock does.
        const restarted = crawl.clock !== null && clock !== crawl.clock && startsNewRun(crawl.clock, progress);
        const before = restarted ? { trail: EMPTY_TRAIL, pace: [] } : crawl;
        const placed = clock !== null && clock !== crawl.clock && at !== null;
        const trail =
          placed && internalFound !== null && internalFetched !== null
            ? addTrailPoint(before.trail, {
                at,
                runMs: clock.runMs,
                ...(typeof progress.crawlingMs === 'number' ? { crawlingMs: progress.crawlingMs } : {}),
                fetched: progress.fetched,
                internalFound,
                internalFetched,
              })
            : before.trail;
        const pace =
          placed && progress.status === 'running' && internalFetched !== null
            ? addPaceSample(before.pace, { runMs: clock.runMs, internalFetched })
            : before.pace;
        return {
          ...crawl,
          status: progress.status,
          progress,
          clock,
          pace,
          // A rate the stream didn't give leaves a gap rather than a false 0 in the sparkline.
          history:
            rate === null
              ? crawl.history
              : crawl.history.length >= HISTORY_POINTS
                ? [...crawl.history.slice(crawl.history.length - HISTORY_POINTS + 1), rate]
                : [...crawl.history, rate],
          trail,
        };
      });
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
    case 'export':
      // An export is no part of a crawl's live state: whoever shows its progress hears it through `onEvent`.
      return state;
  }
}
