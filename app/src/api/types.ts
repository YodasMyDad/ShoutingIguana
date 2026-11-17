import type { ExportJob } from './exports';
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
  /**
   * The score: the category in Simple's words, such as "Search descriptions". Null while it has none. Optional here, as
   * a sidecar from before the score doesn't send it.
   */
  plainTitle?: string | null;
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
  /**
   * Where to fix what it flags: `sources` when each URL is something other URLs refer to (a 404, a redirect, a sitemap
   * entry), so a view shows where it's found (`api.foundOn`); `targets` when each page holds the bad references (it links
   * to broken URLs), so a view shows what its issue names (`api.issueTargets`); `none` otherwise. Left out by a
   * sidecar from before it, which reads as `none`.
   */
  references?: CheckReferences;
  /**
   * The score: the check finds one fault of the whole site, not a page's (robots.txt search engines can't read, no
   * HTTPS), which the score counts as the whole site whatever its count. Optional here, as every field from here on
   * is: a sidecar from before the score doesn't send them.
   */
  siteWide?: boolean;
  /**
   * The score: the check reports one issue for the whole site (or one for each other version of it), so its count isn't
   * a share of the pages: say "checked once, for the whole site", not "1 of 265 pages". For wording only. Every
   * `siteWide` check is reported once.
   */
  reportedOnce?: boolean;
  /**
   * The score: the check in plain words, beside the technical texts. Every check of the catalog has them. A check has
   * all of `plainTitle`, `plainWhat`, `plainFix`, `fixer` and `plainPassed`, or none: null in every plain field means
   * it has no plain words (a check newer than its words), and the technical text is never sent in their place. Limits
   * in them come filled in ("60 characters"), from the current analysis settings, or from a crawl's analysis's with
   * `GET /api/checks?crawl={id}`.
   */
  plainTitle?: string | null;
  /** What's wrong and why it matters, a sentence a string. The first stands alone: lists show only it. */
  plainWhat?: string[] | null;
  /** How to fix it, a step a string. */
  plainFix?: string[] | null;
  /** Who usually makes the fix. */
  fixer?: CheckFixer | null;
  /** The good news when the check finds nothing, as a fact: "Every page has a title". */
  plainPassed?: string | null;
  /** What a fault in the site's template usually is, in one sentence. Null for most checks: 26 have one. */
  plainOneFix?: string | null;
  /**
   * What the check looks for, as a short noun phrase ("A rules file that search engines can't read"), for a check whose
   * title is a sentence about the site. Null for most checks: 12 have one.
   */
  plainLooksFor?: string | null;
  /**
   * Evidence (step 3b): the URL columns that show what the check found, in the order to show them, beside each row's
   * `url` and `detail` (`["title", "titleLength"]` for `title-too-long`, `["redirectChain"]` for the redirect checks).
   * Ask `/rows` for them as for any column. Empty for a check whose URL and detail say it all. Left out by a sidecar
   * from before it, which reads as empty.
   */
  evidence?: string[];
  /**
   * Evidence: what to say about each URL the check's issues name, in the order to say it: field names of a target
   * (`IssueTarget`) and of a group's `related` URL (`sizeBytes`, `img`, `final`, `text`, `position`...). Empty for a
   * check that names no URL; left out by a sidecar from before it.
   */
  targetFacts?: TargetFact[];
}

/**
 * A target's field a check's `targetFacts` names: what the URL is (`statusCode`, `final`, `indexability`, `canonical`,
 * `sizeBytes`, `contentType`, `pixelWidth`, `pixelHeight`, `img`) and how the page links to it (`kind`, `text`,
 * `position`, `fragment`, `flags`, `links`). A name this version doesn't know is left unsaid.
 */
export type TargetFact = string;

/** How a check's flagged URLs stand to the links between URLs: where they're found, what they name, or neither. */
export type CheckReferences = 'sources' | 'targets' | 'none';

/** What a check's flagged URLs are: `GET /api/checks` gives it as each check's `subject` (E3b). */
export type CheckSubject = 'page' | 'url';

/** Who usually makes a check's fix: the site's owner in its editor, a developer or the hosting company, or either. */
export type CheckFixer = 'you' | 'developer' | 'either';

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
  /** 0 is no cap: the engine still starts each host slowly and slows down when one asks. */
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
  /** 0 is no cap: the engine still starts each host slowly and slows down when one asks. */
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

/**
 * `POST /api/crawl-options/start-url`: where an address typed without a scheme starts. `typed`: it had its scheme and
 * nothing was asked. `https`: the site answered on https. `http`: only http answered. `noAnswer`: neither did, so https.
 */
export type StartUrlReason = 'typed' | 'https' | 'http' | 'noAnswer';

