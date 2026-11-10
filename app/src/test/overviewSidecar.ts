// A sidecar for the Overview screen's tests and stories, on top of the Issues one (its catalog and analysis): the URL
// facets the breakdowns read, the crawl's history, alerts and stats, and its start URL's row; and a crawl's progress
// events at any pace, as the engine sends them, for the live states.
import type { FacetBucket, FacetsPage, FacetsQuery, GridQuery, UrlRow } from '../api/grids';
import type { AlertEvent, CrawlHistory, CrawlStats, CrawlStatus, ProgressEvent } from '../api/types';
import type { SseMessage } from '../live/sse';
import { TEST_CRAWL_ID, json, problem, testUrlPage, type FakeRoute } from './fakeSidecar';
import { issuesRoutes, type IssuesSidecarOptions } from './issuesSidecar';

const bucket = (column: string, value: string | number | null, count: number): FacetBucket => ({
  value,
  count,
  filter:
    value === null
      ? { column, op: 'isEmpty' }
      : typeof value === 'number'
        ? { column, op: 'eq', value }
        : { column, op: 'in', values: [value] },
});

const band = (min: number, max: number | null, count: number): FacetBucket =>
  max === null
    ? { value: `${min}+`, min, count, filter: { column: 'responseMs', op: 'gte', value: min } }
    : { value: `${min}-${max}`, min, max, count, filter: { column: 'responseMs', op: 'between', min, max } };

const page = (matching: number, total: number, facets: FacetsPage['facets']): FacetsPage => ({
  matching,
  total,
  summary: { available: true, reason: null },
  unavailable: [],
  facets,
});

/** The garden crawl's breakdowns, as the sidecar answers the Overview's three facet queries. */
export const OVERVIEW_FACETS = {
  // Every URL but the 2 a firewall answered: 6,284 of 6,286.
  status: page(6_284, 6_286, {
    statusClass: [
      bucket('statusClass', '2xx', 4_492),
      bucket('statusClass', '3xx', 16),
      bucket('statusClass', '4xx', 9),
      bucket('statusClass', 'noResponse', 2),
      bucket('statusClass', null, 1_765),
    ],
    statusCode: [
      bucket('statusCode', 200, 4_492),
      bucket('statusCode', 301, 12),
      bucket('statusCode', 302, 4),
      bucket('statusCode', 404, 9),
      bucket('statusCode', null, 1_767),
    ],
  }),
  indexability: page(274, 6_286, {
    indexability: [
      bucket('indexability', 'none', 264),
      bucket('indexability', 'canonicalised', 1),
      bucket('indexability', 'clientError', 9),
    ],
  }),
  pages: page(265, 6_286, {
    depth: [
      bucket('depth', 0, 1),
      bucket('depth', 1, 52),
      bucket('depth', 2, 180),
      bucket('depth', 3, 29),
      bucket('depth', 4, 3),
    ],
    responseTime: [band(0, 249, 249), band(250, 499, 13), band(2_000, null, 3)],
  }),
} as const;

export interface OverviewFacets {
  status: FacetsPage;
  indexability: FacetsPage;
  pages: FacetsPage;
}

/** A crawl that fetched one URL and read no pages: its start URL answered 403. */
export const EMPTY_FACETS: OverviewFacets = {
  status: page(1, 1, { statusClass: [bucket('statusClass', '4xx', 1)], statusCode: [bucket('statusCode', 403, 1)] }),
  indexability: page(1, 1, { indexability: [bucket('indexability', 'clientError', 1)] }),
  pages: page(0, 1, {}),
};

export const OVERVIEW_STATS: CrawlStats = {
  startUrl: 'https://example.com/',
  status: 'completed',
  startedAt: '2026-11-01T10:15:00Z',
  finishedAt: '2026-11-01T10:20:00Z',
  engineVersion: 'test',
  urlCount: 6_286,
  internalUrlCount: 6_246,
  queuedCount: 1_417,
  linkCount: 21_914,
  sitemapCount: 0,
  sitemapErrorCount: 0,
  sitemapUrlCount: 0,
  unlinkedPageCount: 0,
  htmlPageCount: 265,
  indexablePageCount: 264,
  averageResponseMs: 197.8,
  totalBytes: 9_347_451,
  notCheckedCount: 0,
  outcomes: { success: 4_492, redirect: 16, clientError: 9, blocked: 2, robotsDisallowed: 2 },
  statusCodes: { '200': 4_492, '301': 12, '302': 4, '403': 2, '404': 9 },
  skipReasons: { noFollow: 1_765 },
  nonIndexableReasons: { canonicalised: 1, clientError: 9 },
};

