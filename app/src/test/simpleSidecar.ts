// Simple's results over a sidecar of the test's own: the catalog with its plain words and the garden crawl's analysis
// with its score, as today's engine answers them (src/test/simple/*.json, captured from the sidecar on the garden
// crawl analysed by engine 2.0.0-alpha.1), and the states made from them that no fixture holds: perfect, nothing but
// things worth a look, stopped early, a serious fault of the whole site, no score.
import type { CheckGroupRef, GridPage, IssueGroup, IssueGroupPage, IssueRow } from '../api/grids';
import type { Analysis, Checks, CrawlListItem, Score, ScoreLine } from '../api/types';
import { json, problem, testCrawlItem, type FakeRoute } from './fakeSidecar';
import engineChecks from './simple/engine-checks.json';
import gardenAnalysis from './simple/garden-analysis.json';

/** The catalog of 87 checks with their plain words, as `GET /api/checks` answers. */
export const SIMPLE_CHECKS = engineChecks as Checks;

/** The garden crawl's analysis: 87, Good; 2 serious, 3 worth fixing, 14 worth a look; 4 skipped. */
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
    issues: { error: 2, warning: 3, notice: 14 },
    score: { value: 87, band: 'good', partial: false },
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
  const lines = GARDEN_ANALYSIS.score!.lines.filter((line) => (found[line.checkId] ?? 0) > 0);
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
    ...GARDEN_ANALYSIS.score!,
    partial: true,
    covers: { ...GARDEN_ANALYSIS.score!.covers, complete: false, reason: 'stopped' },
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
    score: { ...analysis.score!, lines },
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

const SITE = 'https://www.gardenfurnituresets.co.uk';

/** A page as a check's rows carry it. */
export function issuePage(id: number, path: string, title: string | null, related: string | null = null): IssueRow {
  return { id, url: `${SITE}${path}`, title, detail: null, relatedUrl: related };
}

/** The template fault: the logo's link to the home page, on 264 pages. */
export const HOME_GROUP: IssueGroup = {
  detail: null,
  relatedUrlId: 1,
  relatedUrl: `${SITE}/`,
  pages: 264,
  urls: 264,
  share: 264 / 265,
  likelyTemplate: true,
};

const linkGroup = (id: number, path: string): IssueGroup => ({
  detail: null,
  relatedUrlId: id,
  relatedUrl: `${SITE}${path}`,
  pages: 1,
  urls: 1,
  share: 1 / 265,
  likelyTemplate: false,
});

const BROKEN = ['/edale-4-seater-sofa-set/', '/berlin-cushion-box/', '/wentworth-cube-dining-set/'];

/**
 * Groups and rows for the problems the stories and tests open: `empty-anchor-text` (a template fault),
 * `broken-internal-links` (broken addresses, each with its 404), `image-too-large` (126 causes, more than a page) and
 * `slow-response` (a check that doesn't group, with what was measured).
 */
