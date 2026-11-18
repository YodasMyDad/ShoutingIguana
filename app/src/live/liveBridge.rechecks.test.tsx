// The fix loop in the live bridge (docs/v2-fix-loop.md, section 4): a recheck's end is its own toast ("Recheck done: 4
// of 5 fixed", with See it), never "Analysis finished" or "is checked"; a check's end says what changed since the last
// check; and every comparison is read again when any crawl's analysis ends, as it may be another's previous crawl.
import { QueryClient } from '@tanstack/react-query';
import { act, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { queryKeys } from '../api/queries';
import { setTransport, type Transport } from '../api/transport';
import type { CrawlListItem } from '../api/types';
import { isQuietAnalysis, noteQuietAnalysis } from '../crawl/quietAnalyses';
import { forgetRecheckAsked, noteRecheckAsked, resetRechecksAskedForTests } from '../crawl/rechecks';
import { AppToaster } from '../feedback/AppToaster';
import { dismissToast } from '../feedback/notify';
import { replaceSettings } from '../settings/settingsStore';
import { recheckRecord, recheckResult } from '../test/fixLoopSidecar';
import { startLiveBridge } from './liveBridge';
import { LiveStore, type LiveStoreClock } from './liveStore';

vi.mock('../feedback/announce', () => ({ announce: vi.fn() }));

const ID = '0123456789abcdef';
let item: CrawlListItem;

const frame = () => new Promise<void>((resolve) => requestAnimationFrame(() => resolve()));

const CHECKS = {
  checks: [
    {
      id: 'broken-internal-links',
      severity: 'error',
      title: 'Broken internal links',
      plainTitle: 'Links that lead to broken pages',
    },
    { id: 'title-duplicate', severity: 'warning', title: 'Duplicate titles', plainTitle: 'Pages that share a title' },
    {
      id: 'image-missing-alt',
      severity: 'notice',
      title: 'Images missing alt text',
      plainTitle: 'Images without a description',
    },
  ],
  categories: [],
};

const transport: Transport = {
  kind: 'mock',
  send: async (request) => {
    await frame();
    if (request.path === '/api/checks') return Response.json(CHECKS);
    if (request.path.endsWith('/rechecks/1')) return Response.json(recheckRecord());
    return Response.json(item);
  },
};

const clock: LiveStoreClock = {
  now: () => Date.now(),
  setTimeout: (callback, ms) => setTimeout(callback, ms),
  clearTimeout: (handle) => clearTimeout(handle as number),
  isHidden: () => false,
};

function setup(viewing?: string, queryClient = new QueryClient()) {
  const store = new LiveStore(clock);
  const openCrawl = vi.fn();
  const stop = startLiveBridge({
    queryClient,
    openCrawl,
    store,
    ...(viewing === undefined ? {} : { viewing: () => viewing }),
  });
  const status = (value: string, extra: object = {}) =>
    store.ingest({ event: 'crawl', id: '', data: JSON.stringify({ crawlId: ID, status: value, ...extra }) });
  const recheck = (data: object) =>
    store.ingest({
      event: 'recheck',
      id: '',
      data: JSON.stringify({ crawlId: ID, recheckId: 1, phase: 1, done: 0, total: 8, result: null, ...data }),
    });
  /** A recheck of the broken-links check: rechecking, its run, the analysis that settles it, then its status again. */
  const recheckToTheEnd = async () => {
    status('rechecking');
    recheck({ status: 'running', done: 3 });
    recheck({ status: 'completed', done: 8 });
    status('analyzing');
    recheck({ status: 'completed', done: 8, result: recheckResult() });
    item = { ...item, analyzedAt: '2026-11-17T14:02:41Z' };
    status('completed');
    await act(frame);
  };
  return { stop, openCrawl, status, recheck, recheckToTheEnd, store, queryClient };
}

beforeEach(() => {
  setTransport(transport);
  resetRechecksAskedForTests();
  item = {
    id: ID,
    path: 'C:\\crawls\\gardenfurnituresets.co.uk.sicrawl',
    fileName: 'gardenfurnituresets.co.uk.sicrawl',
    site: 'gardenfurnituresets.co.uk',
    siteKey: 'gardenfurnituresets.co.uk',
    startUrl: 'https://www.gardenfurnituresets.co.uk/',
    status: 'completed',
    startedAt: '2026-11-17T09:00:00Z',
    finishedAt: '2026-11-17T09:40:00Z',
    updatedAt: null,
    fetchedUrlCount: 4521,
    crawledUrlCount: 4521,
    pageCount: 265,
    issues: { error: 2, warning: 4, notice: 13 },
    analyzedAt: '2026-11-17T09:41:00Z',
    score: { value: 85, band: 'good', partial: false },
    outOfDate: false,
    sizeBytes: 0,
    missing: false,
    error: null,
  };
});

afterEach(() => {
  replaceSettings({ mode: 'advanced', hiddenChecks: [], hiddenChecksByCrawl: {} });
  for (const kind of ['finished', 'stopped', 'recheck', 'failed']) dismissToast(`crawl:${ID}:${kind}`);
});

describe('a recheck’s end, in its own toast', () => {
  it('says "Recheck done: 4 of 5 fixed" in Simple, with See it, and never that the results were updated', async () => {
    replaceSettings({ mode: 'simple' });
    render(<AppToaster />);
    const { stop, recheckToTheEnd, openCrawl } = setup('/');
    await recheckToTheEnd();
    expect(await screen.findByText('Recheck done: 4 of 5 fixed')).toBeInTheDocument();
    expect(screen.getByText('gardenfurnituresets.co.uk: Links that lead to broken pages')).toBeInTheDocument();
    expect(screen.queryByText('The results for gardenfurnituresets.co.uk are updated')).toBeNull();
    fireEvent.click(screen.getByRole('button', { name: 'See it' }));
    expect(openCrawl).toHaveBeenCalledWith(ID, 'issues', 'broken-internal-links');
    stop();
  });

  it('says it in Advanced too, never "Analysis finished"', async () => {
    render(<AppToaster />);
    const { stop, recheckToTheEnd } = setup('/');
    await recheckToTheEnd();
    expect(await screen.findByText('Recheck done: 4 of 5 fixed')).toBeInTheDocument();
    expect(screen.getByText('gardenfurnituresets.co.uk: Broken internal links')).toBeInTheDocument();
    expect(screen.queryByText('Analysis finished')).toBeNull();
    stop();
  });

  it('isn’t toasted on the problem’s own page, where the card says the result', async () => {
    replaceSettings({ mode: 'simple' });
    render(<AppToaster />);
    const { stop, recheckToTheEnd } = setup(`/crawls/${ID}/issues/broken-internal-links`);
    await recheckToTheEnd();
    await act(frame);
    await act(frame);
    expect(screen.queryByText('Recheck done: 4 of 5 fixed')).toBeNull();
    expect(screen.queryByText(/are updated|Analysis finished/)).toBeNull();
    stop();
  });

  it('says a stopped recheck as stopped, never as a crawl stopped or finished', async () => {
    replaceSettings({ mode: 'simple' });
    render(<AppToaster />);
    const { stop, status, recheck } = setup('/');
    status('rechecking');
    recheck({ status: 'running', done: 3 });
    status('stopping');
    recheck({ status: 'stopped', done: 3 });
    status('analyzing');
    recheck({ status: 'stopped', done: 3, result: recheckResult({ fixed: 2, still: 1, notRechecked: 2 }) });
    status('completed');
    expect(await screen.findByText('Recheck stopped: 2 of 5 fixed')).toBeInTheDocument();
    expect(screen.queryByText(/was stopped|is checked/)).toBeNull();
    stop();
  });

  it('says why a recheck asked here couldn’t run, when its analysis first ends in a refusal', async () => {
    replaceSettings({ mode: 'simple' });
    render(<AppToaster />);
    const { stop, status } = setup('/');
    noteRecheckAsked(ID, { recheckId: 1, checkId: 'broken-internal-links', judged: 5 });
    status('analyzing');
    status('completed', { error: 'A check of this site is running. Its results will show what’s fixed.' });
    expect(await screen.findByText('The recheck of gardenfurnituresets.co.uk couldn’t run')).toBeInTheDocument();
    expect(screen.queryByText(/the analysis failed|couldn’t be worked out/)).toBeNull();
    forgetRecheckAsked(ID);
    stop();
  });
});

describe('a check’s end, with what changed since the last one', () => {
  it('says the score’s change and the problems fixed and new, in Simple', async () => {
    replaceSettings({ mode: 'simple' });
    render(<AppToaster />);
    const { stop, status } = setup('/');
    status('running');
    status('completed');
    status('analyzing');
    item = {
      ...item,
      previous: {
        id: 'a1b2c3d4e5f60718',
        startedAt: '2026-11-03T09:12:00Z',
        score: { value: 71, band: 'good', partial: false },
        comparable: 'full',
        needsAnalysis: false,
      },
      changes: {
        score: 14,
        partial: false,
        fixed: ['broken-internal-links', 'image-missing-alt'],
        new: ['title-duplicate'],
        fewer: [],
        more: [],
        newCheck: [],
        notCheckedNow: [],
      },
    };
    status('completed');
    expect(await screen.findByText('gardenfurnituresets.co.uk is checked')).toBeInTheDocument();
    // A thing worth a look fixed isn't a problem fixed, as Simple counts them.
    expect(screen.getByText('Score 85 (▲14 since 3 Nov): 1 problem fixed, 1 new')).toBeInTheDocument();
    stop();
  });
});

describe('the last check worked out again by itself (like for like)', () => {
  it('is no news: no toast when its analysis ends', async () => {
    replaceSettings({ mode: 'simple' });
    render(<AppToaster />);
    const { stop, status } = setup('/');
    noteQuietAnalysis(ID);
    status('analyzing');
    item = { ...item, analyzedAt: '2026-11-18T09:00:00Z' };
    status('completed');
    await act(frame);
    await act(frame);
    expect(screen.queryByText(/are updated|is checked/)).toBeNull();
    expect(isQuietAnalysis(ID)).toBe(false);
    stop();
  });
});

describe('the comparisons, when a crawl’s analysis ends', () => {
  it('reads every crawl’s comparison again: the crawl analysed may be another’s previous crawl', async () => {
    const queryClient = new QueryClient();
    const other = 'fedcba9876543210';
    queryClient.setQueryData(queryKeys.changes(other), { crawlId: other, with: null });
    queryClient.setQueryData(queryKeys.fixed(other, 'broken-internal-links', {}), { rows: [] });
    queryClient.setQueryData(queryKeys.analysis(other), { counts: [] });
    const { stop, status } = setup('/', queryClient);
    status('analyzing');
    status('completed');
    await act(frame);
    expect(queryClient.getQueryState(queryKeys.changes(other))?.isInvalidated).toBe(true);
    expect(queryClient.getQueryState(queryKeys.fixed(other, 'broken-internal-links', {}))?.isInvalidated).toBe(true);
    // The other crawl's own reads stand.
    expect(queryClient.getQueryState(queryKeys.analysis(other))?.isInvalidated).toBe(false);
    stop();
  });
});
