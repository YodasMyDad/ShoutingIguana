// What the Sitemaps and robots.txt screens read, for their tests and stories: a list of sitemaps with an index, a
// failed one and one the crawl can't say how it found; origins in each of the four states; a file with two groups;
// and the routes that answer for them as the sidecar does (docs/v2-sidecar-api.md, Sitemaps and robots.txt).
import type {
  FacetsPage,
  GridPage,
  GridQuery,
  RobotsBlockedPage,
  RobotsBlockedQuery,
  RobotsBlockedUrl,
  RobotsFile,
  RobotsLine,
  RobotsOrigin,
  RobotsOriginPage,
  Sitemap,
  SitemapList,
  SummaryState,
  UrlColumn,
  UrlRow,
} from '../api/grids';
import type { Analysis, CrawlListItem } from '../api/types';
import { json, problem, standardRoutes, testCrawlItem, type FakeRoute } from './fakeSidecar';

export const SITE = 'https://example.com';

const AVAILABLE: SummaryState = { available: true, reason: null };

export function testSitemap(overrides: Partial<Sitemap> & Pick<Sitemap, 'id'>): Sitemap {
  return {
    url: `${SITE}/sitemap-${overrides.id}.xml`,
    parentId: null,
    foundVia: 'robotsTxt',
    state: 'read',
    statusCode: 200,
    isIndex: false,
    entries: 0,
    sitemaps: 0,
    error: null,
    urls: { listed: 0, fetched: 0, indexable: 0, nonIndexable: 0 },
    ...overrides,
  };
}

/** An index with three sitemaps under it (one failed), a sitemap given in the settings, and one of unknown origin. */
export const TEST_SITEMAPS: Sitemap[] = [
  testSitemap({
    id: 1,
    url: `${SITE}/sitemap.xml`,
    isIndex: true,
    sitemaps: 3,
    urls: { listed: 6, fetched: 5, indexable: 3, nonIndexable: 2 },
  }),
  testSitemap({
    id: 2,
    url: `${SITE}/sitemaps/pages.xml`,
    parentId: 1,
    foundVia: 'index',
    entries: 4,
    urls: { listed: 4, fetched: 4, indexable: 3, nonIndexable: 1 },
  }),
  testSitemap({
    id: 3,
    url: `${SITE}/sitemaps/products.xml`,
    parentId: 1,
    foundVia: 'index',
    entries: 3,
    urls: { listed: 3, fetched: 2, indexable: 1, nonIndexable: 1 },
  }),
  testSitemap({
    id: 4,
    url: `${SITE}/sitemaps/missing.xml`,
    parentId: 1,
    foundVia: 'index',
    state: 'failed',
    statusCode: 404,
    error: 'HTTP 404',
  }),
  testSitemap({
    id: 5,
    url: `${SITE}/news-sitemap.xml`,
    foundVia: 'options',
    entries: 2,
    urls: { listed: 2, fetched: 2, indexable: 2, nonIndexable: 0 },
  }),
  testSitemap({
    id: 6,
    url: `${SITE}/old-sitemap.xml`,
    foundVia: null,
    entries: 1,
    urls: { listed: 1, fetched: 0, indexable: 0, nonIndexable: 0 },
  }),
];

export const TEST_SITEMAP_LIST: SitemapList = { discovery: 'done', summary: AVAILABLE, sitemaps: TEST_SITEMAPS };