export function problemRoutes(): FakeRoute[] {
  const many = Array.from({ length: 126 }, (_, index) => linkGroup(1_000 + index, `/media/${index}/photo.jpg`));
  return [
    [
      'POST',
      /\/issues\/empty-anchor-text\/groups$/,
      () => json(groupPage('empty-anchor-text', [HOME_GROUP, linkGroup(28, '/greenhouses/')], 265)),
    ],
    [
      'POST',
      /\/issues\/empty-anchor-text\/rows$/,
      (request) => {
        const body = request.body as { group?: CheckGroupRef; limit: number };
        if (body.group?.relatedUrlId === 28) {
          return json(rowsPage([issuePage(1, '/', 'Home', `${SITE}/greenhouses/`)], 1));
        }
        const rows = Array.from({ length: Math.min(body.limit, 264) }, (_, index) =>
          issuePage(index + 2, `/page-${index}/`, `Page ${index}`, `${SITE}/`),
        );
        return json(rowsPage(rows, 264));
      },
    ],
    [
      'POST',
      /\/issues\/broken-internal-links\/groups$/,
      () =>
        json(
          groupPage(
            'broken-internal-links',
            BROKEN.map((path, index) => linkGroup(40 + index, path)),
            5,
          ),
        ),
    ],
    [
      'POST',
      /\/issues\/broken-internal-links\/rows$/,
      (request) => {
        const body = request.body as { group?: CheckGroupRef };
        const index = (body.group?.relatedUrlId ?? 40) - 40;
        return json(
          rowsPage(
            [
              {
                ...issuePage(
                  10 + index,
                  '/blog/best-rattan-patio-sets/',
                  'Best Rattan Patio Sets - Hand Picked by Experts',
                ),
                relatedUrl: `${SITE}${BROKEN[index] ?? '/'}`,
                relatedStatusCode: 404,
                detail: '1 broken URL',
              },
            ],
            1,
          ),
        );
      },
    ],
    [
      'POST',
      /\/issues\/image-too-large\/groups$/,
      (request) => {
        const body = request.body as { offset: number; limit: number };
        return json({
          ...groupPage('image-too-large', many.slice(body.offset, body.offset + body.limit), 61),
          groupCount: 126,
        });
      },
    ],
    [
      'POST',
      /\/issues\/image-too-large\/rows$/,
      () => json(rowsPage([issuePage(5, '/monaco-2-seat-bistro-set/', 'Monaco 2 Seat Bistro Set')], 1)),
    ],
    [
      'POST',
      /\/issues\/slow-response\/rows$/,
      () =>
        json(
          rowsPage(
            [
              { ...issuePage(99, '/login/?register=true', 'Login'), detail: '5,476 ms (first byte after 5,475 ms)' },
              {
                ...issuePage(202, '/burford-natural-table/', 'Burford Dining Table'),
                detail: '5,217 ms (first byte after 5,214 ms)',
              },
              { ...issuePage(7, '/contact-us/', null), detail: '1,204 ms' },
            ],
            3,
          ),
        ),
    ],
    // The broken addresses themselves, with where each is linked from: three of four from the same footer.
    [
      'POST',
      /\/issues\/internal-client-error\/groups$/,
      () =>
        json(
          groupPage(
            'internal-client-error',
            [
              {
                detail: '404 Not Found',
                relatedUrlId: null,
                relatedUrl: null,
                pages: 4,
                urls: 4,
                share: 1,
                likelyTemplate: false,
              },
            ],
            4,
          ),
        ),
    ],
    [
      'POST',
      /\/issues\/internal-client-error\/rows$/,
      (request) => {
        const body = request.body as { limit: number };
        const rows = BROKEN_ADDRESSES.slice(0, body.limit).map((path, index) => ({
          ...issuePage(60 + index, path, null),
          detail: '404 Not Found',
        }));
        return json(rowsPage(rows, BROKEN_ADDRESSES.length));
      },
    ],
    [
      'POST',
      /\/urls\/found-on$/,
      (request) => {
        const { urlIds } = request.body as { urlIds: number[] };
        return json({ urls: urlIds.filter((id) => id >= 60 && id < 60 + BROKEN_ADDRESSES.length).map(brokenFoundOn) });
      },
    ],
    // The logo's link to the home page, in every page's header.
    [
      'POST',
      /\/issues\/empty-anchor-text\/targets$/,
      (request) => {
        const { urlIds } = request.body as { urlIds: number[] };
        return json({
          checkId: 'empty-anchor-text',
          urls: urlIds.map((urlId) => ({
            urlId,
            total: 1,
            targets: [
              {
                id: 1,
                url: `${SITE}/`,
                statusCode: 200,
                fetched: true,
                outcome: 'success',
                kind: 'hyperlink',
                text: null,
                position: 'header',
              },
            ],
          })),
        });
      },
    ],
  ];
}

/** Broken addresses a check of addresses flags, ids 60 on. */
export const BROKEN_ADDRESSES = [
  '/outdoor-pe-rattan-dining-set-with-fire-pit-table/',
  '/monaco-corner-sofa/',
  '/old-range/',
  '/gone-for-good/',
];

/** Where each broken address is found: the first three in /garden-furniture/'s footer (and on other pages), the last in the sitemap only. */
function brokenFoundOn(urlId: number) {
  const index = urlId - 60;
  const link = (id: number, path: string, position: string, text: string | null) => ({
    id,
    url: `${SITE}${path}`,
    kind: 'hyperlink',
    text,
    position,
    flags: [],
    followed: true,
    fragment: null,
    statusCode: 200,
    indexable: true,
    fetched: true,
    outcome: 'success',
  });
  const last = index === BROKEN_ADDRESSES.length - 1;
  return {
    urlId,
    links: last ? 0 : index + 1,
    pages: last ? 0 : index + 1,
    pagesExact: true,
    sources: last
      ? []
      : [
          link(300, '/garden-furniture/', 'footer', 'Our range'),
          link(301, '/sofas/', 'content', null),
          link(302, '/blog/', 'content', null),
        ].slice(0, index + 1),
    sitemaps: last ? { total: 1, sitemaps: [{ id: 1, url: `${SITE}/sitemap.xml` }] } : { total: 0, sitemaps: [] },
    start: false,
  };
}
