// The checks that moved since the crawl compared with, as Overview's comparison card lists them (the audit of
// 2026-11-19, H-06): what got worse, worst first, then what got better, each with its count then and now. "Checks: 2
// fixed · 2 more" hid a check that went from 18 pages to 257 behind the word "more". Pure.
import type { CheckChange } from '../../api/fixLoop';
import type { Check, Severity } from '../../api/types';
import { changeSeverity, type SeverityOf } from '../../changes/changes';
import { affectedNoun } from '../../crawl/checkSubject';
import { formatCount, formatCountOf } from '../../lib/format';

export interface MovedCheck {
  checkId: string;
  title: string;
  severity: Severity | null;
  state: 'new' | 'more' | 'fixed' | 'fewer';
  then: number;
  now: number;
  /** "18 → 257 pages": the count then and now, in what the check flags. */
  counts: string;
  /** For a screen reader: "18 pages then, 257 now". */
  spoken: string;
}

export interface MovedChecks {
  /** Found something new, or flags more: the largest rise first. */
  worse: MovedCheck[];
  /** Fixed, or flags fewer: the largest fall first. */
  better: MovedCheck[];
}

const SEVERITY_ORDER: Record<Severity, number> = { error: 0, warning: 1, notice: 2 };
const rank = (severity: Severity | null) => (severity === null ? 3 : SEVERITY_ORDER[severity]);

/**
 * `checks` are the comparison's counted checks (hidden ones out). A check without a count on either side isn't listed:
 * there is nothing to say of it but its state, which the card's other line does.
 */
export function movedChecks(
  checks: readonly CheckChange[],
  catalog: readonly Pick<Check, 'id' | 'title' | 'severity' | 'subject'>[],
  severityOf?: SeverityOf,
): MovedChecks {
  const byId = new Map(catalog.map((check) => [check.id, check]));
  const moved: MovedCheck[] = [];
  for (const change of checks) {
    const { state } = change;
    if (state !== 'new' && state !== 'more' && state !== 'fixed' && state !== 'fewer') continue;
    const check = byId.get(change.checkId);
    const then = change.then ?? 0;
    const now = change.now ?? 0;
    const noun = affectedNoun(check ?? null);
    moved.push({
      checkId: change.checkId,
      title: check?.title ?? change.checkId,
      severity: changeSeverity(change, severityOf),
      state,
      then,
      now,
      counts: `${formatCount(then)} → ${formatCountOf(now, noun)}`,
      spoken: `${formatCountOf(then, noun)} then, ${formatCount(now)} now`,
    });
  }
  const bySize = (a: MovedCheck, b: MovedCheck) =>
    Math.abs(b.now - b.then) - Math.abs(a.now - a.then) || rank(a.severity) - rank(b.severity);
  return {
    worse: moved.filter((check) => check.state === 'new' || check.state === 'more').sort(bySize),
    better: moved.filter((check) => check.state === 'fixed' || check.state === 'fewer').sort(bySize),
  };
}
