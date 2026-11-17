// A sidecar for the inspector's tests and stories: URLs in full as `/urls/inspect` gives them, their links both ways
// paged as `/urls/inlinks` and `/urls/outlinks` page them, their HTML, and lookup. Each URL is a plain object a test
// builds with `testUrlDetail`, so a state (not fetched, no body, a loop) is the few members that make it.
import type { UrlLookup } from '../api/grids';
import type {
  ContentLimits,
  HtmlSource,
  LinkPage,
  LinkRow,
  LinksQuery,
  PageImage,
  PageImages,
  UrlDetail,
  UrlResponse,
} from '../api/inspector';
import { json, problem, type FakeRoute } from './fakeSidecar';

export const TEST_LIMITS: ContentLimits = {
  titleMinPixels: 200,
  titleMaxPixels: 580,
  descriptionMinPixels: 400,
  descriptionMaxPixels: 985,
  minWords: 200,
  slowResponseMs: 1_000,
  largePageBytes: 1_048_576,
  largeImageBytes: 204_800,
  imageAltMaxLength: 125,
  maxUrlLength: 115,
  fromAnalysis: true,
};

export function testResponse(overrides: Partial<UrlResponse> = {}): UrlResponse {
  return {
    fetched: true,
    outcome: 'success',
    statusCode: 200,
    contentType: 'text/html; charset=utf-8',
    httpVersion: '2.0',
    crawledAt: '2026-11-01T10:16:02Z',
    ttfbMs: 113,
    responseMs: 117,
    sizeBytes: 35_468,
    bodyBytes: 220_480,
    blockReason: null,
    error: null,
    ...overrides,
  };
}

/** A page fetched and read, indexable, with one issue: the plain case every state departs from. */
export function testUrlDetail(overrides: Partial<UrlDetail> = {}): UrlDetail {
  const id = overrides.id ?? 2;
  const url = overrides.url ?? 'https://example.com/garden/patio-set/';
  return {
    id,
    url,
    isInternal: true,
    type: 'page',
    discovery: {
      source: 'link',
      depth: 2,
      foundOn: { id: 1, url: 'https://example.com/' },
      foundVia: 'hyperlink',
      fetchMode: 'crawl',
      state: 'done',
      notFetchedBecause: null,
    },
    response: testResponse(),
    redirects: { hops: [], final: { id, url, statusCode: 200 }, loop: false, tooLong: false },
    indexability: {
      indexable: true,
      reason: 'none',
      judgedBy: 'analysis',
      tests: [
        { test: 'answered', outcome: 'pass', statusCode: 200 },
        { test: 'robotsTxt', outcome: 'pass' },
        { test: 'noindex', outcome: 'pass', meta: false, header: false },
        { test: 'canonical', outcome: 'pass', canonical: { id, url, statusCode: 200 }, canonicalUrl: url },
      ],
    },
    content: {
      titles: [{ text: 'Harpswell Grey Outdoor Metal Patio Sofa Set With Coffee Table', length: 61, width: 597 }],
      descriptions: [
        {
          text: 'Harpswell garden patio set features a loveseat, two chairs & coffee table with a ripple glass tabletop.',
          length: 103,
          width: 638,
        },
      ],
      h1s: [{ text: 'Harpswell Metal Patio Set', length: 25, width: null }],
      headings: [1, 4, 9, 0, 0, 0],
      wordCount: 701,
      language: 'en-GB',
      charset: 'utf-8',
      robots: [],
      metaRefresh: null,
      images: 26,
      imagesMissingAlt: 0,
    },
    limits: TEST_LIMITS,
    links: { inlinks: 3, followedInlinks: 3, nofollowInlinks: 0, linkingPages: 2, outlinks: 4, inSitemap: true },
    headers: [
      { name: 'content-type', value: 'text/html; charset=utf-8' },
      { name: 'cache-control', value: 'private' },
      { name: 'x-frame-options', value: 'SAMEORIGIN' },
      { name: 'x-frame-options', value: 'sameorigin' },
    ],
    structuredData: [
      {
        valid: true,
        types: ['Product'],
        json: JSON.stringify({
          '@context': 'https://schema.org',
          '@type': 'Product',
          name: 'Harpswell Metal Patio Set',
          offers: { '@type': 'Offer', price: 499.99, priceCurrency: 'GBP', availability: 'https://schema.org/InStock' },
        }),
        error: null,
        truncated: false,
      },
    ],
    hreflang: [],
    issues: [
      { checkId: 'title-too-long', severity: 'notice', detail: '597 px, over 580 (61 characters)', related: null },
    ],
    namedBy: { total: 0, checks: [] },
    summary: { available: true, reason: null },
    source: { stored: true },
    ...overrides,
  };
}

