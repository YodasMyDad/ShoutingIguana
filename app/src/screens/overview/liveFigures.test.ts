import { describe, expect, it } from 'vitest';
import type { ProgressEvent } from '../../api/types';
import { applyEvent, liveProgressOf, type LiveCrawl, type LiveState, type TrailPoint } from '../../live/liveStore';
import { countTicks, progressSeries, timeTicks } from './foundAndCrawled';
import {
  PACE_MIN_WINDOW_MS,
  PACE_WINDOW_MS,
  SPARK_POINTS,
  activityWords,
  atUrlLimit,
  crawledSoFar,
  foundSplit,
  paceOf,
  sparkOf,
  timeLeft,
} from './liveFigures';

/**
 * A crawl at a steady pace, as its points on the run's clock: one every half second, `perSecond` of the site’s URLs
 * fetched each second.
 */
function steady(seconds: number, perSecond: number, from = 0): TrailPoint[] {
  return Array.from({ length: seconds * 2 + 1 }, (_, index) => ({
    at: 1_000_000 + index * 500,
    runMs: index * 500,
    fetched: from + (index * perSecond) / 2,
    internalFound: 50_000,
    internalFetched: from + (index * perSecond) / 2,
  }));
}

const START = Date.parse('2026-11-10T09:00:00.000Z');

/** A progress event `wallMs` after the crawl started, as the engine sends it: `elapsedMs` is the run's wall time. */
function event(wallMs: number, extra: Partial<ProgressEvent>): ProgressEvent {
  return {
    crawlId: 'a',
    status: 'running',
    elapsedMs: wallMs,
    discovered: 0,
    queued: 0,
    inFlight: 0,
    fetched: 0,
    htmlPages: 0,
    redirects: 0,
    errors: 0,
    blocked: 0,
    robotsDisallowed: 0,
    skipped: 0,
    bytesDownloaded: 0,
    urlsPerSecond: 0,
    at: new Date(START + wallMs).toISOString(),
    phase: 'crawling',
    internalFound: 50_000,
    internalFetched: 0,
    maxUrls: 50_000,
    ...extra,
  };
}

/** The live store's crawl after these progress events, as the stream brings them. */
function heard(events: readonly ProgressEvent[], from?: LiveState): { state: LiveState; crawl: LiveCrawl } {
  let state: LiveState = from ?? { stream: 'open', live: [], crawls: {} };
  for (const progress of events) state = applyEvent(state, { type: 'progress', data: liveProgressOf(progress) });
  const crawl = state.crawls.a;
  if (!crawl) throw new Error('no crawl');
  return { state, crawl };
}

