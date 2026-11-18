import { describe, expect, it } from 'vitest';
import type { PendingRecheck, RecheckRun } from '../crawl/recheck';
import { recheckPlan, recheckRecord, recheckResult } from '../test/fixLoopSidecar';
import { recheckCardState } from './recheckCardState';

const CHECK = { id: 'broken-internal-links', recheck: ['urls' as const] };
const base = { check: CHECK, plan: recheckPlan(), planFailed: false, rechecks: [], run: null, pending: undefined };
const ID = '0123456789abcdef';

const run = (patch: Partial<RecheckRun>): RecheckRun => ({
  crawlId: ID,
  recheckId: 1,
  stage: 'fetching',
  status: 'running',
  progress: { phase: 1, done: 3, total: 8 },
  result: null,
  error: null,
  ...patch,
});

const pending = (patch: Partial<PendingRecheck> = {}): PendingRecheck => ({
  recheckId: 1,
  ask: { checkId: CHECK.id },
  ended: false,
  error: null,
  ...patch,
});

describe('the recheck card’s state', () => {
  it('is the old card for a sidecar from before the fix loop, and Check the site again for a check it can’t settle', () => {
    expect(recheckCardState({ ...base, check: { id: 'x' } })).toEqual({ kind: 'legacy' });
    expect(recheckCardState({ ...base, check: { id: 'orphan-page', recheck: [] } })).toEqual({ kind: 'unsupported' });
  });

  it('waits for the plan, says when it couldn’t be read, and is ready or refused', () => {
    expect(recheckCardState({ ...base, plan: undefined })).toEqual({ kind: 'loading' });
    expect(recheckCardState({ ...base, plan: undefined, planFailed: true })).toEqual({ kind: 'planFailed' });
    expect(recheckCardState(base)).toMatchObject({ kind: 'ready', ended: null });
    const refused = { type: 'site-busy', title: 'A check of this site is running.' };
    expect(recheckCardState({ ...base, plan: recheckPlan({ refused }) })).toMatchObject({ kind: 'refused', refused });
  });

  it('follows the recheck asked here: starting, its analysis first, fetching, stopping, the analysis that settles it', () => {
    const asked = pending();
    const step = (stage: RecheckRun['stage']) =>
      recheckCardState({ ...base, pending: asked, run: run({ stage }) }) as { kind: string; step?: string };
    expect(step('starting')).toMatchObject({ kind: 'running', step: 'starting' });
    expect(step('analysingFirst')).toMatchObject({ kind: 'running', step: 'first' });
    expect(step('fetching')).toEqual({
      kind: 'running',
      recheckId: 1,
      step: 'fetching',
      progress: { phase: 1, done: 3, total: 8 },
    });
    expect(step('stopping')).toMatchObject({ step: 'stopping' });
    expect(step('analysing')).toMatchObject({ step: 'settling' });
  });

  it('knows a recheck it didn’t ask for, from the crawl’s list, and one of another problem', () => {
    const running = recheckRecord({ status: 'running', result: null, endedAt: null, settledAt: null });
    expect(recheckCardState({ ...base, rechecks: [running], run: run({}) })).toMatchObject({
      kind: 'running',
      recheckId: 1,
    });
    expect(recheckCardState({ ...base, rechecks: [{ ...running, checkId: 'title-too-long' }], run: run({}) })).toEqual({
      kind: 'busyElsewhere',
    });
    expect(
      recheckCardState({
        ...base,
        pending: pending({ recheckId: 2, ask: { checkId: 'title-too-long' } }),
        run: run({ recheckId: 2 }),
      }),
    ).toEqual({ kind: 'busyElsewhere' });
    // One of this check's causes, asked in Advanced: not the card's.
    expect(
      recheckCardState({
        ...base,
        pending: pending({ ask: { checkId: CHECK.id, group: { detail: null, relatedUrlId: 9 } } }),
        run: run({}),
      }),
    ).toEqual({ kind: 'busyElsewhere' });
  });

  it('gives the latest settled recheck of the check, and the stream’s result before the list has it', () => {
    const settled = recheckRecord();
    expect(recheckCardState({ ...base, rechecks: [settled] })).toMatchObject({ kind: 'result', recheck: { id: 1 } });
    // Of one of its causes, or a list of URLs: not the card's.
    expect(
      recheckCardState({ ...base, rechecks: [{ ...settled, group: { detail: null, relatedUrlId: 9 } }] }),
    ).toMatchObject({ kind: 'ready' });
    const unsettled = { ...settled, result: null, settledAt: null };
    const all = recheckResult({ fixed: 5, still: 0 });
    expect(
      recheckCardState({
        ...base,
        rechecks: [unsettled],
        run: run({ stage: 'settled', status: 'completed', result: all, progress: null }),
      }),
    ).toMatchObject({ kind: 'result', recheck: { result: { fixed: 5 } } });
    // Settled, and the list read before it began: the result waits for the list.
    expect(
      recheckCardState({
        ...base,
        pending: pending(),
        rechecks: [],
        run: run({ stage: 'settled', status: 'completed', result: all, progress: null }),
      }),
    ).toMatchObject({ kind: 'running', step: 'settling' });
  });

  it('says why the one asked here didn’t run, and offers it again', () => {
    expect(
      recheckCardState({
        ...base,
        pending: pending({ ended: true, error: 'A check of this site is running.' }),
        run: run({ stage: 'ended', status: null, progress: null, error: 'A check of this site is running.' }),
      }),
    ).toMatchObject({ kind: 'ready', ended: 'A check of this site is running.', failed: false });
  });

  it('says the one asked here failed, and nothing it read was kept, and offers it again', () => {
    expect(
      recheckCardState({
        ...base,
        pending: pending(),
        run: run({ stage: 'ended', status: 'failed', progress: null, error: 'The recheck failed: the disk is full' }),
      }),
    ).toMatchObject({ kind: 'ready', ended: 'The recheck failed: the disk is full', failed: true });
  });
});
