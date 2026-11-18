// The fix loop's words for what changed since the previous crawl, in both modes (docs/v2-fix-loop.md, section 4,
// "Words" and the screens' lines; the glossary in docs/v2-ui-discovery.md): Simple's plain words and Advanced's terms.
// A change is text with a glyph, never colour alone; the score's movement is neutral. Pure.
import type {
  CheckChange,
  CheckChangeState,
  ClearedState,
  ComparisonDifference,
  CrawlComparison,
  GroupChange,
  NotCheckedReason,
  RowChangeState,
} from '../api/fixLoop';
import type { CheckReferences, CheckSubject } from '../api/types';
import { formatCount, formatCountOf, formatList } from '../lib/format';
import type { ChangeCounts, ChangeMode } from './changes';

// ---------------------------------------------------------------------------------------------------------- Dates

const SHORT_DATE = new Intl.DateTimeFormat('en-GB', { day: 'numeric', month: 'short' });
const SHORT_DATE_YEAR = new Intl.DateTimeFormat('en-GB', { day: 'numeric', month: 'short', year: 'numeric' });
const TIME = new Intl.DateTimeFormat('en-GB', { hour: '2-digit', minute: '2-digit' });

/**
 * When the previous crawl was, as the lines say it: "3 Nov", with its year when that isn't this year's ("3 Nov 2025").
 * Empty for a time that doesn't read.
 */
export function changeDate(iso: string | null | undefined, now: number = Date.now()): string {
  const time = iso ? Date.parse(iso) : Number.NaN;
  if (Number.isNaN(time)) return '';
  const sameYear = new Date(time).getFullYear() === new Date(now).getFullYear();
  return (sameYear ? SHORT_DATE : SHORT_DATE_YEAR).format(time);
}

/** "since 3 Nov"; "since your last check" when the date doesn't read. */
export function sinceWords(iso: string | null | undefined, now: number = Date.now()): string {
  const date = changeDate(iso, now);
  return date ? `since ${date}` : 'since your last check';
}

/** "on 3 Nov"; "last time" when the date doesn't read. */
export function onWords(iso: string | null | undefined, now: number = Date.now()): string {
  const date = changeDate(iso, now);
  return date ? `on ${date}` : 'last time';
}

/** "14:02", in the user's time zone; empty for a time that doesn't read. */
export function clockWords(iso: string | null | undefined): string {
  const time = iso ? Date.parse(iso) : Number.NaN;
  return Number.isNaN(time) ? '' : TIME.format(time);
}

/** "today at 14:02", "yesterday at 09:12", "on 17 Nov at 14:02". */
export function whenWords(iso: string | null | undefined, now: number = Date.now()): string {
  const time = iso ? Date.parse(iso) : Number.NaN;
  if (Number.isNaN(time)) return '';
  const day = (at: number) => new Date(at).toDateString();
  const clock = TIME.format(time);
  if (day(time) === day(now)) return `today at ${clock}`;
  if (day(time) === day(now - 24 * 60 * 60 * 1_000)) return `yesterday at ${clock}`;
  return `on ${changeDate(iso, now)} at ${clock}`;
}

// ---------------------------------------------------------------------------------------------------------- Score

/** A count's movement as text with a glyph: "▲14", "▼3", "0". Neutral: no colour carries it. */
export function moveGlyph(change: number): string {
  if (change > 0) return `▲${formatCount(change)}`;
  if (change < 0) return `▼${formatCount(-change)}`;
  return '0';
}

/** What a screen reader hears for a score's movement: "up 14 points", "down 3 points", "no change". */
export function moveSpoken(change: number, unit = 'point'): string {
  if (change === 0) return 'no change';
  return `${change > 0 ? 'up' : 'down'} ${formatCountOf(Math.abs(change), unit)}`;
}

/**
 * Simple's line beside the score's ring on Home: "▲14 since 3 Nov", "Same as 3 Nov", and "so far" when either crawl
 * didn't read all it set out to. Null without a score change: nothing is said.
 */
