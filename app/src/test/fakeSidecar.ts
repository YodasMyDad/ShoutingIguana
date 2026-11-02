// A sidecar for component and router tests: answers the routes a test gives it from plain objects, records every
// request, and says `not-found` for anything else, as the real one does. No fixtures: tests never depend on the
// git-ignored design data.
import type { ProblemBody } from '../api/errors';
import type { GridPage, UrlRow } from '../api/grids';
import type { Transport, TransportRequest } from '../api/transport';
import type { CrawlListItem } from '../api/types';

export const TEST_CRAWL_ID = '0123456789abcdef';

export function testCrawlItem(overrides: Partial<CrawlListItem> = {}): CrawlListItem {
  return {
    id: TEST_CRAWL_ID,
    path: 'C:\\crawls\\example.com.sicrawl',
    fileName: 'example.com.sicrawl',
    site: 'example.com',
    startUrl: 'https://example.com/',
    status: 'completed',
    startedAt: '2026-11-01T10:15:00Z',
    finishedAt: '2026-11-01T10:20:00Z',
    updatedAt: '2026-11-01T10:20:00Z',
    fetchedUrlCount: 3,
    pageCount: 2,
    issues: { error: 1, warning: 2, notice: 3 },
    analyzedAt: '2026-11-01T10:21:00Z',
    outOfDate: false,
    sizeBytes: 18_276_352,
    missing: false,
    error: null,
    ...overrides,
  };
}

export const TEST_URL_ROWS: UrlRow[] = [
  { id: 1, url: 'https://example.com/', statusCode: 200, statusClass: '2xx', title: 'Home', inlinks: 4, issues: 0 },
  { id: 2, url: 'https://example.com/a', statusCode: 404, statusClass: '4xx', title: null, inlinks: 1, issues: 2 },
  { id: 3, url: 'https://example.com/b', statusCode: 200, statusClass: '2xx', title: 'B', inlinks: 2, issues: 1 },
];

/** One page of the URL grid with every row, as the sidecar shapes it. */
export function testUrlPage(overrides: Partial<GridPage<UrlRow>> = {}): GridPage<UrlRow> {
  return {
    columns: ['id', 'url', 'statusCode', 'statusClass', 'title', 'inlinks', 'issues'],
    rows: TEST_URL_ROWS,
    offset: 0,
    matching: TEST_URL_ROWS.length,
    total: TEST_URL_ROWS.length,
    next: null,
    summary: { available: true, reason: null },
    unavailable: [],
    ...overrides,
  };
}

export const json = (body: unknown, status = 200) => Response.json(body, { status });

export const problem = (body: ProblemBody) =>
  new Response(JSON.stringify(body), { status: body.status, headers: { 'Content-Type': 'application/problem+json' } });

export type FakeRoute = [
  method: TransportRequest['method'],
  path: RegExp,
  answer: (request: TransportRequest, match: RegExpMatchArray) => Response | Promise<Response>,
];

export interface FakeSidecar extends Transport {
  /** Every request, in the order sent. */
  readonly requests: TransportRequest[];
}

/** The routes most screens read: the library, the crawl's item, its URL grid, the catalog and health. */
export function standardRoutes(item: CrawlListItem = testCrawlItem()): FakeRoute[] {
  return [
    ['GET', /^\/api\/crawls$/, () => json([item])],
    ['GET', /^\/api\/crawls\/([0-9a-f]{16})$/, () => json(item)],
    ['POST', /^\/api\/crawls\/[0-9a-f]{16}\/urls$/, () => json(testUrlPage())],
    ['GET', /^\/api\/checks$/, () => json({ categories: [], checks: [] })],
    [
      'GET',
      /^\/api\/health$/,
      () => json({ version: 'test', engineVersion: 'test', dataDir: 'C:\\data', stateDir: 'C:\\state', pid: 1 }),
    ],
  ];
}

export function fakeSidecar(routes: readonly FakeRoute[]): FakeSidecar {
  const requests: TransportRequest[] = [];
  return {
    kind: 'mock',
    requests,
    async send(request) {
      if (request.signal?.aborted) throw new DOMException('Aborted', 'AbortError');
      requests.push(request);
      for (const [method, pattern, answer] of routes) {
        const match = method === request.method ? pattern.exec(request.path) : null;
        if (match) return answer(request, match);
      }
      return problem({ type: 'not-found', title: 'The engine doesn’t have this.', status: 404 });
    },
  };
}
