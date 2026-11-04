// The Issues screen's checks list, built from the catalog and the last analysis (docs/v2-ui-ia.md, Issues): checks by
// category in catalog order, each with its severity and pages affected. Pure, so every rule is unit-tested:
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
  /** Checks that found issues, per severity, as the severity filter counts them (text filter and hidden applied). */
  severityCounts: Record<Severity | 'all', number>;
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

const SEVERITY_RANK: Record<Severity, number> = { error: 0, warning: 1, notice: 2 };

/**
 * The check to fix first: the most severe, then the most pages affected, then catalog order. Hidden checks are left
 * out, unless every check with issues is hidden. Null when no check found an issue.
 */
export function firstCheckToFix(entries: readonly CheckEntry[]): CheckEntry | null {
  const found = entries.filter((entry) => entry.outcome !== 'passed' && entry.outcome !== 'skipped');
  const candidates = found.some((entry) => !entry.hidden) ? found.filter((entry) => !entry.hidden) : found;
  let best: CheckEntry | null = null;
  for (const entry of candidates) {
    if (!best) {
      best = entry;
      continue;
    }
    const rank = SEVERITY_RANK[entry.check.severity] - SEVERITY_RANK[best.check.severity];
    if (rank < 0 || (rank === 0 && (entry.count ?? 0) > (best.count ?? 0))) best = entry;
  }
  return best;
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
      titleOf.set(entry.check.category, 'Other checks');
      order.push({ id: entry.check.category, title: 'Other checks' });
    }
  }
  for (const category of order) {
    categories.set(category.id, { id: category.id, title: category.title, passed: 0, entries: [] });
  }

  const severityCounts: Record<Severity | 'all', number> = { all: 0, error: 0, warning: 0, notice: 0 };
  let hiddenCount = 0;
  let passedCount = 0;
  let unfiltered = 0;
  for (const entry of entries) {
    const category = categories.get(entry.check.category);
    if (!category) continue;
    const text = matchesText(entry, category.title, words);
    if (entry.outcome === 'passed' && !entry.hidden) {
      category.passed++;
      passedCount++;
    }
    const found = entry.outcome !== 'passed' && entry.outcome !== 'skipped';
    if (found && text && !entry.hidden) {
      severityCounts.all++;
      severityCounts[entry.check.severity]++;
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

  const shown = [...categories.values()].filter((category) => category.entries.length > 0);
  const visible = shown.flatMap((category) => category.entries);
  return {
    categories: shown,
    all: [...entries],
    visible,
    severityCounts,
    hiddenCount,
    passedCount,
    filteredToNothing: visible.length === 0 && unfiltered > 0,
  };
}

/** The category's title for a check, for its header ("Links"). */
export function categoryTitle(checks: Checks, check: Check): string {
  return checks.categories.find((category) => category.id === check.category)?.title ?? 'Other checks';
}