describe('the pace', () => {
  it('is the site’s URLs fetched over the last minute', () => {
    expect(paceOf(steady(120, 10))).toBeCloseTo(10 / 1000);
    // Fast for a minute, then slow for a minute: only the last minute counts.
    const slowed = [
      ...steady(60, 100),
      ...steady(60, 2, 6_000).map((point) => ({ ...point, at: point.at + 60_000, runMs: point.runMs + 60_000 })),
    ];
    expect(paceOf(slowed)).toBeCloseTo(2 / 1000);
  });

  it('is of the time spent crawling: a pause is no part of it, though progress keeps arriving through one', () => {
    // 100 URLs a second for a minute, 45 s paused (an event every half second all the while, nothing fetched), then on.
    const running = (wallMs: number, crawledMs: number) => event(wallMs, { internalFetched: crawledMs / 10 });
    const before = Array.from({ length: 121 }, (_, index) => running(index * 500, index * 500));
    const paused = Array.from({ length: 90 }, (_, index) =>
      event(60_500 + index * 500, { status: 'paused', internalFetched: 6_000 }),
    );
    const after = Array.from({ length: 4 }, (_, index) => running(105_500 + index * 500, 60_000 + index * 500));
    const { crawl } = heard([...before, ...paused, ...after]);
    // By the wall clock the last minute fetched 15 s' worth: the pace would read 25 a second, and the time left four
    // times too long.
    // (Within the half second either side of the pause, which the stream can't place.)
    expect(paceOf(crawl.pace)).toBeGreaterThan(98 / 1000);
    expect(paceOf(crawl.pace)).toBeLessThan(102 / 1000);
    const left = timeLeft('running', crawl.progress, crawl.pace);
    const truly = ((50_000 - (crawl.progress?.internalFetched ?? 0)) / 100) * 1000;
    expect(left?.ms).toBeGreaterThan(truly * 0.95);
    expect(left?.ms).toBeLessThan(truly * 1.05);
  });

  it('is as exact twelve hours in as at the start: its samples aren’t thinned as the chart’s points are', () => {
    // A polite crawl with a limit: 2 URLs a second, an event every half second. The chart's trail keeps one point in
    // 256 by then, more than two minutes apart, and a pace read from it came and went.
    let state: LiveState | undefined;
    let without = 0;
    const total = 12 * 3600 * 2;
    for (let index = 0; index < total; index++) {
      const now = heard(
        [event(index * 500, { internalFetched: index, maxUrls: 1_000_000, internalFound: 1_000_000 })],
        state,
      );
      state = now.state;
      if (index > 20 && paceOf(now.crawl.pace) === null) without++;
    }
    expect(without).toBe(0);
    const crawl = state?.crawls.a;
    expect(crawl?.trail.every).toBeGreaterThan(100);
    expect(paceOf(crawl?.pace ?? [])).toBeCloseTo(2 / 1000, 6);
    // And they stay a minute's worth, however long it runs.
    expect(crawl?.pace.length).toBeLessThanOrEqual(122);
  });

  it('isn’t known before enough of the crawl has been seen, or while nothing moves', () => {
    expect(paceOf([])).toBeNull();
    expect(paceOf(steady(1, 10))).toBeNull();
    expect(paceOf(steady(PACE_MIN_WINDOW_MS / 1000, 10))).not.toBeNull();
    expect(paceOf(steady(30, 0))).toBeNull();
  });

  it('looks no further back than its window', () => {
    const trail = steady(600, 4);
    const newest = trail.at(-1);
    const from = trail.find((point) => point.runMs >= (newest?.runMs ?? 0) - PACE_WINDOW_MS);
    expect((newest?.runMs ?? 0) - (from?.runMs ?? 0)).toBe(PACE_WINDOW_MS);
  });

  it('starts again when the crawl is continued, with the run', () => {
    const first = Array.from({ length: 21 }, (_, index) => event(index * 500, { internalFetched: index * 50 }));
    const stopped = event(10_010, { status: 'stopped', phase: 'finishing', internalFetched: 1_000 });
    // Continued three days later, more slowly: the engine's `elapsedMs` starts again.
    const later = 3 * 86_400_000;
    const second = Array.from({ length: 21 }, (_, index) => ({
      ...event(later + index * 500, { internalFetched: 1_000 + index * 5 }),
      elapsedMs: index * 500,
    }));
    const { crawl } = heard([...first, stopped, ...second]);
    expect(paceOf(crawl.pace)).toBeCloseTo(10 / 1000, 6);
    expect(crawl.pace[0]).toEqual({ runMs: 0, internalFetched: 1_000 });
  });
});

