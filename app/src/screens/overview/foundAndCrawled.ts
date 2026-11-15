// The "found and crawled" chart's data (docs/v2-sidecar-api.md, History): the run's history from the sidecar, carried
// on by the live store's trail of progress events, and the axes' ticks. Pure.
//
// The chart's axis is time spent crawling: the run being watched on the run's clock (the store's `RunClock`), which is
// also what the pace is read over. A crawl that was continued has earlier runs in its history, counted back from its
// file. From E3c the history says when each run was crawling (`runs`, each with its stretches), so the earlier runs are
// drawn on the same axis, their points placed by the crawling time up to them: the time between runs (seconds or days)
// and the pauses close up, and each later run's start is marked. From an earlier engine, which kept no such times, the
// time between runs can't be told from crawling, and on one axis with this run it would be most of the chart: the
// earlier runs are left out, and the chart starts where this run did, at the counts it started with.
import type { CrawlHistory, CrawlRun } from '../../api/types';
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
  /**
   * The run continued an earlier one. Without `runs` (an earlier engine), its progress isn't drawn: the chart starts
   * at this run's first counts.
   */
  continued: boolean;
  /**
   * E3c: the chart is of the whole crawl, every run on one axis of time spent crawling: where each run after the first
   * began on it, oldest first. Null when the runs' times aren't known, and the chart is of this run alone.
   */
  runStarts: number[] | null;
  /**
   * Where the run being watched starts on the axis: the earlier runs' time spent crawling, which the run's own clock
   * adds to; 0 when the chart is of this run alone.
   */
  offsetMs: number;
}

/** How long the stretches had been crawling at `at`: every stretch that ended by then, and the part of the one it's in. */
export function crawlingAt(at: number, runs: readonly CrawlRun[]): number {
  let total = 0;
  for (const run of runs) {
    for (const stretch of run.crawling) {
      const from = Date.parse(stretch.from);
      const to = Date.parse(stretch.to);
      if (Number.isNaN(from) || Number.isNaN(to) || at <= from) continue;
      total += Math.min(at, to) - from;
    }
  }
  return total;
}

/**
 * The runs before the one being watched, and where the one being watched starts on the crawled-time axis: their time
 * spent crawling. The run being watched is the one this sidecar started (`liveFrom`), which the engine's own start of
 * it follows by a moment: a run that began before it is an earlier one, though it never ended (its process died), and
 * the run being watched may not be in the file yet when the history is read. A crawl the page sees at rest has no
 * run being watched (`liveFrom` null): every run is an earlier one.
 */
function earlierRuns(runs: readonly CrawlRun[], liveFrom: number): { earlier: readonly CrawlRun[]; offset: number } {
  const earlier = Number.isNaN(liveFrom) ? runs : runs.filter((run) => !(Date.parse(run.startedAt) >= liveFrom));
  return { earlier, offset: earlier.reduce((sum, run) => sum + run.crawlingMs, 0) };
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
  // E3c: the runs' times, so the earlier runs go on the same axis as this one.
  const runs = history?.runs ?? null;
  const { earlier, offset } = runs ? earlierRuns(runs, liveFrom) : { earlier: [], offset: 0 };
  // One clock, Elapsed's: the time spent crawling over every run. A trail point the engine timed (`crawlingMs`) sits
  // there; one it didn't, at the run's own clock after the earlier runs. The run's clock counts a pause from before the
  // page first heard of the run (a page loaded again part-way), where the engine's doesn't (round 5's polish: the chart
  // said "over 2 min 21 s" beside Elapsed 30 s).
  const placeOf = (point: TrailPoint) => point.crawlingMs ?? offset + point.runMs;
  const takeover = trail[0];
  const takeoverAt = takeover ? placeOf(takeover) : null;
  // The run being watched, once the file has it: its points go by its stretches of crawling, as the earlier runs' do,
  // not by the wall time since it began, which runs on through a pause.
  const watchedStored = runs !== null && runs.length > earlier.length;
  let continued = false;
  for (const point of history?.points ?? []) {
    const at = Date.parse(point.at);
    if (Number.isNaN(at)) continue;
    // Counted back from the file: an earlier run's (or, for a crawl not live here, all of it).
    if (Number.isNaN(liveFrom) || at < liveFrom) {
      continued ||= point.internalFetched > 0;
      if (runs) {
        // Where the earlier runs' crawling had got to by then, never past where this run starts.
        const elapsedMs = Math.min(offset, crawlingAt(at, earlier));
        points.push({ elapsedMs, found: point.internalFound, crawled: point.internalFetched });
      }
      continue;
    }
    if (takeover && at >= takeover.at) break;
    const elapsedMs = watchedStored ? crawlingAt(at, runs) : offset + (point.elapsedMs ?? at - liveFrom);
    // Never past where the trail takes over, which may know of a pause the history can't tell.
    if (takeoverAt !== null && elapsedMs >= takeoverAt) continue;
    points.push({ elapsedMs, found: point.internalFound, crawled: point.internalFetched });
  }
  for (const point of trail) {
    points.push({ elapsedMs: placeOf(point), found: point.internalFound, crawled: point.internalFetched });
  }
  const last = points.at(-1);
  if (!last) return null;
  // Each later run's start: the crawling time of the runs before it. Every run is one: the earlier ones, and the one
  // being watched, which starts at the offset.
  const watching = runs !== null && (runs.length > earlier.length || !Number.isNaN(liveFrom));
  const all = runs ? [...earlier, ...(watching ? [null] : [])] : [];
  const runStarts = runs
    ? all.slice(1).map((_, index) => earlier.slice(0, index + 1).reduce((sum, run) => sum + run.crawlingMs, 0))
    : null;
  return {
    points,
    spanMs: last.elapsedMs,
    found: last.found,
    crawled: last.crawled,
    continued: continued || all.length > 1,
    runStarts,
    offsetMs: offset,
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
