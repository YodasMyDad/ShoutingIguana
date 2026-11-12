// A sidecar for the Links screen's tests and stories: a small crawl whose links cover what the screen must say (a
// broken target, a redirect, a nofollow link, a link with no text, another site, a URL never fetched, a robots.txt
// refusal), answered by the mock's own links code (api/mock/linkGrid.ts), so a test sees the pages, the counts and
// the refusals the sidecar gives.
import type { GridQuery, LinkFacetsQuery, LinkRow, SummaryState, UrlRow } from '../api/grids';
import { answerGridQuery } from '../api/mock/gridQuery';
import { LINK_COLUMNS, answerLinkFacets, answerLinkQuery, linkRows, type LinkFacts } from '../api/mock/linkGrid';
import type { TransportRequest } from '../api/transport';
import type { CrawlListItem } from '../api/types';
import { json, problem, standardRoutes, testCrawlItem, type FakeRoute } from './fakeSidecar';

const url = (id: number, address: string, facts: Partial<UrlRow>): UrlRow => ({
  id,
  url: address,
  fetched: true,
  isInternal: true,
  source: 'link',
  depth: 1,
  type: 'page',
  statusCode: 200,
  statusClass: '2xx',
  outcome: 'success',
  indexable: true,
  indexability: 'none',
  nofollow: false,
  title: null,
  inlinks: 1,
  issues: 0,
  ...facts,
});

/** The crawl's URLs: the home page, a page, a redirect, a 404, an image, another site's page, a URL never fetched, a 500. */
export const LINKS_URLS: UrlRow[] = [
  url(1, 'https://example.com/', { source: 'seed', depth: 0, title: 'Home', inlinks: 1 }),
  url(2, 'https://example.com/about', { title: 'About', inlinks: 2 }),
  url(3, 'https://example.com/old', {
    statusCode: 301,
    statusClass: '3xx',
    outcome: 'redirect',
    indexable: false,
    indexability: 'redirected',
  }),
  url(4, 'https://example.com/gone', {
    statusCode: 404,
    statusClass: '4xx',
    outcome: 'clientError',
    indexable: false,
    indexability: 'clientError',
    inlinks: 2,
  }),
  url(5, 'https://example.com/img/logo.png', { type: 'image', indexable: null, indexability: null }),
  url(6, 'https://other.example/partner', {
    isInternal: false,
    statusCode: null,
    statusClass: 'noResponse',
    outcome: 'robotsDisallowed',
    indexable: false,
    indexability: 'blockedByRobots',
  }),
  url(7, 'https://example.com/hidden', {
    fetched: false,
    statusCode: null,
    statusClass: null,
    outcome: null,
    indexable: null,
    indexability: null,
  }),
  url(8, 'https://example.com/error', {
    statusCode: 500,
    statusClass: '5xx',
    outcome: 'serverError',
    indexable: false,
    indexability: 'serverError',
  }),
];

const link = (fromId: number, toId: number, facts: Partial<LinkFacts> = {}): LinkFacts => ({
  fromId,
  toId,
  kind: 'hyperlink',
  text: null,
  position: 'content',
  noFollow: false,
  sponsored: false,
  ugc: false,
  imageLink: false,
  fragment: null,
  ...facts,
});

/**
 * The crawl's 13 links, in crawl order. Of them: 3 lead to a 4xx or 5xx (broken), 1 to a redirect, 2 are nofollow, 1
 * is a hyperlink with no text, 1 leads to another site, 2 are a page's links to itself, and 9 are hyperlinks.
 */
export const LINKS_FACTS: LinkFacts[] = [
  link(1, 1, { kind: 'canonical', position: 'head' }),
  link(1, 2, { text: 'About us', position: 'navigation' }),
  link(1, 3, { text: 'Old page' }),
  link(1, 4, { text: 'Gone' }),
  link(1, 5, { kind: 'image', text: 'Logo', position: 'header' }),
  link(1, 6, { text: 'Partner', noFollow: true, sponsored: true }),
  link(1, 7, { text: null, noFollow: true }),
  link(2, 1, { text: 'Home', position: 'navigation' }),
  link(2, 4, { text: 'Gone again', position: 'footer' }),
  link(2, 8, { text: 'Broken server' }),
  link(3, 2, { kind: 'redirect', position: null }),
  link(2, 2, { text: 'About (this page)', fragment: 'top' }),
  link(2, 5, { kind: 'image', text: null }),
];

