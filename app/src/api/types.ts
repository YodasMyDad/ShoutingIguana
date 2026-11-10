import type { CheckGrouping } from './grids';

// The sidecar's JSON, as docs/v2-sidecar-api.md defines it (E1 and E2; the grids' E3a shapes are in grids.ts and
// inspector.ts). camelCase, enums as camelCase strings, times in
// ISO 8601 UTC, durations in ms, sizes in bytes. Crawled text (titles, anchors, details, alert messages) is exactly as
// stored: render it as text, never as HTML. Ids and enums the engine may extend are typed as strings.

export type Severity = 'error' | 'warning' | 'notice';

/** The library's statuses, also carried by the `crawl` and `progress` events. */
export type CrawlStatus =
  | 'running'
  | 'paused'
  | 'stopping'
  | 'analyzing'
  | 'stopped'
  | 'interrupted'
  | 'completed'
  | 'failed'
  | 'inUse'
  | 'unreadable';

export const CRAWL_STATUSES: readonly CrawlStatus[] = [
  'running',
  'paused',
  'stopping',
  'analyzing',
  'stopped',
  'interrupted',
  'completed',
  'failed',
  'inUse',
  'unreadable',
];

/** A status the contract defines: the live stream drops any other at the boundary. */
export function isCrawlStatus(value: unknown): value is CrawlStatus {
  return CRAWL_STATUSES.includes(value as CrawlStatus);
}

/** Statuses that mean this sidecar is crawling it right now. */
export const CRAWLING_STATUSES: readonly CrawlStatus[] = ['running', 'paused', 'stopping'];

/** Statuses that mean this sidecar is working on it: crawling or analysing. */
export const LIVE_STATUSES: readonly CrawlStatus[] = ['running', 'paused', 'stopping', 'analyzing'];

export function isCrawling(status: CrawlStatus): boolean {
  return CRAWLING_STATUSES.includes(status);
}

export function isLive(status: CrawlStatus): boolean {
  return LIVE_STATUSES.includes(status);
}

// ------------------------------------------------------------------------------------------------------------ App

export interface Health {
  version: string;
  engineVersion: string;
  dataDir: string;
  stateDir: string;
  pid: number;
}

// --------------------------------------------------------------------------------------------------------- Checks

export interface CheckCategory {
  id: string;
  /** A plain name such as "Meta descriptions", shown as it is. */
  title: string;
}

export interface Check {
  id: string;
  category: string;
  severity: Severity;
  title: string;
  /** What is wrong. */
  description: string;
  /** Why it matters. */
  why: string;
  /** What to do about it. */
  fix: string;
  /** A page that explains the check, or null. */
  learnMore: string | null;
  /** How its issues group on the Issues screen (E3a). */
  grouping: CheckGrouping;
  /**
   * What the URLs it flags are (E3b): `page` when every one is a page, as `pageCount` counts them, so its count is a part
   * of the pages crawled; `url` when it can flag any URL (a missing image, a redirect, a URL that didn't answer), so its
   * count is of URLs and can pass the pages crawled. Every sidecar from E3b sends it; fixtures captured before don't.
   */
  subject?: CheckSubject;
}

/** What a check's flagged URLs are: `GET /api/checks` gives it as each check's `subject` (E3b). */
export type CheckSubject = 'page' | 'url';

export interface Checks {
  categories: CheckCategory[];
  /** In catalog order. */
  checks: Check[];
}

// ---------------------------------------------------------------------------------------------------- Crawl options

export type UserAgentPreset =
  | 'shoutingIguana'
  | 'chromeDesktop'
  | 'chromeMobile'
  | 'googlebotSmartphone'
  | 'googlebotDesktop'
  | 'bingbot'
  | 'custom';

/** Every public `CrawlOptions` property. In a request, a property left out (or null) takes the engine's default. */
export interface CrawlOptions {
  startUrl?: string | null;
  maxUrls?: number | null;
  maxDepth?: number | null;
  concurrency?: number | null;
  externalConcurrency?: number | null;
  /** 0 is no cap, with the engine's automatic backoff. */
  maxRequestsPerSecond?: number | null;
  userAgent?: UserAgentPreset | null;
  customUserAgent?: string | null;
  acceptLanguage?: string | null;
  /** Header name to value. Stored crawls and saved defaults keep the names with empty values. */
  extraHeaders?: Record<string, string> | null;
  respectRobotsTxt?: boolean | null;
  respectCrawlDelay?: boolean | null;
  checkResources?: boolean | null;
  checkExternalLinks?: boolean | null;
  includeSubdomains?: boolean | null;
  stayInStartFolder?: boolean | null;
  followNofollow?: boolean | null;
  crawlCanonicals?: boolean | null;
  crawlHreflang?: boolean | null;
  crawlPagination?: boolean | null;
  discoverSitemaps?: boolean | null;
  extraSitemaps?: string[] | null;
  storeHtml?: boolean | null;
  useCookies?: boolean | null;
  proxyUrl?: string | null;
  requestTimeoutSeconds?: number | null;
  maxRetries?: number | null;
  maxHtmlBytes?: number | null;
  /** In responses only: the proxy's user name and password were removed, so they must be given again. */
  hasRedactedProxyCredentials?: boolean | null;
}