describe('the time left', () => {
  const trail = steady(60, 2, 2_593); // 2 a second, 2,713 fetched now
  const fetched = trail.at(-1)?.internalFetched ?? 0;

  it('is the longest the crawl can take while it may still find fewer URLs than its limit', () => {
    const left = timeLeft('running', { maxUrls: 10_000, internalFound: 5_722, internalFetched: fetched }, trail);
    expect(left?.bound).toBe('within');
    // 7,287 to go at 2 a second.
    expect(left?.ms).toBeCloseTo(((10_000 - 2_713) / 2) * 1000);
  });

  it('is what’s left once the crawl has found as many URLs as its limit allows', () => {
    const left = timeLeft('running', { maxUrls: 4_000, internalFound: 4_000, internalFetched: fetched }, trail);
    expect(left).toEqual({ ms: ((4_000 - 2_713) / 2) * 1000, bound: 'about' });
  });

  it('is nothing without a URL limit: the crawl ends when it runs out of URLs', () => {
    expect(timeLeft('running', { maxUrls: null, internalFound: 5_722, internalFetched: fetched }, trail)).toBeNull();
    expect(
      timeLeft('running', { maxUrls: undefined, internalFound: 5_722, internalFetched: fetched }, trail),
    ).toBeNull();
  });

  it('is nothing while the crawl is paused or stopping, before there’s a pace, or when the counts aren’t known', () => {
    const progress = { maxUrls: 10_000, internalFound: 5_722, internalFetched: fetched };
    expect(timeLeft('paused', progress, trail)).toBeNull();
    expect(timeLeft('stopping', progress, trail)).toBeNull();
    expect(timeLeft('running', progress, steady(1, 2))).toBeNull();
    expect(timeLeft('running', { ...progress, internalFetched: null }, trail)).toBeNull();
    expect(timeLeft('running', null, trail)).toBeNull();
  });

  it('is nothing once the limit is reached: not “about 0 s left” while the last requests settle', () => {
    expect(timeLeft('running', { maxUrls: 2_713, internalFound: 2_713, internalFetched: fetched }, trail)).toBeNull();
    expect(timeLeft('running', { maxUrls: 2_000, internalFound: 2_000, internalFetched: fetched }, trail)).toBeNull();
    expect(atUrlLimit({ maxUrls: 2_713, internalFetched: 2_713 })).toBe(true);
    expect(atUrlLimit({ maxUrls: 2_713, internalFetched: 2_712 })).toBe(false);
    expect(atUrlLimit({ maxUrls: null, internalFetched: 2_713 })).toBe(false);
    expect(atUrlLimit({ maxUrls: undefined, internalFetched: 2_713 })).toBe(false);
    expect(atUrlLimit(null)).toBe(false);
    // One short of it, there still is.
    expect(timeLeft('running', { maxUrls: 2_714, internalFound: 2_714, internalFetched: fetched }, trail)).toEqual({
      ms: 500,
      bound: 'about',
    });
  });
});

describe('the sparkline', () => {
  it('draws a short history as it is', () => {
    const spark = sparkOf([1, 4, 2, 3]);
    expect(spark.values).toEqual([1, 4, 2, 3]);
    expect(spark.now).toBe(3);
    expect(spark.peak).toBe(4);
    expect(spark.spanMs).toBe(1_500);
  });

  it('averages a long history a stretch at a time, ending at the latest rate itself', () => {
    const history: number[] = Array.from({ length: 600 }, (_, index) => (index < 300 ? 10 : 20));
    history[599] = 55;
    const spark = sparkOf(history);
    expect(spark.values).toHaveLength(SPARK_POINTS);
    expect(spark.values[0]).toBe(10);
    expect(spark.values.at(-2)).toBe(20);
    expect(spark.values.at(-1)).toBe(55);
    expect(spark.peak).toBe(55);
    expect(spark.spanMs).toBe(599 * 500);
  });

  it('has nothing to say before the first rate', () => {
    expect(sparkOf([])).toEqual({ values: [], now: null, peak: null, spanMs: 0 });
  });
});

describe('what the crawl is doing', () => {
  it('is its phase, unless its status says more', () => {
    expect(activityWords('running', 'readingRobotsTxt')).toBe('Reading robots.txt');
    expect(activityWords('running', 'retrying')).toBe('Trying the URLs that didn’t answer once more');
    expect(activityWords('paused', 'crawling')).toBe('Paused');
    expect(activityWords('stopping', 'crawling')).toBe('Stopping');
    // An engine from before E4 doesn't say.
    expect(activityWords('running', null)).toBe('Crawling');
  });
});

