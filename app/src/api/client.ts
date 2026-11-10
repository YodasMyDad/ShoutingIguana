// The typed client for the sidecar (docs/v2-sidecar-api.md). Every call goes through the current transport, and every
// failure becomes one of the typed errors in ./errors: a ProblemError for the contract's problem+json, otherwise
// SidecarUnavailableError, SidecarUnreachableError or UnexpectedResponseError.
import { ProblemError, UnexpectedResponseError, parseProblem } from './errors';
import type {
  FacetsPage,
  FacetsQuery,
  GridPage,
  GridQuery,
  IssueGroupPage,
  IssueRow,
  LinkColumn,
  LinkFacetsPage,
  LinkFacetsQuery,
  LinkRow,
  RobotsBlockedPage,
  RobotsBlockedQuery,
  RobotsFile,
  RobotsOriginPage,
  SitemapList,
  UrlColumn,
  UrlLookup,
  UrlRow,
} from './grids';
import type { HtmlSource, LinkPage, LinksQuery, UrlDetail } from './inspector';
import { getTransport, type TransportRequest } from './transport';
import type {
  AlertEvent,
  Analysis,
  Checks,
  ContinueCrawlRequest,
  CrawlListItem,
  CrawlOptionDefaults,
  CrawlOptions,
  CrawlStats,
  Health,
  IssuePage,
  StartCrawlRequest,
  ValidationResult,
} from './types';

type Signal = AbortSignal | undefined;

/** Sends one call and reads its answer. A body-less success (202, 204) resolves with undefined. */
export async function call<T>(request: TransportRequest): Promise<T> {
  const response = await getTransport().send(request);
  if (!response.ok) throw await toError(response);
  if (response.status === 204 || response.status === 202) {
    await response.body?.cancel();
    return undefined as T;
  }
  const text = await response.text();
  if (text.length === 0) return undefined as T;
  try {
    return JSON.parse(text) as T;
  } catch {
    throw new UnexpectedResponseError(response.status, `The body isn't JSON: ${text.slice(0, 200)}`);
  }
}

async function toError(response: Response): Promise<Error> {
  const text = await response.text().catch(() => '');
  let body: unknown = null;
  try {
    body = text ? JSON.parse(text) : null;
  } catch {
    // Not JSON: reported as unexpected below.
  }
  const problem = parseProblem(body);
  if (problem) return new ProblemError(problem);
  return new UnexpectedResponseError(
    response.status,
    text ? `HTTP ${response.status}: ${text.slice(0, 500)}` : `HTTP ${response.status} with no body`,
  );
}

const crawl = (id: string) => `/api/crawls/${encodeURIComponent(id)}`;

const get = <T>(path: string, signal?: Signal) => call<T>({ method: 'GET', path, signal });
const post = <T>(path: string, body?: unknown, signal?: Signal) => call<T>({ method: 'POST', path, body, signal });

