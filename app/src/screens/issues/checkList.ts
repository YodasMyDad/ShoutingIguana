// The Issues screen's checks list, built from the catalog and the last analysis (docs/v2-ui-ia.md, Issues): checks in the
// order to fix them, by count, by points, by severity, or by category in catalog order, each with its severity and the
// pages or URLs it flags. Pure, so every rule is tested:
// - skipped checks are always listed, in their category, with their reason: hiding them would read as clean;
// - checks that passed are listed only when asked for, and each category says how many passed;
// - the severity and text filters narrow what's listed, and hidden checks stay out unless shown.
import type { Analysis, Check, CheckCategory, Checks, ClearedCheck, Severity } from '../../api/types';
import { formatCount, formatCountOf } from '../../lib/format';
import { SEVERITIES, type CheckOutcome } from '../../ui/severity';

/** One check on this crawl: what it came to, with its count, or its reason when it was skipped. */
export interface CheckEntry {
  check: Check;
  /** The check's severity when it found issues; `passed` when it found none; `skipped` when it didn't run. */
  outcome: CheckOutcome;
  /** URLs flagged. Null for a skipped check: it has no count, never 0. */
  count: number | null;
  /** Why it was skipped, in the engine's words; null when the analysis didn't say (an engine from before E2). */
  skipReason: string | null;
  /** The user hid it from the list. */
  hidden: boolean;
  /**
   * The separate things to change its issues come to (the analysis's `causes`): null for a check that found nothing, and
   * for an analysis read from a sidecar that doesn't count them.
   */
  causes: number | null;
  /** Of `causes`, those likely in the site's template; null as `causes` is. */
  templateCauses: number | null;
  /** The checks a fix of it also clears, with the pages it clears there (the analysis's `clears`). */
  clears: ClearedCheck[];
}

export interface CheckCategoryEntry {
  id: string;
  title: string;
  /** Checks in the category that passed, hidden ones left out: "4 passed". 0 for a part of a sorted list. */
  passed: number;
  /** What the list shows, in the list's order. */
  entries: CheckEntry[];
}

/**
 * The order the checks list is in: the engine's order to fix them (the default, as Overview's Fix first), the most URLs
 * flagged, the most points off the score, by severity, or by category in catalog order (the list as it first was).
 */
export type CheckSort = 'fixFirst' | 'count' | 'points' | 'severity' | 'category';

/** The orders Simple's What to fix offers within each of its parts, which are the severities already. */
export type PlainCheckSort = 'fixFirst' | 'points' | 'count';

export const CHECK_SORTS: readonly { value: CheckSort; label: string }[] = [
  { value: 'fixFirst', label: 'Fix first' },
  { value: 'count', label: 'Most URLs' },
  { value: 'points', label: 'Most points' },
  { value: 'severity', label: 'Severity' },
  { value: 'category', label: 'Category' },
];

/** How the list is ordered, and what the order goes by. */
export interface CheckListOrder {
  sort: CheckSort;
  /** The analysis's `fixFirst`: the engine's order. Absent from an earlier engine's analysis: then the app's own. */
  fixFirst?: readonly string[] | null | undefined;
  /** Points off the score by check id (the score's lines); a check without a line takes none. */
  points?: ReadonlyMap<string, number> | undefined;
}

export interface CheckListFilters {
  /** One severity, or every one when empty. */
  severity: readonly Severity[];
  /** Matches a check's title or category, ignoring case. */
  q: string;
  /**
   * One category, by its id in the catalog: what a link to a category's checks names (Overview's "Issues by
   * category"), since the category's title as text would match other categories' checks too. An id the catalog
   * doesn't have stands for the checks whose category it doesn't name ("Other checks").
   */
  category?: string | undefined;
  showPassed: boolean;
  showHidden: boolean;
  /** Checks the user hid. */
  hidden: ReadonlySet<string>;
}

export interface CheckList {
  categories: CheckCategoryEntry[];
  /** Every check of the catalog, as it came out on this crawl, in catalog order. */
  all: CheckEntry[];
  /** The rows the list shows, in order: what the arrow keys move through. */
  visible: CheckEntry[];
  /**
   * Every check the severity, text and category filters keep, whether the list shows it or not (passed, hidden):
   * where the first check to fix is picked from, so it's one the filters are about.
   */
  matching: CheckEntry[];
  /**
   * Checks that found issues, per severity, as the severity filter counts them (the text and category filters and
   * hidden applied).
   */
  severityCounts: Record<Severity | 'all', number>;
  /** The category the list is narrowed to, with its title for the filter's words. Null when it isn't. */
  category: { id: string; title: string } | null;
  /** Hidden checks the filters would otherwise show. */
  hiddenCount: number;
  /** Checks that passed, hidden ones left out: the switch's "Show the 63 checks that passed". */
  passedCount: number;
  /** The severity or text filter hides every check that would otherwise show. */
  filteredToNothing: boolean;
}