/**
 * A crawl whose site's twin host stopped answering, as the real sidecar answered for one of the engine's test site:
 * 6,914 outcomes stored (the library's `fetchedUrlCount`, and the last progress event's `fetched`), 5 of them URLs that
 * weren't checked. The URL grid has those 5 as not fetched, so it counts 6,909 fetched, 13 of them with no response,
 * and the status facets count the 5 among the 145 URLs with no status class. `/stats` has them among its 8 network
 * errors, and says how many they are.
 */
export const DEAD_TWIN = {
  item: { fetchedUrlCount: 6_914, pageCount: 6_172 },
  notChecked: 5,
  status: page(7_053, 7_054, {
    statusClass: [
      bucket('statusClass', '2xx', 6_326),
      bucket('statusClass', '3xx', 264),
      bucket('statusClass', '4xx', 277),
      bucket('statusClass', '5xx', 28),
      bucket('statusClass', 'noResponse', 13),
      bucket('statusClass', null, 145),
    ],
    statusCode: [
      bucket('statusCode', 200, 6_326),
      bucket('statusCode', 301, 203),
      bucket('statusCode', 302, 61),
      bucket('statusCode', 404, 277),
      bucket('statusCode', 500, 28),
      bucket('statusCode', null, 158),
    ],
  }),
  stats: {
    notCheckedCount: 5,
    outcomes: {
      success: 6_326,
      redirect: 264,
      clientError: 277,
      serverError: 28,
      blocked: 1,
      robotsDisallowed: 10,
      networkError: 8,
    },
    statusCodes: { '200': 6_326, '301': 203, '302': 61, '403': 1, '404': 277, '500': 28 },
    skipReasons: { noFollow: 140 },
  },
  /** Its last progress event's part that differs from the grid's counts. */
  progress: {
    fetched: 6_914,
    discovered: 7_054,
    queued: 0,
    inFlight: 0,
    skipped: 140,
    htmlPages: 6_172,
    statusClasses: { '2xx': 6_326, '3xx': 264, '4xx': 277, '5xx': 28, other: 0, noResponse: 18, blocked: 1 },
    statusCodes: { '200': 6_326, '301': 203, '302': 61, '404': 277, '500': 28 },
    noResponse: { robotsDisallowed: 10, timeout: 0, networkError: 3, notChecked: 5, processingError: 0 },
  },
} as const;

/** The figures a done crawl's item has in these tests and stories: the garden crawl's. */
export const OVERVIEW_ITEM = { fetchedUrlCount: 4_521, pageCount: 265 } as const;

const START = Date.parse('2026-11-01T10:15:00.000Z');

export interface CrawlScript {
  /** URLs fetched a second. */
  rate: number;
  /** The site's size: what the crawl finds in the end. */
  size: number;
  /** The crawl's URL limit, or null without one. */
  maxUrls?: number | null;
  status?: CrawlStatus;
  crawlId?: string;
}

/**
 * A crawl's progress event `seconds` in, as E4 sends it: every count of the same moment, so the classes add up to
 * what was fetched. It finds the site quickly (four times as fast as it crawls it) and crawls at a steady rate.
 */