export function testLinkRow(overrides: Partial<LinkRow> = {}): LinkRow {
  return {
    id: 1,
    url: 'https://example.com/',
    kind: 'hyperlink',
    text: 'Home',
    position: 'navigation',
    flags: [],
    followed: true,
    fragment: null,
    statusCode: 200,
    indexable: true,
    fetched: true,
    ...overrides,
  };
}

/** A page of links, as `/urls/inlinks` and `/urls/outlinks` page them: by kind, from an offset. */
export function linkPage(rows: readonly LinkRow[], query: LinksQuery): LinkPage {
  const kinds = query.kinds;
  const matching = kinds ? rows.filter((row) => row.kind !== null && kinds.includes(row.kind)) : rows;
  const offset = query.offset ?? 0;
  const limit = query.limit ?? 100;
  const page = matching.slice(offset, offset + limit);
  return {
    rows: page,
    offset,
    total: matching.length,
    next: offset + page.length < matching.length ? String(offset + page.length) : null,
  };
}

export const TEST_HTML = [
  '<!doctype html>',
  '<html lang="en-GB">',
  '  <head>',
  '    <title>Harpswell Grey Outdoor Metal Patio Sofa Set With Coffee Table</title>',
  '    <meta name="description" content="Harpswell garden patio set">',
  '    <link rel="canonical" href="https://example.com/garden/patio-set/">',
  '  </head>',
  '  <body>',
  '    <h1>Harpswell Metal Patio Set</h1>',
  '    <p>A patio set for the garden: a loveseat, two chairs and a coffee table.</p>',
  '    <a href="/garden/">Garden furniture</a>',
  '    <script>document.title = "<b>never markup</b>";</script>',
  '  </body>',
  '</html>',
].join('\n');

const NOT_FOUND = () =>
  problem({ type: 'url-not-found', title: 'This crawl doesn’t have that URL.', status: 404, detail: 'No such URL.' });

const FAILED = () =>
  problem({
    type: 'internal',
    title: 'Something went wrong in the engine.',
    status: 500,
    detail: 'SqliteException: database disk image is malformed',
  });

const never = () => new Promise<Response>(() => undefined);

/** `pending` never answers, `error` fails as the engine does. */
type Answer<T> = T | 'pending' | 'error';

export interface InspectorSidecarOptions {
  /** The URLs the crawl has, by id. An id it doesn't have is `url-not-found`. */
  urls?: Record<number, Answer<UrlDetail>> | ((urlId: number) => Answer<UrlDetail> | undefined);
  inlinks?: Record<number, Answer<readonly LinkRow[]>>;
  outlinks?: Record<number, Answer<readonly LinkRow[]>>;
  /** Each URL's HTML. A URL without one answers with the test page for a page, `notPage` for anything else. */
  source?: Record<number, Answer<HtmlSource>>;
  /** URLs by the text typed in "Go to URL…". Anything else is `url-not-found`. */
  lookup?: Record<string, UrlLookup>;
  /** Delays every inspector answer: how long a slow engine takes. */
  delayMs?: number;
  /** Step 3b: each URL's images (`/urls/images`). A URL the crawl has without any shows none. */
  images?: Record<number, Answer<readonly PageImage[]>>;
}

