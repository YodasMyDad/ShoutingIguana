// A sidecar for the Issues screen's tests and stories: a small catalog over three categories, an analysis with a
// check of each kind (grouped by detail, by related URL, flat, skipped and passed), and groups and rows answered as
// the real `/groups` and `/rows` do (a group narrows the rows, `after` reads on, `detail` sorts).
import type { GridPage, GridQuery, IssueGroup, IssueRow } from '../api/grids';
import type { IssueTargetsOfUrl, LinkRow, UrlFoundOn } from '../api/inspector';
import type { Analysis, Check, CheckReferences, CheckSubject, Checks, CrawlListItem } from '../api/types';
import { json, problem, standardRoutes, testCrawlItem, type FakeRoute } from './fakeSidecar';

const check = (
  id: string,
  category: string,
  severity: Check['severity'],
  title: string,
  grouping: Check['grouping'],
  subject: CheckSubject,
  references: CheckReferences = 'none',
) =>
  ({
    id,
    category,
    severity,
    title,
    description: `What’s wrong with ${title}.`,
    why: `Why ${title} matters.`,
    fix: `How to fix ${title}.`,
    learnMore:
      id === 'empty-anchor-text' ? 'https://developers.google.com/search/docs/crawling-indexing/links-crawlable' : null,
    grouping,
    subject,
    references,
  }) satisfies Check;

export const ISSUES_CHECKS: Checks = {
  categories: [
    { id: 'responseCodes', title: 'Response codes' },
    { id: 'links', title: 'Links' },
    { id: 'titles', title: 'Titles' },
  ],
  checks: [
    // Each check's subject is the engine's own: the response codes flag any URL, the rest only pages.
    // And where to fix each, as the engine's catalog says: a 404 is found on the pages linking to it, a page without
    // anchor text names the URLs it links to.
    check('internal-client-error', 'responseCodes', 'error', 'Internal URL returns 4xx', 'detail', 'url', 'sources'),
    check('internal-server-error', 'responseCodes', 'error', 'Internal URL returns 5xx', 'detail', 'url', 'sources'),
    check(
      'empty-anchor-text',
      'links',
      'notice',
      'Internal links without anchor text',
      'relatedUrl',
      'page',
      'targets',
    ),
    check('orphan-page', 'links', 'notice', 'Orphan page in a sitemap', 'none', 'page', 'sources'),
    check('title-too-long', 'titles', 'notice', 'Title too long', 'none', 'page'),
  ],
};

/** The same catalog as an engine from before E3b gives it, and the design fixtures: no check says what it flags. */
export const ISSUES_CHECKS_WITHOUT_SUBJECTS: Checks = {
  ...ISSUES_CHECKS,
  checks: ISSUES_CHECKS.checks.map(({ subject: _subject, ...rest }) => rest),
};

/**
 * The crawl's own counts as its file has them: 7 pages crawled (one more than the 6 the analysis audited, as the
 * engine leaves a Cloudflare `/cdn-cgi/` page out of its checks but not out of its page count), and 40 URLs crawled,
 * which is also what the analysis saw (`ISSUES_ANALYSIS.crawledUrlCount`).
 */
export const ISSUES_CRAWL = { pageCount: 7, fetchedUrlCount: 40, crawledUrlCount: 40 } as const;

export const ISSUES_ANALYSIS: Analysis = {
  analyzedAt: '2026-11-01T10:21:00Z',
  engineVersion: 'test',
  pagesAnalyzed: 6,
  // The URLs crawled when it ran: what a URL check's count is a share of (a current engine's analysis keeps it).
  crawledUrlCount: 40,
  durationMs: 1_000,
  isPartial: true,
  outOfDate: false,
  outOfDateReasons: [],
  notes: ['No sitemap could be read, so orphan pages weren’t checked.'],
  counts: [
    { checkId: 'internal-client-error', count: 2 },
    { checkId: 'internal-server-error', count: 0 },
    { checkId: 'empty-anchor-text', count: 6 },
    { checkId: 'title-too-long', count: 3 },
  ],
  skipped: [{ checkId: 'orphan-page', reason: { code: 'no-sitemap', text: 'No sitemap could be read.' } }],
};

/** The anchor-text check's rows: five pages link to the home page without text, one to /garden/. */
export const ANCHOR_ROWS: (IssueRow & { group: number })[] = [
  ...[2, 3, 4, 5, 6].map((id) => ({
    id,
    url: `https://example.com/page-${id}/`,
    detail: 'Links to 1 URL',
    relatedUrl: 'https://example.com/',
    group: 1,
  })),
  {
    id: 7,
    url: 'https://example.com/page-7/',
    detail: 'Links to 1 URL',
    relatedUrl: 'https://example.com/garden/',
    group: 9,
  },
];

