// The fix loop's states, shared by Simple and Advanced (docs/v2-fix-loop.md, section 4, "The states, at a glance"):
// what a crawl's comparison with its previous crawl is in now, and what a mode counts of it. The words for each are in
// ./changeWords and ./recheckWords. Pure.
//
// The rules every screen keeps:
// - A first crawl (no previous) shows nothing: no line, no zeros, no "+0".
// - Hidden checks are left out of every count and list, as everywhere in the app; the sidecar's answers have them all.
// - Simple counts problems (Serious and Worth fixing), as its other counts do; Advanced counts every check.
// - A check that didn't run last time (`newCheck`) is never a new problem; "not checked" is never fixed.
import type { CheckChange, CheckChangeState, Comparability, CrawlChanges, CrawlComparison } from '../api/fixLoop';
import type { Check, CrawlListItem, CrawlStatus, Severity } from '../api/types';
import { isLive } from '../api/types';

/** Simple's words or Advanced's terms. */
export type ChangeMode = 'simple' | 'advanced';

/** A check's severity by id, from the catalog: null for a check it doesn't have. */
export type SeverityOf = (checkId: string) => Severity | null;

/** The catalog's severities, for `SeverityOf`. */
export function severitiesOf(checks: readonly Pick<Check, 'id' | 'severity'>[]): SeverityOf {
  const severities = new Map(checks.map((check) => [check.id, check.severity]));
  return (checkId) => severities.get(checkId) ?? null;
}

/** Whether a mode counts a check of this severity: Simple, only problems; Advanced, every check. */
export function counts(mode: ChangeMode, severity: Severity | null): boolean {
  if (severity === null) return false;
  return mode === 'advanced' || severity === 'error' || severity === 'warning';
}

/** How many checks are in each state, as a mode counts them. */
export interface ChangeCounts {
  fixed: number;
  new: number;
  fewer: number;
  more: number;
  /** Didn't run last time: never counted as new. */
  newCheck: number;
  notCheckedNow: number;
}

export const NO_CHANGE_COUNTS: ChangeCounts = { fixed: 0, new: 0, fewer: 0, more: 0, newCheck: 0, notCheckedNow: 0 };

/** The checks of each state a mode counts: in the sidecar's order, the hidden ones left out. */
export interface CountedChanges {
  fixed: string[];
  new: string[];
  fewer: string[];
  more: string[];
  newCheck: string[];
  notCheckedNow: string[];
}

/**
 * A library item's changes (`changes`, from cached summaries) as a mode counts them: hidden checks out, and in Simple only
 * problems. A fixed check counts by its severity in the catalog, which is the same then and now.
 */
export function countedItemChanges(
  changes: CrawlChanges,
  severityOf: SeverityOf,
  hidden: ReadonlySet<string>,
  mode: ChangeMode,
): CountedChanges {
  const keep = (ids: readonly string[]) => ids.filter((id) => !hidden.has(id) && counts(mode, severityOf(id)));
  return {
    fixed: keep(changes.fixed),
    new: keep(changes.new),
    fewer: keep(changes.fewer),
    more: keep(changes.more),
    newCheck: keep(changes.newCheck),
    notCheckedNow: keep(changes.notCheckedNow),
  };
}

/** How many of each. */
export function changeCounts(changes: CountedChanges): ChangeCounts {
  return {
    fixed: changes.fixed.length,
    new: changes.new.length,
    fewer: changes.fewer.length,
    more: changes.more.length,
    newCheck: changes.newCheck.length,
    notCheckedNow: changes.notCheckedNow.length,
  };
}

/**
 * A check's severity as a comparison counts it: a check gone (fixed) by its severity then, any other by its severity
 * now, and the catalog's where the answer has none.
 */
export function changeSeverity(check: CheckChange, severityOf?: SeverityOf): Severity | null {
  const own = check.state === 'fixed' ? (check.severityThen ?? check.severity) : (check.severity ?? check.severityThen);
  return own ?? severityOf?.(check.checkId) ?? null;
}