/** The routes the inspector reads. Put them before the screen's own (`standardRoutes`, `issuesRoutes`). */
export function inspectorRoutes(options: InspectorSidecarOptions = {}): FakeRoute[] {
  const urlFor = (urlId: number) =>
    typeof options.urls === 'function' ? options.urls(urlId) : (options.urls ?? { 2: testUrlDetail() })[urlId];
  const wait = async () => {
    if (options.delayMs) await new Promise((resolve) => setTimeout(resolve, options.delayMs));
  };
  const answer = async <T>(value: Answer<T> | undefined, ok: (value: T) => Response): Promise<Response> => {
    await wait();
    if (value === undefined) return NOT_FOUND();
    if (value === 'pending') return never();
    if (value === 'error') return FAILED();
    return ok(value);
  };
  const links = (direction: 'inlinks' | 'outlinks'): FakeRoute => [
    'POST',
    new RegExp(`^/api/crawls/[0-9a-f]{16}/urls/${direction}$`),
    (request) => {
      const query = request.body as LinksQuery & { urlId: number };
      if (urlFor(query.urlId) === undefined) return NOT_FOUND();
      return answer(options[direction]?.[query.urlId] ?? [], (rows) => json(linkPage(rows, query)));
    },
  ];
  return [
    [
      'POST',
      /^\/api\/crawls\/[0-9a-f]{16}\/urls\/inspect$/,
      (request) => answer(urlFor((request.body as { urlId: number }).urlId), (detail) => json(detail)),
    ],
    links('inlinks'),
    links('outlinks'),
    [
      'POST',
      /^\/api\/crawls\/[0-9a-f]{16}\/urls\/source$/,
      (request) => {
        const { urlId } = request.body as { urlId: number };
        const detail = urlFor(urlId);
        if (detail === undefined) return NOT_FOUND();
        const fallback: HtmlSource =
          typeof detail === 'object' && detail.content !== null
            ? { stored: true, html: TEST_HTML, bytes: TEST_HTML.length, truncated: false }
            : { stored: false, reason: 'notPage' };
        return answer(options.source?.[urlId] ?? fallback, (source) => json(source));
      },
    ],
    [
      'POST',
      /^\/api\/crawls\/[0-9a-f]{16}\/urls\/lookup$/,
      async (request) => {
        await wait();
        const text = (request.body as { url: string }).url.trim();
        if (text === '' || /\s/.test(text)) {
          return problem({
            type: 'invalid-query',
            title: 'The engine couldn’t run this view.',
            status: 422,
            errors: { url: ['That isn’t a URL.'] },
          });
        }
        const found = options.lookup?.[text];
        return found ? json(found) : NOT_FOUND();
      },
    ],
    // Step 3b: a page's images, each file once, the largest first.
    [
      'POST',
      /^\/api\/crawls\/[0-9a-f]{16}\/urls\/images$/,
      (request) => {
        const { urlId, limit = 100 } = request.body as { urlId: number; limit?: number };
        if (urlFor(urlId) === undefined) return NOT_FOUND();
        return answer(options.images?.[urlId] ?? [], (rows) =>
          json({
            urlId,
            total: rows.length,
            links: rows.reduce((sum, row) => sum + row.links, 0),
            rows: rows.slice(0, limit),
            pixelSizes: rows.length === 0 || rows.some((row) => typeof row.target.pixelWidth === 'number'),
          } satisfies PageImages),
        );
      },
    ],
  ];
}

/** A page's image file, as `/urls/images` lists it: an image over 200 KB, by default. */
export function testPageImage(facts: Partial<PageImage> = {}, target: Partial<PageImage['target']> = {}): PageImage {
  return {
    id: 40,
    url: 'https://www.gardenfurnituresets.co.uk/wp-content/uploads/harpswell-hero.jpg',
    links: 1,
    text: 'Harpswell metal patio set on a lawn',
    positions: ['content'],
    target: {
      statusCode: 200,
      outcome: 'success',
      contentType: 'image/jpeg',
      sizeBytes: 2_202_009,
      pixelWidth: 4000,
      pixelHeight: 3000,
      final: null,
      indexability: null,
      canonical: null,
      ...target,
    },
    ...facts,
  };
}