/** The URLs the test sitemaps list, with what the crawl did with each. */
export const SITEMAP_URL_ROWS: (UrlRow & { sitemaps: number[] })[] = [
  {
    id: 1,
    url: `${SITE}/`,
    fetched: true,
    outcome: 'success',
    statusCode: 200,
    statusClass: '2xx',
    indexable: true,
    indexability: 'none',
    title: 'Home',
    depth: 0,
    sitemaps: [2],
  },
  {
    id: 2,
    url: `${SITE}/about`,
    fetched: true,
    outcome: 'success',
    statusCode: 200,
    statusClass: '2xx',
    indexable: true,
    indexability: 'none',
    title: 'About',
    depth: 1,
    sitemaps: [2],
  },
  {
    id: 3,
    url: `${SITE}/old`,
    fetched: true,
    outcome: 'redirect',
    statusCode: 301,
    statusClass: '3xx',
    indexable: false,
    indexability: 'redirected',
    title: null,
    depth: 1,
    sitemaps: [2],
  },
  {
    id: 4,
    url: `${SITE}/contact`,
    fetched: true,
    outcome: 'success',
    statusCode: 200,
    statusClass: '2xx',
    indexable: true,
    indexability: 'none',
    title: 'Contact',
    depth: 1,
    sitemaps: [2, 3],
  },
  {
    id: 5,
    url: `${SITE}/p/gone`,
    fetched: true,
    outcome: 'clientError',
    statusCode: 404,
    statusClass: '4xx',
    indexable: false,
    indexability: 'clientError',
    title: null,
    depth: 2,
    sitemaps: [3],
  },
  {
    id: 6,
    url: `${SITE}/p/unreached`,
    fetched: false,
    outcome: null,
    statusCode: null,
    statusClass: null,
    indexable: null,
    indexability: null,
    title: null,
    depth: 2,
    sitemaps: [3],
  },
  {
    id: 7,
    url: `${SITE}/news/a`,
    fetched: true,
    outcome: 'success',
    statusCode: 200,
    statusClass: '2xx',
    indexable: true,
    indexability: 'none',
    title: 'News A',
    depth: 2,
    sitemaps: [5],
  },
  {
    id: 8,
    url: `${SITE}/news/b`,
    fetched: true,
    outcome: 'success',
    statusCode: 200,
    statusClass: '2xx',
    indexable: true,
    indexability: 'none',
    title: 'News B',
    depth: 2,
    sitemaps: [5],
  },
  {
    id: 9,
    url: `${SITE}/legacy`,
    fetched: false,
    outcome: null,
    statusCode: null,
    statusClass: null,
    indexable: null,
    indexability: null,
    title: null,
    depth: 1,
    sitemaps: [6],
  },
];

/** The sitemaps whose URLs a sitemap's grid shows: its own, and every sitemap's under it. */
function under(sitemaps: readonly Sitemap[], id: number): number[] {
  const children = sitemaps.filter((sitemap) => sitemap.parentId === id).flatMap((child) => under(sitemaps, child.id));
  return [id, ...children];
}

/** A sitemap's grid as the sidecar answers it, for the filters the screens use (`is`, `in` on one column). */
export function sitemapUrlsPage(
  body: GridQuery & { sitemapId?: number },
  sitemaps: readonly Sitemap[] = TEST_SITEMAPS,
  summary: SummaryState = AVAILABLE,
): GridPage<UrlRow> {
  const within = under(sitemaps, body.sitemapId ?? 0);
  const listed = SITEMAP_URL_ROWS.filter((row) => row.sitemaps.some((id) => within.includes(id)));
  const value = (row: UrlRow, column: string) =>
    (row as unknown as Record<string, string | number | boolean | null | undefined>)[column];
  let rows = listed.filter((row) =>
    body.filters.every((filter) => {
      if (filter.op === 'is') return value(row, filter.column) === filter.value;
      if (filter.op === 'in') return filter.values.includes(value(row, filter.column) as string | number);
      return true;
    }),
  );
  const search = body.search?.toLowerCase();
  if (search) rows = rows.filter((row) => `${row.url ?? ''} ${row.title ?? ''}`.toLowerCase().includes(search));
  const [sort] = body.sort;
  if (sort) {
    rows = [...rows].sort((a, b) => {
      const order = String(value(a, sort.column) ?? '').localeCompare(String(value(b, sort.column) ?? ''), 'en', {
        numeric: true,
      });
      return sort.desc ? -order : order;
    });
  }
  const offset = body.offset ?? 0;
  const unavailable = summary.available
    ? []
    : (body.columns ?? []).filter((column) => ANALYSIS_COLUMNS.includes(column));
  return {
    columns: ['id', ...(body.columns ?? ['url']).filter((column) => !unavailable.includes(column))],
    rows: rows.slice(offset, offset + body.limit).map(({ sitemaps: _sitemaps, ...row }) => row),
    offset,
    matching: rows.length,
    total: listed.length,
    next: null,
    summary,
    unavailable,
  };
}

const ANALYSIS_COLUMNS = ['inlinks', 'issues'];

/** The URL grid's columns the screens ask about: which sort. */
export const TEST_URL_COLUMNS: UrlColumn[] = [
  ['url', 'text', true],
  ['fetched', 'boolean', true],
  ['outcome', 'enum', false],
  ['statusCode', 'number', true],
  ['indexability', 'enum', true],
  ['title', 'text', false],
  ['depth', 'number', true],
  ['responseMs', 'number', true],
  ['sizeBytes', 'number', true],
  ['inlinks', 'number', true],
  ['issues', 'number', true],
].map(([id, type, sortable]) => ({
  id: id as string,
  type: type as UrlColumn['type'],
  needsAnalysis: ANALYSIS_COLUMNS.includes(id as string),
  sortable: sortable as boolean,
  filterable: true,
  values: [],
  issueRowsOnly: false,
}));

