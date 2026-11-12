// The live crawl's figures (docs/v2-ui-ia.md, Overview (live)): the time left when a URL limit makes one meaningful,
// the rate's sparkline, what the crawl is doing, and what was found split into crawled, queued and not followed. Pure,
// from what the live store keeps. Every time here is the run's time spent crawling (the store's `RunClock`): it stands
// still while the crawl is paused, so a pause is no part of the pace.
import type { CrawlPhase, CrawlStatus } from '../../api/types';
import { PACE_WINDOW_MS, type LiveProgress, type PaceSample } from '../../live/liveStore';

/**
 * The pace is measured over the last minute the run spent crawling: long enough to ride out a slow page, short enough
 * to follow a change. The store keeps a sample of every progress event that far back.
 */
export { PACE_WINDOW_MS };
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

/**
 * The site's URLs fetched per ms of crawling, over the pace window: from the newest sample back to the oldest within a
 * minute of the run's time. Null when too little has been seen or nothing moved. The samples are the store's
 * (`LiveCrawl.pace`): taken only while the crawl was running, on the run's clock.
 */
export function paceOf(samples: readonly PaceSample[]): number | null {
  const newest = samples.at(-1);
  if (!newest) return null;
  const from = samples.find((sample) => sample.runMs >= newest.runMs - PACE_WINDOW_MS) ?? newest;
  const span = newest.runMs - from.runMs;
  if (span < PACE_MIN_WINDOW_MS) return null;
  const pace = (newest.internalFetched - from.internalFetched) / span;
  return pace > 0 ? pace : null;
}

/** Whether the crawl has fetched as many of the site's URLs as its limit allows: nothing is left to wait for. */
export function atUrlLimit(progress: Pick<LiveProgress, 'maxUrls' | 'internalFetched'> | null): boolean {
  const limit = progress?.maxUrls;
  const fetched = progress?.internalFetched;
  return typeof limit === 'number' && limit > 0 && typeof fetched === 'number' && fetched >= limit;
}

/**
 * How long a running crawl with a URL limit has left at its present pace. Null without a limit (it ends when it runs
 * out of URLs, which nothing can foretell), while it isn't running, before there's a pace to go by, and once the
 * limit is reached: there's no time left to speak of then, and "about 0 s left" would only count the last requests
 * settling.
 */
export function timeLeft(
  status: CrawlStatus | null,
  progress: Pick<LiveProgress, 'maxUrls' | 'internalFound' | 'internalFetched'> | null,
  samples: readonly PaceSample[],
): TimeLeft | null {
  if (status !== 'running' || !progress) return null;
  const { maxUrls, internalFound, internalFetched } = progress;
  if (maxUrls === null || maxUrls === undefined || maxUrls <= 0) return null;
  if (internalFound === null || internalFetched === null) return null;
  if (atUrlLimit(progress)) return null;
  const pace = paceOf(samples);
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

/**
 * The URLs crawled so far, as the URL grid counts them (`fetched` is true). E4's `fetched` is every URL the crawl has
 * stored an outcome for, and that includes the ones it never asked because their host had stopped answering
 * (`noResponse.notChecked`), which the grid has as not fetched: they come out. Null when `fetched` isn't known.
 */
export function crawledSoFar(
  progress: (Pick<LiveProgress, 'fetched' | 'noResponse'> & Partial<Pick<LiveProgress, 'crawled'>>) | null,
): number | null {
  // E3c's own count, the same subtraction made by the engine; worked out here for an engine that doesn't send it.
  if (typeof progress?.crawled === 'number') return progress.crawled;
  const fetched = progress?.fetched ?? null;
  return fetched === null ? null : fetched - notCheckedSoFar(progress);
}

/** The URLs that weren't checked so far: 0 when the stream hasn't said. Never more than `fetched`, which counts them. */
export function notCheckedSoFar(progress: Pick<LiveProgress, 'fetched' | 'noResponse'> | null): number {
  return Math.max(0, Math.min(progress?.noResponse?.notChecked ?? 0, progress?.fetched ?? 0));
}

export interface FoundSplit {
  /** Every URL found. */
  found: number;
  /** As the URL grid counts them: the ones that weren't checked left out. */
  crawled: number;
  /** Waiting or on their way. */
  queued: number;
  /** Found but not to be fetched: nofollow links, past a limit. */
  notFollowed: number;
  /** Never asked, their site having stopped answering: neither crawled nor left out by a rule. Usually 0. */
  notChecked: number;
}

/** What was found, split by what became of it. Null unless every part is known, as the parts wouldn't add up. */
export function foundSplit(
  progress:
    | (Pick<LiveProgress, 'discovered' | 'fetched' | 'queued' | 'inFlight' | 'skipped' | 'noResponse'> &
        Partial<Pick<LiveProgress, 'crawled'>>)
    | null,
): FoundSplit | null {
  if (!progress) return null;
  const { discovered, fetched, queued, inFlight, skipped } = progress;
  if (discovered === null || fetched === null || queued === null || skipped === null) return null;
  const notChecked = notCheckedSoFar(progress);
  return {
    found: discovered,
    crawled: crawledSoFar(progress) ?? fetched - notChecked,
    queued: queued + (inFlight ?? 0),
    notFollowed: skipped,
    notChecked,
  };
}
