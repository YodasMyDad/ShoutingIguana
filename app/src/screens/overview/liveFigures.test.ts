import { describe, expect, it } from 'vitest';
import type { TrailPoint } from '../../live/liveStore';
import { countTicks, progressSeries, timeTicks } from './foundAndCrawled';
import {
  PACE_MIN_WINDOW_MS,
  PACE_WINDOW_MS,
  SPARK_POINTS,
  activityWords,
  foundSplit,
  paceOf,
  sparkOf,
  timeLeft,
} from './liveFigures';

/** A trail at a steady pace: a point every half second, `perSecond` of the site’s URLs fetched each second. */
function steady(seconds: number, perSecond: number, from = 0): TrailPoint[] {
  return Array.from({ length: seconds * 2 + 1 }, (_, index) => ({
    at: 1_000_000 + index * 500,
    fetched: from + (index * perSecond) / 2,
    internalFound: 50_000,
    internalFetched: from + (index * perSecond) / 2,
  }));
}

describe('the pace', () => {
  it('is the site’s URLs fetched over the last minute', () => {
    expect(paceOf(steady(120, 10))).toBeCloseTo(10 / 1000);
    // Fast for a minute, then slow for a minute: only the last minute counts.
    const slowed = [...steady(60, 100), ...steady(60, 2, 6_000).map((point) => ({ ...point, at: point.at + 60_000 }))];
    expect(paceOf(slowed)).toBeCloseTo(2 / 1000);
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
    const from = trail.find((point) => point.at >= (newest?.at ?? 0) - PACE_WINDOW_MS);
    expect((newest?.at ?? 0) - (from?.at ?? 0)).toBe(PACE_WINDOW_MS);
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

  it('is never less than nothing', () => {
    expect(timeLeft('running', { maxUrls: 2_000, internalFound: 2_000, internalFetched: fetched }, trail)?.ms).toBe(0);
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
    expect(foundSplit({ discovered: 5_722, fetched: 2_713, queued: 1_415, inFlight: 2, skipped: 1_592 })).toEqual({
      found: 5_722,
      crawled: 2_713,
      queued: 1_417,
      notFollowed: 1_592,
    });
  });

  it('isn’t split when a part isn’t known', () => {
    expect(foundSplit({ discovered: 5_722, fetched: null, queued: 1_415, inFlight: 2, skipped: 1_592 })).toBeNull();
    expect(foundSplit(null)).toBeNull();
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
    { at: at(1_000), fetched: 9, internalFound: 90, internalFetched: 8 },
    { at: at(1_500), fetched: 20, internalFound: 130, internalFetched: 18 },
  ];

  it('is the history, then the trail from where the history ends, from the crawl’s start', () => {
    expect(progressSeries(history, trail)).toEqual({
      points: [
        { elapsedMs: 500, found: 40, crawled: 1 },
        { elapsedMs: 1_000, found: 90, crawled: 8 },
        { elapsedMs: 1_500, found: 130, crawled: 18 },
      ],
      spanMs: 1_500,
      found: 130,
      crawled: 18,
    });
  });

  it('is the trail alone before the history arrives, and nothing before either', () => {
    expect(progressSeries(undefined, trail)?.points).toEqual([
      { elapsedMs: 0, found: 90, crawled: 8 },
      { elapsedMs: 500, found: 130, crawled: 18 },
    ]);
    expect(progressSeries(undefined, [])).toBeNull();
    expect(progressSeries({ ...history, points: [] }, [])).toBeNull();
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