export function progressAt(seconds: number, script: CrawlScript): ProgressEvent {
  const { rate, size, maxUrls = null, status = 'running', crawlId = TEST_CRAWL_ID } = script;
  const goal = maxUrls === null ? size : Math.min(size, maxUrls);
  const fetched = Math.min(goal, Math.round(rate * seconds));
  const discovered = Math.min(size, Math.max(fetched, 1 + Math.round(rate * 4 * seconds)));
  const skipped = Math.round(discovered * 0.03);
  const internalFound = Math.min(goal, discovered - skipped);
  const noResponse = Math.floor(fetched / 500);
  const blocked = Math.floor(fetched / 1_400);
  const serverErrors = Math.floor(fetched / 400);
  const clientErrors = Math.floor(fetched / 60);
  const redirects = Math.floor(fetched / 90);
  const ok = fetched - redirects - clientErrors - serverErrors - noResponse - blocked;
  return {
    crawlId,
    status,
    elapsedMs: Math.round(seconds * 1_000),
    discovered,
    queued: Math.max(0, discovered - skipped - fetched - 8),
    inFlight: fetched < goal ? 8 : 0,
    fetched,
    htmlPages: Math.round(fetched * 0.9),
    redirects,
    errors: clientErrors + serverErrors,
    blocked,
    robotsDisallowed: noResponse,
    skipped,
    bytesDownloaded: fetched * 4_200,
    // A little unevenness, the same every time, so the sparkline has something to draw.
    urlsPerSecond: fetched === 0 ? 0 : Math.round(rate * (1 + 0.04 * Math.sin(seconds * 1.7)) * 10) / 10,
    at: new Date(START + Math.round(seconds * 1_000)).toISOString(),
    phase: fetched === 0 ? 'starting' : fetched >= goal ? 'finishing' : 'crawling',
    internalFound,
    internalFetched: Math.min(internalFound, fetched),
    maxUrls,
    statusClasses: {
      '2xx': ok,
      '3xx': redirects,
      '4xx': clientErrors,
      '5xx': serverErrors,
      other: 0,
      noResponse,
      blocked,
    },
    statusCodes: {
      ...(ok > 0 ? { '200': ok } : {}),
      ...(redirects > 0 ? { '301': redirects } : {}),
      ...(clientErrors > 0 ? { '404': clientErrors } : {}),
      ...(serverErrors > 0 ? { '500': serverErrors } : {}),
    },
    noResponse: { robotsDisallowed: noResponse, timeout: 0, networkError: 0, notChecked: 0, processingError: 0 },
    waits: [],
    moreWaits: {},
  };
}

/** A stream message, as `liveStore.ingest` takes it. */
export const message = (event: string, data: unknown): SseMessage => ({ event, id: '', data: JSON.stringify(data) });

/** The progress events from the crawl's start to `seconds` in, one every half second, as the engine sends them. */
export function progressUntil(seconds: number, script: CrawlScript, extra: Partial<ProgressEvent> = {}): SseMessage[] {
  const messages: SseMessage[] = [];
  for (let at = 0; at <= seconds; at += 0.5)
    messages.push(message('progress', { ...progressAt(at, script), ...extra }));
  return messages;
}

/** A history that ends `seconds` in: what `/history` says of a crawl the page opened on mid-way. */
export function historyUntil(seconds: number, script: CrawlScript): CrawlHistory {
  const step = Math.max(0.5, Math.ceil(seconds / 300) / 2);
  const points = [];
  for (let at = 0; at <= seconds; at += step) {
    const progress = progressAt(at, script);
    points.push({
      at: progress.at ?? '',
      fetched: progress.fetched,
      htmlPages: progress.htmlPages,
      internalFetched: progress.internalFetched ?? 0,
      internalFound: progress.internalFound ?? 0,
      errors: progress.errors,
      blocked: progress.blocked,
      // As the sidecar's own live points: the time spent in the run they belong to.
      elapsedMs: progress.elapsedMs,
    });
  }
  return {
    crawlId: script.crawlId ?? TEST_CRAWL_ID,
    startedAt: new Date(START).toISOString(),
    liveFrom: new Date(START).toISOString(),
    points,
  };
}

export const alert = (extra: Partial<AlertEvent> & Pick<AlertEvent, 'code' | 'id'>): AlertEvent => ({
  crawlId: TEST_CRAWL_ID,
  level: 'warning',
  message: 'The engine’s own words.',
  at: '2026-11-01T10:16:00.000Z',
  data: null,
  ...extra,
});