export function homeScoreLine(change: number | null, since: string | null, partial: boolean): string | null {
  if (change === null) return null;
  const date = changeDate(since);
  const when = date ? date : 'your last check';
  const line = change === 0 ? `Same as ${when}` : `${moveGlyph(change)} ${sinceWords(since)}`;
  return partial ? `${line} so far` : line;
}

/** Advanced's score cell: "85 ▲14"; the score alone without a change. */
export function scoreWithMove(score: number, change: number | null): string {
  return change === null || change === 0 ? formatCount(score) : `${formatCount(score)} ${moveGlyph(change)}`;
}

/** "71 → 85": a score then and now. */
export function thenNow(then: number, now: number): string {
  return `${formatCount(then)} → ${formatCount(now)}`;
}

/**
 * The score breakdown's points then, in its line's second part: "was −7"; "none then" for a line only now (a new
 * problem took the points). Null where they're the same.
 */
export function pointsThenWords(then: number, now: number): string | null {
  if (then === now) return null;
  return then === 0 ? 'none then' : `was −${formatCount(then)}`;
}

// ---------------------------------------------------------------------------------------------------------- Counts

/**
 * Simple's change line under "To fix" on Home: "3 fixed · 1 new since 3 Nov". Null when nothing was fixed and nothing
 * is new: nothing is said, never "0 fixed".
 */
export function homeChangeLine(counts: Pick<ChangeCounts, 'fixed' | 'new'>, since: string | null): string | null {
  const parts = [
    counts.fixed > 0 ? `${formatCount(counts.fixed)} fixed` : '',
    counts.new > 0 ? `${formatCount(counts.new)} new` : '',
  ].filter(Boolean);
  return parts.length > 0 ? `${parts.join(' · ')} ${sinceWords(since)}` : null;
}

/** "3 problems fixed, 1 new", "1 problem fixed", "2 new problems", or null for neither. */
export function problemsMovedWords(counts: Pick<ChangeCounts, 'fixed' | 'new'>): string | null {
  const { fixed, new: added } = counts;
  if (fixed > 0 && added > 0) return `${formatCountOf(fixed, 'problem')} fixed, ${formatCount(added)} new`;
  if (fixed > 0) return `${formatCountOf(fixed, 'problem')} fixed`;
  if (added > 0) return `${formatCount(added)} new ${added === 1 ? 'problem' : 'problems'}`;
  return null;
}

/** The Summary's change line in parts, so a screen can make the counts links: each part's words, or null. */
export interface SummaryChangeParts {
  /** "Since your last check on 3 Nov" (the colon follows). */
  lead: string;
  /** "score 71 → 85"; null without a score change (scored another way). */
  score: string | null;
  /** "3 problems fixed", "1 problem fixed"; null for none. */
  fixed: string | null;
  /** "1 new" after the fixed ones, "2 new problems" alone; null for none. */
  added: string | null;
  /** "no problems fixed or new" when neither moved; null otherwise. */
  none: string | null;
  /** " so far" for a comparison either side of which didn't read all it set out to; empty otherwise. */
  soFar: string;
}

export function summaryChangeParts(args: {
  since: string | null;
  score: { then: number; now: number } | null;
  counts: Pick<ChangeCounts, 'fixed' | 'new'>;
  partial: boolean;
}): SummaryChangeParts {
  const { since, score, counts, partial } = args;
  const { fixed, new: added } = counts;
  return {
    lead: `Since your last check ${onWords(since)}`,
    score: score ? `score ${thenNow(score.then, score.now)}` : null,
    fixed: fixed > 0 ? `${formatCountOf(fixed, 'problem')} fixed` : null,
    added:
      added > 0
        ? fixed > 0
          ? `${formatCount(added)} new`
          : `${formatCount(added)} new ${added === 1 ? 'problem' : 'problems'}`
        : null,
    none: fixed === 0 && added === 0 ? 'no problems fixed or new' : null,
    soFar: partial ? ' so far' : '',
  };
}

