import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { setTransport } from '../api/transport';
import { RECHECK_NEEDS_SECRETS, recheckEvent, recheckPlan, recheckRoutes } from '../test/fixLoopSidecar';
import { fakeSidecar, json, problem } from '../test/fakeSidecar';
import { askedRecheck, recheckUnderway, resetRechecksAskedForTests, startRecheck } from './rechecks';

const ID = '0123456789abcdef';
const ASK = { checkId: 'broken-internal-links' };

beforeEach(() => resetRechecksAskedForTests());
afterEach(() => resetRechecksAskedForTests());

describe('asking for a recheck', () => {
  it('starts it and keeps that it was asked, for the screens to follow before its first event', async () => {
    const sidecar = fakeSidecar(recheckRoutes());
    setTransport(sidecar);
    const started = await startRecheck(ID, ASK);
    expect(started).toMatchObject({ kind: 'started', started: { recheckId: 1, judged: 5 } });
    expect(askedRecheck(ID)).toEqual({ recheckId: 1, checkId: 'broken-internal-links', judged: 5 });
    expect(sidecar.requests.at(-1)).toMatchObject({ method: 'POST', body: { checkId: 'broken-internal-links' } });
  });

  it('asks again without the headers, when told to', async () => {
    const sidecar = fakeSidecar(recheckRoutes());
    setTransport(sidecar);
    await startRecheck(ID, ASK, { skipMissingHeaders: true });
    expect(sidecar.requests.at(-1)?.body).toEqual({ checkId: 'broken-internal-links', skipMissingHeaders: true });
  });

  it('says what the crawl file doesn’t keep, why it can’t run, or that it failed: never throws', async () => {
    setTransport(fakeSidecar(recheckRoutes({ start: () => problem(RECHECK_NEEDS_SECRETS) })));
    expect(await startRecheck(ID, ASK)).toEqual({
      kind: 'needsSecrets',
      title: RECHECK_NEEDS_SECRETS.title,
      missingHeaders: ['Authorization'],
      proxyCredentials: false,
    });
    setTransport(
      fakeSidecar(
        recheckRoutes({
          start: () =>
            problem({
              type: 'site-busy',
              title: 'A check of this site is running. Its results will show what’s fixed.',
              status: 409,
            }),
        }),
      ),
    );
    expect(await startRecheck(ID, ASK)).toEqual({
      kind: 'refused',
      refused: { type: 'site-busy', title: 'A check of this site is running. Its results will show what’s fixed.' },
    });
    setTransport(fakeSidecar(recheckRoutes({ start: () => json({}, 500) })));
    expect((await startRecheck(ID, ASK)).kind).toBe('failed');
    expect(askedRecheck(ID)).toBeUndefined();
  });
});

describe('a recheck underway', () => {
  const asking = { recheckId: 1, checkId: 'broken-internal-links', judged: 5 };

  it('is the rechecking status, and the analyses around it', () => {
    expect(recheckUnderway({ status: 'rechecking', recheck: null }, undefined)).toBe(true);
    // The analysis first, before its first event: asked here.
    expect(recheckUnderway({ status: 'analyzing', recheck: null }, asking)).toBe(true);
    // The analysis after it, until it's settled.
    expect(recheckUnderway({ status: 'analyzing', recheck: recheckEvent({ status: 'completed' }) }, undefined)).toBe(
      true,
    );
    expect(recheckUnderway({ status: 'stopping', recheck: recheckEvent({ status: 'running' }) }, undefined)).toBe(true);
  });

  it('isn’t an analysis of its own, nor a crawl at rest', () => {
    expect(recheckUnderway({ status: 'analyzing', recheck: null }, undefined)).toBe(false);
    expect(
      recheckUnderway(
        { status: 'analyzing', recheck: recheckEvent({ status: 'completed', result: recheckPlanResult() }) },
        undefined,
      ),
    ).toBe(false);
    expect(recheckUnderway({ status: 'completed', recheck: recheckEvent({ status: 'completed' }) }, asking)).toBe(
      false,
    );
    expect(recheckUnderway(undefined, undefined)).toBe(false);
  });
});

function recheckPlanResult() {
  const plan = recheckPlan();
  return { fixed: plan.judged, still: 0, notChecked: 0, notRechecked: 0, new: 0, scoreBefore: 86, scoreAfter: 88 };
}