export type CrawlOptionField = keyof CrawlOptions;

export interface SpeedPreset {
  /** `polite`, `standard` or `fast`. */
  id: string;
  concurrency: number;
  /** 0 is no cap, with the engine's automatic backoff. */
  maxRequestsPerSecond: number;
}

export interface CrawlOptionDefaults {
  /** The user's saved defaults, or the engine's. Never a start URL, header value or proxy password. */
  options: CrawlOptions;
  speedPresets: SpeedPreset[];
}

/** Messages keyed by option field. Empty when the options are valid. */
export type FieldErrors = Record<string, string[]>;

export interface ValidationResult {
  errors: FieldErrors;
}

// --------------------------------------------------------------------------------------------------------- Library

/** Checks with at least one issue, by severity: the Issues screen's rows, not issue rows. */
export interface IssueCounts {
  error: number;
  warning: number;
  notice: number;
}

export interface CrawlListItem {
  /** 16 hex characters, stable while the file stays where it is. */
  id: string;
  path: string;
  fileName: string;
  /** The start URL's host without a leading `www.`; the library groups by it. */
  site: string | null;
  startUrl: string | null;
  status: CrawlStatus;
  startedAt: string | null;
  finishedAt: string | null;
  updatedAt: string | null;
  /**
   * URLs with a stored outcome, as a running crawl's `fetched` counts them: the URLs crawled and those that weren't
   * checked (never asked, their host having stopped answering), which the URL grid has as not fetched. No grid filter
   * gives this count: `crawledUrlCount` is the grid's.
   */
  fetchedUrlCount: number;
  /**
   * E3c: the URLs crawled, as the URL grid counts them (`fetched` is true): `fetchedUrlCount` less the URLs that
   * weren't checked. The count to show as "N URLs"; a running crawl's is the progress event's `crawled`. Optional here,
   * as an engine from before E3c doesn't send it.
   */
  crawledUrlCount?: number;
  /** Internal URLs that returned HTML the crawler parsed. */
  pageCount: number;
  /** Null before the first analysis. */
  issues: IssueCounts | null;
  analyzedAt: string | null;
  /** The last analysis no longer stands. False before the first one. */
  outOfDate: boolean;
  sizeBytes: number;
  /** A file opened from elsewhere that isn't there any more. */
  missing: boolean;
  /** Why the file couldn't be read, in plain words: only when the status is `unreadable`. */
  error: string | null;
}

// --------------------------------------------------------------------------------------------------- Crawl control

export interface StartCrawlRequest {
  options: CrawlOptions;
  /** True unless false is given. A crawl that stops isn't analysed by itself. */
  analyzeWhenDone?: boolean;
}

/** What may change when a crawl continues. A field left out keeps the stored value. */
export interface ContinueCrawlRequest {
  concurrency?: number;
  maxRequestsPerSecond?: number;
  requestTimeoutSeconds?: number;
  /** The headers to send from now on, with their values: crawl files keep only the names. */
  extraHeaders?: Record<string, string>;
  /** Needed again when the stored proxy had a user name and password. */
  proxyUrl?: string;
  /** Continue without the stored headers that weren't given again, instead of asking for them. */
  skipMissingHeaders?: boolean;
}

// ------------------------------------------------------------------------------------------------------- Analysis

export interface CheckCount {
  checkId: string;
  /** URLs the check flagged: 0 means it ran and found nothing. */
  count: number;
}

export interface SkipReason {
  /** Stable, to key on: `no-sitemap`, `crawl-unfinished`, `rule-failed`... */
  code: string;
  /** A plain sentence to show beside the check. */
  text: string;
}

export interface SkippedCheck {
  checkId: string;
  /** Null when an engine from before E2 ran the analysis: show the notes instead. */
  reason: SkipReason | null;
}

export type OutOfDateReason = 'crawlChanged' | 'engineChanged' | 'settingsChanged';