/**
 * The Summary's change line: "Since your last check on 3 Nov: score 71 → 85, 3 problems fixed, 1 new." Without a score
 * change (scored another way), the problems alone; with nothing moved, "no problems fixed or new"; "so far" for a
 * comparison either side of which didn't read all it set out to.
 */
export function summaryChangeLine(args: Parameters<typeof summaryChangeParts>[0]): string {
  const parts = summaryChangeParts(args);
  const body = [parts.score, parts.fixed, parts.added, parts.none].filter(Boolean).join(', ');
  return `${parts.lead}: ${body}${parts.soFar}.`;
}

/** The tooltip and the report's note when the previous crawl was analysed again first (like for like). */
export const ANALYSED_AGAIN_WORDS = 'Your last check was worked out again with today’s checks.';

/**
 * Advanced's line of checks by state: "5 checks fixed · 2 new · 6 fewer · 1 more · 1 new check". Null when none moved.
 */
export function checksMovedLine(counts: ChangeCounts): string | null {
  const parts = [
    counts.fixed > 0 ? `${formatCountOf(counts.fixed, 'check')} fixed` : '',
    counts.new > 0 ? `${formatCount(counts.new)} new` : '',
    counts.fewer > 0 ? `${formatCount(counts.fewer)} fewer` : '',
    counts.more > 0 ? `${formatCount(counts.more)} more` : '',
    counts.newCheck > 0 ? formatCountOf(counts.newCheck, 'new check') : '',
    counts.notCheckedNow > 0 ? `${formatCount(counts.notCheckedNow)} not checked` : '',
  ].filter(Boolean);
  return parts.length > 0 ? parts.join(' · ') : null;
}

/** Advanced's line of issues: "41 issues fixed · 6 new · 3 not checked this time" (gone counts as fixed). */
export function issuesMovedLine(split: {
  fixed: number;
  gone: number;
  notChecked: number;
  new: number;
  newUrl: number;
}): string | null {
  const fixed = split.fixed + split.gone;
  const added = split.new + split.newUrl;
  const parts = [
    fixed > 0 ? `${formatCountOf(fixed, 'issue')} fixed` : '',
    added > 0 ? `${formatCount(added)} new` : '',
    split.notChecked > 0 ? `${formatCount(split.notChecked)} not checked this time` : '',
  ].filter(Boolean);
  return parts.length > 0 ? parts.join(' · ') : null;
}

// ---------------------------------------------------------------------------------------------------------- A check

/**
 * A check's change in its count's second line (What to fix, Fix these first) or Advanced's "Since" column:
 * - Simple: "9 on 3 Nov" (fewer, more), "same as 3 Nov", "New since 3 Nov", "Fixed since 3 Nov", "Not looked for last
 *   time"; null for a check skipped now, which says its own "couldn't check" reason as before.
 * - Advanced: "−5", "+5", "0", "New", "Fixed", "New check", "Not checked".
 */
export function checkChangeWords(
  change: Pick<CheckChange, 'state' | 'then' | 'now'>,
  since: string | null,
  mode: ChangeMode,
): string | null {
  const delta = (change.now ?? 0) - (change.then ?? 0);
  if (mode === 'advanced') {
    const words: Record<CheckChangeState, string> = {
      new: 'New',
      fixed: 'Fixed',
      fewer: `−${formatCount(-delta)}`,
      more: `+${formatCount(delta)}`,
      same: '0',
      newCheck: 'New check',
      notCheckedNow: 'Not checked',
    };
    return words[change.state];
  }
  switch (change.state) {
    case 'new':
      return `New ${sinceWords(since)}`;
    case 'fixed':
      return `Fixed ${sinceWords(since)}`;
    case 'fewer':
    case 'more':
      return `${formatCount(change.then)} ${onWords(since)}`;
    case 'same':
      return `same as ${changeDate(since) || 'last time'}`;
    case 'newCheck':
      return 'Not looked for last time';
    case 'notCheckedNow':
      return null;
  }
}

/**
 * Advanced's tooltip for a check's change: its row split, "5 fixed, 0 new, 2 not checked this time" (gone counted as
 * fixed, a new URL as new). Null when counts only, where rows weren't compared.
 */