/** The figures the header's answer is built from: counts only, so it never claims what the engine can't say. */
export interface AnalysisTotals {
  /** Checks that found at least one issue. */
  found: number;
  /** Issues: one check on one URL. */
  issues: number;
  passed: number;
  skipped: number;
  /**
   * The separate things to change the found checks' issues come to (their `causes` added up, a check another one's fix
   * clears entirely left out as the same job); null when a found check has none (an analysis from a sidecar that doesn't
   * count them), so a sum is never short. Left out where only the counts are known.
   */
  causes?: number | null;
  /** Of `causes`, those likely in the site's template. */
  templateCauses?: number;
  /**
   * The headline's part (round 3): the errors' and warnings' things to change, what moves the score; null as `causes`
   * is. Left out where only the counts are known.
   */
  fixCauses?: number | null;
  /** Of `fixCauses`, those likely in the site's template. */
  fixTemplateCauses?: number;
  /** Notices that found something: "14 notices worth a look", as Simple's "14 things worth a look" counts them. */
  notices?: number;
}

/** What each check came to on this crawl, in catalog order. */
export function checkEntries(
  checks: Checks,
  analysis: Analysis,
  hidden: ReadonlySet<string> = new Set(),
): CheckEntry[] {
  const counts = new Map(analysis.counts.map((entry) => [entry.checkId, entry]));
  const skipped = new Map(analysis.skipped.map((entry) => [entry.checkId, entry.reason]));
  return checks.checks.map((check) => {
    const ran = counts.get(check.id);
    // A check the analysis doesn't mention ran under an engine that didn't have it: it's skipped, never clean.
    if (ran === undefined || skipped.has(check.id)) {
      return {
        check,
        outcome: 'skipped',
        count: null,
        skipReason: skipped.get(check.id)?.text ?? null,
        hidden: hidden.has(check.id),
        causes: null,
        templateCauses: null,
        clears: [],
      };
    }
    const found = ran.count > 0;
    return {
      check,
      outcome: found ? check.severity : 'passed',
      count: ran.count,
      skipReason: null,
      hidden: hidden.has(check.id),
      causes: found ? (ran.causes ?? null) : null,
      templateCauses: found ? (ran.templateCauses ?? null) : null,
      clears: found ? (ran.clears ?? []) : [],
    };
  });
}

/**
 * The checks an answer counts: the ones the user hasn't hidden. A hidden check is left out of every figure there is
 * (the sentence, the severity counts, the categories, Fix first), on Issues, Overview and the sidebar alike, so they
 * can't disagree with each other; `hiddenTotals` says what was left out, so nothing reads as clean because it's
 * hidden.
 */
export function countedEntries(entries: readonly CheckEntry[]): CheckEntry[] {
  return entries.filter((entry) => !entry.hidden);
}

/** What the hidden checks came to on this crawl. */
export interface HiddenTotals {
  /** Hidden checks, whatever they came to. */
  checks: number;
  /** Those that found issues, and their issues. */
  found: number;
  issues: number;
}

/** The hidden checks' totals, or null when none is hidden. */
export function hiddenTotals(entries: readonly CheckEntry[]): HiddenTotals | null {
  const hidden = entries.filter((entry) => entry.hidden);
  if (hidden.length === 0) return null;
  const found = hidden.filter((entry) => entry.outcome !== 'passed' && entry.outcome !== 'skipped');
  return {
    checks: hidden.length,
    found: found.length,
    issues: found.reduce((sum, entry) => sum + (entry.count ?? 0), 0),
  };
}

/**
 * What the answer leaves out, after it: "1 hidden check" and " isn’t counted: it found 30 issues.", in two parts so
 * the first can be the link to where hidden checks are shown.
 */
export function hiddenWords({ checks, found, issues }: HiddenTotals): { count: string; rest: string } {
  const what =
    found === 0
      ? ''
      : checks === 1
        ? `: it found ${formatCountOf(issues, 'issue')}`
        : `: ${formatCount(found)} of them found ${formatCountOf(issues, 'issue')}`;
  return {
    count: formatCountOf(checks, 'hidden check'),
    rest: ` ${checks === 1 ? 'isn’t' : 'aren’t'} counted${what}.`,
  };
}

