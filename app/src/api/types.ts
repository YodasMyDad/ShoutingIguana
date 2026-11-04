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
}

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
  /** URLs with a stored outcome, as a running crawl's `fetched` counts them. The library's "URLs". */
  fetchedUrlCount: number;
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
}

export type AlertLevel = 'info' | 'warning' | 'error';

export interface AlertEvent {
  crawlId: string;
  level: AlertLevel;
  /** Stable: `blocked`, `mostly-forbidden`, `site-down`... Key text and actions on it. */
  code: string;
  /** Engine text; may include text from the site. */
  message: string;
  at: string;
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
