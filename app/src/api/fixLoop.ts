// The fix loop's JSON (docs/v2-sidecar-api.md, "Changes since another crawl" and "Rechecks", both "(fix loop)"; the
// design is docs/v2-fix-loop.md): a crawl compared with the site's previous crawl, by check, by cause and by URL, and
// a problem's pages fetched again into the same crawl file ("Recheck these 5 pages": "4 of 5 fixed"). The shapes are
// the sidecar's as built (S1, S2), written by hand as the app's other types are. Ids and enums the engine may extend
// are typed as their known values; every count is the engine's, hidden checks included: the app leaves those out.
import type { CrawlSignature } from './types';
import type { OutOfDateReason, ScoreSummary, Severity } from './types';

// ------------------------------------------------------------------------------------------- The library's part

/** How far two crawls compare: in full, in part (they covered different things), or by their counts alone. */
export type Comparability = 'full' | 'partial' | 'countsOnly';

/**
 * The crawl a library item is compared with by default (`previous` on an item): the newest crawl of the same `siteKey`
 * that started before it, has an analysis that read a page, and isn't live, in use, interrupted, missing or unreadable;
 * one with the same start URL first. Null for a site's first crawl, and for a crawl never analysed.
 */
export interface PreviousCrawl {
  id: string;
  /** The previous crawl's own `startedAt`: the "3 Nov" of "since 3 Nov". */
  startedAt: string | null;
  /** Its score in brief, as its own item has it; null when it has none. */
  score: ScoreSummary | null;
  comparable: Comparability;
  /**
   * The two analyses were made by other rules (engine, checks or limits), or the previous crawl changed since its own:
   * counts only until the previous crawl is analysed again (`POST /analyze` on it). Simple does that by itself.
   */
  needsAnalysis: boolean;
}

/**
 * What changed since `previous`, check by check, from the two crawls' cached summaries (no file opened): `changes` on a
 * library item. Every check is listed, hidden or not, in this crawl's fix-first order, then catalog order. Null with no
 * `previous`, and while the crawl is live here, interrupted, in use elsewhere or unreadable.
 */
export interface CrawlChanges {
  /** The score now less then; null when either has none, or they were scored by different versions. */
  score: number | null;
  /** Either crawl didn't read all it set out to: the change is "so far". */
  partial: boolean;
  /** Found something then, nothing now (and this crawl read what it flagged: else it is `fewer`). */
  fixed: string[];
  /** Ran then and found nothing; finds something now. */
  new: string[];
  fewer: string[];
  more: string[];
  /** Didn't run then: newer than that analysis, or skipped. Never a new problem. */
  newCheck: string[];
  /** Ran then; skipped now. */
  notCheckedNow: string[];
}

/** A crawl's latest recheck in brief. */
export interface LastRecheck {
  /** Its number in the crawl file: 1, 2... */
  id: number;
  endedAt: string | null;
  /** The check it was asked for; null for a list of URLs. */
  checkId: string | null;
  /** How many URLs it gives a result for. */
  judged: number;
  /** How many it found fixed; null until the analysis after it settles it. */
  fixed: number | null;
}

/** `rechecks` on a library item: how many, and the latest in brief. Null for a crawl never rechecked. */
export interface RechecksBrief {
  count: number;
  last: LastRecheck;
}

// ------------------------------------------------------------------------------------------- GET /changes

/** The crawl a comparison is with, as the library has it. */
export interface ComparedCrawl {
  id: string;
  startedAt: string | null;
  finishedAt: string | null;
  /** A library status: `completed`, `stopped`... */
  status: string;
  startUrl: string | null;
  analyzedAt: string | null;
  pagesAnalyzed: number | null;
  crawledUrlCount: number;
  /** `previous`, the crawl's default, or `picked`, named by `with`. */
  chosen: 'previous' | 'picked';
}

/** What differs between the two crawls, each naming its own code (docs/v2-fix-loop.md, "Fair or not"). */
export type DifferenceCode =
  | 'startOrigin'
  | 'startPath'
  | 'scope'
  | 'limits'
  | 'fetching'
  | 'client'
  | 'timeouts'
  | 'stoppedEarly'
  | 'coverage'
  | 'rules';

/** Which crawl a difference is about: `then` the other, `now` this one. */
export type DifferenceSide = 'then' | 'now' | 'both';

export type DifferenceValue = string | number | boolean | string[] | null;

/**
 * One difference. `field` and `side` are left out where it has none; `then` and `now` are given for every option's
 * difference (null for none, as a limit's none) and otherwise only where it has values (`startOrigin`, `startPath`,
 * `coverage`'s two `pagesAnalyzed`, a `rules` version).
 */