export function analysisTotals(entries: readonly CheckEntry[]): AnalysisTotals {
  let found = 0;
  let issues = 0;
  let passed = 0;
  let skipped = 0;
  let notices = 0;
  for (const entry of entries) {
    if (entry.outcome === 'skipped') skipped++;
    else if (entry.outcome === 'passed') passed++;
    else {
      found++;
      issues += entry.count ?? 0;
      if (entry.outcome === 'notice') notices++;
    }
  }
  const all = workOf(entries);
  const fix = workOf(entries.filter((entry) => entry.outcome === 'error' || entry.outcome === 'warning'));
  return {
    found,
    issues,
    passed,
    skipped,
    causes: all.causes,
    templateCauses: all.templateCauses,
    fixCauses: fix.causes,
    fixTemplateCauses: fix.templateCauses,
    notices,
  };
}

/**
 * The things to change among the entries that found something, and those in the template; a check another one of them
 * clears entirely by its fix is the same job, so its own aren't added. Null when one has none (an earlier sidecar's).
 */
function workOf(entries: readonly CheckEntry[]): { causes: number | null; templateCauses: number } {
  const found = entries.filter((entry) => entry.outcome !== 'passed' && entry.outcome !== 'skipped');
  const foundIds = new Set(found.map((entry) => entry.check.id));
  const sameJob = new Set(
    found
      .flatMap((entry) => entry.clears.filter((cleared) => cleared.all && foundIds.has(cleared.checkId)))
      .map((cleared) => cleared.checkId),
  );
  let causes: number | null = 0;
  let templateCauses = 0;
  for (const entry of found) {
    if (sameJob.has(entry.check.id)) continue;
    causes = causes === null || entry.causes === null ? null : causes + entry.causes;
    templateCauses += entry.templateCauses ?? 0;
  }
  return { causes, templateCauses };
}

/**
 * The work in a phrase, from the things to change and how many of them are in the template: "about 40 things to change,
 * 3 of them in the site’s template", "1 thing to change, in the site’s template". "About", as which issues share a
 * cause is the engine's judgement from how they group; never for one.
 */
export function causeWords(causes: number, templateCauses: number, to: 'change' | 'fix' = 'change'): string {
  const things = causes === 1 ? `1 thing to ${to}` : `about ${formatCount(causes)} things to ${to}`;
  if (templateCauses <= 0) return things;
  if (causes === 1) return `${things}, in the site’s template`;
  return `${things}, ${templateCauses === causes ? 'all' : formatCount(templateCauses)} of them in the site’s template`;
}

/** The header's answer in its parts (`answerParts`): what moves the score, the notices, then the counts. */
export interface AnswerParts {
  /** "About 77 things to fix, 3 of them in the site’s template", or "Nothing to fix"; null without things counted. */
  fix: string | null;
  /** "14 notices worth a look"; null with none, or without things counted. */
  notices: string | null;
  /** "21 checks found 1,124 issues on this crawl. 116 passed and 4 were skipped." */
  counts: string;
}

const capitalised = (text: string) => `${text.charAt(0).toUpperCase()}${text.slice(1)}`;

/**
 * The header's answer, leading with what moves the score when the analysis counts its things to change (round 3): the
 * errors' and warnings' things to fix and how many are in the template; then the notices, quieter, as checks worth a
 * look (Simple's "14 things worth a look"); then the counts. Without things counted (an earlier sidecar's analysis),
 * only the counts: "19 checks found 933 issues on this crawl. 63 passed and 3 were skipped."
 */
export function answerParts(totals: AnalysisTotals): AnswerParts {
  const { found, causes, fixCauses, fixTemplateCauses, notices } = totals;
  // Counted only when every check that found something has its things counted: an analysis that counts none has 0
  // errors' things too when only notices found something, which isn't "nothing to fix".
  const counted = found > 0 && causes !== null && causes !== undefined && fixCauses !== null && fixCauses !== undefined;
  return {
    fix: counted
      ? fixCauses === 0
        ? 'Nothing to fix'
        : capitalised(causeWords(fixCauses, fixTemplateCauses ?? 0, 'fix'))
      : null,
    notices: counted && notices ? `${formatCount(notices)} ${notices === 1 ? 'notice' : 'notices'} worth a look` : null,
    counts: countsSentence(totals),
  };
}

