// A crawl's URL grid as the mock answers it, for the URL grid's stories and tests: its pages, its facets and its
// columns, with a state a story can change between reads (a crawl that grows, a read that waits, a list of columns
// that can't be read).
import type { FacetsQuery, GridQuery, SummaryState, UrlRow } from '../api/grids';
import { answerFacets, answerGridQuery } from '../api/mock/gridQuery';
import { URL_COLUMNS } from '../api/mock/urlColumns';
import type { CrawlListItem } from '../api/types';
import { ISSUES_CHECKS } from './issuesSidecar';
import { json, problem, standardRoutes, testCrawlItem, type FakeRoute } from './fakeSidecar';

const PATHS = [
  '',
  'garden-furniture/',
  'garden-furniture/rattan-sofa-sets/',
  'garden-furniture/rattan-sofa-sets/harpswell-grey-outdoor-metal-patio-sofa-set-with-coffee-table/',
  'blog/how-to-store-garden-furniture-in-winter/',
  'contact-us/',
  'old-page/',
  'images/hero.jpg',
  'css/site.css',
  'private/',
];

/**
 * URLs of one of everything the grid has words for: pages that answered, a 404 and a 500, a redirect, an image and a
 * stylesheet, a URL robots.txt refused, one not checked, one never crawled, another site's, and one with a title too
 * long for its column. `count` repeats the pattern under numbered paths, for a crawl of many URLs.
 */
export function testUrlRows(count = 12): UrlRow[] {
  return Array.from({ length: count }, (_, index): UrlRow => {
    const id = index + 1;
    const kind = index % 12;
    const path = `${PATHS[kind % PATHS.length] ?? ''}${index >= 12 ? `?page=${Math.floor(index / 12)}` : ''}`;
    const base: UrlRow = {
      id,
      url: kind === 10 ? `https://other.example.org/${path}` : `https://example.com/${path}`,
      fetched: true,
      isInternal: kind !== 10,
      source: id === 1 ? 'seed' : 'link',
      depth: Math.min(kind, 4),
      type: kind === 7 ? 'image' : kind === 8 ? 'stylesheet' : 'page',
      statusCode: 200,
      statusClass: '2xx',
      outcome: 'success',
      indexable: true,
      indexability: 'none',
      title: `Garden furniture ${id}`,
      description: `Everything for the garden, ${id}.`,
      h1: `Garden furniture ${id}`,
      h1Count: 1,
      wordCount: 300 + ((id * 37) % 900),
      outlinks: 20 + (id % 7),
      sizeBytes: 20_000 + id * 311,
      responseMs: 80 + ((id * 53) % 2_400),
      ttfbMs: 40 + ((id * 17) % 300),
      titleLength: 30 + (id % 40),
      descriptionLength: 120 + (id % 50),
      canonicalUrl: `https://example.com/${path}`,
      noindex: false,
      nofollow: false,
      inSitemap: id % 3 !== 0,
      inlinks: 400 - ((id * 31) % 400),
      followedInlinks: 380 - ((id * 31) % 380),
      nofollowInlinks: id % 5,
      linkingPages: 90 - ((id * 7) % 90),
      issues: id % 4,
      errors: id % 6 === 0 ? 1 : 0,
      warnings: id % 4 === 0 ? 1 : 0,
      notices: id % 2,
    };
    switch (kind) {
      case 3:
        return {
          ...base,
          title:
            'Harpswell Grey Outdoor Metal Patio Sofa Set With Coffee Table, Cushions and a Weatherproof Cover | Garden Furniture Sets',
        };
      case 5:
        return {
          ...base,
          statusCode: 301,
          statusClass: '3xx',
          outcome: 'redirect',
          indexable: false,
          indexability: 'redirected',
          redirectTarget: 'https://example.com/contact/',
          title: null,
          wordCount: null,
        };
      case 6:
        return {
          ...base,
          statusCode: 404,
          statusClass: '4xx',
          outcome: 'clientError',
          indexable: false,
          indexability: 'clientError',
          title: null,
          wordCount: null,
        };
      case 7:
      case 8:
        return { ...base, title: null, h1: null, h1Count: null, wordCount: null, description: null };
      case 9:
        return {
          ...base,
          statusCode: null,
          statusClass: 'noResponse',
          outcome: 'robotsDisallowed',
          indexable: false,
          indexability: 'blockedByRobots',
          title: null,
          wordCount: null,
          responseMs: null,
          ttfbMs: null,
          sizeBytes: null,
        };
      case 10:
        return {
          ...base,
          fetched: false,
          statusCode: null,
          statusClass: null,
          outcome: null,
          indexable: null,
          indexability: null,
          title: null,
          wordCount: null,
          responseMs: null,
          ttfbMs: null,
          sizeBytes: null,
          depth: 2,
        };
      case 11:
        return {
          ...base,
          fetched: false,
          statusCode: null,
          statusClass: null,
          outcome: 'notChecked',
          indexable: null,
          indexability: null,
          title: null,
          wordCount: null,
          responseMs: null,
          ttfbMs: null,
          sizeBytes: null,
        };
      default:
        return id % 5 === 0
          ? {
              ...base,
              statusCode: 500,
              statusClass: '5xx',
              outcome: 'serverError',
              indexable: false,
              indexability: 'serverError',
              title: null,
              wordCount: null,
            }
          : base;
    }
  });
}