export interface StartUrlChoice {
  startUrl: string;
  reason: StartUrlReason;
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
   * E3c: the time the crawl has spent crawling, over every run of it, in ms: each run from its start to its end, less
   * the time it was paused, so neither a pause nor the time between a stop and a continue is in it. A running crawl's
   * is the progress event's `crawlingMs`. Null when it isn't known: an engine from before E3c made the crawl, or a run
   * of it. Optional here, as such an engine's sidecar doesn't send it.
   */
  crawlingMs?: number | null;
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
  /**
   * Internal URLs that returned HTML the crawler parsed, as the crawl counts them: every one, Cloudflare's own
   * `/cdn-cgi/` endpoints included, which the analysis doesn't audit. So it can be more than `pagesAnalyzed`.
   */
  pageCount: number;
  /**
   * The pages the last analysis audited, which its score covers: the count to show beside the score. Null before the
   * first analysis; optional here, as a sidecar from before E7 doesn't send it.
   */
  pagesAnalyzed?: number | null;
  /** Null before the first analysis. */
  issues: IssueCounts | null;
  analyzedAt: string | null;
  /**
   * The score: the last analysis's score in brief. Null when there is none: never analysed, no page read, or analysed
   * by an engine from before the score. The whole of it is the analysis's `score`. Optional here, as a sidecar from
   * before the score doesn't send it.
   */
  score?: ScoreSummary | null;
  /** The last analysis no longer stands. False before the first one. */
  outOfDate: boolean;
  sizeBytes: number;
  /**
   * E6: the crawl kept the site's icon, which `api.favicon` then gives. False for a site with no icon the engine keeps,
   * for a crawl made before E6, and for a running crawl until its icon has arrived, a few requests after its first
   * page. Absent from a sidecar built before E6.
   */
  hasFavicon?: boolean;
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
  /**
   * Evidence: a check that doesn't group (`grouping` `none`), of pages, not reported once, flagged at least 90% of the
   * pages analysed and at least 10: probably the site's template, so one fix. False for every other check, whose groups
   * say it instead. Left out by a sidecar from before it.
   */
  likelyTemplate?: boolean;
}

export interface SkipReason {
  /** Stable, to key on: `no-sitemap`, `crawl-unfinished`, `rule-failed`... */
  code: string;
  /** A plain sentence to show beside the check. */
  text: string;
  /**
   * The score: why the check couldn't run, in Simple's words, by the code. Every code the engine gives has them; null
   * for a code it has no words for (never the engine's `text` in its place). Optional here, as a sidecar from before
   * the score doesn't send it.
   */
  plainText?: string | null;
  /** The score: what would let the check run, in Simple's words. Null with `plainText`. */
  plainAction?: string | null;
}

export interface SkippedCheck {
  checkId: string;
  /** Null when an engine from before E2 ran the analysis: show the notes instead. */
  reason: SkipReason | null;
}

export type OutOfDateReason = 'crawlChanged' | 'engineChanged' | 'settingsChanged';

// ----------------------------------------------------------------------------------------- Analysis settings (E6)

export type AnalysisLimitUnit = 'characters' | 'words' | 'bits' | 'bytes' | 'ms';

/** One limit an analysis judges by ("a title longer than this is too long"), with what Settings needs to show it. */
export interface AnalysisLimit {
  /** The limit's name, as the engine's analysis settings and every crawl file have it: `titleMaxLength`. */
  id: string;
  /** The value in force. */
  value: number;
  /** The engine's own value. */
  default: number;
  unit: AnalysisLimitUnit;
  /** The range it may be set within, both ends included. */
  min: number;
  max: number;
  /** One sentence saying what the limit does, to show as it is. */
  description: string;
}

/**
 * `GET /api/analysis-settings`, and what a save or a reset answers with: the limits in force. An analysis that ran with
 * other limits is out of date (`settingsChanged`) from the next read of the library or of its analysis; no event is
 * sent, so read the library again after a save. An analysis under way keeps the limits it began with.
 */
export interface AnalysisSettings {
  /** Each limit's value by name: the body a save and `validate` take. */
  settings: Record<string, number>;
  /** Every limit, in the order Settings shows them. */
  limits: AnalysisLimit[];
  /** True while every limit is at the engine's value. */
  isDefault: boolean;
}

/**
 * Limits to save or to check, by name. Every limit is optional: one left out, or null, takes the engine's value. A
 * value out of its range, or not a whole number, is `invalid-settings` (422) with `errors` (`AnalysisSettingsErrors`);
 * a name that is no limit's is a `bad-request`.
 */
export type AnalysisSettingsChange = Record<string, number | null>;