export interface Analysis {
  analyzedAt: string;
  engineVersion: string;
  pagesAnalyzed: number;
  durationMs: number;
  /** Null when an engine from before E2 ran the analysis: show the notes. */
  isPartial: boolean | null;
  outOfDate: boolean;
  outOfDateReasons: OutOfDateReason[];
  notes: string[];
  /** Every check that ran, in catalog order. */
  counts: CheckCount[];
  /** Every catalog check missing from the counts: skipped, never 0. */
  skipped: SkippedCheck[];
}

// ------------------------------------------------------------------------------------------------------------ Data

export interface CrawlStats {
  startUrl: string;
  /** As stored: `running` is also what an interrupted crawl leaves behind. */
  status: 'running' | 'paused' | 'stopped' | 'completed' | 'failed';
  startedAt: string;
  finishedAt: string | null;
  engineVersion: string;
  /** Every URL in the file, fetched or not. */
  urlCount: number;
  internalUrlCount: number;
  queuedCount: number;
  linkCount: number;
  sitemapCount: number;
  sitemapErrorCount: number;
  sitemapUrlCount: number;
  unlinkedPageCount: number;
  htmlPageCount: number;
  indexablePageCount: number;
  averageResponseMs: number;
  totalBytes: number;
  /** URLs left out of the indexability breakdown because they weren't checked. */
  notCheckedCount: number;
  outcomes: Record<string, number>;
  /** Keyed by the status code as a string. */
  statusCodes: Record<string, number>;
  skipReasons: Record<string, number>;
  nonIndexableReasons: Record<string, number>;
  /**
   * E3c: the part of `outcomes` and `statusCodes` that is the crawler's own probes of the site's other versions (the
   * URL grid's `source` `siteVersion`), keyed as those are. `outcomes` and `statusCodes` count them among the rest;
   * the site's own are each less its part here. Optional here, as an engine from before E3c doesn't send it.
   */
  siteVersions?: SiteVersionStats;
}

/** E3c: the crawler's probes of the site's other versions with a stored outcome, by outcome and by status code. */
export interface SiteVersionStats {
  fetched: number;
  outcomes: Record<string, number>;
  /** Keyed by the status code as a string. */
  statusCodes: Record<string, number>;
}

export interface IssueRow {
  url: string;
  detail: string | null;
  relatedUrl: string | null;
}

export interface IssuePage {
  rows: IssueRow[];
  /** URLs the check flagged in all; null when the last analysis skipped the check, never 0. */
  total: number | null;
}

// -------------------------------------------------------------------------------------------------- Live events

export interface HelloEvent {
  /** The crawls running, paused or stopping in this process. Their latest progress follows. */
  live: string[];
  /** E4: the crawls being analysed here. Their latest `analysis` event follows. */
  analyzing?: string[];
}

/** E4: what a crawl is doing as a whole. Whether it's paused is its status. */
export type CrawlPhase = 'starting' | 'readingRobotsTxt' | 'crawling' | 'readingSitemaps' | 'retrying' | 'finishing';

export const CRAWL_PHASES: readonly CrawlPhase[] = [
  'starting',
  'readingRobotsTxt',
  'crawling',
  'readingSitemaps',
  'retrying',
  'finishing',
];

/**
 * E4: every URL fetched, once, so they add up to `fetched`. A firewall or bot challenge is `blocked` whatever its
 * status; `other` is a status outside 200–599; `noResponse` is a URL with no status recorded.
 */
export interface StatusClassCounts {
  '2xx': number;
  '3xx': number;
  '4xx': number;
  '5xx': number;
  other: number;
  noResponse: number;
  blocked: number;
}

/** E4: why URLs got no response; they add up to `statusClasses.noResponse`. `notChecked` is unknown, not broken. */
export interface NoResponseCounts {
  robotsDisallowed: number;
  timeout: number;
  networkError: number;
  notChecked: number;
  processingError: number;
}

/**
 * E3c: the part of a progress event's figures that is the crawler's own probes of the site's other versions (http or
 * https, www or not: the URL grid's `source` `siteVersion`), which nothing on the site leads to. Each is shaped as the
 * event's own and counted within it, so a figure less its part here is the URLs the site itself led to: the grid's
 * count with `{ column: 'source', op: 'notIn', values: ['siteVersion'] }` added to the figure's filter.
 */
export interface SiteVersionCounts {
  /** The probes with an outcome: among the event's `fetched`. Its classes add up to it. */
  fetched: number;
  statusClasses: StatusClassCounts;
  /** By code as a string: among the event's `statusCodes`. */
  statusCodes: Record<string, number>;
  noResponse: NoResponseCounts;
}