/** The site's pages by whether a sitemap lists them: 2 of 9 aren't in one. */
export const COVERAGE_PAGE: FacetsPage = {
  matching: 9,
  total: 12,
  summary: AVAILABLE,
  unavailable: [],
  facets: {
    inSitemap: [
      { value: false, count: 2, filter: { column: 'inSitemap', op: 'is', value: false } },
      { value: true, count: 7, filter: { column: 'inSitemap', op: 'is', value: true } },
    ],
  },
};

// ------------------------------------------------------------------------------------------------------ robots.txt

export function testOrigin(overrides: Partial<RobotsOrigin> & Pick<RobotsOrigin, 'origin'>): RobotsOrigin {
  return {
    isInternal: false,
    state: 'read',
    statusCode: 200,
    fetchedAt: '2026-11-01T10:15:02Z',
    error: null,
    bytes: 120,
    blocked: 0,
    ...overrides,
  };
}

/** One origin in each state: read, missing, unreachable (its URLs refused) and unanswered (the crawl went ahead). */
export const TEST_ORIGINS: RobotsOrigin[] = [
  testOrigin({ origin: SITE, isInternal: true, bytes: 164, blocked: 4 }),
  testOrigin({ origin: 'https://cdn.example.net', state: 'unreachable', statusCode: 503, bytes: null, blocked: 2 }),
  testOrigin({ origin: 'https://partner.example.org', state: 'missing', statusCode: 404, bytes: null }),
  testOrigin({
    origin: 'https://slow.example.io',
    state: 'unanswered',
    statusCode: null,
    error: 'Timed out',
    bytes: null,
  }),
];

const line = (
  number: number,
  text: string,
  kind: RobotsLine['kind'],
  applies: boolean | null = null,
  blocked: number | null = null,
  problem: RobotsLine['problem'] = null,
): RobotsLine => ({ number, text, kind, applies, blocked, problem });

/** A file with a group for every crawler, a group for this one (which the crawl obeyed), and lines of every kind. */
export const TEST_FILE_LINES: RobotsLine[] = [
  line(1, '# Keep crawlers out of the back office', 'comment'),
  line(2, 'User-agent: *', 'userAgent', false),
  line(3, 'Disallow: /', 'disallow', false),
  line(4, '', 'blank'),
  line(5, 'User-agent: ShoutingIguana', 'userAgent', true),
  line(6, 'Disallow: /admin/', 'disallow', true, 3),
  line(7, 'Disallow: /cart', 'disallow', true, 0),
  line(8, 'Allow: /admin/help', 'allow', true),
  line(9, 'Crawl-delay: soon', 'crawlDelay', true, null, 'invalidValue'),
  line(10, 'Host: example.com', 'other', null, null, 'unknownDirective'),
  line(11, 'just some words', 'invalid', null, null, 'notADirective'),
  line(12, `Sitemap: ${SITE}/sitemap.xml`, 'sitemap', true),
];

export const TEST_FILE: RobotsFile = {
  ...TEST_ORIGINS[0]!,
  userAgent: 'ShoutingIguana',
  lines: TEST_FILE_LINES,
  unmatched: { unreachable: 0, unknown: 1 },
};

export const TEST_BLOCKED: RobotsBlockedUrl[] = [
  { id: 21, url: `${SITE}/admin/`, reason: 'rule', line: 6, rule: 'Disallow: /admin/', error: null },
  { id: 22, url: `${SITE}/admin/users`, reason: 'rule', line: 6, rule: 'Disallow: /admin/', error: null },
  { id: 23, url: `${SITE}/admin/orders?page=2`, reason: 'rule', line: 6, rule: 'Disallow: /admin/', error: null },
  { id: 24, url: `${SITE}/old-private`, reason: 'unknown', line: null, rule: null, error: null },
];

export function blockedPage(
  query: RobotsBlockedQuery,
  all: readonly RobotsBlockedUrl[] = TEST_BLOCKED,
): RobotsBlockedPage {
  const matching = all.filter(
    (row) =>
      (query.line === undefined || row.line === query.line) &&
      (query.reason === undefined || row.reason === query.reason),
  );
  const offset = query.offset ?? 0;
  return {
    origin: query.origin,
    total: all.length,
    matching: matching.length,
    offset,
    rows: matching.slice(offset, offset + (query.limit ?? 100)),
  };
}

