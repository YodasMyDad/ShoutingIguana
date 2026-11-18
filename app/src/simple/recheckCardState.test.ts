import { describe, expect, it } from 'vitest';
import { recheckEvent, recheckPlan, recheckRecord, recheckResult } from '../test/fixLoopSidecar';
import { recheckCardState } from './recheckCardState';

const CHECK = { id: 'broken-internal-links', recheck: ['urls' as const] };
const base = { check: CHECK, plan: recheckPlan(), planFailed: false, rechecks: [], live: undefined, asked: undefined };

describe('the recheck card’s state', () => {
  it('is the old card for a sidecar from before the fix loop, and Check the site again for a check it can’t settle', () => {
    expect(recheckCardState({ ...base, check: { id: 'x' } })).toEqual({ kind: 'legacy' });
    expect(recheckCardState({ ...base, check: { id: 'orphan-page', recheck: [] } })).toEqual({ kind: 'unsupported' });
  });

  it('waits for the plan, says when it couldn’t be read, and is ready or refused', () => {
    expect(recheckCardState({ ...base, plan: undefined })).toEqual({ kind: 'loading' });
    expect(recheckCardState({ ...base, plan: undefined, planFailed: true })).toEqual({ kind: 'planFailed' });
    expect(recheckCardState(base)).toMatchObject({ kind: 'ready' });
    const refused = { type: 'site-busy', title: 'A check of this site is running.' };
    expect(recheckCardState({ ...base, plan: recheckPlan({ refused }) })).toMatchObject({ kind: 'refused', refused });
  });

  it('follows its recheck: its analysis first, fetching, stopping, then the analysis that settles it', () => {
    const asked = { recheckId: 1, checkId: CHECK.id, judged: 5 };
    expect(recheckCardState({ ...base, asked, live: { status: 'analyzing', recheck: null } })).toMatchObject({
      kind: 'running',
      step: 'first',
    });
    const running = recheckEvent();
    expect(recheckCardState({ ...base, asked, live: { status: 'rechecking', recheck: running } })).toEqual({
      kind: 'running',
      recheckId: 1,
      step: 'fetching',
      progress: running,
    });
    expect(recheckCardState({ ...base, asked, live: { status: 'stopping', recheck: running } })).toMatchObject({
      step: 'stopping',
    });
    expect(
      recheckCardState({
        ...base,
        asked,
        live: { status: 'analyzing', recheck: recheckEvent({ status: 'completed' }) },
      }),
    ).toMatchObject({ step: 'settling' });
  });

  it('knows a recheck it didn’t ask for, from the crawl’s list, and one of another problem', () => {
    const running = recheckRecord({ status: 'running', result: null, endedAt: null, settledAt: null });
    expect(
      recheckCardState({ ...base, rechecks: [running], live: { status: 'rechecking', recheck: recheckEvent() } }),
    ).toMatchObject({ kind: 'running', recheckId: 1 });
    expect(
      recheckCardState({
        ...base,
        rechecks: [{ ...running, checkId: 'title-too-long' }],
        live: { status: 'rechecking', recheck: recheckEvent() },
      }),
    ).toEqual({ kind: 'busyElsewhere' });
    expect(
      recheckCardState({
        ...base,
        asked: { recheckId: 2, checkId: 'title-too-long', judged: 3 },
        live: { status: 'rechecking', recheck: recheckEvent({ recheckId: 2 }) },
      }),
    ).toEqual({ kind: 'busyElsewhere' });
  });

  it('gives the latest settled recheck of the check, and the stream’s result before the list has it', () => {
    const settled = recheckRecord();
    expect(recheckCardState({ ...base, rechecks: [settled] })).toMatchObject({ kind: 'result', recheck: { id: 1 } });
    // Of one of its causes, or a list of URLs: not the card's.
    expect(
      recheckCardState({ ...base, rechecks: [{ ...settled, group: { detail: null, relatedUrlId: 9 } }] }),
    ).toMatchObject({
      kind: 'ready',
    });
    const unsettled = { ...settled, result: null, settledAt: null };
    expect(
      recheckCardState({
        ...base,
        rechecks: [unsettled],
        live: {
          status: 'completed',
          recheck: recheckEvent({ status: 'completed', result: recheckResult({ fixed: 5, still: 0 }) }),
        },
      }),
    ).toMatchObject({ kind: 'result', recheck: { result: { fixed: 5 } } });
  });
});
