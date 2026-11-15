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

  it('counts a run whose process died among the earlier ones: the run being watched is the one begun from liveFrom', () => {
    // A run of 100 s that ended, one of 60 s cut short (its end never stored), then this run, continued a day later.
    const cut: CrawlHistory = {
      crawlId: 'a',
      startedAt: iso(T0),
      liveFrom: iso(T0 + 2 * DAY),
      points: [point(T0 + 2 * DAY, 300)],
      crawlingMs: 160_000,
      runs: [
        {
          startedAt: iso(T0),
          endedAt: iso(T0 + 100_000),
          crawlingMs: 100_000,
          crawling: [{ from: iso(T0), to: iso(T0 + 100_000) }],
        },
        {
          startedAt: iso(T0 + DAY),
          endedAt: null,
          crawlingMs: 60_000,
          crawling: [{ from: iso(T0 + DAY), to: iso(T0 + DAY + 60_000) }],
        },
      ],
    };
    const series = progressSeries(cut, [
      { at: T0 + 2 * DAY + 1_000, runMs: 1_000, fetched: 301, internalFetched: 301, internalFound: 500 },
    ]);
    expect(series?.offsetMs).toBe(160_000);
    expect(series?.runStarts).toEqual([100_000, 160_000]);
    expect(series?.spanMs).toBe(161_000);
    // Once the engine has stored the run being watched (it began a moment after liveFrom), the same.
    const stored = progressSeries(
      {
        ...cut,
        runs: [...(cut.runs ?? []), { startedAt: iso(T0 + 2 * DAY + 50), endedAt: null, crawlingMs: 0, crawling: [] }],
      },
      [{ at: T0 + 2 * DAY + 1_000, runMs: 1_000, fetched: 301, internalFetched: 301, internalFound: 500 }],
    );
    expect([stored?.offsetMs, stored?.runStarts]).toEqual([160_000, [100_000, 160_000]]);
  });

  // Round 5's polish: a crawl paused for minutes, Overview opened on it after: the history's points came at the wall
  // time since the run began, so the chart said "over 3 min 53 s" beside Elapsed 13 s, the pause a long flat line.
  it('places the run being watched by its time spent crawling too: a pause before the screen opened takes no room', () => {
    const start = T0 + 2 * DAY;
    const watched: CrawlRun = {
      startedAt: iso(start + 20),
      endedAt: null,
      crawlingMs: 13_000,
      crawling: [
        { from: iso(start + 20), to: iso(start + 5_020) },
        { from: iso(start + 225_000), to: iso(start + 233_000) },
      ],
    };
    const points = [0, 2_000, 5_000, 60_000, 120_000, 224_000, 228_000, 233_000].map((ms, index) => ({
      ...point(start + ms, index * 10),
      elapsedMs: ms,
    }));
    const series = progressSeries(
      { crawlId: 'a', startedAt: iso(start), liveFrom: iso(start), points, crawlingMs: 13_000, runs: [watched] },
      [{ at: start + 234_000, runMs: 14_000, fetched: 90, internalFetched: 90, internalFound: 500 }],
    );
    expect(series?.spanMs).toBe(14_000);
    // The pause closes up: every point between its ends sits at 5 s.
    expect(series?.points.map((entry) => entry.elapsedMs)).toEqual([
      0, 1_980, 4_980, 5_000, 5_000, 5_000, 8_000, 13_000, 14_000,
    ]);
  });

  it('places the trail by the engine’s time spent crawling: a page loaded again part-way can’t know of a pause', () => {
    // The page loaded while the crawl was paused: its run clock began at the engine's wall time since the run began,
    // 2 min 11 s, where the engine had crawled 20 s. Elapsed shows the engine's.
    const start = T0 + 3 * DAY;
    const series = progressSeries({ crawlId: 'a', startedAt: iso(start), liveFrom: iso(start), points: [] }, [
      {
        at: start + 131_000,
        runMs: 131_000,
        crawlingMs: 20_000,
        fetched: 400,
        internalFetched: 400,
        internalFound: 900,
      },
      {
        at: start + 141_000,
        runMs: 141_000,
        crawlingMs: 30_000,
        fetched: 600,
        internalFetched: 600,
        internalFound: 950,
      },
    ]);
    expect(series?.spanMs).toBe(30_000);
    expect(series?.points.map((entry) => entry.elapsedMs)).toEqual([20_000, 30_000]);
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