export function originsPage(
  body: { offset?: number; limit?: number },
  origins: readonly RobotsOrigin[] = TEST_ORIGINS,
  extra: Partial<Pick<RobotsOriginPage, 'respected' | 'userAgent'>> = {},
): RobotsOriginPage {
  const offset = body.offset ?? 0;
  return {
    total: origins.length,
    offset,
    respected: true,
    userAgent: 'ShoutingIguana',
    blocked: origins.reduce((sum, origin) => sum + origin.blocked, 0),
    origins: origins.slice(offset, offset + (body.limit ?? 100)),
    ...extra,
  };
}

export interface SitemapsRobotsOptions {
  item?: Partial<CrawlListItem>;
  /** The list of sitemaps, or a failure in its place. */
  sitemaps?: SitemapList | Response;
  /** The site's pages by whether a sitemap lists them, or a failure. */
  coverage?: FacetsPage | Response;
  /** The last analysis: which checks it skipped, and why. Without one, the engine has none to give (404). */
  analysis?: Pick<Analysis, 'skipped'> | Response;
  origins?: readonly RobotsOrigin[];
  respected?: boolean;
  /** Each origin's file, by origin; an origin without one is `origin-not-found`. */
  files?: Record<string, RobotsFile | Response>;
  /** Each origin's blocked URLs, by origin. */
  blocked?: Record<string, readonly RobotsBlockedUrl[] | Response>;
  /** Answers after this long, for the loading states. */
  delayMs?: number;
}

const answer = (body: unknown) => (body instanceof Response ? body.clone() : json(body));

/** The sidecar's routes for the two screens, over the test data or what a test gives. */
export function sitemapsRobotsRoutes(options: SitemapsRobotsOptions = {}): FakeRoute[] {
  const list = options.sitemaps ?? TEST_SITEMAP_LIST;
  const origins = options.origins ?? TEST_ORIGINS;
  const files = options.files ?? { [SITE]: TEST_FILE };
  const blocked = options.blocked ?? { [SITE]: TEST_BLOCKED };
  const wait = async () => {
    if (options.delayMs !== undefined) await new Promise((resolve) => setTimeout(resolve, options.delayMs));
  };
  const crawl = String.raw`^\/api\/crawls\/[0-9a-f]{16}`;
  return [
    [
      'GET',
      new RegExp(`${crawl}/sitemaps$`),
      async () => {
        await wait();
        return answer(list);
      },
    ],
    [
      'POST',
      new RegExp(`${crawl}/sitemaps/urls$`),
      async (request) => {
        await wait();
        const body = request.body as GridQuery & { sitemapId?: number };
        const known = list instanceof Response ? TEST_SITEMAPS : list.sitemaps;
        if (!known.some((sitemap) => sitemap.id === body.sitemapId)) {
          return problem({ type: 'sitemap-not-found', title: 'This crawl has no sitemap with that id.', status: 404 });
        }
        return json(sitemapUrlsPage(body, known, list instanceof Response ? AVAILABLE : list.summary));
      },
    ],
    ['GET', /^\/api\/url-columns$/, () => json(TEST_URL_COLUMNS)],
    ['POST', new RegExp(`${crawl}/urls/facets$`), () => answer(options.coverage ?? COVERAGE_PAGE)],
    ...(options.analysis === undefined
      ? []
      : [['GET', new RegExp(`${crawl}/analysis$`), () => answer(options.analysis)] satisfies FakeRoute]),
    [
      'POST',
      new RegExp(`${crawl}/robots$`),
      async (request) => {
        await wait();
        const extra = options.respected === undefined ? {} : { respected: options.respected };
        return json(originsPage(request.body ?? {}, origins, extra));
      },
    ],
    [
      'POST',
      new RegExp(`${crawl}/robots/file$`),
      async (request) => {
        await wait();
        const { origin } = request.body as { origin: string };
        const file = files[origin];
        return file === undefined
          ? problem({ type: 'origin-not-found', title: 'This crawl has no robots.txt for that origin.', status: 404 })
          : answer(file);
      },
    ],
    [
      'POST',
      new RegExp(`${crawl}/robots/blocked$`),
      async (request) => {
        await wait();
        const query = request.body as RobotsBlockedQuery;
        const rows = blocked[query.origin] ?? [];
        return rows instanceof Response ? rows.clone() : json(blockedPage(query, rows));
      },
    ],
    ...standardRoutes(testCrawlItem(options.item)),
  ];
}