/** A comparison's checks as a mode counts them: hidden ones out, and in Simple only problems. In the sidecar's order. */
export function countedChecks(
  comparison: CrawlComparison,
  hidden: ReadonlySet<string>,
  mode: ChangeMode,
  severityOf?: SeverityOf,
): CheckChange[] {
  return comparison.checks.filter(
    (check) => !hidden.has(check.checkId) && counts(mode, changeSeverity(check, severityOf)),
  );
}

/** A comparison's checks counted by state, from `countedChecks`. */
export function comparisonCounts(checks: readonly CheckChange[]): ChangeCounts {
  const tally = { ...NO_CHANGE_COUNTS };
  for (const check of checks) {
    if (check.state === 'same') continue;
    tally[check.state] += 1;
  }
  return tally;
}

/** One check's change in a comparison, or null when the comparison doesn't list it (it ran in neither crawl). */
export function checkChangeOf(comparison: CrawlComparison | null, checkId: string): CheckChange | null {
  return comparison?.checks.find((check) => check.checkId === checkId) ?? null;
}

/**
 * Whether a check's change says anything worth a mark: anything but `same` with nothing moved. A `same` whose rows
 * moved (2 fixed, 2 new) still has its split to say.
 */
export function hasMoved(check: CheckChange): boolean {
  if (check.state !== 'same') return true;
  return (check.fixed ?? 0) + (check.gone ?? 0) + (check.new ?? 0) + (check.newUrl ?? 0) > 0;
}

// ------------------------------------------------------------------------------------------- The states at a glance

/**
 * Where a crawl's comparison stands, as every screen shows it (docs/v2-fix-loop.md, "The states, at a glance"):
 * - `none`: nothing to compare (a first crawl, a crawl never analysed, a previous crawl that went): show nothing;
 * - `live`: this crawl is being crawled, analysed or rechecked here: nothing until its results are worked out;
 * - `loading`: the comparison is on its way: Simple shows the screen without the change line, Advanced a skeleton;
 * - `needsAnalysis`: the previous crawl was analysed by other rules: Simple analyses it again by itself (the line
 *   appears when done), Advanced shows the counts-only comparison with "Analyse it again";
 * - `shown`: a comparison to show, `full`, `partial` (one caution line) or `countsOnly` (counts, no marks on rows).
 */
export type ComparisonStage =
  | { kind: 'none' }
  | { kind: 'live' }
  | { kind: 'loading' }
  | { kind: 'needsAnalysis'; previousId: string; comparison: CrawlComparison | null }
  | { kind: 'shown'; comparison: CrawlComparison; comparable: Comparability };

/**
 * The stage, from the crawl's item and status, and what `/changes` answered (undefined while it's on its way, null for
 * no comparison). `previousNeedsAnalysis` names why the previous crawl's analysis isn't like this one's.
 */
export function comparisonStage(
  item: Pick<CrawlListItem, 'previous'> | undefined,
  status: CrawlStatus | null,
  comparison: CrawlComparison | null | undefined,
): ComparisonStage {
  if (status !== null && isLive(status)) return { kind: 'live' };
  if (!item?.previous) return { kind: 'none' };
  if (comparison === undefined) return { kind: 'loading' };
  if (comparison === null) return { kind: 'none' };
  if (comparison.previousNeedsAnalysis.length > 0 || item.previous.needsAnalysis) {
    return { kind: 'needsAnalysis', previousId: comparison.with.id, comparison };
  }
  return { kind: 'shown', comparison, comparable: comparison.comparable };
}

/** Whether a comparison's rows can be marked: never with counts only, where no URL could be matched. */
export function marksRows(comparable: Comparability): boolean {
  return comparable !== 'countsOnly';
}

/** The state of a check whose state is a count's direction, for sorting and filtering: the drop, largest first. */
export function countDrop(check: Pick<CheckChange, 'then' | 'now'>): number {
  return (check.then ?? 0) - (check.now ?? 0);
}

/** The states a check's change counts as "changed" (Advanced's Changed filter). */
export const CHANGED_STATES: readonly CheckChangeState[] = ['new', 'fixed', 'fewer', 'more'];
