// Simple's results over a sidecar of the test's own: the catalog with its plain words and the garden crawl's analysis
// with its score, as today's engine answers them (src/test/simple/*.json, captured from the sidecar on the garden
// crawl analysed by engine 2.0.0-alpha.1), and the states made from them that no fixture holds: perfect, nothing but
// things worth a look, stopped early, a serious fault of the whole site, no score.
import type { GridPage, IssueGroup, IssueGroupPage, IssueRow } from '../api/grids';
import type { Analysis, Checks, CrawlListItem, Score, ScoreLine } from '../api/types';
import { json, problem, testCrawlItem, type FakeRoute } from './fakeSidecar';
import engineChecks from './simple/engine-checks.json';
import gardenAnalysis from './simple/garden-analysis.json';

/** The catalog of 87 checks with their plain words, as `GET /api/checks` answers. */
export const SIMPLE_CHECKS = engineChecks as Checks;

/** The garden crawl's analysis: 85, Good; 2 serious, 4 worth fixing, 13 worth a look; 4 skipped. */
export const GARDEN_ANALYSIS = gardenAnalysis as Analysis;

/** The garden crawl's library item, as the Summary reads it. */
export function gardenItem(overrides: Partial<CrawlListItem> = {}): CrawlListItem {
  return testCrawlItem({
    site: 'gardenfurnituresets.co.uk',
    startUrl: 'https://www.gardenfurnituresets.co.uk/',
    status: 'completed',
    startedAt: '2026-10-31T14:13:30Z',
    finishedAt: '2026-10-31T14:51:00Z',
    analyzedAt: GARDEN_ANALYSIS.analyzedAt,
    crawlingMs: 2_245_000,
    pageCount: 265,
    fetchedUrlCount: 4_521,
    crawledUrlCount: 4_521,
    issues: { error: 2, warning: 4, notice: 13 },
    score: { value: 85, band: 'good', partial: false },
    outOfDate: false,
    ...overrides,
  });
}

function scoreWith(lines: ScoreLine[], changes: Partial<Score> = {}): Score {
  const base = GARDEN_ANALYSIS.score!;
  return { ...base, lines, ...changes };
}

/** The garden analysis with only the given checks finding something, at the given counts. */
export function analysisWith(
  found: Record<string, number>,
  score: Partial<Score> | null,
  changes: Partial<Analysis> = {},
): Analysis {
  const counts = GARDEN_ANALYSIS.counts.map((count) => ({ ...count, count: found[count.checkId] ?? 0 }));
  const fixFirst = GARDEN_ANALYSIS.fixFirst?.filter((id) => (found[id] ?? 0) > 0) ?? [];
  const lines = (GARDEN_ANALYSIS.score!).lines.filter((line) => (found[line.checkId] ?? 0) > 0);
  return {
    ...GARDEN_ANALYSIS,
    counts,
    fixFirst,
    score: score === null ? null : scoreWith(lines, score),
    noScoreReason: score === null ? 'noPages' : null,
    ...changes,
  };
}

/** Every check found nothing: 100, Very good. */
export const PERFECT_ANALYSIS = analysisWith(
  {},
  { value: 100, band: 'veryGood', lost: 0, beforeCeiling: 100, ifFixed: { checkIds: [], value: 100 } },
);

/** Only things worth a look: 100, nothing to fix. */
export const NOTICES_ANALYSIS = analysisWith(
  { 'empty-anchor-text': 265, 'image-missing-dimensions': 230, 'description-too-long': 210 },
  { value: 100, band: 'veryGood', lost: 0, beforeCeiling: 100, ifFixed: { checkIds: [], value: 100 } },
);

/** The garden crawl stopped early: the same problems, a partial score of what it read. */
export const STOPPED_ANALYSIS: Analysis = {
  ...GARDEN_ANALYSIS,
  isPartial: true,
  score: {
    ...(GARDEN_ANALYSIS.score!),
    partial: true,
    covers: { ...(GARDEN_ANALYSIS.score!).covers, complete: false, reason: 'stopped' },
  },
};

/** No HTTPS, a serious fault of the whole site, leads: 40 at best. */
export const SITE_WIDE_ANALYSIS: Analysis = (() => {
  const analysis = analysisWith(
    { 'no-https': 1, 'broken-internal-links': 5 },
    {
      value: 36,
      band: 'poor',
      lost: 64,
      beforeCeiling: 36,
      ifFixed: { checkIds: ['no-https', 'broken-internal-links'], value: 100 },
    },
  );
  const lines: ScoreLine[] = [
    { checkId: 'no-https', severity: 'error', count: 1, of: 4_521, unit: 'site', extent: 'site', points: 60 },
    {
      checkId: 'broken-internal-links',
      severity: 'error',
      count: 5,
      of: 265,
      unit: 'pages',
      extent: 'some',
      points: 4,
    },
  ];
  return {
    ...analysis,
    fixFirst: ['no-https', 'broken-internal-links'],
    score: { ...(analysis.score!), lines },
  };
})();

/** An analysis that read no page. */
export const NO_PAGES_ANALYSIS: Analysis = {
  ...analysisWith({}, null),
  pagesAnalyzed: 0,
  noScoreReason: 'noPages',
};

/** A check's groups, as `/issues/{check}/groups` answers. */
export function groupPage(checkId: string, groups: IssueGroup[], total: number): IssueGroupPage {
  return {
    checkId,
    grouping: groups.length > 0 && groups[0]?.detail === null ? 'relatedUrl' : 'detail',
    total,
    groupCount: groups.length,
    offset: 0,
    groups,
  };
}

/** A check's rows, as `/issues/{check}/rows` answers. */
export function rowsPage(rows: IssueRow[], total: number): GridPage<IssueRow> {
  return {
    columns: ['id', 'url', 'title', 'detail', 'relatedUrl'],
    rows,
    offset: 0,
    matching: total,
    total,
    next: null,
    summary: { available: true, reason: null },
    unavailable: [],
  };
}

/** The routes Simple's screens read: the library, the item, the catalog, the analysis, and no groups by default. */
export function simpleRoutes(item: CrawlListItem, analysis: Analysis | 'not-analyzed'): FakeRoute[] {
  return [
    ['GET', /^\/api\/crawls$/, () => json([item])],
    ['GET', /^\/api\/crawls\/([0-9a-f]{16})$/, () => json(item)],
    ['GET', /^\/api\/checks/, () => json(SIMPLE_CHECKS)],
    [
      'GET',
      /^\/api\/crawls\/[0-9a-f]{16}\/analysis$/,
      () =>
        analysis === 'not-analyzed'
          ? problem({ type: 'not-analyzed', title: 'This crawl hasn’t been analysed.', status: 404 })
          : json(analysis),
    ],
    ['GET', /^\/api\/crawls\/[0-9a-f]{16}\/alerts$/, () => json([])],
    [
      'GET',
      /^\/api\/crawls\/[0-9a-f]{16}\/stats$/,
      () => json({ queuedCount: 1_380, urlCount: item.fetchedUrlCount + 1_380 }),
    ],
    [
      'POST',
      /^\/api\/crawls\/[0-9a-f]{16}\/issues\/[^/]+\/groups$/,
      (_request, match) => json(groupPage(match[0].split('/')[5] ?? '', [], 0)),
    ],
    [
      'GET',
      /^\/api\/health$/,
      () => json({ version: 'test', engineVersion: 'test', dataDir: 'C:\\data', stateDir: 'C:\\state', pid: 1 }),
    ],
  ];
}