/** The answer as one line of text: its parts, " · " between them (the window title, a copy, a test). */
export function answerSentence(totals: AnalysisTotals): string {
  const { fix, notices, counts } = answerParts(totals);
  return [fix, notices, counts].filter(Boolean).join(' · ');
}

function countsSentence({ found, issues, passed, skipped }: AnalysisTotals): string {
  const first =
    found === 0
      ? 'No check found an issue on this crawl.'
      : `${formatCountOf(found, 'check')} found ${formatCountOf(issues, 'issue')} on this crawl.`;
  const were = (count: number) => (count === 1 ? 'was' : 'were');
  const second =
    passed > 0 && skipped > 0
      ? `${formatCount(passed)} passed and ${formatCount(skipped)} ${were(skipped)} skipped.`
      : passed > 0
        ? found === 0
          ? passed === 1
            ? 'The 1 check counted passed.'
            : `All ${formatCountOf(passed, 'check')} passed.`
          : `${formatCount(passed)} passed.`
        : skipped > 0
          ? `${formatCount(skipped)} ${were(skipped)} skipped.`
          : '';
  return second ? `${first} ${second}` : first;
}

/**
 * Checks that found issues, per severity, hidden ones left out: what the severity filter and the sidebar's Errors,
 * Warnings and Notices count, from the same entries, so they can't disagree. `keep` narrows it further (the list's
 * text filter).
 */
export function foundBySeverity(
  entries: readonly CheckEntry[],
  keep: (entry: CheckEntry) => boolean = () => true,
): Record<Severity | 'all', number> {
  const counts: Record<Severity | 'all', number> = { all: 0, error: 0, warning: 0, notice: 0 };
  for (const entry of entries) {
    if (entry.outcome === 'passed' || entry.outcome === 'skipped' || entry.hidden || !keep(entry)) continue;
    counts.all++;
    counts[entry.check.severity]++;
  }
  return counts;
}

const SEVERITY_RANK: Record<Severity, number> = { error: 0, warning: 1, notice: 2 };

/**
 * The checks that found issues, in the order to fix them. `order` is the analysis's `fixFirst`, the engine's order
 * (docs/v2-sidecar-api.md, The score): the most severe, a fault of the whole site before any other of its severity,
 * then the most URLs flagged, then catalog order; every host takes it as it is, never sorted again by count, which
 * would put a site-wide fault last of its severity. Without it (an earlier engine's analysis), the app's own: the most
 * severe, then the most pages or URLs affected (each check's count, whichever it is of), then catalog order. Hidden
 * checks are left out, unless every check with issues is hidden. Overview's "Fix first" is this list, and Issues
 * opens on its first.
 */
export function rankChecksToFix(entries: readonly CheckEntry[], order?: readonly string[] | null): CheckEntry[] {
  const found = entries.filter((entry) => entry.outcome !== 'passed' && entry.outcome !== 'skipped');
  const candidates = found.some((entry) => !entry.hidden) ? found.filter((entry) => !entry.hidden) : found;
  // A stable sort, so checks level on severity and count keep catalog order.
  const own = [...candidates].sort(
    (a, b) => SEVERITY_RANK[a.check.severity] - SEVERITY_RANK[b.check.severity] || (b.count ?? 0) - (a.count ?? 0),
  );
  if (!order) return own;
  // The engine's order; a check it doesn't rank (it knows every check that found something) goes after, as the app's.
  const place = new Map(order.map((checkId, index) => [checkId, index]));
  const ranked = own.filter((entry) => place.has(entry.check.id));
  ranked.sort((a, b) => (place.get(a.check.id) ?? 0) - (place.get(b.check.id) ?? 0));
  return [...ranked, ...own.filter((entry) => !place.has(entry.check.id))];
}

/**
 * The check to fix first, in the engine's order (`order`, the analysis's `fixFirst`) or, without it, the app's.
 * Hidden checks are left out, unless every check with issues is hidden. Null when no check found an issue.
 */
export function firstCheckToFix(entries: readonly CheckEntry[], order?: readonly string[] | null): CheckEntry | null {
  return rankChecksToFix(entries, order)[0] ?? null;
}

/** The title every category the catalog doesn't name (a newer engine's) is listed under, rather than its id. */
const OTHER_CHECKS = 'Other checks';

/**
 * Whether a check is in the category a filter names. An id the catalog doesn't have names every check whose category
 * it doesn't have either: the ones listed as "Other checks".
 */