describe('what was found', () => {
  it('splits into crawled, queued (with what’s on its way) and not followed', () => {
    expect(
      foundSplit({ discovered: 5_722, fetched: 2_713, queued: 1_415, inFlight: 2, skipped: 1_592, noResponse: null }),
    ).toEqual({
      found: 5_722,
      crawled: 2_713,
      queued: 1_417,
      notFollowed: 1_592,
      notChecked: 0,
    });
  });

  it('isn’t split when a part isn’t known', () => {
    expect(
      foundSplit({ discovered: 5_722, fetched: null, queued: 1_415, inFlight: 2, skipped: 1_592, noResponse: null }),
    ).toBeNull();
    expect(foundSplit(null)).toBeNull();
  });

  // The engine's test site with a dead twin host, at its last progress event: 6,914 outcomes stored, 5 of them URLs
  // never asked. The URL grid counts 6,909 as fetched.
  const reasons = { robotsDisallowed: 10, timeout: 0, networkError: 3, notChecked: 5, processingError: 0 };

  it('counts as crawled what the URL grid does: not the URLs that weren’t checked', () => {
    expect(crawledSoFar({ fetched: 6_914, noResponse: reasons })).toBe(6_909);
    expect(crawledSoFar({ fetched: 6_914, noResponse: null })).toBe(6_914);
    expect(crawledSoFar({ fetched: null, noResponse: reasons })).toBeNull();
    expect(crawledSoFar(null)).toBeNull();
  });

  it('splits the URLs that weren’t checked out of the crawled, so the parts still add up to what was found', () => {
    const split = foundSplit({
      discovered: 7_054,
      fetched: 6_914,
      queued: 0,
      inFlight: 0,
      skipped: 140,
      noResponse: reasons,
    });
    expect(split).toEqual({ found: 7_054, crawled: 6_909, queued: 0, notFollowed: 140, notChecked: 5 });
    expect((split?.crawled ?? 0) + (split?.queued ?? 0) + (split?.notFollowed ?? 0) + (split?.notChecked ?? 0)).toBe(
      split?.found,
    );
  });
});