export const api = {
  // App
  health: (signal?: Signal) => get<Health>('/api/health', signal),
  shutdown: () => post<undefined>('/api/shutdown'),

  // Checks
  checks: (signal?: Signal) => get<Checks>('/api/checks', signal),

  // Crawl options
  crawlOptionDefaults: (signal?: Signal) => get<CrawlOptionDefaults>('/api/crawl-options/defaults', signal),
  /** Saves "my defaults". The start URL, header values and proxy credentials are never saved. */
  saveCrawlOptionDefaults: (options: CrawlOptions) =>
    call<CrawlOptionDefaults>({ method: 'PUT', path: '/api/crawl-options/defaults', body: options }),
  /** Field-keyed messages; always 200 for options that parse. */
  validateCrawlOptions: (options: CrawlOptions, signal?: Signal) =>
    post<ValidationResult>('/api/crawl-options/validate', options, signal),

  // Library
  crawls: (signal?: Signal) => get<CrawlListItem[]>('/api/crawls', signal),
  crawl: (id: string, signal?: Signal) => get<CrawlListItem>(crawl(id), signal),
  /** Adds a crawl file from anywhere: an absolute path ending in `.sicrawl`. */
  openCrawl: (path: string) => post<CrawlListItem>('/api/crawls/open', { path }),
  /** Removes it from the library, not from disk. Undone by restoreCrawl. */
  removeCrawl: (id: string) => call<undefined>({ method: 'DELETE', path: crawl(id) }),
  restoreCrawl: (id: string) => post<CrawlListItem>(`${crawl(id)}/restore`),
  /** Deletes the file. Only after a confirmation that names the crawl. */
  deleteCrawlFile: (id: string) => post<undefined>(`${crawl(id)}/delete-file`),

  // Crawl control
  startCrawl: (request: StartCrawlRequest) => post<CrawlListItem>('/api/crawls', request),
  pauseCrawl: (id: string) => post<undefined>(`${crawl(id)}/pause`),
  resumeCrawl: (id: string) => post<undefined>(`${crawl(id)}/resume`),
  stopCrawl: (id: string) => post<undefined>(`${crawl(id)}/stop`),
  /** The stored options: header names with empty values, and hasRedactedProxyCredentials. */
  crawlOptions: (id: string, signal?: Signal) => get<CrawlOptions>(`${crawl(id)}/options`, signal),
  /** Resumes from the file. `needs-secrets` names what must be given again. */
  continueCrawl: (id: string, request: ContinueCrawlRequest = {}) =>
    post<CrawlListItem>(`${crawl(id)}/continue`, request),
  /** The alerts since the sidecar started, oldest first. */
  alerts: (id: string, signal?: Signal) => get<AlertEvent[]>(`${crawl(id)}/alerts`, signal),

  // Analysis
  analyze: (id: string) => post<undefined>(`${crawl(id)}/analyze`),
  cancelAnalysis: (id: string) => post<undefined>(`${crawl(id)}/analyze/cancel`),
  analysis: (id: string, signal?: Signal) => get<Analysis>(`${crawl(id)}/analysis`, signal),

  // Data
  stats: (id: string, signal?: Signal) => get<CrawlStats>(`${crawl(id)}/stats`, signal),
  issues: (id: string, checkId: string, page: { limit: number; offset: number }, signal?: Signal) =>
    get<IssuePage>(
      `${crawl(id)}/issues/${encodeURIComponent(checkId)}?limit=${page.limit}&offset=${page.offset}`,
      signal,
    ),

  // Grids (E3a). One fixed URL per grid, so one preflight per session.
  urlColumns: (signal?: Signal) => get<UrlColumn[]>('/api/url-columns', signal),
  urls: (id: string, query: GridQuery, signal?: Signal) => post<GridPage<UrlRow>>(`${crawl(id)}/urls`, query, signal),
  urlFacets: (id: string, query: FacetsQuery, signal?: Signal) =>
    post<FacetsPage>(`${crawl(id)}/urls/facets`, query, signal),
  /** A URL of the crawl by its text: `url-not-found` when the crawl has none, `invalid-query` when it isn't a URL. */
  lookupUrl: (id: string, url: string, signal?: Signal) => post<UrlLookup>(`${crawl(id)}/urls/lookup`, { url }, signal),
  /** One URL in full. `url-not-found` for an id the crawl doesn't have, `invalid-query` without one. */
  urlDetail: (id: string, urlId: number, signal?: Signal) =>
    post<UrlDetail>(`${crawl(id)}/urls/inspect`, { urlId }, signal),
  urlLinks: (id: string, urlId: number, direction: 'inlinks' | 'outlinks', query: LinksQuery, signal?: Signal) =>
    post<LinkPage>(`${crawl(id)}/urls/${direction}`, { urlId, ...query }, signal),
  /** The stored HTML, streamed as one JSON object. `bytes` is null for a page over 512 MB. */
  urlSource: (id: string, urlId: number, signal?: Signal) =>
    post<HtmlSource>(`${crawl(id)}/urls/source`, { urlId }, signal),
  issueGroups: (id: string, checkId: string, page: { offset: number; limit: number }, signal?: Signal) =>
    post<IssueGroupPage>(`${crawl(id)}/issues/${encodeURIComponent(checkId)}/groups`, page, signal),
  issueRows: (id: string, checkId: string, query: GridQuery, signal?: Signal) =>
    post<GridPage<IssueRow>>(`${crawl(id)}/issues/${encodeURIComponent(checkId)}/rows`, query, signal),

  // Links, sitemaps and robots.txt (E3b).
  linkColumns: (signal?: Signal) => get<LinkColumn[]>('/api/link-columns', signal),
  /** The links grid: one sort key, `id` or a column of the target. `group` isn't taken. */
  links: (id: string, query: GridQuery, signal?: Signal) =>
    post<GridPage<LinkRow>>(`${crawl(id)}/links`, query, signal),
  linkFacets: (id: string, query: LinkFacetsQuery, signal?: Signal) =>
    post<LinkFacetsPage>(`${crawl(id)}/links/facets`, query, signal),
  sitemaps: (id: string, signal?: Signal) => get<SitemapList>(`${crawl(id)}/sitemaps`, signal),
  /** The URL grid for one sitemap (an index: every sitemap under it). `sitemap-not-found` for an id the crawl lacks. */
  sitemapUrls: (id: string, sitemapId: number, query: GridQuery, signal?: Signal) =>
    post<GridPage<UrlRow>>(`${crawl(id)}/sitemaps/urls`, { sitemapId, ...query }, signal),
  robotsOrigins: (id: string, page: { offset: number; limit: number }, signal?: Signal) =>
    post<RobotsOriginPage>(`${crawl(id)}/robots`, page, signal),
  /** One origin's robots.txt, line by line. `origin-not-found` for an origin the crawl has none for. */
  robotsFile: (id: string, origin: string, signal?: Signal) =>
    post<RobotsFile>(`${crawl(id)}/robots/file`, { origin }, signal),
  robotsBlocked: (id: string, query: RobotsBlockedQuery, signal?: Signal) =>
    post<RobotsBlockedPage>(`${crawl(id)}/robots/blocked`, query, signal),
};

export type Api = typeof api;
