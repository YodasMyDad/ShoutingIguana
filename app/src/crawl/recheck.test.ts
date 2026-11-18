import { afterEach, describe, expect, it, vi } from 'vitest';
import { setTransport } from '../api/transport';
import type * as NotifyModule from '../feedback/notify';
import { notifyError, notifyResult } from '../feedback/notify';
import { EMPTY_TRAIL, LiveStore, type LiveCrawl } from '../live/liveStore';
import { fakeSidecar, json, problem } from '../test/fakeSidecar';
import { RECHECK_NEEDS_SECRETS, recheckEvent, recheckPlan, recheckResult } from '../test/fixLoopSidecar';
import { setCrawlSettingsOpener } from './actions';
import {
  isRecheckRunning,
  pendingRecheck,
  recheckRunOf,
  resetPendingRechecks,
  startRecheck,
  watchPendingRechecks,
  type PendingRecheck,
} from './recheck';

vi.mock('../feedback/notify', async (importOriginal) => ({
  ...(await importOriginal<typeof NotifyModule>()),
  notifyError: vi.fn(),
  notifyResult: vi.fn(),
}));

const ID = '0123456789abcdef';
const ASK = { checkId: 'broken-internal-links' };

afterEach(() => {
  vi.mocked(notifyError).mockClear();
  vi.mocked(notifyResult).mockClear();
  resetPendingRechecks();
  setCrawlSettingsOpener(null);
});

function live(patch: Partial<LiveCrawl> = {}): LiveCrawl {
  return {
    id: ID,
    status: null,
    progress: null,
    history: [],
    trail: EMPTY_TRAIL,
    clock: null,
    pace: [],
    alerts: [],
    analysis: null,
    recheck: null,
    error: null,
    errorType: null,
    ...patch,
  };
}

const started = (patch: Partial<PendingRecheck> = {}): PendingRecheck => ({
  recheckId: 2,
  ask: ASK,
  ended: false,
  error: null,
  ...patch,
});

describe('startRecheck', () => {
  it('starts one, and keeps its number from the 202 until its own events come', async () => {
    const sidecar = fakeSidecar([['POST', /\/rechecks$/, () => json({ recheckId: 3, ...recheckPlan() }, 202)]]);
    setTransport(sidecar);
    const answer = await startRecheck(ID, ASK);
    expect(answer?.recheckId).toBe(3);
    expect(sidecar.requests[0]?.body).toMatchObject(ASK);
    expect(pendingRecheck(ID)).toMatchObject({ recheckId: 3, ended: false });
  });

  it('asks for header values as Continue does, with Recheck without, which starts it skipping them', async () => {
    const sidecar = fakeSidecar([
      [
        'POST',
        /\/rechecks$/,
        (request) =>
          (request.body as { skipMissingHeaders?: boolean }).skipMissingHeaders
            ? json({ recheckId: 1, ...recheckPlan() }, 202)
            : problem({ ...RECHECK_NEEDS_SECRETS, missingHeaders: [...RECHECK_NEEDS_SECRETS.missingHeaders] }),
      ],
    ]);
    setTransport(sidecar);
    expect(await startRecheck(ID, ASK)).toBeNull();
    const [title, options] = vi.mocked(notifyResult).mock.calls[0] ?? [];
    expect(title).toBe(RECHECK_NEEDS_SECRETS.title);
    expect(options?.description).toBe(
      'Its headers (Authorization) aren’t kept in the crawl file. Enter them again in its settings, or recheck without them.',
    );
    expect(options?.action?.label).toBe('Recheck without');
    options?.action?.onClick();
    await vi.waitFor(() => expect(pendingRecheck(ID)?.recheckId).toBe(1));
    expect(sidecar.requests[1]?.body).toMatchObject({ ...ASK, skipMissingHeaders: true });
  });

  it('sends a proxy password to the crawl’s settings, where the shell can open them', async () => {
    setTransport(
      fakeSidecar([
        [
          'POST',
          /\/rechecks$/,
          () => problem({ ...RECHECK_NEEDS_SECRETS, missingHeaders: [], proxyCredentials: true }),
        ],
      ]),
    );
    const opened: string[] = [];
    setCrawlSettingsOpener((id) => opened.push(id));
    await startRecheck(ID, ASK);
    const options = vi.mocked(notifyResult).mock.calls[0]?.[1];
    expect(options?.description).toBe(
      'Its proxy password isn’t kept in the crawl file. Enter it again in the crawl’s settings to recheck.',
    );
    options?.action?.onClick();
    expect(opened).toEqual([ID]);
  });

  it('says any other refusal in the sidecar’s words, with Retry where trying again can work', async () => {
    setTransport(
      fakeSidecar([
        [
          'POST',
          /\/rechecks$/,
          () =>
            problem({
              type: 'site-busy',
              title: 'A check of this site is running. Its results will show what’s fixed.',
              status: 409,
            }),
        ],
      ]),
    );
    expect(await startRecheck(ID, ASK)).toBeNull();
    expect(vi.mocked(notifyError).mock.calls[0]?.[1]).toMatchObject({ id: `crawl:${ID}:recheck` });
    expect(pendingRecheck(ID)).toBeUndefined();
  });
});