export interface UrlsSidecarOptions {
  item?: Partial<CrawlListItem>;
  summary?: SummaryState;
  rows?: UrlRow[];
  /** `GET /api/url-columns` fails. */
  columnsFail?: boolean;
  /** `POST /urls` fails with this. */
  urlsProblem?: Parameters<typeof problem>[0];
}

export interface UrlsSidecarState {
  rows: UrlRow[];
  summary: SummaryState;
  /** While set, every page read waits for it: a read that takes a while. */
  hold: Promise<void> | null;
}

/** The routes of a crawl's URL grid, and the standard ones. `state` can be changed between reads. */
export function urlsRoutes(options: UrlsSidecarOptions = {}): { routes: FakeRoute[]; state: UrlsSidecarState } {
  const item = testCrawlItem({ fetchedUrlCount: 10, pageCount: 6, ...options.item });
  const state: UrlsSidecarState = {
    rows: options.rows ?? testUrlRows(),
    summary: options.summary ?? { available: true, reason: null },
    hold: null,
  };
  const failure = { type: 'internal', title: 'Something went wrong in the engine.', status: 500 } as const;
  const routes: FakeRoute[] = [
    ['GET', /^\/api\/url-columns$/, () => (options.columnsFail ? problem(failure) : json(URL_COLUMNS))],
    [
      'POST',
      /^\/api\/crawls\/[0-9a-f]{16}\/urls$/,
      async (request) => {
        if (options.urlsProblem) return problem(options.urlsProblem);
        if (state.hold) await state.hold;
        const answer = answerGridQuery(state.rows, request.body as GridQuery, state.summary);
        return answer.ok ? json(answer.page) : problem(answer.problem);
      },
    ],
    [
      'POST',
      /^\/api\/crawls\/[0-9a-f]{16}\/urls\/facets$/,
      (request) => {
        const answer = answerFacets(state.rows, request.body as FacetsQuery, state.summary);
        return answer.ok ? json(answer.page) : problem(answer.problem);
      },
    ],
    ['POST', /^\/api\/crawls\/[0-9a-f]{16}\/analyze$/, () => new Response(null, { status: 202 })],
    ['GET', /^\/api\/checks$/, () => json(ISSUES_CHECKS)],
    ...standardRoutes(item).filter(([, pattern]) => !pattern.source.endsWith('urls$')),
  ];
  return { routes, state };
}