export interface ComparisonDifference {
  code: DifferenceCode;
  field?: string;
  side?: DifferenceSide;
  then?: DifferenceValue;
  now?: DifferenceValue;
}

/** A check's state between the two crawls. */
export type CheckChangeState = 'new' | 'fixed' | 'fewer' | 'more' | 'same' | 'newCheck' | 'notCheckedNow';

/** Every check that took points on either side, with its points then and now (0 where it took none). */
export interface ScoreLineChange {
  checkId: string;
  then: number;
  now: number;
}

/** The score then and now. */
export interface ScoreChange {
  then: ScoreSummary;
  now: ScoreSummary;
  /** Now less then; null where the scores' versions differ. */
  change: number | null;
  lines: ScoreLineChange[];
}

/** How the rows moved, added up. Null when the comparison is counts only. */
export interface RowSplit {
  fixed: number;
  /** Flagged then; this crawl didn't find the URL (for a `sources` check, "no longer linked"). Counted as fixed. */
  gone: number;
  /** Flagged then; this crawl couldn't judge it. **Never counted as fixed.** */
  notChecked: number;
  still: number;
  new: number;
  newUrl: number;
  notComparedThen: number;
}

/** The checks of a comparison by state. */
export interface CheckStateCounts {
  fixed: number;
  new: number;
  fewer: number;
  more: number;
  same: number;
  newCheck: number;
  notCheckedNow: number;
}

export interface ComparisonTotals {
  checks: CheckStateCounts;
  issues: RowSplit | null;
}

/**
 * One check compared: its state, the URLs it flagged then and now (null on the side it didn't run), and how its rows
 * moved (each null when the comparison is counts only).
 */
export interface CheckChange {
  checkId: string;
  severity: Severity | null;
  severityThen: Severity | null;
  state: CheckChangeState;
  then: number | null;
  now: number | null;
  fixed: number | null;
  gone: number | null;
  notChecked: number | null;
  still: number | null;
  new: number | null;
  newUrl: number | null;
  notComparedThen: number | null;
}

/** `GET /api/crawls/{id}/changes`: the comparison in full. */
export interface CrawlComparison {
  crawlId: string;
  with: ComparedCrawl;
  comparable: Comparability;
  differences: ComparisonDifference[];
  /**
   * Why the other crawl's analysis isn't like this one's (`engineChanged`, `settingsChanged`, `crawlChanged`): empty
   * when the two are like for like. Not empty, the comparison is counts only until the other crawl is analysed again.
   */
  previousNeedsAnalysis: OutOfDateReason[];
  /** Null when either crawl has no score. */
  score: ScoreChange | null;
  totals: ComparisonTotals;
  /** Every check that ran on either side, in this crawl's fix-first order, then catalog order. */
  checks: CheckChange[];
}

/** `/changes` for a crawl with no previous crawl (a site's first): nothing to compare, which the UI shows as nothing. */
export interface NoComparison {
  crawlId: string;
  with: null;
}

export type ChangesAnswer = CrawlComparison | NoComparison;

/** A comparison, or null for nothing to compare. */
export function comparisonOf(answer: ChangesAnswer | undefined): CrawlComparison | null {
  return answer?.with ? answer : null;
}

/** What a page of a check's rows (or of what it no longer flags) was compared with. */
export interface Compared {
  /** The other crawl's id. */
  with: string;
  startedAt: string | null;
  comparable: Comparability;
}

/** What a page of a check's groups was compared with. */
export interface GroupsCompared extends Compared {
  /**
   * How many causes then have no URL now: what `/fixed` lists by cause. Null for a check that isn't grouped or didn't
   * run in both, and when counts only.
   */
  fixedGroups: number | null;
}

/** `compareWith` on a check's groups and rows, `/fixed` and the report: the previous crawl, or a crawl's id. */
export type CompareWith = 'previous' | (string & {});

/** A group's change: a cause matched across the two files (a duplicate cluster by the pages it shares). */
export interface GroupChange {
  state: 'new' | 'fewer' | 'more' | 'same';
  /** Its URLs then. */
  then: number;
}

/** A row flagged now, compared: how it stood then. */
export type RowChangeState = 'still' | 'new' | 'newUrl' | 'notComparedThen';

export interface RowChange {
  state: RowChangeState;
  /** Its detail then; null unless `still`. */
  detailThen: string | null;
}

// ------------------------------------------------------------------------------------------- POST /fixed

