// The mock's Sitemaps and robots.txt endpoints (docs/v2-sidecar-api.md, E3b), over the fixtures the design data's
// `sitemaps-robots.mjs` step saves from the real sidecar: the list of sitemaps, which of the URL fixture's rows each
// lists, the origins, each origin's file and its blocked URLs. A block of its own: mockTransport.ts adds these routes
// to its own with one line.
import type { ProblemBody } from '../errors';
import type {
  GridQuery,
  RobotsBlockedPage,
  RobotsBlockedQuery,
  RobotsBlockedUrl,
  RobotsFile,
  RobotsOriginPage,
  Sitemap,
  SitemapList,
  SummaryState,
  UrlRow,
} from '../grids';
import type { TransportRequest } from '../transport';
import type { CrawlListItem } from '../types';
import { optionalFixture, type FixtureCrawl } from './fixtures';
import { answerGridQuery, pagingErrors } from './gridQuery';

type Handler = (match: RegExpMatchArray, request: TransportRequest) => Promise<Response>;
export type MockRoute = [method: TransportRequest['method'], pattern: RegExp, handler: Handler];

/** What these routes need of the mock they're part of. */
export interface MockCrawls {
  json: (body: unknown) => Response;
  problem: (body: ProblemBody) => Response;
  /** The crawl's item, or null when the library has none with this id. */
  item: (id: string) => Promise<CrawlListItem | null>;
  notFound: (id: string) => ProblemBody;
  folder: (id: string) => FixtureCrawl;
  /** The crawl's URLs: while one started here runs, those its replay has reached. */
  rows: (id: string) => Promise<Required<UrlRow>[]>;
  summary: (item: CrawlListItem) => SummaryState;
  /** A crawl started here is still running. */
  live: (id: string) => boolean;
}

const sitemapNotFound = (): ProblemBody => ({
  type: 'sitemap-not-found',
  title: 'This crawl has no sitemap with that id.',
  status: 404,
});

const originNotFound = (): ProblemBody => ({
  type: 'origin-not-found',
  title: 'This crawl has no robots.txt for that origin.',
  status: 404,
});

const invalid = (errors: Record<string, string[]>): ProblemBody => ({
  type: 'invalid-query',
  title: 'This view’s query can’t be run.',
  status: 422,
  detail: Object.values(errors).flat()[0] ?? '',
  errors,
});

const NO_SITEMAPS: SitemapList = { discovery: 'done', summary: { available: true, reason: null }, sitemaps: [] };
const NO_ORIGINS: RobotsOriginPage = {
  total: 0,
  offset: 0,
  respected: true,
  userAgent: 'ShoutingIguana',
  blocked: 0,
  origins: [],
};

/** A sitemap and every sitemap under it: what its grid lists. */
function withDescendants(sitemaps: readonly Sitemap[], id: number, seen = new Set<number>()): number[] {
  if (seen.has(id)) return [];
  seen.add(id);
  return [
    id,
    ...sitemaps
      .filter((sitemap) => sitemap.parentId === id)
      .flatMap((child) => withDescendants(sitemaps, child.id, seen)),
  ];
}

