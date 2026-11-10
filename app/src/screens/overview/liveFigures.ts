// The live crawl's figures (docs/v2-ui-ia.md, Overview (live)): the time left when a URL limit makes one meaningful,
// the rate's sparkline, what the crawl is doing, and what was found split into crawled, queued and not followed. Pure,
// from what the live store keeps.
import type { CrawlPhase, CrawlStatus } from '../../api/types';
import type { LiveProgress, TrailPoint } from '../../live/liveStore';

/** The pace is measured over the last minute: long enough to ride out a slow page, short enough to follow a change. */
export const PACE_WINDOW_MS = 60_000;
/** Less than this much of the crawl seen, and there's no pace to speak of yet. */
export const PACE_MIN_WINDOW_MS = 5_000;
/** The sparkline draws at most this many points: each then stands for a stretch of the rate's history. */
export const SPARK_POINTS = 120;

export interface TimeLeft {
  ms: number;
  /**
   * `about`: the crawl has already found as many URLs as its limit allows, so this is what's left. `within`: it may
   * yet find fewer than the limit, so this is the longest it can take at this pace.
   */
  bound: 'about' | 'within';
}

/** The site's URLs fetched per ms over the pace window, or null when too little has been seen or nothing moved. */
export function paceOf(trail: readonly TrailPoint[]): number | null {
  const newest = trail.at(-1);
  if (!newest) return null;
  const from = trail.find((point) => point.at >= newest.at - PACE_WINDOW_MS) ?? newest;
  const span = newest.at - from.at;
  if (span < PACE_MIN_WINDOW_MS) return null;
  const pace = (newest.internalFetched - from.internalFetched) / span;
  return pace > 0 ? pace : null;
}

/**
 * How long a running crawl with a URL limit has left at its present pace. Null without a limit (it ends when it runs
 * out of URLs, which nothing can foretell), while it isn't running, and before there's a pace to go by.
 */
export function timeLeft(
  status: CrawlStatus | null,
  progress: Pick<LiveProgress, 'maxUrls' | 'internalFound' | 'internalFetched'> | null,
  trail: readonly TrailPoint[],
): TimeLeft | null {
  if (status !== 'running' || !progress) return null;
  const { maxUrls, internalFound, internalFetched } = progress;
  if (maxUrls === null || maxUrls === undefined || maxUrls <= 0) return null;
  if (internalFound === null || internalFetched === null) return null;
  const pace = paceOf(trail);
  if (pace === null) return null;
  // The limit counts the site's own URLs, so the crawl heads for the smaller of what it has found and the limit.
  return { ms: Math.max(0, maxUrls - internalFetched) / pace, bound: internalFound >= maxUrls ? 'about' : 'within' };
}

export interface Spark {
  /** Oldest first, at most SPARK_POINTS: the rate's history, averaged a stretch at a time when it's longer. */
  values: number[];
  /** The latest rate. */
  now: number | null;
  /** The highest rate in the history. */
  peak: number | null;
  /** How long the history spans. */
  spanMs: number;
}

/**
 * The rate's history as the sparkline draws it. `intervalMs` is how far apart the samples are (the engine's pace). The
 * last point is always the latest rate itself, so the line ends at the figure beside it.
 */
export function sparkOf(history: readonly number[], intervalMs = 500, points: number = SPARK_POINTS): Spark {
  const now = history.at(-1) ?? null;
  const peak = history.length > 0 ? history.reduce((max, value) => Math.max(max, value), 0) : null;
  const spanMs = Math.max(0, history.length - 1) * intervalMs;
  if (history.length <= points) return { values: [...history], now, peak, spanMs };
  const values: number[] = [];
  const body = history.length - 1;
  const size = body / (points - 1);
  for (let index = 0; index < points - 1; index++) {
    const start = Math.floor(index * size);
    const end = Math.max(start + 1, Math.floor((index + 1) * size));
    let sum = 0;
    for (let at = start; at < end; at++) sum += history[at] ?? 0;
    values.push(sum / (end - start));
  }
  values.push(history[body] ?? 0);
  return { values, now, peak, spanMs };
}

const PHASES: Record<CrawlPhase, string> = {
  starting: 'Starting',
  readingRobotsTxt: 'Reading robots.txt',
  crawling: 'Crawling',
  readingSitemaps: 'Reading the sitemaps',
  retrying: 'Trying the URLs that didn’t answer once more',
  finishing: 'Finishing',
};

/** What the crawl is doing as a whole, in a few words: its status when that says more than its phase. */
export function activityWords(status: CrawlStatus | null, phase: CrawlPhase | null): string {
  if (status === 'paused') return 'Paused';
  if (status === 'stopping') return 'Stopping';
  return phase ? PHASES[phase] : 'Crawling';
}

export interface FoundSplit {
  /** Every URL found. */
  found: number;
  crawled: number;
  /** Waiting or on their way. */
  queued: number;
  /** Found but not to be fetched: nofollow links, past a limit. */
  notFollowed: number;
}

/** What was found, split three ways. Null unless every part is known, as the parts wouldn't add up. */
export function foundSplit(
  progress: Pick<LiveProgress, 'discovered' | 'fetched' | 'queued' | 'inFlight' | 'skipped'> | null,
): FoundSplit | null {
  if (!progress) return null;
  const { discovered, fetched, queued, inFlight, skipped } = progress;
  if (discovered === null || fetched === null || queued === null || skipped === null) return null;
  return { found: discovered, crawled: fetched, queued: queued + (inFlight ?? 0), notFollowed: skipped };
}