export const ISSUE_GROUPS: Record<string, IssueGroup[]> = {
  'empty-anchor-text': [
    { detail: null, relatedUrlId: 1, relatedUrl: 'https://example.com/', pages: 5, share: 5 / 6, likelyTemplate: true },
    {
      detail: null,
      relatedUrlId: 9,
      relatedUrl: 'https://example.com/garden/',
      pages: 1,
      share: 1 / 6,
      likelyTemplate: false,
    },
  ],
  'internal-client-error': [
    { detail: '404 Not Found', relatedUrlId: null, relatedUrl: null, pages: 2, share: 1, likelyTemplate: false },
  ],
};

export const ISSUE_ROWS: Record<string, (IssueRow & { group?: number })[]> = {
  'empty-anchor-text': ANCHOR_ROWS,
  'internal-client-error': [
    { id: 10, url: 'https://example.com/old/', detail: '404 Not Found' },
    { id: 11, url: 'https://example.com/gone/', detail: '404 Not Found' },
  ],
  'title-too-long': [
    { id: 12, url: 'https://example.com/a/', detail: '78 characters' },
    { id: 13, url: 'https://example.com/b/', detail: '64 characters' },
    { id: 14, url: 'https://example.com/c/', detail: '91 characters' },
  ],
};

/** A link to a broken URL, as `/urls/found-on` names its page. */
const sourceLink = (id: number, url: string, position: LinkRow['position'], text: string | null): LinkRow => ({
  id,
  url,
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

/**
 * Where each broken URL is found: /old/ on three pages (the first in its footer), /gone/ on one page and in the
 * sitemap. Answered as `/urls/found-on` does: in the order asked, a URL it doesn't have left out.
 */
export const FOUND_ON: Record<number, UrlFoundOn> = {
  10: {
    urlId: 10,
    links: 3,
    pages: 3,
    pagesExact: true,
    sources: [
      sourceLink(20, 'https://example.com/garden-furniture/', 'footer', 'Old range'),
      sourceLink(21, 'https://example.com/sofas/', 'content', 'See the old range'),
      sourceLink(22, 'https://example.com/blog/', 'content', null),
    ],
    sitemaps: { total: 0, sitemaps: [] },
    start: false,
  },
  11: {
    urlId: 11,
    links: 1,
    pages: 1,
    pagesExact: true,
    sources: [sourceLink(20, 'https://example.com/garden-furniture/', 'content', 'Gone for good')],
    sitemaps: { total: 1, sitemaps: [{ id: 1, url: 'https://example.com/sitemap.xml' }] },
    start: false,
  },
};

/** What each page without anchor text names: the home page from its header, page 7 /garden/ from its content. */
export const ISSUE_TARGETS: Record<string, Record<number, IssueTargetsOfUrl>> = {
  'empty-anchor-text': Object.fromEntries(
    ANCHOR_ROWS.map((row): [number, IssueTargetsOfUrl] => [
      row.id,
      {
        urlId: row.id,
        total: 1,
        targets: [
          {
            id: row.group,
            url: row.relatedUrl ?? '',
            statusCode: 200,
            fetched: true,
            outcome: 'success',
            kind: 'hyperlink',
            text: null,
            position: row.group === 1 ? 'header' : 'content',
          },
        ],
      },
    ]),
  ),
};

export function issueRowsPage(
  checkId: string,
  query: GridQuery,
  source: Record<string, (IssueRow & { group?: number })[]> = ISSUE_ROWS,
): GridPage<IssueRow> {
  let rows = source[checkId] ?? [];
  if (query.group) {
    const id = query.group.relatedUrlId;
    rows = rows.filter((row) => (row.group === undefined ? row.detail === query.group?.detail : row.group === id));
  }
  if (query.sort[0]?.column === 'detail') {
    const desc = query.sort[0].desc;
    rows = [...rows].sort((a, b) => (desc ? -1 : 1) * (a.detail ?? '').localeCompare(b.detail ?? ''));
  }
  const start = query.after ? Number(query.after) : (query.offset ?? 0);
  const page = rows.slice(start, start + query.limit).map(({ group: _group, ...row }) => row);
  const end = start + page.length;
  return {
    columns: ['id', ...(query.columns ?? ['url'])],
    rows: page,
    offset: query.after ? null : start,
    matching: rows.length,
    total: rows.length,
    next: end < rows.length ? String(end) : null,
    summary: { available: true, reason: null },
    unavailable: [],
  };
}

export interface IssuesSidecarOptions {
  item?: Partial<CrawlListItem>;
  /** The last analysis; null for a crawl never analysed, `error` for one that can't be read, `pending` for never. */
  analysis?: Analysis | null | 'error' | 'pending';
  checks?: Checks;
  groups?: Record<string, IssueGroup[]>;
  rows?: Record<string, (IssueRow & { group?: number })[]>;
  /** Where each URL is found, by id. */
  foundOn?: Record<number, UrlFoundOn>;
  /** What each flagged page names, by check and page id. */
  targets?: Record<string, Record<number, IssueTargetsOfUrl>>;
  onAnalyse?: () => void;
  onCancelAnalysis?: () => void;
  onContinue?: () => void;
}

/** The routes the Issues screen reads, with the standard ones (library, item, health) after them. */
export function issuesRoutes(options: IssuesSidecarOptions = {}): FakeRoute[] {
  const item = testCrawlItem({
    pageCount: ISSUES_CRAWL.pageCount,
    fetchedUrlCount: ISSUES_CRAWL.fetchedUrlCount,
    crawledUrlCount: ISSUES_CRAWL.crawledUrlCount,
    ...options.item,
  });
  const analysis = options.analysis === undefined ? ISSUES_ANALYSIS : options.analysis;
  const groups = options.groups ?? ISSUE_GROUPS;
  const rows = options.rows ?? ISSUE_ROWS;
  const counted = typeof analysis === 'object' && analysis !== null ? analysis : ISSUES_ANALYSIS;
  return [
    ['GET', /^\/api\/checks$/, () => json(options.checks ?? ISSUES_CHECKS)],
    [
      'GET',
      /^\/api\/crawls\/[0-9a-f]{16}\/analysis$/,
      () =>
        analysis === 'pending'
          ? new Promise<Response>(() => undefined)
          : analysis === 'error'
            ? problem({
                type: 'internal',
                title: 'Something went wrong in the engine.',
                status: 500,
                detail: 'SqliteException: database disk image is malformed',
              })
            : analysis === null
              ? problem({ type: 'not-analyzed', title: 'This crawl hasn’t been analysed yet.', status: 404 })
              : json(analysis),
    ],
    [
      'POST',
      /^\/api\/crawls\/[0-9a-f]{16}\/analyze$/,
      () => {
        options.onAnalyse?.();
        return new Response(null, { status: 202 });
      },
    ],
    [
      'POST',
      /^\/api\/crawls\/[0-9a-f]{16}\/analyze\/cancel$/,
      () => {
        options.onCancelAnalysis?.();
        return new Response(null, { status: 202 });
      },
    ],
    [
      'POST',
      /^\/api\/crawls\/[0-9a-f]{16}\/continue$/,
      () => {
        options.onContinue?.();
        return json({ ...item, status: 'running' });
      },
    ],
    [
      'POST',
      /^\/api\/crawls\/[0-9a-f]{16}\/issues\/([a-z0-9-]+)\/groups$/,
      (request, match) => {
        const checkId = match[1] ?? '';
        const list = groups[checkId] ?? [];
        const { offset, limit } = request.body as { offset: number; limit: number };
        return json({
          checkId,
          grouping: 'detail',
          total: counted.counts.find((count) => count.checkId === checkId)?.count ?? null,
          groupCount: list.length,
          offset,
          groups: list.slice(offset, offset + limit),
        });
      },
    ],
    [
      'POST',
      /^\/api\/crawls\/[0-9a-f]{16}\/urls\/found-on$/,
      (request) => {
        const { urlIds, limit = 5 } = request.body as { urlIds: number[]; limit?: number };
        const known = options.foundOn ?? FOUND_ON;
        return json({
          urls: [...new Set(urlIds)]
            .map((id) => known[id])
            .filter((url): url is UrlFoundOn => url !== undefined)
            .map((url) => ({ ...url, sources: url.sources.slice(0, limit) })),
        });
      },
    ],
    [
      'POST',
      /^\/api\/crawls\/[0-9a-f]{16}\/issues\/([a-z0-9-]+)\/targets$/,
      (request, match) => {
        const checkId = match[1] ?? '';
        const { urlIds } = request.body as { urlIds: number[] };
        const named = (options.targets ?? ISSUE_TARGETS)[checkId] ?? {};
        return json({ checkId, urls: [...new Set(urlIds)].map((id) => named[id]).filter((url) => url !== undefined) });
      },
    ],
    [
      'POST',
      /^\/api\/crawls\/[0-9a-f]{16}\/issues\/([a-z0-9-]+)\/rows$/,
      (request, match) => json(issueRowsPage(match[1] ?? '', request.body as GridQuery, rows)),
    ],
    ...standardRoutes(item).filter(([method, pattern]) => !(method === 'GET' && pattern.source.includes('checks'))),
  ];
}
