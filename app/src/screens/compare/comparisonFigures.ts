// The figures of Overview's comparison card (docs/v2-fix-loop.md, Advanced: "Score 71 → 85 ▲14 │ Errors 3 → 2 ▼1 …",
// then the checks and the issues by how they moved). Hidden checks are left out of every figure, as everywhere. Pure.
import type { CrawlComparison, RowSplit } from '../../api/fixLoop';
import type { Severity } from '../../api/types';
import {
  changeSeverity,
  comparisonCounts,
  countedChecks,
  type ChangeCounts,
  type SeverityOf,
} from '../../changes/changes';
import { moveGlyph } from '../../changes/changeWords';
import { formatCount } from '../../lib/format';

export interface SeverityFigure {
  severity: Severity;
  /** Checks with issues of this severity then, and now. */
  then: number;
  now: number;
}

export interface ComparisonFigures {
  /** Null when either crawl has no score. `change` is null where they were scored another way. */
  score: { then: number; now: number; change: number | null; partial: boolean } | null;
  severities: SeverityFigure[];
  checks: ChangeCounts;
  /** The issues' split over the checks counted; null when counts only. */
  issues: Pick<RowSplit, 'fixed' | 'gone' | 'notChecked' | 'new' | 'newUrl'> | null;
}

const SEVERITIES: readonly Severity[] = ['error', 'warning', 'notice'];

export function comparisonFigures(
  comparison: CrawlComparison,
  hidden: ReadonlySet<string>,
  severityOf: SeverityOf,
): ComparisonFigures {
  const checks = countedChecks(comparison, hidden, 'advanced', severityOf);
  const severities = SEVERITIES.map((severity) => ({
    severity,
    then: checks.filter(
      (check) => (check.then ?? 0) > 0 && (check.severityThen ?? changeSeverity(check, severityOf)) === severity,
    ).length,
    now: checks.filter(
      (check) => (check.now ?? 0) > 0 && (check.severity ?? changeSeverity(check, severityOf)) === severity,
    ).length,
  }));
  const { score } = comparison;
  const split = comparison.totals.issues === null ? null : { fixed: 0, gone: 0, notChecked: 0, new: 0, newUrl: 0 };
  if (split) {
    for (const check of checks) {
      split.fixed += check.fixed ?? 0;
      split.gone += check.gone ?? 0;
      split.notChecked += check.notChecked ?? 0;
      split.new += check.new ?? 0;
      split.newUrl += check.newUrl ?? 0;
    }
  }
  return {
    score: score
      ? {
          then: score.then.value,
          now: score.now.value,
          change: score.change,
          partial: score.then.partial || score.now.partial,
        }
      : null,
    severities,
    checks: comparisonCounts(checks),
    issues: split,
  };
}

/**
 * A figure then and now: "3 → 2" shown, "▼1" for the eye (none without a move, or a change that can't be said), and
 * for a screen reader, "Errors: 3 then, 2 now, down 1".
 */
export function figureWords(
  label: string,
  then: number,
  now: number,
  change: number | null = now - then,
): { shown: string; glyph: string | null; spoken: string } {
  const shown = `${formatCount(then)} → ${formatCount(now)}`;
  const base = `${label}: ${formatCount(then)} then, ${formatCount(now)} now`;
  if (change === null) return { shown, glyph: null, spoken: base };
  if (change === 0) return { shown, glyph: null, spoken: `${base}, no change` };
  return {
    shown,
    glyph: moveGlyph(change),
    spoken: `${base}, ${change > 0 ? 'up' : 'down'} ${formatCount(Math.abs(change))}`,
  };
}