export function isInCategory(checks: Checks, check: Check, category: string): boolean {
  const known = (id: string) => checks.categories.some((candidate) => candidate.id === id);
  return known(category) ? check.category === category : !known(check.category);
}

/** A category by its id, with the title it's listed under. */
export function categoryNamed(checks: Checks, id: string): { id: string; title: string } {
  return { id, title: checks.categories.find((category) => category.id === id)?.title ?? OTHER_CHECKS };
}

function matchesText(entry: CheckEntry, category: string, words: readonly string[]): boolean {
  if (words.length === 0) return true;
  const haystack = `${entry.check.title} ${category}`.toLowerCase();
  return words.every((word) => haystack.includes(word));
}

/**
 * The checks that found issues in `order`'s order, the rest after them as they came (a stable sort, so checks level on
 * every key keep catalog order):
 * - `fixFirst`: the engine's order (the analysis's `fixFirst`), a check it doesn't rank after, by the app's own order
 *   (`rankChecksToFix`): the most severe, then the most URLs;
 * - `count`: the most URLs flagged, then fix-first;
 * - `points`: the most points off the score, then fix-first, so notices (no points) come last;
 * - `severity`: the most severe, then fix-first;
 * - `category`: as they came.
 */
export function sortChecks(entries: readonly CheckEntry[], order: CheckListOrder): CheckEntry[] {
  if (order.sort === 'category') return [...entries];
  const found = (entry: CheckEntry) => entry.outcome !== 'passed' && entry.outcome !== 'skipped';
  const place = order.fixFirst ? new Map(order.fixFirst.map((checkId, index) => [checkId, index])) : null;
  const severity = (entry: CheckEntry) => SEVERITY_RANK[entry.check.severity];
  const fixFirst = (a: CheckEntry, b: CheckEntry) => {
    const [first, second] = [place?.get(a.check.id), place?.get(b.check.id)];
    if (first !== undefined && second !== undefined) return first - second;
    if (first !== undefined || second !== undefined) return first !== undefined ? -1 : 1;
    return severity(a) - severity(b) || (b.count ?? 0) - (a.count ?? 0);
  };
  const points = (entry: CheckEntry) => order.points?.get(entry.check.id) ?? 0;
  const compare: Record<Exclude<CheckSort, 'category'>, (a: CheckEntry, b: CheckEntry) => number> = {
    fixFirst,
    count: (a, b) => (b.count ?? 0) - (a.count ?? 0) || fixFirst(a, b),
    points: (a, b) => points(b) - points(a) || fixFirst(a, b),
    severity: (a, b) => severity(a) - severity(b) || fixFirst(a, b),
  };
  return [...entries.filter(found).sort(compare[order.sort]), ...entries.filter((entry) => !found(entry))];
}

const SORTED_PARTS: Record<CheckOutcome, string> = {
  error: 'Errors',
  warning: 'Warnings',
  notice: 'Notices',
  skipped: 'Skipped',
  passed: 'Passed',
};

/**
 * A sorted list's parts: one of the checks that found issues, in the order asked for, or one a severity when sorted by
 * severity; then the skipped checks and the ones that passed, each in catalog order.
 */
function sortedParts(listed: readonly CheckEntry[], order: CheckListOrder): CheckCategoryEntry[] {
  const parts = new Map<string, CheckCategoryEntry>();
  for (const entry of sortChecks(listed, order)) {
    const found = entry.outcome !== 'passed' && entry.outcome !== 'skipped';
    const key = found && order.sort !== 'severity' ? 'found' : entry.outcome;
    let part = parts.get(key);
    if (!part) {
      part = {
        id: `sorted-${key}`,
        title: key === 'found' ? 'Found issues' : SORTED_PARTS[entry.outcome],
        passed: 0,
        entries: [],
      };
      parts.set(key, part);
    }
    part.entries.push(entry);
  }
  const rank = ['found', 'error', 'warning', 'notice', 'skipped', 'passed'];
  return [...parts.entries()].sort(([a], [b]) => rank.indexOf(a) - rank.indexOf(b)).map(([, part]) => part);
}

/**
 * The list as it shows: by category in catalog order, each with the checks the filters keep, or, sorted (`view`),
 * the checks that found issues in that order, then the skipped and passed ones. A category the catalog doesn't name (a
 * newer engine's) is listed last, as "Other checks", rather than under its id.
 */
