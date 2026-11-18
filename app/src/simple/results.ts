// What Simple's results are made of (docs/v2-ui-simple.md, "The Summary" and "What to fix"), from the catalog, the
// last analysis and the checks the user hid in Advanced. One model, so the Summary's counts, its five fixes, What to
// fix's groups and the sidebar's count are the same entries and can't disagree. Hidden checks are left out of every
// list and count, as Advanced leaves them out; the score is the engine's and counts them.
import type { Analysis, Checks, PlatformOwner, Severity, SkipReason } from '../api/types';
import { checkEntries, rankChecksToFix, type CheckEntry } from '../screens/issues/checkList';
import { plainTitle, wholesOf, type ProblemCounts, type Wholes } from './plainWords';

/** The most problems "Fix these first" lists. */
export const FIX_FIRST_COUNT = 5;

export interface SkippedGroup {
  /** The reason's code, or `null` for checks an earlier engine skipped without saying why. */
  code: string | null;
  reason: SkipReason | null;
  entries: CheckEntry[];
}

/** A check that found what only the site's platform can change (the Shopify lane, phase B), with who and why. */
export interface PlatformEntry {
  entry: CheckEntry;
  owner: PlatformOwner;
}

export interface PlainResults {
  /** Checks that found something and aren't hidden, by severity, each in the engine's order to fix them. */
  found: Record<Severity, CheckEntry[]>;
  /** Checks with problems, by severity: the Summary's counts and What to fix's groups. */
  counts: ProblemCounts;
  /**
   * Checks that found what only the site's platform can change (the analysis's `counts[].platform`), hidden ones left
   * out: never a problem to fix, so in none of `found`, `counts` or `fixFirst`; listed apart, each with its reason.
   */
  platform: PlatformEntry[];
  /** Up to five Serious and Worth fixing problems, in the engine's "Fix first" order. */
  fixFirst: CheckEntry[];
  /**
   * The score with `fixFirst` fixed, when the engine's `ifFixed` is of exactly those checks: a hidden check among the
   * engine's five makes its figure about other checks, and then nothing is said.
   */
  ifFixed: number | null;
  /** Checks that ran and found nothing, hidden ones left out. */
  passed: CheckEntry[];
  /** Checks that ran, hidden ones left out: "63 of 82 checks found nothing wrong". */
  ran: number;
  /** Checks that couldn't run, by reason, in catalog order of each reason's first check. Never hidden: a gap isn't. */
  skipped: SkippedGroup[];
  skippedCount: number;
  /** The plain titles of categories in which every check that ran found nothing, at least one having run. */
  fineCategories: string[];
  /** Checks the user hid in Advanced. */
  hidden: CheckEntry[];
  /** A serious fault of the whole site that stands, which leads the sentence and the list. */
  siteWideSerious: CheckEntry | null;
  wholes: Wholes;
}

/** Simple's results for an analysis. `hidden` is the checks the user hid in Advanced. */
export function plainResults(checks: Checks, analysis: Analysis, hidden: ReadonlySet<string>): PlainResults {
  const all = checkEntries(checks, analysis, hidden);
  const shown = all.filter((entry) => !entry.hidden);
  // What the site's platform sets is no problem to fix: apart from the rest, with who sets it.
  const owners = new Map(analysis.counts.flatMap((count) => (count.platform ? [[count.checkId, count.platform]] : [])));
  const ranked = rankChecksToFix(shown, analysis.fixFirst);
  const toFix = ranked.filter((entry) => !owners.has(entry.check.id));
  const found: Record<Severity, CheckEntry[]> = {
    error: toFix.filter((entry) => entry.outcome === 'error'),
    warning: toFix.filter((entry) => entry.outcome === 'warning'),
    notice: toFix.filter((entry) => entry.outcome === 'notice'),
  };
  const platform = ranked.flatMap((entry) => {
    const owner = owners.get(entry.check.id);
    return owner && entry.outcome !== 'passed' && entry.outcome !== 'skipped' ? [{ entry, owner }] : [];
  });
  const fixFirst = [...found.error, ...found.warning].slice(0, FIX_FIRST_COUNT);
  const score = analysis.score ?? null;
  const sameFive =
    score !== null &&
    score.ifFixed.checkIds.length === fixFirst.length &&
    fixFirst.every((entry) => score.ifFixed.checkIds.includes(entry.check.id));

  const skippedEntries = all.filter((entry) => entry.outcome === 'skipped');
  const reasons = new Map(analysis.skipped.map((skip) => [skip.checkId, skip.reason]));
  const groups = new Map<string, SkippedGroup>();
  for (const entry of skippedEntries) {
    const reason = reasons.get(entry.check.id) ?? null;
    const key = reason?.code ?? '';
    const group = groups.get(key) ?? { code: reason?.code ?? null, reason, entries: [] };
    group.entries.push(entry);
    groups.set(key, group);
  }

  const passed = shown.filter((entry) => entry.outcome === 'passed');
  const ran = shown.filter((entry) => entry.outcome !== 'skipped');
  const fineCategories = checks.categories
    .filter((category) => {
      const inCategory = ran.filter((entry) => entry.check.category === category.id);
      return inCategory.length > 0 && inCategory.every((entry) => entry.outcome === 'passed');
    })
    .map((category) => category.plainTitle ?? category.title);
  const siteWideSerious = found.error.find((entry) => entry.check.siteWide) ?? null;

  return {
    found,
    counts: { error: found.error.length, warning: found.warning.length, notice: found.notice.length },
    platform,
    fixFirst,
    ifFixed: sameFive && fixFirst.length > 0 ? score.ifFixed.value : null,
    passed,
    ran: ran.length,
    skipped: [...groups.values()],
    skippedCount: skippedEntries.length,
    fineCategories,
    hidden: all.filter((entry) => entry.hidden),
    siteWideSerious,
    wholes: wholesOf(analysis),
  };
}

/** The Summary's sentence's lead for a site-wide serious fault: its plain title. */
export function siteWideLead(results: PlainResults): string | null {
  return results.siteWideSerious ? plainTitle(results.siteWideSerious.check) : null;
}
