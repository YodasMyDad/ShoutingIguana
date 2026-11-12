// The Issues screen's checks list, built from the catalog and the last analysis (docs/v2-ui-ia.md, Issues): checks by
// category in catalog order, each with its severity and the pages or URLs it flags. Pure, so every rule is tested:
// - skipped checks are always listed, in their category, with their reason: hiding them would read as clean;
// - checks that passed are listed only when asked for, and each category says how many passed;
// - the severity and text filters narrow what's listed, and hidden checks stay out unless shown.
import type { Analysis, Check, CheckCategory, Checks, Severity } from '../../api/types';
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
}

export interface CheckCategoryEntry {
  id: string;
  title: string;
  /** Checks in the category that passed, hidden ones left out: "4 passed". */
  passed: number;
  /** What the list shows, in catalog order. */
  entries: CheckEntry[];
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
}

/** What each check came to on this crawl, in catalog order. */
export function checkEntries(
  checks: Checks,
  analysis: Analysis,
  hidden: ReadonlySet<string> = new Set(),
): CheckEntry[] {
  const counts = new Map(analysis.counts.map((entry) => [entry.checkId, entry.count]));
  const skipped = new Map(analysis.skipped.map((entry) => [entry.checkId, entry.reason]));
  return checks.checks.map((check) => {
    const count = counts.get(check.id);
    // A check the analysis doesn't mention ran under an engine that didn't have it: it's skipped, never clean.
    if (count === undefined || skipped.has(check.id)) {
      return {
        check,
        outcome: 'skipped',
        count: null,
        skipReason: skipped.get(check.id)?.text ?? null,
        hidden: hidden.has(check.id),
      };
    }
    return {
      check,
      outcome: count > 0 ? check.severity : 'passed',
      count,
      skipReason: null,
      hidden: hidden.has(check.id),
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
  for (const entry of entries) {
    if (entry.outcome === 'skipped') skipped++;
    else if (entry.outcome === 'passed') passed++;
    else {
      found++;
      issues += entry.count ?? 0;
    }
  }
  return { found, issues, passed, skipped };
}

/**
 * The header's answer, from counts only: "19 checks found 933 issues on this crawl. 63 passed and 3 were skipped."
 */
export function answerSentence({ found, issues, passed, skipped }: AnalysisTotals): string {
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
          ? `All ${formatCountOf(passed, 'check')} passed.`
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
 * The checks that found issues, in the order to fix them: the most severe, then the most pages or URLs affected (each
 * check's count, whichever it is of), then catalog order. Hidden checks are left out, unless every check with issues
 * is hidden. Overview's "Fix first" is this list, and Issues opens on its first.
 */
export function rankChecksToFix(entries: readonly CheckEntry[]): CheckEntry[] {
  const found = entries.filter((entry) => entry.outcome !== 'passed' && entry.outcome !== 'skipped');
  const candidates = found.some((entry) => !entry.hidden) ? found.filter((entry) => !entry.hidden) : found;
  // A stable sort, so checks level on severity and count keep catalog order.
  return [...candidates].sort(
    (a, b) => SEVERITY_RANK[a.check.severity] - SEVERITY_RANK[b.check.severity] || (b.count ?? 0) - (a.count ?? 0),
  );
}

/**
 * The check to fix first: the most severe, then the most pages or URLs affected, then catalog order. Hidden checks
 * are left out, unless every check with issues is hidden. Null when no check found an issue.
 */
export function firstCheckToFix(entries: readonly CheckEntry[]): CheckEntry | null {
  return rankChecksToFix(entries)[0] ?? null;
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
 * The list as it shows: categories in catalog order, each with the checks the filters keep. A category the catalog
 * doesn't name (a newer engine's) is listed last, as "Other checks", rather than under its id.
 */
export function buildCheckList(checks: Checks, entries: readonly CheckEntry[], filters: CheckListFilters): CheckList {
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
  const shown = [...categories.values()].filter((category) => category.entries.length > 0);
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