export function checkSplitWords(change: CheckChange): string | null {
  if (change.fixed === null || change.new === null) return null;
  const fixed = change.fixed + (change.gone ?? 0);
  const added = change.new + (change.newUrl ?? 0);
  const parts = [`${formatCount(fixed)} fixed`, `${formatCount(added)} new`];
  if ((change.notChecked ?? 0) > 0) parts.push(`${formatCount(change.notChecked)} not checked this time`);
  if ((change.notComparedThen ?? 0) > 0) parts.push(`${formatCount(change.notComparedThen)} not compared`);
  return parts.join(', ');
}

/** The problem page's count line's addition: "was 9 on 3 Nov". Null where it says nothing (the same, or new). */
export function wasWords(change: Pick<CheckChange, 'state' | 'then'>, since: string | null): string | null {
  if (change.state !== 'fewer' && change.state !== 'more') return null;
  return `was ${formatCount(change.then)} ${onWords(since)}`;
}

/** What Copy this list adds after a count: "(new since 3 Nov)", "(was 9)". Empty where it adds nothing. */
export function copyChangeWords(change: Pick<CheckChange, 'state' | 'then'>, since: string | null): string {
  if (change.state === 'new') return ` (new ${sinceWords(since)})`;
  if (change.state === 'fewer' || change.state === 'more') return ` (was ${formatCount(change.then)})`;
  if (change.state === 'newCheck') return ' (not looked for last time)';
  return '';
}

/** The heading of What to fix's Fixed group: "Fixed since your last check on 3 Nov (3)"; the words alone without a count. */
export function fixedGroupTitle(since: string | null, count?: number): string {
  const title = `Fixed since your last check ${onWords(since)}`;
  return count === undefined ? title : `${title} (${formatCount(count)})`;
}

/**
 * How many the Fixed group holds, in the unit of Simple's headline ("6 problems to fix and 22 more worth a look"):
 * "3 problems and 1 more worth a look", "1 problem", "2 worth a look".
 */
export function fixedCountWords(problems: number, notices: number): string {
  const parts: string[] = [];
  if (problems > 0) parts.push(formatCountOf(problems, 'problem'));
  if (notices > 0) parts.push(`${formatCount(notices)}${problems > 0 ? ' more' : ''} worth a look`);
  return parts.join(' and ');
}

/**
 * A fixed check in the Fixed group: its count then, "9 addresses on 3 Nov", "14 pages on 3 Nov", "whole site on 3 Nov"
 * for a fault of the whole site.
 */
export function fixedThenWords(
  check: { subject?: CheckSubject; siteWide?: boolean; reportedOnce?: boolean },
  then: number | null,
  since: string | null,
): string {
  if (check.siteWide || check.reportedOnce || then === null) return `whole site ${onWords(since)}`;
  const pages = (check.subject ?? 'page') === 'page';
  const noun = pages ? (then === 1 ? 'page' : 'pages') : then === 1 ? 'address' : 'addresses';
  return `${formatCount(then)} ${noun} ${onWords(since)}`;
}

// ---------------------------------------------------------------------------------------------------------- Causes and rows

/**
 * A cause's muted line in Simple ("Still there since 3 Nov", "New since 3 Nov"), or Advanced's state cell ("New",
 * "−3", "+1", "0").
 */
export function groupChangeWords(change: GroupChange, urlsNow: number, since: string | null, mode: ChangeMode): string {
  if (mode === 'simple')
    return change.state === 'new' ? `New ${sinceWords(since)}` : `Still there ${sinceWords(since)}`;
  if (change.state === 'new') return 'New';
  const delta = urlsNow - change.then;
  return delta > 0 ? `+${formatCount(delta)}` : delta < 0 ? `−${formatCount(-delta)}` : '0';
}

/** A row flagged now, compared: Advanced's "Still", "New", "New URL", "Not compared"; Simple's plain line. */
export function rowChangeWords(state: RowChangeState, since: string | null, mode: ChangeMode): string {
  if (mode === 'advanced') {
    return { still: 'Still', new: 'New', newUrl: 'New URL', notComparedThen: 'Not compared' }[state];
  }
  return {
    still: `Still there ${sinceWords(since)}`,
    new: `New ${sinceWords(since)}`,
    newUrl: `New ${sinceWords(since)}`,
    notComparedThen: 'Not checked last time',
  }[state];
}