/**
 * What `invalid-settings` (and `validate`) key their messages by: a limit's name (`titleMaxLength`) for a value of its
 * own; `settings` (`ANALYSIS_SETTINGS_WHOLE`) for a rule only the limits together break; and, for a reset,
 * `limits[<index>]` for a name that's no limit's. Show a key with no field of its own above the form.
 */
export type AnalysisSettingsErrors = Record<string, string[]>;

/** The key of a refusal about the analysis's limits as a whole, not one of them. */
export const ANALYSIS_SETTINGS_WHOLE = 'settings';

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
  /**
   * The score: the URLs crawled as they stood when the analysis ran. A check whose `subject` is `url` flagged a share of
   * these, as a `page` check flagged a share of `pagesAnalyzed`: use these two beside a check's count, never the
   * library's counts, which are the crawl's as it is now. Null when an engine from before the score ran the analysis;
   * optional here, as such an engine's sidecar doesn't send it.
   */
  crawledUrlCount?: number | null;
  /**
   * The score: the ids of the checks that found something, in the order to fix them: the most severe; within a
   * severity a `siteWide` check before any other, however small its count; then the most URLs flagged; then catalog
   * order. Take the order from here rather than sorting by count. Checks the user has hidden are in it: hiding is
   * the app's.
   */
  fixFirst?: string[];
  /** The score: the site's score as the analysis stored it, or null when there is none (`noScoreReason` says why). */
  score?: Score | null;
  /** The score: why there is no score; null when there is one. */
  noScoreReason?: NoScoreReason | null;
}

/** The score as a word: 90 and over, 70 and over, 50 and over, under 50. */
export type ScoreBand = 'veryGood' | 'good' | 'needsWork' | 'poor';

/**
 * How much of the site a check's count is: under 1 in 100, 1 in 100 or more, 1 in 10 or more, half or more; or the whole
 * site, for a site-wide fault whatever its count.
 */
export type ScoreExtent = 'few' | 'some' | 'many' | 'most' | 'site';

/** What a score line's count is a share of: the pages read, the URLs crawled, or nothing (a site-wide fault). */
export type ScoreUnit = 'pages' | 'urls' | 'site';

/** Why a score covers only part of the site: the crawl was stopped or failed, or a limit kept internal URLs out. */
export type ScoreIncompleteReason = 'stopped' | 'failed' | 'urlLimit' | 'depthLimit';

/**
 * Why an analysis has no score: it read no page, or it stored none this engine reads (an engine from before the score
 * ran it). A crawl never analysed has no analysis to ask (`not-analyzed`).
 */
export type NoScoreReason = 'noPages' | 'notScored';

/** A score's headline, on a library item. */
export interface ScoreSummary {
  /** 0 to 100. */
  value: number;
  band: ScoreBand;
  /** The crawl didn't read all of the site, so the score is of what it read so far. */
  partial: boolean;
}

/** One check that found something, and what it takes off the score. */
export interface ScoreLine {
  checkId: string;
  /** The check's severity when the analysis ran. */
  severity: Severity;
  /** The URLs it flagged. */
  count: number;
  /** What the count is a share of, as `unit` says. For a `site` line it isn't for showing: say "the whole site". */
  of: number;
  unit: ScoreUnit;
  extent: ScoreExtent;
  /** Points off the score. 0 for a notice. */
  points: number;
}

/** What a score was worked out from: "covers all 265 pages and 82 of 86 checks". */
export interface ScoreCoverage {
  /** The pages the analysis read: its `pagesAnalyzed`. */
  pages: number;
  /** The URLs crawled when the analysis ran: its `crawledUrlCount`. */
  urls: number;
  checksRun: number;
  checksSkipped: number;
  /** The crawl read all it set out to: it finished, and no limit kept internal URLs out. */
  complete: boolean;
  /** Why not; null when it is complete. */
  reason: ScoreIncompleteReason | null;
  /** The site's URLs robots.txt kept the crawler out of, which weren't read and so aren't covered. */
  blockedByRobots: number;
}

/**
 * The site's score, 0 to 100, as the analysis worked it out and stored it (docs/v2-sidecar-api.md, "The score"): every
 * number in it is the analysis's own, so nothing here is to be recounted from the crawl as it is now.
 */
