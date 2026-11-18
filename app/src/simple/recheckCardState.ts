// The problem page's "Fixed it?" card as a recheck (docs/v2-fix-loop.md, section 4, "One problem"): which of its states
// it's in, from the check, the plan, the crawl's rechecks and the recheck's run (crawl/recheck.ts, shared with
// Advanced). Pure.
import type { Recheck, RecheckAsk, RecheckPlan, RecheckRefusal } from '../api/fixLoop';
import type { Check } from '../api/types';
import { isRecheckRunning, type PendingRecheck, type RecheckRun } from '../crawl/recheck';

/** Where a running recheck is: asked, its analysis first, fetching, stopping, or the analysis that settles it. */
export type RecheckStep = 'starting' | 'first' | 'fetching' | 'stopping' | 'settling';

export type RecheckCardState =
  /** A sidecar from before the fix loop: no recheck, the old card's Check the site again. */
  | { kind: 'legacy' }
  /** The check's `recheck` is empty: only a full crawl can show a fix. */
  | { kind: 'unsupported' }
  | { kind: 'loading' }
  /** The plan couldn't be read. */
  | { kind: 'planFailed' }
  /**
   * It can run: `ended` says why the last one asked here didn't run, when the sidecar said; `failed`, that it ran and
   * failed, which kept nothing it read.
   */
  | { kind: 'ready'; plan: RecheckPlan; ended: string | null; failed: boolean }
  | { kind: 'refused'; plan: RecheckPlan; refused: RecheckRefusal }
  /** A recheck of another of this crawl's problems runs: this one waits for it. */
  | { kind: 'busyElsewhere' }
  | { kind: 'running'; recheckId: number; step: RecheckStep; progress: RecheckRun['progress'] }
  /** The latest recheck of the check, settled: its result, and whether it can be rechecked again now. */
  | { kind: 'result'; recheck: Recheck & { result: NonNullable<Recheck['result']> }; plan: RecheckPlan | null };

/** A recheck of the whole check (not one of its causes, nor a list of URLs): what the Simple card gives the result of. */
function ofTheCheck(recheck: Pick<Recheck, 'checkId' | 'group' | 'urlList'>, checkId: string): boolean {
  return recheck.checkId === checkId && recheck.group === null && !recheck.urlList;
}

function askOfTheCheck(ask: RecheckAsk, checkId: string): boolean {
  return ask.checkId === checkId && ask.group === undefined && ask.urlIds === undefined;
}

const STEPS: Record<Exclude<RecheckRun['stage'], 'settled' | 'ended'>, RecheckStep> = {
  starting: 'starting',
  analysingFirst: 'first',
  fetching: 'fetching',
  stopping: 'stopping',
  analysing: 'settling',
};

export function recheckCardState(input: {
  check: Pick<Check, 'id' | 'recheck'>;
  plan: RecheckPlan | undefined;
  planFailed: boolean;
  /** The crawl's rechecks, newest first; undefined while they're read. */
  rechecks: readonly Recheck[] | undefined;
  /** The crawl's recheck as it runs here (`useRecheckRun`). */
  run: RecheckRun | null;
  /** The recheck this window started on the crawl (`pendingRecheck`). */
  pending: PendingRecheck | undefined;
}): RecheckCardState {
  const { check, plan, planFailed, rechecks, run, pending } = input;
  if (check.recheck === undefined) return { kind: 'legacy' };
  if (check.recheck.length === 0) return { kind: 'unsupported' };

  // Whose the run is: the one asked here says; else the crawl's list does.
  const asked = pending?.recheckId === run?.recheckId ? pending : undefined;
  const listed = run ? rechecks?.find((recheck) => recheck.id === run.recheckId) : undefined;
  const ours =
    run !== null && (asked ? askOfTheCheck(asked.ask, check.id) : listed ? ofTheCheck(listed, check.id) : false);

  if (run && isRecheckRunning(run)) {
    if (!ours) return { kind: 'busyElsewhere' };
    const step = run.stage === 'settled' || run.stage === 'ended' ? 'settling' : STEPS[run.stage];
    return { kind: 'running', recheckId: run.recheckId, step, progress: run.progress };
  }

  // Settled, but not in the crawl's list yet (read before it began): its result comes with the list's next read.
  if (run?.stage === 'settled' && ours && !listed) {
    return { kind: 'running', recheckId: run.recheckId, step: 'settling', progress: null };
  }

  // The latest settled recheck of the check: from the list, its result from the stream when the list hasn't been read
  // again since it settled.
  const latest = rechecks?.find((recheck) => ofTheCheck(recheck, check.id));
  if (latest) {
    const fresh = run?.stage === 'settled' && run.recheckId === latest.id ? run : null;
    const result = latest.result ?? fresh?.result ?? null;
    if (result) {
      const status = latest.result ? latest.status : (fresh?.status ?? latest.status);
      return { kind: 'result', recheck: { ...latest, status, result }, plan: plan ?? null };
    }
  }

  if (planFailed) return { kind: 'planFailed' };
  if (!plan) return { kind: 'loading' };
  if (plan.refused) return { kind: 'refused', plan, refused: plan.refused };
  const ended = run?.stage === 'ended' && ours ? (run.error ?? 'The recheck didn’t run.') : null;
  return { kind: 'ready', plan, ended, failed: ended !== null && run?.status === 'failed' };
}
