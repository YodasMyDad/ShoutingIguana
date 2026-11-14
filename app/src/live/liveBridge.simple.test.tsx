// The finished toast in each mode (docs/v2-ui-simple.md, "The report at the end" 2), through the real toaster: in
// Simple "{site} is checked", "Score 85: 6 problems to fix", with See the summary and Open the report; in Advanced its
// own words, gaining Open the report.
import { QueryClient } from '@tanstack/react-query';
import { act, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { setTransport, type Transport } from '../api/transport';
import type { CrawlListItem } from '../api/types';
import { announce } from '../feedback/announce';
import { AppToaster } from '../feedback/AppToaster';
import { dismissToast } from '../feedback/notify';
import { replaceSettings } from '../settings/settingsStore';
import { startLiveBridge } from './liveBridge';
import { LiveStore, type LiveStoreClock } from './liveStore';

vi.mock('../feedback/announce', () => ({ announce: vi.fn() }));

const ID = '0123456789abcdef';
let item: CrawlListItem;
const asked: string[] = [];

const frame = () => new Promise<void>((resolve) => requestAnimationFrame(() => resolve()));

/** The checks and the analysis, for a user who has hidden a check: one Error and one Warning found something. */
const CHECKS = {
  checks: [
    { id: 'broken-internal-links', severity: 'error' },
    { id: 'title-duplicate', severity: 'warning' },
    { id: 'slow-response', severity: 'warning' },
    { id: 'image-missing-alt', severity: 'notice' },
  ],
  categories: [],
};
const ANALYSIS = {
  analyzedAt: '2026-10-31T14:40:00Z',
  counts: [
    { checkId: 'broken-internal-links', count: 5 },
    { checkId: 'title-duplicate', count: 6 },
    { checkId: 'slow-response', count: 3 },
    { checkId: 'image-missing-alt', count: 40 },
  ],
  skipped: [],
};

const transport: Transport = {
  kind: 'mock',
  send: async (request) => {
    await frame();
    asked.push(request.path);
    if (request.path === '/api/checks') return Response.json(CHECKS);
    if (request.path.endsWith('/analysis')) return Response.json(ANALYSIS);
    return Response.json(item);
  },
};

const clock: LiveStoreClock = {
  now: () => Date.now(),
  setTimeout: (callback, ms) => setTimeout(callback, ms),
  clearTimeout: (handle) => clearTimeout(handle as number),
  isHidden: () => false,
};

function setup() {
  const store = new LiveStore(clock);
  const openCrawl = vi.fn();
  const stop = startLiveBridge({ queryClient: new QueryClient(), openCrawl, store });
  const status = (value: string) =>
    store.ingest({ event: 'crawl', id: '', data: JSON.stringify({ crawlId: ID, status: value }) });
  /** A crawl that runs, completes and goes on to its analysis, which ends with the item analysed and scored. */
  const finish = async () => {
    status('running');
    status('completed');
    status('analyzing');
    item = {
      ...item,
      status: 'completed',
      analyzedAt: '2026-10-31T14:40:00Z',
      issues: { error: 2, warning: 4, notice: 13 },
      score: { value: 85, band: 'good', partial: false },
    };
    status('completed');
    await act(frame);
  };
  return { stop, finish, openCrawl, status, store };
}

beforeEach(() => {
  asked.length = 0;
  setTransport(transport);
  item = {
    id: ID,
    path: 'C:\\crawls\\gardenfurnituresets.co.uk.sicrawl',
    fileName: 'gardenfurnituresets.co.uk.sicrawl',
    site: 'gardenfurnituresets.co.uk',
    startUrl: 'https://www.gardenfurnituresets.co.uk/',
    status: 'running',
    startedAt: null,
    finishedAt: null,
    updatedAt: null,
    fetchedUrlCount: 4521,
    crawledUrlCount: 4521,
    pageCount: 265,
    issues: null,
    analyzedAt: null,
    outOfDate: false,
    sizeBytes: 0,
    missing: false,
    error: null,
  };
});

afterEach(() => {
  replaceSettings({ mode: 'advanced', hiddenChecks: [], hiddenChecksByCrawl: {} });
  dismissToast(`crawl:${ID}:finished`);
  dismissToast(`crawl:${ID}:stopped`);
});

describe('the finished toast in Simple', () => {
  beforeEach(() => replaceSettings({ mode: 'simple', hiddenChecks: [], hiddenChecksByCrawl: {} }));

  it('says the site is checked, its score and the problems to fix, with See the summary and Open the report', async () => {
    render(<AppToaster />);
    const { stop, finish, openCrawl } = setup();
    await finish();
    expect(await screen.findByText('gardenfurnituresets.co.uk is checked')).toBeInTheDocument();
    expect(screen.getByText('Score 85: 6 problems to fix')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'See the summary' }));
    expect(openCrawl).toHaveBeenCalledWith(ID, 'overview');
    expect(screen.queryAllByRole('button', { name: 'Open the report' })).toHaveLength(1);
    stop();
  });

  it('counts the problems without the checks the user has hidden, as What to fix does', async () => {
    replaceSettings({ mode: 'simple', hiddenChecks: ['slow-response'] });
    render(<AppToaster />);
    const { stop, finish } = setup();
    await finish();
    // 2 Errors and 4 Warnings by the library; 1 and 1 here, one Warning hidden: 2 problems.
    expect(await screen.findByText('Score 85: 2 problems to fix')).toBeInTheDocument();
    expect(asked).toContain('/api/checks');
    stop();
  });

  it('says "so far" for a partial score', async () => {
    render(<AppToaster />);
    const { stop, status } = setup();
    status('running');
    status('completed');
    status('analyzing');
    item = {
      ...item,
      status: 'completed',
      analyzedAt: '2026-10-31T14:40:00Z',
      issues: { error: 0, warning: 1, notice: 2 },
      score: { value: 96, band: 'veryGood', partial: true },
    };
    status('completed');
    expect(await screen.findByText('Score 96 so far: 1 problem to fix')).toBeInTheDocument();
    stop();
  });

  it('says a stopped check in Simple’s words, with Continue', async () => {
    render(<AppToaster />);
    const { stop, status } = setup();
    status('running');
    status('stopping');
    item = { ...item, status: 'stopped', crawledUrlCount: 1234 };
    status('stopped');
    expect(await screen.findByText('The check of gardenfurnituresets.co.uk was stopped')).toBeInTheDocument();
    expect(screen.getByText('1,234 addresses checked. You can continue it later.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Continue' })).toBeInTheDocument();
    stop();
  });

  // Round 5, gap 2: the sidecar works out a stopped check's results; its toast then says them, so far.
  it('says a stopped check’s results so far once they are worked out, with See the summary and Continue', async () => {
    render(<AppToaster />);
    const { stop, status } = setup();
    status('running');
    status('stopping');
    item = { ...item, status: 'stopped', crawledUrlCount: 1234 };
    status('stopped');
    expect(await screen.findByText('1,234 addresses checked. You can continue it later.')).toBeInTheDocument();
    status('analyzing');
    item = {
      ...item,
      analyzedAt: '2026-10-31T14:40:00Z',
      issues: { error: 1, warning: 1, notice: 2 },
      score: { value: 87, band: 'good', partial: true },
    };
    status('stopped');
    expect(
      await screen.findByText('Score 87 so far: 2 problems to fix. You can continue it later.'),
    ).toBeInTheDocument();
    expect(screen.getAllByText('The check of gardenfurnituresets.co.uk was stopped')).toHaveLength(1);
    expect(screen.getByRole('button', { name: 'See the summary' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Continue' })).toBeInTheDocument();
    stop();
  });
});

describe('a check that ends without its results, or fails, in Simple', () => {
  beforeEach(() => replaceSettings({ mode: 'simple', hiddenChecks: [], hiddenChecksByCrawl: {} }));

  it('a first analysis cancelled: checked, its results not worked out, with Work out the results', async () => {
    render(<AppToaster />);
    const { stop, status, openCrawl } = setup();
    status('running');
    status('completed');
    status('analyzing');
    // Cancelled: the analysis stored nothing, so the check is completed and was never analysed.
    item = { ...item, status: 'completed' };
    status('completed');
    expect(await screen.findByText('gardenfurnituresets.co.uk is checked')).toBeInTheDocument();
    expect(
      screen.getByText('Its results weren’t worked out. Work out the results to see its score and what to fix.'),
    ).toBeInTheDocument();
    expect(screen.queryByText('The results are ready.')).toBeNull();
    fireEvent.click(screen.getByRole('button', { name: 'See the summary' }));
    expect(openCrawl).toHaveBeenCalledWith(ID, 'overview');
    asked.length = 0;
    fireEvent.click(screen.getByRole('button', { name: 'Work out the results' }));
    await act(frame);
    expect(asked).toContain(`/api/crawls/${ID}/analyze`);
    stop();
  });

  it('a failed check: it couldn’t finish, in the words of the shell’s notice', async () => {
    render(<AppToaster />);
    const { stop, status, store } = setup();
    status('running');
    store.ingest({
      event: 'crawl',
      id: '',
      data: JSON.stringify({ crawlId: ID, status: 'failed', error: 'Connection refused' }),
    });
    expect(await screen.findByText('The check of gardenfurnituresets.co.uk couldn’t finish')).toBeInTheDocument();
    expect(screen.queryByText(/the crawl failed/)).toBeNull();
    stop();
  });

  it('results that failed to be worked out: said so, in Simple’s words', async () => {
    render(<AppToaster />);
    const { stop, status, store } = setup();
    status('running');
    status('completed');
    status('analyzing');
    item = { ...item, status: 'completed' };
    store.ingest({
      event: 'crawl',
      id: '',
      data: JSON.stringify({ crawlId: ID, status: 'completed', error: 'The disk is full.' }),
    });
    expect(
      await screen.findByText('The results for gardenfurnituresets.co.uk couldn’t be worked out'),
    ).toBeInTheDocument();
    expect(screen.queryByText(/the analysis failed/)).toBeNull();
    stop();
  });

  it('announces a check’s status in Simple’s words', async () => {
    vi.mocked(announce).mockClear();
    const { stop, status } = setup();
    status('running');
    status('analyzing');
    status('stopped');
    await vi.waitFor(() => expect(vi.mocked(announce)).toHaveBeenCalledTimes(3));
    expect(vi.mocked(announce).mock.calls.map((call) => call[0])).toEqual([
      'gardenfurnituresets.co.uk: checking',
      'gardenfurnituresets.co.uk: working out the results',
      'gardenfurnituresets.co.uk: stopped early',
    ]);
    stop();
  });
});

describe('the finished toast in Advanced', () => {
  it('keeps its words and its View issues, and gains Open the report', async () => {
    render(<AppToaster />);
    const { stop, finish, openCrawl } = setup();
    await finish();
    expect(await screen.findByText('Crawl finished')).toBeInTheDocument();
    expect(screen.getByText('gardenfurnituresets.co.uk: 4,521 URLs, 19 checks found issues')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'View issues' }));
    expect(openCrawl).toHaveBeenCalledWith(ID, 'issues');
    expect(screen.queryAllByRole('button', { name: 'Open the report' })).toHaveLength(1);
    stop();
  });

  it('leaves the checks hidden in this crawl and in every crawl out of its count, as Issues does (round 5)', async () => {
    replaceSettings({ hiddenChecks: ['slow-response'], hiddenChecksByCrawl: { [ID]: ['image-missing-alt'] } });
    render(<AppToaster />);
    const { stop, finish } = setup();
    await finish();
    // 19 by the library; one Error and one Warning left once a Warning and a Notice are hidden.
    expect(await screen.findByText('gardenfurnituresets.co.uk: 4,521 URLs, 2 checks found issues')).toBeInTheDocument();
    stop();
  });
});
