// The found-and-crawled chart's axis is time spent crawling: from E3c's `/history.runs`, every run of a continued crawl
// on it, the time between runs and the pauses closed up (docs/v2-sidecar-api.md, History, "Plotting crawled time").
import { describe, expect, it } from 'vitest';
import type { CrawlHistory, CrawlRun } from '../../api/types';
import { crawlingAt, progressSeries } from './foundAndCrawled';

const T0 = Date.parse('2026-10-29T10:00:00.000Z');
const DAY = 24 * 60 * 60_000;
const iso = (ms: number) => new Date(ms).toISOString();
const point = (at: number, crawled: number) => ({
  at: iso(at),
  fetched: crawled,
  htmlPages: crawled,
  internalFetched: crawled,
  internalFound: 500,
  errors: 0,
  blocked: 0,
});

// Two runs a day apart, at rest: the first crawled 2 min with a minute's pause in the middle, the second 1 min.
const RUNS: CrawlRun[] = [
  {
    startedAt: iso(T0),
    endedAt: iso(T0 + 180_000),
    crawlingMs: 120_000,
    crawling: [
      { from: iso(T0), to: iso(T0 + 60_000) },
      { from: iso(T0 + 120_000), to: iso(T0 + 180_000) },
    ],
  },
  {
    startedAt: iso(T0 + DAY),
    endedAt: iso(T0 + DAY + 60_000),
    crawlingMs: 60_000,
    crawling: [{ from: iso(T0 + DAY), to: iso(T0 + DAY + 60_000) }],
  },
];

describe('time spent crawling', () => {
  it('counts every stretch that ended by then and the part of the one it falls in', () => {
    expect(crawlingAt(T0, RUNS)).toBe(0);
    expect(crawlingAt(T0 + 30_000, RUNS)).toBe(30_000);
    // In the pause it stands where the stretch before it ended.
    expect(crawlingAt(T0 + 90_000, RUNS)).toBe(60_000);
    expect(crawlingAt(T0 + 150_000, RUNS)).toBe(90_000);
    // Between the runs, a day: it stands still.
    expect(crawlingAt(T0 + DAY / 2, RUNS)).toBe(120_000);
    expect(crawlingAt(T0 + DAY + 30_000, RUNS)).toBe(150_000);
    expect(crawlingAt(T0 + 2 * DAY, RUNS)).toBe(180_000);
  });
});

describe('the chart of a continued crawl', () => {
  const history: CrawlHistory = {
    crawlId: 'a',
    startedAt: iso(T0),
    liveFrom: null,
    points: [
      point(T0, 0),
      point(T0 + 60_000, 100),
      point(T0 + 90_000, 100),
      point(T0 + 180_000, 200),
      point(T0 + DAY + 60_000, 300),
    ],
    crawlingMs: 180_000,
    runs: RUNS,
  };

  it('places every run on the time spent crawling, so a day between runs takes no room', () => {
    const series = progressSeries(history, []);
    expect(series?.points.map((entry) => [entry.elapsedMs, entry.crawled])).toEqual([
      [0, 0],
      [60_000, 100],
      [60_000, 100],
      [120_000, 200],
      [180_000, 300],
    ]);
    expect(series?.spanMs).toBe(180_000);
    // The second run's start is marked where the first's crawling ended.
    expect(series?.runStarts).toEqual([120_000]);
    expect(series?.continued).toBe(true);
  });

  it('draws only the run being watched for an earlier engine, which kept no run times', () => {
    const { runs: _runs, crawlingMs: _ms, ...older } = history;
    const live = { ...older, liveFrom: iso(T0 + DAY) };
    const series = progressSeries(live, [
      { at: T0 + DAY + 60_000, runMs: 60_000, fetched: 300, internalFetched: 300, internalFound: 500 },
    ]);
    expect(series?.runStarts).toBeNull();
    expect(series?.offsetMs).toBe(0);
    expect(series?.points.map((entry) => entry.elapsedMs)).toEqual([60_000]);
    expect(series?.continued).toBe(true);
  });
});
