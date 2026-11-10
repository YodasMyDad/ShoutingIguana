// The "found and crawled" chart's data (docs/v2-sidecar-api.md, History): the run's history from the sidecar, carried
// on by the live store's trail of progress events, and the axes' ticks. Pure.
//
// The chart is of the run being watched, on the run's clock (the store's `RunClock`): the time it has spent crawling,
// which is also what Elapsed shows and what the pace is read over. A crawl that was continued has earlier runs in its
// history, counted back from its file, but the file doesn't keep when each run began and ended, so the time between
// them (seconds or days) can't be told from crawling: on one axis with this run it would be most of the chart. They
// are left out, and the chart starts where this run did, at the counts it started with.
import type { CrawlHistory } from '../../api/types';
import type { TrailPoint } from '../../live/liveStore';

export interface ProgressPoint {
  /** Ms the run had spent crawling. */
  elapsedMs: number;
  /** The site's own URLs found, and those crawled. */
  found: number;
  crawled: number;
}

export interface ProgressSeries {
  points: ProgressPoint[];
  /** How long the run has spent crawling: the points' span, and what Elapsed shows. */
  spanMs: number;
  /** The latest counts. */
  found: number;
  crawled: number;
  /** The run continued an earlier one, whose progress isn't drawn: the chart starts at this run's first counts. */
  continued: boolean;
}

/**
 * This run's points from the history (the ones the sidecar took live, from `liveFrom`), then the trail's. The history
 * is read once when the screen opens, and places a point by the engine's `elapsedMs`, the wall time since the run
 * began; the trail has every progress event since the page first heard of the run, placed on the run's clock, which
 * knows when the crawl was paused, so it takes over from its first point. Null when there's nothing to draw yet.
 */
export function progressSeries(history: CrawlHistory | undefined, trail: readonly TrailPoint[]): ProgressSeries | null {
  const points: ProgressPoint[] = [];
  const liveFrom = history?.liveFrom ? Date.parse(history.liveFrom) : Number.NaN;
  const takeover = trail[0];
  let continued = false;
  for (const point of history?.points ?? []) {
    const at = Date.parse(point.at);
    if (Number.isNaN(at)) continue;
    // Counted back from the file: an earlier run's (or, for a crawl not live here, all of it).
    if (Number.isNaN(liveFrom) || at < liveFrom) {
      continued ||= point.internalFetched > 0;
      continue;
    }
    if (takeover && at >= takeover.at) break;
    const elapsedMs = point.elapsedMs ?? at - liveFrom;
    // Never past where the trail takes over, which may know of a pause the history can't tell.
    if (takeover && elapsedMs >= takeover.runMs) continue;
    points.push({ elapsedMs, found: point.internalFound, crawled: point.internalFetched });
  }
  for (const point of trail) {
    points.push({ elapsedMs: point.runMs, found: point.internalFound, crawled: point.internalFetched });
  }
  const last = points.at(-1);
  if (!last) return null;
  return { points, spanMs: last.elapsedMs, found: last.found, crawled: last.crawled, continued };
}

const COUNT_STEPS = [1, 2, 5] as const;

/** Round steps up a count axis: 0, 2,000, 4,000, 6,000. At most `most` ticks above 0, the last at or above `max`. */
export function countTicks(max: number, most = 3): number[] {
  if (!Number.isFinite(max) || max <= 0) return [0, 1];
  const rough = max / most;
  const magnitude = 10 ** Math.floor(Math.log10(rough));
  const step = Math.max(1, (COUNT_STEPS.find((candidate) => candidate * magnitude >= rough) ?? 10) * magnitude);
  const ticks: number[] = [];
  for (let value = 0; value < max + step; value += step) ticks.push(value);
  return ticks;
}

const SECOND = 1_000;
const MINUTE = 60 * SECOND;
const HOUR = 60 * MINUTE;
const TIME_STEPS = [
  SECOND,
  2 * SECOND,
  5 * SECOND,
  10 * SECOND,
  15 * SECOND,
  30 * SECOND,
  MINUTE,
  2 * MINUTE,
  5 * MINUTE,
  10 * MINUTE,
  15 * MINUTE,
  30 * MINUTE,
  HOUR,
  2 * HOUR,
  6 * HOUR,
  12 * HOUR,
  24 * HOUR,
];

/** Round times along the axis: 0, 5 min, 10 min. At most `most` ticks after 0, none past the span. */
export function timeTicks(spanMs: number, most = 5): number[] {
  if (!Number.isFinite(spanMs) || spanMs <= 0) return [0];
  const step =
    TIME_STEPS.find((candidate) => spanMs / candidate <= most) ?? Math.ceil(spanMs / most / (24 * HOUR)) * 24 * HOUR;
  const ticks: number[] = [];
  for (let value = 0; value <= spanMs; value += step) ticks.push(value);
  return ticks;
}
