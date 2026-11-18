// What to fix's Fixed group, its entries (docs/v2-fix-loop.md, section 4, "What to fix"): the checks that found
// something on the site's last check and nothing now, problems first; or, for a site's first check, what its rechecks
// cleared. Hidden checks stay out, as everywhere. Pure.
import type { CrawlComparison, Recheck } from '../api/fixLoop';
import type { Check, Severity } from '../api/types';
import { fixedThenWords, onWords } from '../changes/changeWords';
import { formatCount } from '../lib/format';

/** Its place in What to fix's address: where the Summary's "3 problems fixed" lands. */
export const FIXED_SECTION = 'fixed-since';

/** A check in the group: the check, and what to say of it then. */
export interface FixedEntry {
  check: Check;
  /** Its severity: the group counts problems, then things worth a look, as Simple's headline does. */
  severity: Severity;
  /** "9 addresses on 3 Nov", "3 pages before the recheck on 17 Nov". */
  then: string;
}

const ORDER: Record<Severity, number> = { error: 0, warning: 1, notice: 2 };

/** The checks fixed since the last check, problems first, each in the comparison's order. */
export function fixedSinceEntries(
  comparison: CrawlComparison,
  checks: readonly Check[],
  hidden: ReadonlySet<string>,
): FixedEntry[] {
  const byId = new Map(checks.map((check) => [check.id, check]));
  return comparison.checks
    .filter((change) => change.state === 'fixed' && !hidden.has(change.checkId))
    .flatMap((change) => {
      const check = byId.get(change.checkId);
      return check
        ? [{ check, then: fixedThenWords(check, change.then, comparison.with.startedAt), severity: check.severity }]
        : [];
    })
    .sort((a, b) => ORDER[a.severity] - ORDER[b.severity]);
}

/**
 * A first check's Fixed group: the checks a recheck cleared, those that found something before it and nothing now
 * (`passed`), each from its latest settled recheck.
 */
export function clearedByRechecks(
  rechecks: readonly Recheck[],
  checks: readonly Check[],
  passed: ReadonlySet<string>,
  hidden: ReadonlySet<string>,
): FixedEntry[] {
  const byId = new Map(checks.map((check) => [check.id, check]));
  const seen = new Set<string>();
  const entries: FixedEntry[] = [];
  for (const recheck of rechecks) {
    const id = recheck.checkId;
    if (!id || seen.has(id) || !recheck.result || (recheck.countBefore ?? 0) === 0) continue;
    seen.add(id);
    const check = byId.get(id);
    if (!check || !passed.has(id) || hidden.has(id)) continue;
    const before = recheck.countBefore ?? 0;
    const pages = (check.subject ?? 'page') === 'page';
    const noun = pages ? (before === 1 ? 'page' : 'pages') : before === 1 ? 'address' : 'addresses';
    entries.push({
      check,
      severity: check.severity,
      then: `${formatCount(before)} ${noun} before the recheck ${onWords(recheck.endedAt ?? recheck.startedAt)}`,
    });
  }
  return entries;
}
