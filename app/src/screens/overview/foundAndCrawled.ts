// The "found and crawled" chart's data (docs/v2-sidecar-api.md, History): the crawl's history from the sidecar, carried
// on by the live store's trail of progress events, and the axes' ticks. Pure.
import type { CrawlHistory } from '../../api/types';
import type { TrailPoint } from '../../live/liveStore';

export interface ProgressPoint {
  /** Ms since the crawl first started. */
  elapsedMs: number;
  /** The site's own URLs found, and those crawled. */
  found: number;
  crawled: number;
}

export interface ProgressSeries {
  points: ProgressPoint[];
  /** How long the points span. */
  spanMs: number;
  /** The latest counts. */
  found: number;
  crawled: number;
}

/**
 * The history's points, then the trail's from where the history ends. The history is read once when the screen opens;
 * the trail has every progress event since the page first heard of the crawl, so between them nothing is missing.
 * Null when there's nothing to draw yet.
 */
export function progressSeries(history: CrawlHistory | undefined, trail: readonly TrailPoint[]): ProgressSeries | null {
  const points: { at: number; found: number; crawled: number }[] = [];
  for (const point of history?.points ?? []) {
    const at = Date.parse(point.at);
    if (!Number.isNaN(at)) points.push({ at, found: point.internalFound, crawled: point.internalFetched });
  }
  const end = points.at(-1)?.at ?? Number.NEGATIVE_INFINITY;
  for (const point of trail) {
    if (point.at > end) points.push({ at: point.at, found: point.internalFound, crawled: point.internalFetched });
  }
  const first = points[0];
  const last = points.at(-1);
  if (!first || !last) return null;
  const started = history ? Date.parse(history.startedAt) : Number.NaN;
  const zero = Number.isNaN(started) ? first.at : Math.min(started, first.at);
  return {
    points: points.map((point) => ({ elapsedMs: point.at - zero, found: point.found, crawled: point.crawled })),
    spanMs: last.at - zero,
    found: last.found,
    crawled: last.crawled,
  };
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