/** A row flagged then and not now: fixed, gone (not found now), or not checked this time (never fixed). */
export type ClearedState = 'fixed' | 'gone' | 'notChecked';

/** Why this crawl didn't judge a URL the check flagged then. */
export type NotCheckedCode = 'notRead' | 'failed' | 'blocked' | 'rateLimited' | 'robots' | 'redirects' | 'checkSkipped';

export interface NotCheckedReason {
  code: NotCheckedCode;
  /** The engine's words: "Answered 500", "Now forwards to /new/". */
  text: string;
}

/** A row the check flagged in the other crawl and doesn't now. */
export interface ClearedIssue {
  url: string;
  /** Its id in this crawl; null when this crawl didn't find it. */
  urlId: number | null;
  /** Its id in the other crawl. */
  thenUrlId: number;
  state: ClearedState;
  /** Why a `notChecked` row wasn't judged; null otherwise. */
  reason: NotCheckedReason | null;
  detailThen: string | null;
  relatedUrlThen: string | null;
}

/** A cause the check had then with no URL now: its URLs then, and how they moved. */
export interface ClearedGroup {
  detail: string | null;
  relatedUrl: string | null;
  sharedText: string | null;
  then: number;
  fixed: number;
  gone: number;
  notChecked: number;
}

/** One cause in the other crawl, by its detail and its related URL's text (ids differ between files). */
export interface ClearedGroupKey {
  detail: string | null;
  relatedUrl: string | null;
}

export interface FixedQuery {
  /** `previous` when left out. */
  compareWith?: CompareWith;
  /** `rows` (when left out) or `groups`. */
  level?: 'rows' | 'groups';
  /** All three when left out. */
  states?: ClearedState[];
  /** Narrows rows to one cause then. */
  group?: ClearedGroupKey;
  offset?: number;
  /** 100 when left out. */
  limit?: number;
}

/**
 * `POST /issues/{checkId}/fixed`: `rows` at level `rows`, `groups` at level `groups`, never both. `compared` is null,
 * with no total and no rows, for a crawl with no previous crawl.
 */
export interface FixedPage {
  checkId: string;
  compared: Compared | null;
  level: 'rows' | 'groups';
  /** Null when the comparison is counts only, or the check didn't run in the other crawl. */
  total: number | null;
  offset: number;
  rows?: ClearedIssue[];
  groups?: ClearedGroup[];
}

// ------------------------------------------------------------------------------------------- Rechecks

/** What a recheck of a check reads again (`recheck` on each check). Empty: only a full crawl can show a fix. */
export type RecheckKind = 'urls' | 'sitemaps' | 'robotsTxt' | 'siteVersions';

/** One of a check's groups, as `/rows` takes it, or as a recheck keeps it. */
export interface RecheckGroup {
  detail: string | null;
  relatedUrlId: number | null;
}

/** What a recheck is asked to settle: a check (optionally one of its groups), or a list of URLs (with a check, or none). */
export interface RecheckAsk {
  checkId?: string;
  group?: RecheckGroup;
  /** 1 to 1,000 URL ids of the crawl, in place of the check's. */
  urlIds?: number[];
}

/** Why a recheck can't run now: the problem `POST /rechecks` would give. */
export type RecheckRefusalType =
  'crawl-busy' | 'site-busy' | 'recheck-not-latest' | 'recheck-unsupported' | 'not-analyzed' | 'not-analysable';

export interface RecheckRefusal {
  type: RecheckRefusalType | (string & {});
  /** The sidecar's words: "Recheck works on the site’s latest check." */
  title: string;
}

/** What Continue would ask for: header values and proxy credentials the crawl file doesn't keep. */
export interface RecheckSecrets {
  missingHeaders: string[];
  proxyCredentials: boolean;
}

/** `POST /rechecks/plan`: what a recheck would do, without doing it. */
export interface RecheckPlan {
  checkId: string | null;
  group: RecheckGroup | null;
  urlList: boolean;
  kinds: RecheckKind[];
  /** The URLs whose result it gives. */
  judged: number;
  /** What it fetches first. */
  phase1: number;
  /** For a `sources` check, the most it fetches next. */
  phase2Max: number;
  /** 1,000. */
  limit: number;
  /** It would pass `limit`: it takes the most important first and leaves `notRechecked` as the crawl found them. */
  capped: boolean;
  notRechecked: number;
  /** About how long, from the crawl's own pace, with robots.txt and the analysis after. */
  estimateMs: number;
  /** It would fetch more than half the crawl's URLs: a full check takes about as long and settles every check. */
  suggestFullCheck: boolean;
  /** The crawl's analysis is out of date: the recheck analyses it first. */
  analysisFirst: boolean;
  /** The check's count in the analysis as it stands; null without a check. */
  countBefore: number | null;
  secrets: RecheckSecrets;
  /** Null, or why it can't run now. */
  refused: RecheckRefusal | null;
}