export function sitemapsRobotsRoutes(mock: MockCrawls): MockRoute[] {
  const crawl = String.raw`^\/api\/crawls\/([0-9a-f]{16})`;

  const savedSitemaps = async (id: string) =>
    (await optionalFixture<SitemapList>(`api/${mock.folder(id)}/sitemaps.json`)) ?? NO_SITEMAPS;

  /** The URL fixture's rows a sitemap lists, each once: its own and those of every sitemap under it. */
  async function listedRows(id: string, sitemaps: readonly Sitemap[], sitemapId: number) {
    const bySitemap =
      (await optionalFixture<Record<string, number[]>>(`api/${mock.folder(id)}/sitemap-urls.json`)) ?? {};
    const ids = new Set(withDescendants(sitemaps, sitemapId).flatMap((each) => bySitemap[String(each)] ?? []));
    return (await mock.rows(id)).filter((row) => ids.has(row.id));
  }

  /**
   * The list, with each sitemap's URLs counted over the rows the mock's grid has for it, so every count opens exactly
   * its rows here too (the 100k crawl's URL fixture is a sample of its URLs, and the saved counts are of them all).
   */
  async function sitemapList(id: string, item: CrawlListItem): Promise<SitemapList> {
    const saved = await savedSitemaps(id);
    const summary = mock.summary(item);
    const sitemaps = await Promise.all(
      saved.sitemaps.map(async (sitemap) => {
        const rows = await listedRows(id, saved.sitemaps, sitemap.id);
        const count = (column: 'fetched' | 'indexable', value: boolean) =>
          rows.filter((row) => row[column] === value).length;
        return {
          ...sitemap,
          urls: {
            listed: rows.length,
            fetched: count('fetched', true),
            indexable: count('indexable', true),
            nonIndexable: count('indexable', false),
          },
        };
      }),
    );
    return { discovery: mock.live(id) ? 'running' : saved.discovery, summary, sitemaps };
  }

  const savedOrigins = async (id: string) =>
    (await optionalFixture<RobotsOriginPage>(`api/${mock.folder(id)}/robots.json`)) ?? NO_ORIGINS;

  const origin = (body: unknown): string | null => {
    const value = (body as { origin?: unknown } | undefined)?.origin;
    return typeof value === 'string' && value !== '' ? value : null;
  };

  return [
    [
      'GET',
      new RegExp(`${crawl}/sitemaps$`),
      async ([, id = '']) => {
        const item = await mock.item(id);
        return item ? mock.json(await sitemapList(id, item)) : mock.problem(mock.notFound(id));
      },
    ],
    [
      'POST',
      new RegExp(`${crawl}/sitemaps/urls$`),
      async ([, id = ''], request) => {
        const item = await mock.item(id);
        if (!item) return mock.problem(mock.notFound(id));
        const { sitemapId, ...query } = (request.body ?? {}) as GridQuery & { sitemapId?: unknown };
        if (typeof sitemapId !== 'number') return mock.problem(invalid({ sitemapId: ['Give the sitemap’s id.'] }));
        const { sitemaps } = await savedSitemaps(id);
        if (!sitemaps.some((sitemap) => sitemap.id === sitemapId)) return mock.problem(sitemapNotFound());
        const answer = answerGridQuery(await listedRows(id, sitemaps, sitemapId), query, mock.summary(item));
        return answer.ok ? mock.json(answer.page) : mock.problem(answer.problem);
      },
    ],
    [
      'POST',
      new RegExp(`${crawl}/robots$`),
      async ([, id = ''], request) => {
        if (!(await mock.item(id))) return mock.problem(mock.notFound(id));
        const body = (request.body ?? {}) as { offset?: number; limit?: number };
        const offset = body.offset ?? 0;
        const limit = body.limit ?? 100;
        const errors = pagingErrors(offset, limit);
        if (Object.keys(errors).length > 0) return mock.problem(invalid(errors));
        const saved = await savedOrigins(id);
        return mock.json({
          ...saved,
          total: saved.origins.length,
          offset,
          origins: saved.origins.slice(offset, offset + limit),
        } satisfies RobotsOriginPage);
      },
    ],
    [
      'POST',
      new RegExp(`${crawl}/robots/file$`),
      async ([, id = ''], request) => {
        if (!(await mock.item(id))) return mock.problem(mock.notFound(id));
        const asked = origin(request.body);
        if (asked === null) {
          return mock.problem(invalid({ origin: ['Give the origin, as the list of origins names it.'] }));
        }
        const files = await optionalFixture<Record<string, RobotsFile>>(`api/${mock.folder(id)}/robots-files.json`);
        const file = files?.[asked];
        return file ? mock.json(file) : mock.problem(originNotFound());
      },
    ],
    [
      'POST',
      new RegExp(`${crawl}/robots/blocked$`),
      async ([, id = ''], request) => {
        if (!(await mock.item(id))) return mock.problem(mock.notFound(id));
        const query = (request.body ?? {}) as Partial<RobotsBlockedQuery>;
        const asked = origin(query);
        if (asked === null) {
          return mock.problem(invalid({ origin: ['Give the origin, as the list of origins names it.'] }));
        }
        if (query.reason !== undefined && !['rule', 'unreachable', 'unknown'].includes(query.reason)) {
          return mock.problem(
            invalid({
              reason: [`There’s no reason called '${query.reason}': there are rule, unreachable and unknown.`],
            }),
          );
        }
        const offset = query.offset ?? 0;
        const limit = query.limit ?? 100;
        const errors = pagingErrors(offset, limit);
        if (Object.keys(errors).length > 0) return mock.problem(invalid(errors));
        const saved = await savedOrigins(id);
        if (!saved.origins.some((each) => each.origin === asked)) return mock.problem(originNotFound());
        const byOrigin =
          (await optionalFixture<Record<string, RobotsBlockedUrl[]>>(`api/${mock.folder(id)}/robots-blocked.json`)) ??
          {};
        const all = byOrigin[asked] ?? [];
        const matching = all.filter(
          (row) =>
            (query.line === undefined || row.line === query.line) &&
            (query.reason === undefined || row.reason === query.reason),
        );
        return mock.json({
          origin: asked,
          total: all.length,
          matching: matching.length,
          offset,
          rows: matching.slice(offset, offset + limit),
        } satisfies RobotsBlockedPage);
      },
    ],
  ];
}