/**
 * A row or cause flagged then and not now: "Fixed"; for one this crawl didn't find, "No longer linked" (Simple: "No
 * longer linked from any page") when the check is of what others refer to (a 404, a redirect), else "No longer found";
 * "Not checked this time", never fixed.
 */
export function clearedWords(state: ClearedState, references: CheckReferences | undefined, mode: ChangeMode): string {
  if (state === 'fixed') return 'Fixed';
  if (state === 'notChecked') return 'Not checked this time';
  if (references === 'sources') return mode === 'simple' ? 'No longer linked from any page' : 'No longer linked';
  return 'No longer found';
}

/** Whether a cleared state counts as fixed: fixed, and gone (not found now). Not checked never does. */
export function clearedIsFixed(state: ClearedState): boolean {
  return state === 'fixed' || state === 'gone';
}

/** Why a row wasn't checked this time, in a mode's words: the engine's text, or Simple's plain reason. */
export function notCheckedWords(reason: NotCheckedReason | null, mode: ChangeMode): string {
  if (!reason) return mode === 'simple' ? 'It wasn’t read this time.' : 'Not read this time';
  if (mode === 'advanced') return reason.text;
  switch (reason.code) {
    case 'notRead':
      return 'It wasn’t read this time.';
    case 'failed':
      return 'It didn’t answer properly this time.';
    case 'blocked':
      return 'The site’s protection stopped the check reading it.';
    case 'rateLimited':
      return 'The site asked the check to slow down, so it wasn’t read.';
    case 'robots':
      return 'The site’s rules file now asks search engines not to read it.';
    case 'redirects':
      return 'It now forwards to another address.';
    case 'checkSkipped':
      return 'This check couldn’t look for it this time.';
  }
}

// ---------------------------------------------------------------------------------------------------------- Fair or not

/**
 * Simple's one caution line for a comparison that isn't like for like, or null for one that is (`full`). One line,
 * for the difference that matters most: another start address (counts only), other rules, how much each read,
 * a check stopped early, another start page, other limits, other settings.
 */
export function cautionLine(comparison: Pick<CrawlComparison, 'comparable' | 'differences'>): string | null {
  if (comparison.comparable === 'full') return null;
  const of = (code: ComparisonDifference['code']) => comparison.differences.find((entry) => entry.code === code);
  const origin = of('startOrigin');
  if (origin) {
    return `This check started at ${String(origin.now)}, the last at ${String(origin.then)}, so only the totals are compared.`;
  }
  if (of('rules')) return 'Your last check was worked out with other checks, so only the totals are compared.';
  const coverage = of('coverage');
  if (coverage && typeof coverage.then === 'number' && typeof coverage.now === 'number') {
    const more = coverage.then > coverage.now;
    return `Your last check covered ${more ? 'more' : 'less'} of the site (${formatCountOf(coverage.then, 'page')} against ${formatCount(coverage.now)} now), so some of it couldn’t be compared.`;
  }
  const stopped = of('stoppedEarly');
  if (stopped) {
    const who = stopped.side === 'both' ? 'Both checks' : stopped.side === 'then' ? 'Your last check' : 'This check';
    return `${who} stopped before the end, so what ${stopped.side === 'both' ? 'they' : 'it'} didn’t read couldn’t be compared.`;
  }
  const path = of('startPath');
  if (path) {
    return `This check started at ${String(path.now)}, the last at ${String(path.then)}, so some of it couldn’t be compared.`;
  }
  if (of('limits')) return 'This check and your last had different limits, so some of it couldn’t be compared.';
  if (of('scope') || of('fetching')) {
    return 'This check and your last were set to read different things, so some of it couldn’t be compared.';
  }
  return comparison.comparable === 'countsOnly' ? 'Only the totals are compared.' : null;
}