export interface OverviewSidecarOptions extends IssuesSidecarOptions {
  /** The three breakdowns; `pending` never answers, `error` fails each. */
  facets?: Partial<OverviewFacets> | 'pending' | 'error';
  /**
   * URLs that weren't checked (their site had stopped answering): what the grid counts under `outcome` in
   * `notChecked`. The status facets count them among the URLs with no status class. None unless said.
   */
  notChecked?: number;
  history?: CrawlHistory;
  alerts?: AlertEvent[];
  /** `pending` never answers: the breakdowns mustn't wait for it. */
  stats?: CrawlStats | 'pending';
  /** The start URL's row; null for a crawl that has none. */
  startUrl?: UrlRow | null;
  onPause?: () => void;
  onResume?: () => void;
  onStop?: () => void;
}

/** The routes the Overview reads, then the Issues sidecar's (the catalog, the analysis, the item). */
export function overviewRoutes(options: OverviewSidecarOptions = {}): FakeRoute[] {
  const facets = options.facets ?? {};
  const answerFacets = (query: FacetsQuery): Response | Promise<Response> => {
    if (facets === 'pending') return new Promise<Response>(() => undefined);
    if (facets === 'error') {
      return problem({
        type: 'internal',
        title: 'Something went wrong in the engine.',
        status: 500,
        detail: 'SqliteException: database is locked',
      });
    }
    const wanted = query.facets ?? [];
    const status = facets.status ?? OVERVIEW_FACETS.status;
    // The status breakdown's second read: only the URLs that weren't checked, of which it takes the count.
    if (query.filters?.some((filter) => filter.column === 'outcome' && filter.op === 'in')) {
      const count = options.notChecked ?? 0;
      return json(page(count, status.total, count > 0 ? { statusClass: [bucket('statusClass', null, count)] } : {}));
    }
    if (wanted.includes('indexability')) return json(facets.indexability ?? OVERVIEW_FACETS.indexability);
    if (wanted.includes('depth')) return json(facets.pages ?? OVERVIEW_FACETS.pages);
    return json(status);
  };
  const startUrl =
    options.startUrl === undefined
      ? { id: 1, url: 'https://example.com/', statusCode: 200, outcome: 'success' as const, contentType: 'text/html' }
      : options.startUrl;
  const control = (path: string, heard: (() => void) | undefined, status: number): FakeRoute => [
    'POST',
    new RegExp(`^/api/crawls/[0-9a-f]{16}/${path}$`),
    () => {
      heard?.();
      return new Response(null, { status });
    },
  ];
  return [
    ['POST', /^\/api\/crawls\/[0-9a-f]{16}\/urls\/facets$/, (request) => answerFacets(request.body as FacetsQuery)],
    [
      'POST',
      /^\/api\/crawls\/[0-9a-f]{16}\/urls$/,
      (request) => {
        const query = request.body as GridQuery;
        const seed = query.filters.some((filter) => filter.column === 'source');
        return json(
          seed ? testUrlPage({ rows: startUrl ? [startUrl] : [], matching: startUrl ? 1 : 0 }) : testUrlPage(),
        );
      },
    ],
    [
      'GET',
      /^\/api\/crawls\/[0-9a-f]{16}\/history$/,
      () =>
        json(
          options.history ??
            ({
              crawlId: TEST_CRAWL_ID,
              startedAt: new Date(START).toISOString(),
              liveFrom: null,
              points: [],
            } satisfies CrawlHistory),
        ),
    ],
    ['GET', /^\/api\/crawls\/[0-9a-f]{16}\/alerts$/, () => json(options.alerts ?? [])],
    [
      'GET',
      /^\/api\/crawls\/[0-9a-f]{16}\/stats$/,
      () =>
        options.stats === 'pending' ? new Promise<Response>(() => undefined) : json(options.stats ?? OVERVIEW_STATS),
    ],
    control('pause', options.onPause, 204),
    control('resume', options.onResume, 204),
    control('stop', options.onStop, 202),
    ...issuesRoutes({ ...options, item: { ...OVERVIEW_ITEM, ...options.item } }),
  ];
}