/** E4: `robotsTxt`, `otherSitesRobotsTxt`, `crawlDelay`, `backoff`, `heldBack`, `siteDown` or `robotsTxtCoolDown`. */
export type CrawlWaitReason =
  'robotsTxt' | 'otherSitesRobotsTxt' | 'crawlDelay' | 'backoff' | 'heldBack' | 'siteDown' | 'robotsTxtCoolDown';

/** E4: something holding part of the crawl up now. Fields that don't apply to its reason are left out. */
export interface CrawlWait {
  reason: CrawlWaitReason;
  host?: string;
  urls?: number;
  durationMs?: number;
}

export interface ProgressEvent {
  crawlId: string;
  status: CrawlStatus;
  elapsedMs: number;
  discovered: number;
  queued: number;
  inFlight: number;
  fetched: number;
  htmlPages: number;
  redirects: number;
  errors: number;
  blocked: number;
  robotsDisallowed: number;
  skipped: number;
  bytesDownloaded: number;
  /** Every URL fetched per second, pages and the rest alike. */
  urlsPerSecond: number;
  // E4 (docs/v2-sidecar-api.md, Progress). Optional here, as an E1 engine doesn't send them; the live store reads each
  // at the boundary, and one that's missing or malformed is null there.
  /** When the numbers were taken. */
  at?: string;
  phase?: CrawlPhase;
  /** The site's own URLs the crawl means to fetch (queued, in flight or fetched), and those it has fetched. */
  internalFound?: number;
  internalFetched?: number;
  /** The URL limit, or null without one. */
  maxUrls?: number | null;
  statusClasses?: StatusClassCounts;
  /** URLs with a status that weren't blocked, by code as a string. */
  statusCodes?: Record<string, number>;
  noResponse?: NoResponseCounts;
  /** At most five per reason, the longest first. */
  waits?: CrawlWait[];
  /** How many more waits there are, by reason, beyond those listed. */
  moreWaits?: Partial<Record<CrawlWaitReason, number>>;
  // E3c. Optional here, as an engine from before E3c doesn't send them.
  /**
   * The URLs crawled so far, as the URL grid counts them (`fetched` is true): `fetched` less `noResponse.notChecked`.
   * It ends at the library item's `crawledUrlCount`.
   */
  crawled?: number;
  /** The part of the figures above that is the crawler's own probes of the site's other versions. */
  siteVersions?: SiteVersionCounts;
}

export type AlertLevel = 'info' | 'warning' | 'error';

export interface AlertEvent {
  crawlId: string;
  /** E4: numbers the crawl's alerts from 1, across every run. An alert is its `id` and its `at` together. */
  id?: number;
  level: AlertLevel;
  /** Stable: `blocked`, `mostly-forbidden`, `site-down`... Key text and actions on it. */
  code: string;
  /** Engine text; may include text from the site. */
  message: string;
  at: string;
  /** E4: what the alert is about, for its action (`host`, `origin`, `url`, `count`...); null for one without facts. */
  data?: AlertData | null;
}

/** E4: an alert's facts. Fields that don't apply to its code are left out. */
export interface AlertData {
  host?: string;
  origin?: string;
  url?: string;
  reason?: string;
  count?: number;
  sampled?: number;
  urlCount?: number;
  durationMs?: number;
}

/** E4: `GET /api/crawls/{id}/history`, one point. File points leave out what the file can't say. */
export interface HistoryPoint {
  at: string;
  fetched: number;
  htmlPages: number;
  internalFetched: number;
  internalFound: number;
  errors: number;
  blocked: number;
  elapsedMs?: number;
  discovered?: number;
  queued?: number;
  skipped?: number;
}

/** E4: a crawl's progress since it started, for the "found and crawled" chart. */
export interface CrawlHistory {
  crawlId: string;
  /** When the crawl first started: the chart's zero. */
  startedAt: string;
  /** Where the points taken live begin; null for a crawl not live here. */
  liveFrom: string | null;
  /** Oldest first, at most about 600, ending at the latest. */
  points: HistoryPoint[];
}

export interface CrawlEvent {
  crawlId: string;
  status: CrawlStatus;
  /** Why the crawl or its analysis failed. */
  error?: string;
  /** The problem type when the UI can act on it: `crawl-busy` means trying again can work. */
  errorType?: string;
}

/** E4: analysis progress by phase. */
export interface AnalysisEvent {
  crawlId: string;
  phase: string;
  done: number;
  total: number;
}

export type SidecarEvent =
  | { type: 'hello'; data: HelloEvent }
  | { type: 'progress'; data: ProgressEvent }
  | { type: 'alert'; data: AlertEvent }
  | { type: 'crawl'; data: CrawlEvent }
  | { type: 'library'; data: Record<string, never> }
  | { type: 'analysis'; data: AnalysisEvent };