describe('the found-and-crawled series', () => {
  const history = {
    crawlId: 'a',
    startedAt: '2026-11-01T14:00:00.000Z',
    liveFrom: '2026-11-01T14:00:00.000Z',
    points: [
      {
        at: '2026-11-01T14:00:00.500Z',
        fetched: 1,
        htmlPages: 1,
        internalFetched: 1,
        internalFound: 40,
        errors: 0,
        blocked: 0,
      },
      {
        at: '2026-11-01T14:00:01.000Z',
        fetched: 9,
        htmlPages: 5,
        internalFetched: 8,
        internalFound: 90,
        errors: 0,
        blocked: 0,
      },
    ],
  };
  const at = (ms: number) => Date.parse('2026-11-01T14:00:00.000Z') + ms;
  const trail: TrailPoint[] = [
    { at: at(1_000), runMs: 1_000, fetched: 9, internalFound: 90, internalFetched: 8 },
    { at: at(1_500), runMs: 1_500, fetched: 20, internalFound: 130, internalFetched: 18 },
  ];

  it('is the run’s history, then the trail from its first point, on the run’s clock', () => {
    expect(progressSeries(history, trail)).toEqual({
      points: [
        { elapsedMs: 500, found: 40, crawled: 1 },
        { elapsedMs: 1_000, found: 90, crawled: 8 },
        { elapsedMs: 1_500, found: 130, crawled: 18 },
      ],
      spanMs: 1_500,
      found: 130,
      crawled: 18,
      continued: false,
      runStarts: null,
      offsetMs: 0,
    });
  });

  it('is the trail alone before the history arrives, where the run’s clock puts it, and nothing before either', () => {
    expect(progressSeries(undefined, trail)?.points).toEqual([
      { elapsedMs: 1_000, found: 90, crawled: 8 },
      { elapsedMs: 1_500, found: 130, crawled: 18 },
    ]);
    expect(progressSeries(undefined, [])).toBeNull();
    expect(progressSeries({ ...history, points: [] }, [])).toBeNull();
  });

  it('is of the run being watched: a continued crawl’s earlier runs, days ago, are no part of its time', () => {
    // Ten minutes of crawling three days ago, as the file's points have it (no `elapsedMs`: they aren't of this run),
    // then this run, two minutes old. On one wall-clock axis this run was 0.05% of the chart.
    const first = Date.parse('2026-11-07T09:00:00.000Z');
    const liveFrom = first + 3 * 86_400_000;
    const iso = (ms: number) => new Date(ms).toISOString();
    const point = (ms: number, fetched: number) => ({
      at: iso(ms),
      fetched,
      htmlPages: fetched,
      internalFetched: fetched,
      internalFound: 5_000,
      errors: 0,
      blocked: 0,
    });
    const continued = {
      crawlId: 'c',
      startedAt: iso(first),
      liveFrom: iso(liveFrom),
      points: [
        ...Array.from({ length: 11 }, (_, index) => point(first + index * 60_000, index * 100)),
        { ...point(liveFrom, 1_000), elapsedMs: 0 },
        { ...point(liveFrom + 500, 1_005), elapsedMs: 500 },
      ],
    };
    const now: TrailPoint[] = Array.from({ length: 239 }, (_, index) => ({
      at: liveFrom + (index + 2) * 500,
      runMs: (index + 2) * 500,
      fetched: 1_010 + index * 5,
      internalFound: 5_000,
      internalFetched: 1_010 + index * 5,
    }));
    const series = progressSeries(continued, now);
    expect(series?.continued).toBe(true);
    // Two minutes, all of it this run: it starts at the counts the run started with.
    expect(series?.spanMs).toBe(120_000);
    expect(series?.points[0]).toEqual({ elapsedMs: 0, found: 5_000, crawled: 1_000 });
    expect(series?.points).toHaveLength(241);
    expect(
      series?.points.every((entry, index, all) => index === 0 || entry.elapsedMs > (all[index - 1]?.elapsedMs ?? 0)),
    ).toBe(true);
    // A history without `elapsedMs` on its live points is placed by where the run began.
    const bare = {
      ...continued,
      points: continued.points.map(({ at: when, ...rest }) => ({ ...rest, at: when, elapsedMs: undefined })),
    };
    expect(progressSeries(bare as typeof continued, [])?.points).toEqual([
      { elapsedMs: 0, found: 5_000, crawled: 1_000 },
      { elapsedMs: 500, found: 5_000, crawled: 1_005 },
    ]);
  });

  it('stands still through a pause the page saw, where the history alone would count it', () => {
    // The page has watched from 1 s in; the crawl was paused from 2 s to 47 s of the run's wall time. A history read
    // afterwards has points all through the pause, by wall time: the trail takes over from its own first point.
    const pausedHistory = {
      ...history,
      points: [
        { ...history.points[0]!, elapsedMs: 500 },
        ...[1_000, 2_000, 25_000, 47_000, 48_000].map((ms) => ({
          ...history.points[1]!,
          at: new Date(at(ms)).toISOString(),
          elapsedMs: ms,
        })),
      ],
    };
    const watched: TrailPoint[] = [
      { at: at(1_000), runMs: 1_000, fetched: 9, internalFound: 90, internalFetched: 8 },
      { at: at(2_000), runMs: 2_000, fetched: 20, internalFound: 130, internalFetched: 18 },
      { at: at(47_000), runMs: 2_000, fetched: 20, internalFound: 130, internalFetched: 18 },
      { at: at(48_000), runMs: 3_000, fetched: 30, internalFound: 150, internalFetched: 28 },
    ];
    const series = progressSeries(pausedHistory, watched);
    expect(series?.points.map((entry) => entry.elapsedMs)).toEqual([500, 1_000, 2_000, 2_000, 3_000]);
    expect(series?.spanMs).toBe(3_000);
  });
});

describe('the axes', () => {
  it('count in round steps, to at least the most there is', () => {
    expect(countTicks(5_722)).toEqual([0, 2_000, 4_000, 6_000]);
    expect(countTicks(112_577)).toEqual([0, 50_000, 100_000, 150_000]);
    expect(countTicks(7)).toEqual([0, 5, 10]);
    expect(countTicks(2)).toEqual([0, 1, 2]);
    expect(countTicks(0)).toEqual([0, 1]);
  });

  it('mark time in round steps, none past the end', () => {
    const minutes = (count: number) => count * 60_000;
    expect(timeTicks(minutes(22.4))).toEqual([0, minutes(5), minutes(10), minutes(15), minutes(20)]);
    expect(timeTicks(82_000)).toEqual([0, 30_000, 60_000]);
    expect(timeTicks(4_000)).toEqual([0, 1_000, 2_000, 3_000, 4_000]);
    expect(timeTicks(0)).toEqual([0]);
  });
});