const FIELD_NAMES: Record<string, string> = {
  stayInStartFolder: 'Stay in the start folder',
  includeSubdomains: 'Include subdomains',
  followNofollow: 'Follow nofollow links',
  crawlCanonicals: 'Crawl canonicals',
  crawlHreflang: 'Crawl hreflang',
  crawlPagination: 'Crawl pagination',
  maxUrls: 'Max URLs',
  maxDepth: 'Max depth',
  checkResources: 'Check resources',
  checkExternalLinks: 'Check external links',
  discoverSitemaps: 'Discover sitemaps',
  extraSitemaps: 'Extra sitemaps',
  respectRobotsTxt: 'Respect robots.txt',
  userAgent: 'User agent',
  acceptLanguage: 'Accept-Language',
  headers: 'Headers',
  useCookies: 'Cookies',
  requestTimeoutSeconds: 'Request timeout',
  maxRetries: 'Retries',
  analysisVersion: 'Analysis version',
  checksFingerprint: 'Checks',
  scoreVersion: 'Score version',
  settings: 'Analysis settings',
};

function valueWords(value: ComparisonDifference['then']): string {
  if (value === undefined || value === null) return 'none';
  if (typeof value === 'boolean') return value ? 'on' : 'off';
  if (typeof value === 'number') return formatCount(value);
  if (Array.isArray(value)) return value.length === 0 ? 'none' : value.join(', ');
  return value;
}

/**
 * One of Advanced's Differences lines: "Start URL: http://example.com/ then, https://example.com/ now", "Max URLs: none
 * then, 5,000 now", "Stopped early: this crawl", "Pages analysed: 265 then, 50 now", "Analysed by other rules:
 * Analysis settings".
 */
export function differenceWords(difference: ComparisonDifference): string {
  const field = difference.field ? (FIELD_NAMES[difference.field] ?? difference.field) : null;
  const values =
    difference.then !== undefined || difference.now !== undefined
      ? `${valueWords(difference.then)} then, ${valueWords(difference.now)} now`
      : null;
  switch (difference.code) {
    case 'startOrigin':
      return `Start URL’s scheme or host: ${values ?? 'differs'}`;
    case 'startPath':
      return `Start URL’s path: ${values ?? 'differs'}`;
    case 'stoppedEarly':
      return `Stopped early: ${difference.side === 'both' ? 'both crawls' : difference.side === 'then' ? 'that crawl' : 'this crawl'}`;
    case 'coverage':
      return `Pages analysed: ${values ?? 'very different'}`;
    case 'rules':
      return `Analysed by other rules${field ? `: ${field}` : ''}${values && field ? ` (${values})` : ''}`;
    default:
      return `${field ?? difference.code}: ${values ?? 'differs'}`;
  }
}

/** Whether a difference only notes something (the client, the timeouts) rather than limiting what's compared. */
export function isNoteOnly(difference: ComparisonDifference): boolean {
  return difference.code === 'client' || difference.code === 'timeouts';
}

/** "Differences (2)": what Advanced's fold says. */
export function differencesTitle(count: number): string {
  return `Differences (${formatCount(count)})`;
}

/** The sentence Advanced shows while the previous crawl needs analysing: "The 3 Nov crawl was analysed by an earlier version…". */
export function needsAnalysisWords(since: string | null, reasons: readonly string[]): string {
  const crawl = changeDate(since) ? `The ${changeDate(since)} crawl` : 'The other crawl';
  const why = reasons.includes('engineChanged')
    ? 'was analysed by an earlier version'
    : reasons.includes('settingsChanged')
      ? 'was analysed with other settings'
      : 'changed since it was analysed';
  return `${crawl} ${why}: analyse it again to compare like for like.`;
}

/** The checks a list of ids names, by their titles, for a sentence: "Broken links, Missing titles and 1 more". */
export function namesWords(titles: readonly string[], most = 3): string {
  if (titles.length <= most) return formatList(titles);
  return formatList([...titles.slice(0, most - 1), `${formatCount(titles.length - most + 1)} more`]);
}