/** `POST /rechecks`: the plan's ask, and Continue's secrets. */
export interface StartRecheckRequest extends RecheckAsk {
  extraHeaders?: Record<string, string>;
  proxyUrl?: string;
  /** Shopify crawler signatures, by host: a crawl file keeps none, so a recheck sends only those given here. */
  signatures?: Record<string, CrawlSignature>;
  skipMissingHeaders?: boolean;
}

/** The 202's body: the recheck's number in the file (keys its events before they come), and the plan as asked. */
export interface StartedRecheck extends RecheckPlan {
  recheckId: number;
}

export type RecheckStatus = 'running' | 'completed' | 'stopped' | 'failed' | 'interrupted';

/** What a recheck found, once the analysis after it settled it. Kept as found: later analyses don't change it. */
export interface RecheckResult {
  fixed: number;
  /** What the check flags now among those it judged. */
  still: number;
  /** Couldn't be judged: no answer, an error, blocked, rate-limited, robots.txt, now forwarding elsewhere. */
  notChecked: number;
  /** Past the cap, or the stop. */
  notRechecked: number;
  /** In scope, not flagged before, flagged now. */
  new: number;
  scoreBefore: number | null;
  scoreAfter: number | null;
}

/**
 * How the site answered a recheck for the URLs it judges, as its run ended: read; refused again, by why; or never asked.
 * They add up to `judged`; `refused` is the four refusals together.
 */
export interface RecheckAnswers {
  read: number;
  /** Told to slow down (429). */
  rateLimited: number;
  /** A firewall or bot check answered. */
  blocked: number;
  /** Disallowed by robots.txt, or it couldn't be read. */
  robotsTxt: number;
  noAnswer: number;
  /** Past the cap, or the run ended before them. */
  notAsked: number;
  refused: number;
}

/** One recheck of a crawl, as its file keeps it. */
export interface Recheck {
  id: number;
  status: RecheckStatus;
  startedAt: string;
  endedAt: string | null;
  settledAt: string | null;
  /** Its own time spent crawling; null when not kept. */
  crawlingMs: number | null;
  checkId: string | null;
  group: RecheckGroup | null;
  urlList: boolean;
  kinds: RecheckKind[];
  judged: number;
  /** The requests it sent the site: its pages, robots.txt and sitemaps; never a URL it didn't ask. */
  fetched: number;
  countBefore: number | null;
  /** Null until the analysis after it settles it. */
  result: RecheckResult | null;
  /** Null for a recheck by an engine before they were kept. */
  answers: RecheckAnswers | null;
}

/** `GET /rechecks`: newest first. */
export interface RecheckList {
  rechecks: Recheck[];
}

export type RecheckRole = 'judged' | 'named' | 'referring' | 'new' | 'letGo';

export type RecheckOutcome = 'fixed' | 'still' | 'notChecked' | 'notRechecked' | 'new';

export interface RecheckRowsQuery {
  outcomes?: RecheckOutcome[];
  roles?: RecheckRole[];
  offset?: number;
  /** 1 to 1,000; 100 when left out. */
  limit?: number;
}

/** One URL of a recheck. */
export interface RecheckRow {
  urlId: number;
  url: string;
  role: RecheckRole;
  /** For a judged URL; null for the other roles. */
  outcome: RecheckOutcome | null;
  flaggedBefore: boolean;
  detailBefore: string | null;
  detailAfter: string | null;
  statusCode: number | null;
}

/** `POST /rechecks/{n}/rows`: judged first, then crawl order. */
export interface RecheckRowsPage {
  total: number;
  offset: number;
  rows: RecheckRow[];
}

/**
 * The `recheck` event: one as the run starts, then its progress at most every 500 ms (`running`), one as the run ends
 * (`completed`, `stopped` or `failed`, no result), and one once settled, with `result`: the cue to read the crawl again.
 */
export interface RecheckEvent {
  crawlId: string;
  recheckId: number;
  status: RecheckStatus;
  /** 1, then 2 for the pages referring to what still fails; null before its first progress. */
  phase: number | null;
  /** Its fetches done of `total`, in the phase. */
  done: number;
  total: number;
  result: RecheckResult | null;
}