export function buildCheckList(
  checks: Checks,
  entries: readonly CheckEntry[],
  filters: CheckListFilters,
  view: CheckListOrder = { sort: 'category' },
): CheckList {
  const words = filters.q.trim().toLowerCase().split(/\s+/).filter(Boolean);
  const severities = new Set<Severity>(filters.severity.length > 0 ? filters.severity : SEVERITIES);
  const categories = new Map<string, CheckCategoryEntry>();
  const order: CheckCategory[] = [...checks.categories];
  const titleOf = new Map(order.map((category) => [category.id, category.title]));
  for (const entry of entries) {
    if (!titleOf.has(entry.check.category)) {
      titleOf.set(entry.check.category, OTHER_CHECKS);
      order.push({ id: entry.check.category, title: OTHER_CHECKS });
    }
  }
  const only = filters.category;
  const inCategory = (entry: CheckEntry) => only === undefined || isInCategory(checks, entry.check, only);
  for (const category of order) {
    categories.set(category.id, { id: category.id, title: category.title, passed: 0, entries: [] });
  }

  let hiddenCount = 0;
  let passedCount = 0;
  let unfiltered = 0;
  const matching: CheckEntry[] = [];
  for (const entry of entries) {
    const category = categories.get(entry.check.category);
    if (!category) continue;
    // The text and category filters together: what the severity then chooses among.
    const text = inCategory(entry) && matchesText(entry, category.title, words);
    if (text && severities.has(entry.check.severity)) matching.push(entry);
    if (entry.outcome === 'passed' && !entry.hidden) {
      category.passed++;
      passedCount++;
    }
    // Shown at all: found and skipped checks always, passed ones when asked for, hidden ones when shown.
    const listed = entry.outcome !== 'passed' || filters.showPassed;
    if (!listed) continue;
    if (entry.hidden && !filters.showHidden) {
      if (text && severities.has(entry.check.severity)) hiddenCount++;
      continue;
    }
    unfiltered++;
    if (!text || !severities.has(entry.check.severity)) continue;
    category.entries.push(entry);
  }

  const severityCounts = foundBySeverity(
    entries,
    (entry) => inCategory(entry) && matchesText(entry, categories.get(entry.check.category)?.title ?? '', words),
  );
  const byCategory = [...categories.values()].filter((category) => category.entries.length > 0);
  const shown =
    view.sort === 'category'
      ? byCategory
      : sortedParts(
          byCategory.flatMap((category) => category.entries),
          view,
        );
  const visible = shown.flatMap((category) => category.entries);
  return {
    categories: shown,
    all: [...entries],
    visible,
    matching,
    severityCounts,
    category: only === undefined ? null : categoryNamed(checks, only),
    hiddenCount,
    passedCount,
    filteredToNothing: visible.length === 0 && unfiltered > 0,
  };
}

/** The category's title for a check, for its header ("Links"). */
export function categoryTitle(checks: Checks, check: Check): string {
  return categoryNamed(checks, check.category).title;
}

/** A check related to another by a fix (the catalog's `clears`), as it came out on this crawl. */
export interface RelatedCheck {
  entry: CheckEntry;
  /** The pages the fix clears there. */
  pages: number;
  /** They are every page it flagged. */
  all: boolean;
  /** Its points off the score, when the fix clears all of it and it takes some; null otherwise. */
  points: number | null;
}

/**
 * The checks related to one by a fix, on this crawl (docs/v2-sidecar-api.md, Analysis: `counts[].clears`): those a fix of
 * it also clears, and those whose fix clears some of its own pages. Only checks that found issues, as the analysis
 * counts them; a hidden one too, as hiding it doesn't change what a fix does.
 */
export function relatedChecks(
  entries: readonly CheckEntry[],
  entry: CheckEntry,
  points?: ReadonlyMap<string, number>,
): { clears: RelatedCheck[]; clearedBy: RelatedCheck[] } {
  const byId = new Map(entries.map((candidate) => [candidate.check.id, candidate]));
  const pointsOf = (id: string, all: boolean) => {
    const value = all ? (points?.get(id) ?? 0) : 0;
    return value > 0 ? value : null;
  };
  const clears = entry.clears.flatMap((cleared) => {
    const other = byId.get(cleared.checkId);
    return other
      ? [{ entry: other, pages: cleared.pages, all: cleared.all, points: pointsOf(cleared.checkId, cleared.all) }]
      : [];
  });
  const clearedBy = entries.flatMap((other) =>
    other.clears
      .filter((cleared) => cleared.checkId === entry.check.id)
      .map((cleared) => ({ entry: other, pages: cleared.pages, all: cleared.all, points: null })),
  );
  return { clears, clearedBy };
}
