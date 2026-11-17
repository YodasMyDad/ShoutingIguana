// The typed client for the sidecar (docs/v2-sidecar-api.md). Every call goes through the current transport, and every
// failure becomes one of the typed errors in ./errors: a ProblemError for the contract's problem+json, otherwise
// SidecarUnavailableError, SidecarUnreachableError or UnexpectedResponseError.
import { ProblemError, UnexpectedResponseError, parseProblem } from './errors';
import {
  continueWithSecrets,
  forgetCrawlSecrets,
  keepCrawlSecrets,
  keepSecrets,
  keptSecrets,
  secretsOf,
  withSecrets,
  type CrawlSecrets,
} from '../crawl/secrets';
import { currentHiddenChecks } from '../settings/hiddenChecks';
import type { ExportJob, ExportRequest, ReportOptions } from './exports';
import type {
  FacetsPage,
  FacetsQuery,
  GridPage,
  GridQuery,
  UrlGridQuery,
  IssueGroupPage,
  IssueGroupsQuery,
  IssueRow,
  LinkColumn,
  LinkEndsPage,
  LinkEndsQuery,
  LinkFacetsPage,
  LinkFacetsQuery,
  LinkRow,
  RobotsBlockedPage,
  RobotsBlockedQuery,
  RobotsFile,
  RobotsOriginPage,
  RobotsOriginsQuery,
  SearchResults,
  SitemapList,
  UrlColumn,
  UrlLookup,
  UrlRow,
} from './grids';
import type { FoundOnPage, HtmlSource, IssueTargetsPage, LinkPage, LinksQuery, UrlDetail } from './inspector';
import { getTransport, type TransportRequest } from './transport';
import type {
  AlertEvent,
  Analysis,
  AnalysisSettings,
  AnalysisSettingsChange,
  Checks,
  ContinueCrawlRequest,
  CrawlHistory,
  CrawlListItem,
  CrawlOptionDefaults,
  CrawlOptions,
  CrawlStats,
  Health,
  IssuePage,
  StartCrawlRequest,
  ValidationResult,
  StartUrlChoice,
  ProxyTestResult,
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

/** Sends one call whose answer is text, not JSON: the report's document. */
export async function callText(request: TransportRequest): Promise<string> {
  const response = await getTransport().send(request);
  if (!response.ok) throw await toError(response);
  return response.text();
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

/**
 * Sends one call whose answer is a file's bytes, not JSON. A success with no body (204) resolves with null: there is
 * nothing to show. A failure is the typed error any call's is.
 */
export async function callBytes(request: TransportRequest): Promise<Blob | null> {
  const response = await getTransport().send(request);
  if (!response.ok) throw await toError(response);
  if (response.status === 204) {
    await response.body?.cancel();
    return null;
  }
  return response.blob();
}

const exportJob = (exportId: string) => `/api/exports/${encodeURIComponent(exportId)}`;

/** The report's options as its query string: only what was given, so the sidecar's defaults stand for the rest. */
function reportQuery(options: ReportOptions): string {
  const query = new URLSearchParams();
  if (options.cut !== undefined) query.set('cut', options.cut);
  if (options.examples !== undefined) query.set('examples', String(options.examples));
  if (options.lookExamples !== undefined) query.set('lookExamples', String(options.lookExamples));
  if (options.hiddenChecks?.length) query.set('hiddenChecks', options.hiddenChecks.join(','));
  const text = query.toString();
  return text ? `?${text}` : '';
}

const get = <T>(path: string, signal?: Signal) => call<T>({ method: 'GET', path, signal });
const post = <T>(path: string, body?: unknown, signal?: Signal) => call<T>({ method: 'POST', path, body, signal });

/**
 * A grid's read (its page, facets, link ends) or export with the checks hidden in its crawl, so its issue counts leave
 * them out as every other count does (round 5 review, also noted). Nothing is added while none is hidden; a query
 * that names its own is sent as it is.
 */
function withHiddenChecks<T extends object>(crawlId: string, query: T): T {
  if ('hiddenChecks' in query) return query;
  const hidden = currentHiddenChecks(crawlId);
  return hidden.length > 0 ? { ...query, hiddenChecks: hidden } : query;
}

/** The exports of a grid, which take the grid's query and so its hidden checks. */
const GRID_EXPORTS: ReadonlySet<ExportRequest['kind']> = new Set(['urls', 'links', 'sitemapUrls', 'issueRows']);

/** The secrets of the crawls removed from the library while the app runs, for Undo (restoreCrawl) to put back. */
const removedSecrets = new Map<string, CrawlSecrets>();

export const api = {
  // App
  health: (signal?: Signal) => get<Health>('/api/health', signal),
  shutdown: () => post<undefined>('/api/shutdown'),

  // Checks
  checks: (signal?: Signal) => get<Checks>('/api/checks', signal),

  // Crawl options
  /** "My defaults", with the proxy's password and the headers' values back in from the credential store (the app). */
  crawlOptionDefaults: async (signal?: Signal) => {
    const defaults = await get<CrawlOptionDefaults>('/api/crawl-options/defaults', signal);
    return { ...defaults, options: withSecrets(defaults.options, await keptSecrets('defaults')) };
  },
  /**
   * Saves "my defaults". The start URL, header values and proxy credentials are never saved by the engine: given
   * `secrets` (the options as typed), the app keeps those in the credential store instead.
   */
  saveCrawlOptionDefaults: async (options: CrawlOptions, secrets?: CrawlOptions) => {
    const saved = await call<CrawlOptionDefaults>({
      method: 'PUT',
      path: '/api/crawl-options/defaults',
      body: options,
    });
    if (!secrets) return saved;
    const kept = secretsOf(secrets);
    await keepSecrets('defaults', kept);
    return { ...saved, options: withSecrets(saved.options, kept) };
  },
  /** Field-keyed messages; always 200 for options that parse. */
  validateCrawlOptions: (options: CrawlOptions, signal?: Signal) =>
    post<ValidationResult>('/api/crawl-options/validate', options, signal),
  /**
   * Where an address typed without a scheme starts: https when the site answers there, else http when only http does.
   * Asked with `options` (the saved defaults when left out); an address with its scheme comes back as typed.
   */
  startUrlFor: (address: string, options?: CrawlOptions, signal?: Signal) =>
    post<StartUrlChoice>('/api/crawl-options/start-url', options ? { address, options } : { address }, signal),
  /**
   * Round 5: fetches `url` once through the proxy, its user name and password included (used for this fetch only), and
   * says what happened. 200 whatever the fetch found; `bad-request` for a proxy or address that can't be tried.
   */
  testProxy: (proxyUrl: string, url: string, signal?: Signal) =>
    post<ProxyTestResult>('/api/proxy/test', { proxyUrl, url }, signal),

  // Library
  crawls: (signal?: Signal) => get<CrawlListItem[]>('/api/crawls', signal),
  crawl: (id: string, signal?: Signal) => get<CrawlListItem>(crawl(id), signal),
  /**
   * E6: the site's icon as the crawl kept it: a PNG, ICO, GIF, JPEG or WebP, its `type` the one its bytes are. Null for
   * a crawl that has none (`hasFavicon` on its item says which to expect), where the app draws a letter. It needs the
   * token, so show it from an object URL of the blob: an `<img src>` can't ask for it.
   */
  favicon: (id: string, signal?: Signal) => callBytes({ method: 'GET', path: `${crawl(id)}/favicon`, signal }),
  /** Adds a crawl file from anywhere: an absolute path ending in `.sicrawl`. */
  openCrawl: (path: string) => post<CrawlListItem>('/api/crawls/open', { path }),
  /**
   * Removes it from the library, not from disk. Undone by restoreCrawl. Its secrets leave the credential store at once
   * (an app closed before the Undo runs out leaves none behind), held here for restoreCrawl to put back.
   */
  removeCrawl: async (id: string) => {
    await call<undefined>({ method: 'DELETE', path: crawl(id) });
    const kept = await forgetCrawlSecrets(id);
    if (kept) removedSecrets.set(id, kept);
  },
  restoreCrawl: async (id: string) => {
    const item = await post<CrawlListItem>(`${crawl(id)}/restore`);
    const kept = removedSecrets.get(id);
    removedSecrets.delete(id);
    if (kept) await keepSecrets(`crawl:${id}`, kept);
    return item;
  },
  /** Deletes the file. Only after a confirmation that names the crawl. Its secrets are forgotten with it. */
  deleteCrawlFile: async (id: string) => {
    await post<undefined>(`${crawl(id)}/delete-file`);
    removedSecrets.delete(id);
    await forgetCrawlSecrets(id);
  },

  // Crawl control
  /** Starts a crawl. In the app, what it was given that the engine doesn't keep is kept for Continue. */
  startCrawl: async (request: StartCrawlRequest) => {
    const item = await post<CrawlListItem>('/api/crawls', request);
    keepCrawlSecrets(item.id, request.options);
    return item;
  },
  pauseCrawl: (id: string) => post<undefined>(`${crawl(id)}/pause`),
  resumeCrawl: (id: string) => post<undefined>(`${crawl(id)}/resume`),
  stopCrawl: (id: string) => post<undefined>(`${crawl(id)}/stop`),
  /** The stored options: header names with empty values, and hasRedactedProxyCredentials. */
  crawlOptions: async (id: string, signal?: Signal) =>
    withSecrets(await get<CrawlOptions>(`${crawl(id)}/options`, signal), await keptSecrets(`crawl:${id}`)),
  /**
   * Resumes from the file. `needs-secrets` names what must be given again: in the app, what the crawl was last
   * given is sent from the credential store first, so it's asked for only when nothing was kept.
   */
  continueCrawl: async (id: string, request: ContinueCrawlRequest = {}) => {
    const item = await post<CrawlListItem>(
      `${crawl(id)}/continue`,
      continueWithSecrets(request, await keptSecrets(`crawl:${id}`)),
    );
    if (request.proxyUrl !== undefined || request.extraHeaders !== undefined) {
      keepCrawlSecrets(id, { proxyUrl: request.proxyUrl ?? null, extraHeaders: request.extraHeaders ?? null });
    }
    return item;
  },
  /** The crawl's alerts from its file, oldest first, up to the last 1,000: every run's. */
  alerts: (id: string, signal?: Signal) => get<AlertEvent[]>(`${crawl(id)}/alerts`, signal),
  /** E4: the crawl's progress since it started, for the "found and crawled" chart. */
  history: (id: string, signal?: Signal) => get<CrawlHistory>(`${crawl(id)}/history`, signal),

  // Analysis
  analyze: (id: string) => post<undefined>(`${crawl(id)}/analyze`),
  cancelAnalysis: (id: string) => post<undefined>(`${crawl(id)}/analyze/cancel`),
  analysis: (id: string, signal?: Signal) => get<Analysis>(`${crawl(id)}/analysis`, signal),

  // Analysis settings (E6): the limits every analysis here judges by.
  analysisSettings: (signal?: Signal) => get<AnalysisSettings>('/api/analysis-settings', signal),
  /**
   * Saves limits and answers with all of them. `invalid-settings` (422) saves nothing; its `errors` are keyed as
   * `AnalysisSettingsErrors` says: by a limit's name, or `settings` for the limits as a whole.
   */
  saveAnalysisSettings: (limits: AnalysisSettingsChange) =>
    call<AnalysisSettings>({ method: 'PUT', path: '/api/analysis-settings', body: limits }),
  /** What a save of the same limits would refuse (`AnalysisSettingsErrors`); always 200 for a body of limits. Nothing is saved. */
  validateAnalysisSettings: (limits: AnalysisSettingsChange, signal?: Signal) =>
    post<ValidationResult>('/api/analysis-settings/validate', limits, signal),
  /** Puts the named limits back to the engine's values; all of them when none is named. A name that's no limit's is `invalid-settings` on `limits[<index>]`. */
  resetAnalysisSettings: (limits?: string[]) =>
    post<AnalysisSettings>('/api/analysis-settings/reset', limits ? { limits } : undefined),

  // Data
  stats: (id: string, signal?: Signal) => get<CrawlStats>(`${crawl(id)}/stats`, signal),
  issues: (id: string, checkId: string, page: { limit: number; offset: number }, signal?: Signal) =>
    get<IssuePage>(
      `${crawl(id)}/issues/${encodeURIComponent(checkId)}?limit=${page.limit}&offset=${page.offset}`,
      signal,
    ),

  // Grids (E3a). One fixed URL per grid, so one preflight per session.
  urlColumns: (signal?: Signal) => get<UrlColumn[]>('/api/url-columns', signal),
  urls: (id: string, query: UrlGridQuery, signal?: Signal) =>
    post<GridPage<UrlRow>>(`${crawl(id)}/urls`, withHiddenChecks(id, query), signal),
  urlFacets: (id: string, query: FacetsQuery, signal?: Signal) =>
    post<FacetsPage>(`${crawl(id)}/urls/facets`, withHiddenChecks(id, query), signal),
  /** A URL of the crawl by its text: `url-not-found` when the crawl has none, `invalid-query` when it isn't a URL. */
  lookupUrl: (id: string, url: string, signal?: Signal) => post<UrlLookup>(`${crawl(id)}/urls/lookup`, { url }, signal),
  /**
   * E6: search as it's typed, for the palette. `limit` is 1 to 50 (10 when left out). An empty `text` finds nothing and
   * reads the crawl's text, so ask it as the palette opens. `invalid-query` for a text over 1,000 characters.
   */
  search: (id: string, text: string, limit?: number, signal?: Signal) =>
    post<SearchResults>(`${crawl(id)}/search`, limit === undefined ? { text } : { text, limit }, signal),
  /** One URL in full. `url-not-found` for an id the crawl doesn't have, `invalid-query` without one. */
  urlDetail: (id: string, urlId: number, signal?: Signal) =>
    post<UrlDetail>(`${crawl(id)}/urls/inspect`, { urlId }, signal),
  urlLinks: (id: string, urlId: number, direction: 'inlinks' | 'outlinks', query: LinksQuery, signal?: Signal) =>
    post<LinkPage>(`${crawl(id)}/urls/${direction}`, { urlId, ...query }, signal),
  /**
   * Where each of up to 100 URLs is found, in one read: its links, the pages they're on, the first `limit` of them (0 to
   * 50, 5 when left out) and the sitemaps listing it. `invalid-query` for none, too many or too many listed.
   */
  foundOn: (id: string, urlIds: readonly number[], limit?: number, signal?: Signal) =>
    post<FoundOnPage>(`${crawl(id)}/urls/found-on`, limit === undefined ? { urlIds } : { urlIds, limit }, signal),
  /** The stored HTML, streamed as one JSON object. `bytes` is null for a page over 512 MB. */
  urlSource: (id: string, urlId: number, signal?: Signal) =>
    post<HtmlSource>(`${crawl(id)}/urls/source`, { urlId }, signal),
  /** A page of a check's groups; `sort` (evidence) asks for an order other than the check's own. */
  issueGroups: (id: string, checkId: string, page: IssueGroupsQuery, signal?: Signal) =>
    post<IssueGroupPage>(`${crawl(id)}/issues/${encodeURIComponent(checkId)}/groups`, page, signal),
  /**
   * What each of up to 100 of a check's flagged pages names (its broken targets, its canonical), the first `limit` of
   * each (0 to 50, 5 when left out). `check-not-found`, `not-analyzed` and `invalid-query` as `/rows`.
   */
  issueTargets: (id: string, checkId: string, urlIds: readonly number[], limit?: number, signal?: Signal) =>
    post<IssueTargetsPage>(
      `${crawl(id)}/issues/${encodeURIComponent(checkId)}/targets`,
      limit === undefined ? { urlIds } : { urlIds, limit },
      signal,
    ),
  issueRows: (id: string, checkId: string, query: UrlGridQuery, signal?: Signal) =>
    post<GridPage<IssueRow>>(
      `${crawl(id)}/issues/${encodeURIComponent(checkId)}/rows`,
      withHiddenChecks(id, query),
      signal,
    ),

  // Links, sitemaps and robots.txt (E3b).
  linkColumns: (signal?: Signal) => get<LinkColumn[]>('/api/link-columns', signal),
  /** The links grid: one sort key, `id` or a column of the target. `group` isn't taken. */
  links: (id: string, query: GridQuery, signal?: Signal) =>
    post<GridPage<LinkRow>>(`${crawl(id)}/links`, withHiddenChecks(id, query), signal),
  linkFacets: (id: string, query: LinkFacetsQuery, signal?: Signal) =>
    post<LinkFacetsPage>(`${crawl(id)}/links/facets`, withHiddenChecks(id, query), signal),
  /**
   * E6: the URLs at one end of the links a view matches, each once: its targets, or the pages its links are on. Its
   * `total` is the URL grid's `matching` under the same links as `linkEnds`.
   */
  linkEnds: (id: string, query: LinkEndsQuery, signal?: Signal) =>
    post<LinkEndsPage>(`${crawl(id)}/links/ends`, withHiddenChecks(id, query), signal),
  sitemaps: (id: string, signal?: Signal) => get<SitemapList>(`${crawl(id)}/sitemaps`, signal),
  /** The URL grid for one sitemap (an index: every sitemap under it). `sitemap-not-found` for an id the crawl lacks. */
  sitemapUrls: (id: string, sitemapId: number, query: UrlGridQuery, signal?: Signal) =>
    post<GridPage<UrlRow>>(`${crawl(id)}/sitemaps/urls`, withHiddenChecks(id, { sitemapId, ...query }), signal),
  /** E6: `search` finds origins by name and `blocked` filters them; the page then has `matching` and `counts`. */
  robotsOrigins: (id: string, query: RobotsOriginsQuery, signal?: Signal) =>
    post<RobotsOriginPage>(`${crawl(id)}/robots`, query, signal),
  /** One origin's robots.txt, line by line. `origin-not-found` for an origin the crawl has none for. */
  robotsFile: (id: string, origin: string, signal?: Signal) =>
    post<RobotsFile>(`${crawl(id)}/robots/file`, { origin }, signal),
  robotsBlocked: (id: string, query: RobotsBlockedQuery, signal?: Signal) =>
    post<RobotsBlockedPage>(`${crawl(id)}/robots/blocked`, query, signal),

  // Export and the report (E5). The sidecar writes the file; the job's progress and its end come as `export` events.
  /**
   * Starts an export to a path from a save dialog, and answers with the job, running. Refused at once when it can't
   * be an export (`invalid-export`, `invalid-query`), or its file can't be written (`export-not-writable`,
   * `export-file-in-use`); what only the crawl can say (`not-analyzed`, `needs-analysis`...) ends the job as failed.
   */
  startExport: (id: string, request: ExportRequest) =>
    post<ExportJob>(`${crawl(id)}/export`, GRID_EXPORTS.has(request.kind) ? withHiddenChecks(id, request) : request),
  /** An export by its id: one running, or one of the last 64 that ended. `export-not-found` otherwise. */
  exportJob: (exportId: string, signal?: Signal) => get<ExportJob>(exportJob(exportId), signal),
  /** Cancels a running export, which then leaves no file. One that has ended stays as it ended. */
  cancelExport: (exportId: string) => post<undefined>(`${exportJob(exportId)}/cancel`),
  /**
   * The report's document: one self-contained HTML file, as text, for a sandboxed frame's `srcdoc`. `not-analyzed`
   * when the crawl has never been analysed.
   */
  report: (id: string, options: ReportOptions = {}, signal?: Signal) =>
    callText({ method: 'GET', path: `${crawl(id)}/report${reportQuery(options)}`, signal }),
};

export type Api = typeof api;