describe('recheckRunOf: where a recheck stands', () => {
  it('is nothing with neither a recheck started nor one heard', () => {
    expect(recheckRunOf(live({ status: 'completed' }), undefined)).toBeNull();
    expect(recheckRunOf(undefined, undefined)).toBeNull();
  });

  it('is starting until the crawl goes live, then its analysis first, then the fetching', () => {
    expect(recheckRunOf(live(), started())?.stage).toBe('starting');
    expect(recheckRunOf(live({ status: 'analyzing' }), started())?.stage).toBe('analysingFirst');
    expect(recheckRunOf(live({ status: 'rechecking' }), started())).toMatchObject({
      stage: 'fetching',
      recheckId: 2,
      progress: null,
    });
  });

  it('follows the run’s own events: its progress, Stop, the analysis after it, and the result', () => {
    const running = recheckEvent({ recheckId: 2, status: 'running', phase: 1, done: 3, total: 8 });
    expect(recheckRunOf(live({ status: 'rechecking', recheck: running }), started())).toMatchObject({
      stage: 'fetching',
      status: 'running',
      progress: { phase: 1, done: 3, total: 8 },
    });
    expect(recheckRunOf(live({ status: 'stopping', recheck: running }), started())?.stage).toBe('stopping');
    const ended = recheckEvent({ recheckId: 2, status: 'stopped', result: null });
    expect(recheckRunOf(live({ status: 'analyzing', recheck: ended }), started())).toMatchObject({
      stage: 'analysing',
      status: 'stopped',
    });
    const settled = recheckEvent({ recheckId: 2, status: 'completed', result: recheckResult() });
    expect(recheckRunOf(live({ status: 'completed', recheck: settled }), started())).toMatchObject({
      stage: 'settled',
      result: recheckResult(),
    });
  });

  it('shows a recheck heard but not started here (another window, or before a reload)', () => {
    const settled = recheckEvent({ recheckId: 4, status: 'completed', result: recheckResult() });
    expect(recheckRunOf(live({ recheck: settled }), undefined)).toMatchObject({ stage: 'settled', recheckId: 4 });
  });

  it('ignores an earlier recheck’s events once a new one is started', () => {
    const earlier = recheckEvent({ recheckId: 1, status: 'completed', result: recheckResult() });
    expect(recheckRunOf(live({ status: 'analyzing', recheck: earlier }), started())).toMatchObject({
      stage: 'analysingFirst',
      recheckId: 2,
      result: null,
    });
  });

  it('ends a recheck the sidecar refused after its analysis first, with its reason, never running for ever', () => {
    expect(
      recheckRunOf(
        live({ status: 'completed' }),
        started({ ended: true, error: 'Recheck works on the site’s latest check.' }),
      ),
    ).toMatchObject({
      stage: 'ended',
      error: 'Recheck works on the site’s latest check.',
    });
  });

  it('isRecheckRunning: under way until settled or ended', () => {
    expect(isRecheckRunning(null)).toBe(false);
    expect(isRecheckRunning(recheckRunOf(live({ status: 'rechecking' }), started()))).toBe(true);
    expect(isRecheckRunning(recheckRunOf(live(), started({ ended: true })))).toBe(false);
  });
});

describe('watchPendingRechecks', () => {
  const crawlEvent = (status: string, error?: string) => ({
    event: 'crawl',
    data: JSON.stringify({ crawlId: ID, status, ...(error ? { error } : {}) }),
    id: '',
  });

  it('marks a recheck ended when the crawl comes to rest before any of its run’s events', async () => {
    const store = new LiveStore();
    const stop = watchPendingRechecks(store);
    setTransport(fakeSidecar([['POST', /\/rechecks$/, () => json({ recheckId: 2, ...recheckPlan() }, 202)]]));
    await startRecheck(ID, ASK);
    store.ingest(crawlEvent('analyzing'));
    expect(pendingRecheck(ID)?.ended).toBe(false);
    store.ingest(crawlEvent('completed', 'Recheck works on the site’s latest check.'));
    expect(pendingRecheck(ID)).toMatchObject({ ended: true, error: 'Recheck works on the site’s latest check.' });
    stop();
  });

  it('marks it ended though an earlier recheck’s events were heard: they aren’t its run’s', async () => {
    const store = new LiveStore();
    const stop = watchPendingRechecks(store);
    store.ingest({
      event: 'recheck',
      data: JSON.stringify(recheckEvent({ crawlId: ID, recheckId: 1, status: 'completed', result: recheckResult() })),
      id: '',
    });
    setTransport(fakeSidecar([['POST', /\/rechecks$/, () => json({ recheckId: 2, ...recheckPlan() }, 202)]]));
    await startRecheck(ID, ASK);
    store.ingest(crawlEvent('analyzing'));
    store.ingest(crawlEvent('completed'));
    expect(pendingRecheck(ID)?.ended).toBe(true);
    stop();
  });

  it('leaves a recheck whose run was heard alone: its events say how it ended', async () => {
    const store = new LiveStore();
    const stop = watchPendingRechecks(store);
    setTransport(fakeSidecar([['POST', /\/rechecks$/, () => json({ recheckId: 2, ...recheckPlan() }, 202)]]));
    await startRecheck(ID, ASK);
    store.ingest({ event: 'recheck', data: JSON.stringify(recheckEvent({ crawlId: ID, recheckId: 2 })), id: '' });
    store.ingest(crawlEvent('completed'));
    expect(pendingRecheck(ID)?.ended).toBe(false);
    stop();
  });
});