export interface Score {
  /** The formula's version. */
  version: number;
  value: number;
  band: ScoreBand;
  /** The points every line takes off, added up. It can pass 100. */
  lost: number;
  /** The score before the ceiling of 89 that holds while an error check has issues. */
  beforeCeiling: number;
  /** The ceiling is what holds the score: it would be `beforeCeiling` otherwise. */
  heldBySerious: boolean;
  /** The crawl didn't read all of the site: always `!covers.complete`. */
  partial: boolean;
  covers: ScoreCoverage;
  /** Every check that found something, in `fixFirst` order, a notice among them at 0 points. */
  lines: ScoreLine[];
  /** The score with the first five errors and warnings in `fixFirst` order gone: "from 85 to 98". */
  ifFixed: { checkIds: string[]; value: number };
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
  /** E5: the exports running here, by id. Their latest `export` event follows. */
  exports?: string[];
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
 *
 * The figure's own filter isn't always its class's name (docs/v2-sidecar-api.md, Progress, has the table): the event
 * counts block pages as `blocked` and the URLs not checked under `noResponse`, where the grid's `statusClass` files a
 * block page by its status and has a URL not checked in no class. So a class or a status code takes
 * `{ column: 'outcome', op: 'notIn', values: ['blocked'] }` beside it, `blocked` is `outcome` in `['blocked']`,
 * `noResponse` is `outcome` in `['robotsDisallowed', 'timeout', 'networkError', 'processingError', 'notChecked']`, and
 * each `noResponse` reason is `outcome` in that one.
 */
export interface SiteVersionCounts {
  /** The probes with an outcome: among the event's `fetched`. Its classes add up to it. */
  fetched: number;
  statusClasses: StatusClassCounts;
  /** By code as a string: among the event's `statusCodes`. */
  statusCodes: Record<string, number>;
  noResponse: NoResponseCounts;
}

/**
 * E4: `robotsTxt`, `otherSitesRobotsTxt`, `crawlDelay`, `backoff`, `heldBack`, `siteDown` or `robotsTxtCoolDown`; 3b:
 * `sectionLimited`.
 */
export type CrawlWaitReason =
  | 'robotsTxt'
  | 'otherSitesRobotsTxt'
  | 'crawlDelay'
  | 'backoff'
  | 'heldBack'
  | 'siteDown'
  | 'robotsTxtCoolDown'
  | 'sectionLimited';

/** E4: something holding part of the crawl up now. Fields that don't apply to its reason are left out. */
export interface CrawlWait {
  reason: CrawlWaitReason;
  host?: string;
  urls?: number;
  durationMs?: number;
  /** For `sectionLimited`: the section the site limits, "/products/". */
  section?: string;
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
  /**
   * The time spent crawling so far, over every run of the crawl: the earlier runs' time, and this run's without its
   * pauses, so it stands still while the crawl is paused, where `elapsedMs` (this run's alone) runs on. Null when an
   * earlier run's time wasn't kept.
   */
  crawlingMs?: number | null;
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
  /**
   * E3c: the time spent crawling over every run, in ms: the runs' added up, as the crawl file has it. That's the library
   * item's for a crawl at rest; while a crawl runs the item's is the progress event's, up to about five seconds ahead
   * of this one. Null when it isn't known (an engine from before E3c made the crawl, or a run of it). Optional here, as
   * such an engine doesn't send it.
   */
  crawlingMs?: number | null;
  /**
   * E3c: when the crawl was crawling: each run, oldest first, with the stretches it spent crawling in. A moment's
   * place on an axis of time spent crawling is the stretches' time up to it, so the time between runs and the time
   * paused close up. Null when the crawl's time isn't known, or it was paused too often to list (over 1,000 stretches).
   */
  runs?: CrawlRun[] | null;
}

/** E3c: one run of a crawl, from its start or its being continued to its being stopped or its end. */
export interface CrawlRun {
  startedAt: string;
  /** Null for a run that hasn't ended: the one going now, or one cut short when its process died. */
  endedAt: string | null;
  /** The time it spent crawling: its stretches added up. */
  crawlingMs: number;
  /** The stretches it was crawling in, oldest first: one for a run never paused, and a pause is the gap between two. */
  crawling: CrawlingStretch[];
}

/** E3c: a stretch of time spent crawling, unbroken by a pause or a stop. */
export interface CrawlingStretch {
  from: string;
  /** When it ended; for the one going now, when the engine last stored it, at most about five seconds ago. */
  to: string;
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
  | { type: 'analysis'; data: AnalysisEvent }
  /** E5: an export as it stands, whole (./exports): as it starts, as it goes on, and as it ends. */
  | { type: 'export'; data: ExportJob };

/** Round 5: what one fetch through a proxy found (`POST /api/proxy/test`). */
export type ProxyTestOutcome =
  'ok' | 'proxyAuthRequired' | 'proxyRefused' | 'proxyUnreachable' | 'siteUnreachable' | 'timeout' | 'tls' | 'failed';

/** The proxy test's answer: `message` is one plain sentence, shown as it is. `status` is null with no HTTP answer. */
export interface ProxyTestResult {
  outcome: ProxyTestOutcome;
  status: number | null;
  elapsedMs: number;
  message: string;
}
