// The problem page's "Fixed it?" card as a recheck (docs/v2-fix-loop.md, section 4, "One problem"): which of its states
// it's in, from the check, the plan, the crawl's rechecks, the live stream and what was asked here. Pure.
import type { Recheck, RecheckEvent, RecheckPlan, RecheckRefusal } from '../api/fixLoop';
import type { Check, CrawlStatus } from '../api/types';
import { recheckUnderway, type AskedRecheck } from '../crawl/rechecks';

/** Where a running recheck is: its analysis first, fetching, stopping, or the analysis that settles it. */
export type RecheckStep = 'first' | 'fetching' | 'stopping' | 'settling';

export type RecheckCardState =
  /** A sidecar from before the fix loop: no recheck, the old card's Check the site again. */
  | { kind: 'legacy' }
  /** The check's `recheck` is empty: only a full crawl can show a fix. */
  | { kind: 'unsupported' }
  | { kind: 'loading' }
  /** The plan couldn't be read. */
  | { kind: 'planFailed' }
  | { kind: 'ready'; plan: RecheckPlan }
  | { kind: 'refused'; plan: RecheckPlan; refused: RecheckRefusal }
  /** A recheck of another problem of this check runs: this one waits for it. */
  | { kind: 'busyElsewhere' }
  | { kind: 'running'; recheckId: number | null; step: RecheckStep; progress: RecheckEvent | null }
  /** The latest recheck of the check, settled: its result, and whether it can be rechecked again now. */
  | { kind: 'result'; recheck: Recheck & { result: NonNullable<Recheck['result']> }; plan: RecheckPlan | null };

/** A recheck of the whole check (not one of its causes, nor a list of URLs): what the Simple card gives the result of. */
function ofTheCheck(recheck: Recheck, checkId: string): boolean {
  return recheck.checkId === checkId && recheck.group === null && !recheck.urlList;
}

export function recheckCardState(input: {
  check: Pick<Check, 'id' | 'recheck'>;
  plan: RecheckPlan | undefined;
  planFailed: boolean;
  /** The crawl's rechecks, newest first; undefined while they're read. */
  rechecks: readonly Recheck[] | undefined;
  live: { status: CrawlStatus | null; recheck: RecheckEvent | null } | undefined;
  asked: AskedRecheck | undefined;
}): RecheckCardState {
  const { check, plan, planFailed, rechecks, live, asked } = input;
  if (check.recheck === undefined) return { kind: 'legacy' };
  if (check.recheck.length === 0) return { kind: 'unsupported' };

  const running = rechecks?.find((recheck) => recheck.status === 'running' && ofTheCheck(recheck, check.id));
  const ours = asked ? (asked.checkId === check.id ? asked.recheckId : undefined) : running?.id;
  if (recheckUnderway(live, asked) || (live?.status === 'rechecking' && running)) {
    if (ours === undefined) return { kind: 'busyElsewhere' };
    const event = live?.recheck?.recheckId === ours ? live.recheck : null;
    const status = live?.status ?? null;
    const step: RecheckStep =
      status === 'rechecking' ? 'fetching' : status === 'stopping' ? 'stopping' : event === null ? 'first' : 'settling';
    return { kind: 'running', recheckId: ours, step, progress: event };
  }

  // The latest settled recheck of the check: from the list, or from the stream's last event when the list hasn't been
  // read again since it settled.
  const event = live?.recheck?.result ? live.recheck : null;
  const latest = rechecks?.find((recheck) => ofTheCheck(recheck, check.id));
  if (latest) {
    const result = latest.result ?? (event?.recheckId === latest.id ? event.result : null);
    if (result) {
      const status = latest.result ? latest.status : (event?.status ?? latest.status);
      return { kind: 'result', recheck: { ...latest, status, result }, plan: plan ?? null };
    }
  }

  if (planFailed) return { kind: 'planFailed' };
  if (!plan) return { kind: 'loading' };
  if (plan.refused) return { kind: 'refused', plan, refused: plan.refused };
  return { kind: 'ready', plan };
}