/** What the tests count on, said once: the grid, each preset and each facet land on these. */
export const LINKS_COUNTS = {
  total: 13,
  broken: 3,
  redirects: 1,
  nofollow: 2,
  noText: 1,
  external: 1,
  hyperlinks: 9,
  images: 2,
} as const;

export function testLinkRows(
  facts: readonly LinkFacts[] = LINKS_FACTS,
  urls: readonly UrlRow[] = LINKS_URLS,
): LinkRow[] {
  return linkRows(facts, urls);
}

export interface LinksSidecarOptions {
  item?: Partial<CrawlListItem>;
  /** Whether the crawl has its URL summary; it has by default. */
  summary?: SummaryState;
  links?: LinkRow[];
  urls?: UrlRow[];
  /** `GET /api/link-columns` fails. */
  columnsFail?: boolean;
  /** `POST /links/facets` fails. */
  facetsFail?: boolean;
  /** `POST /links` fails with this. */
  linksProblem?: Parameters<typeof problem>[0];
}

export interface LinksSidecarState {
  links: LinkRow[];
  urls: UrlRow[];
  summary: SummaryState;
  /** While set, every links read (pages and facets) waits for it: a read that takes a while. */
  hold: Promise<void> | null;
  /** Only links reads this says yes to wait for `hold`; every one, when it isn't set. */
  holds: ((query: GridQuery) => boolean) | null;
}

/**
 * The routes of a crawl with links: the grid, its facets and columns, the URL grid over the same URLs (for a URL's
 * words by its id), and the standard ones. `state` can be changed between reads: a crawl that grows, a read that
 * waits.
 */
export function linksRoutes(options: LinksSidecarOptions = {}): { routes: FakeRoute[]; state: LinksSidecarState } {
  const item = testCrawlItem({ fetchedUrlCount: 7, pageCount: 2, ...options.item });
  const state: LinksSidecarState = {
    links: options.links ?? testLinkRows(LINKS_FACTS, options.urls ?? LINKS_URLS),
    urls: options.urls ?? LINKS_URLS,
    summary: options.summary ?? { available: true, reason: null },
    hold: null,
    holds: null,
  };
  const failure = { type: 'internal', title: 'Something went wrong in the engine.', status: 500 } as const;
  const waits = async (query: GridQuery) => {
    if (state.hold && (state.holds === null || state.holds(query))) await state.hold;
  };
  const routes: FakeRoute[] = [
    ['GET', /^\/api\/link-columns$/, () => (options.columnsFail ? problem(failure) : json(LINK_COLUMNS))],
    [
      'POST',
      /^\/api\/crawls\/[0-9a-f]{16}\/links$/,
      async (request: TransportRequest) => {
        const query = request.body as GridQuery;
        await waits(query);
        if (request.signal?.aborted) throw new DOMException('Aborted', 'AbortError');
        if (options.linksProblem) return problem(options.linksProblem);
        const answer = answerLinkQuery(state.links, query, state.summary);
        return answer.ok ? json(answer.page) : problem(answer.problem);
      },
    ],
    [
      'POST',
      /^\/api\/crawls\/[0-9a-f]{16}\/links\/facets$/,
      async (request: TransportRequest) => {
        const query = (request.body ?? {}) as LinkFacetsQuery;
        await waits({
          filters: query.filters ?? [],
          sort: [],
          limit: 1,
          ...(query.search ? { search: query.search } : {}),
        });
        if (options.facetsFail) return problem(failure);
        const answer = answerLinkFacets(state.links, query, state.summary);
        return answer.ok ? json(answer.page) : problem(answer.problem);
      },
    ],
    [
      'POST',
      /^\/api\/crawls\/[0-9a-f]{16}\/urls$/,
      (request: TransportRequest) => {
        const answer = answerGridQuery(state.urls, request.body as GridQuery, state.summary);
        return answer.ok ? json(answer.page) : problem(answer.problem);
      },
    ],
    ...standardRoutes(item).filter(([, pattern]) => !pattern.source.endsWith('urls$')),
  ];
  return { routes, state };
}
