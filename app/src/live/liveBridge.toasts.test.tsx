// The live bridge's toasts through the real toaster (liveBridge.test.ts hears only what the bridge asks of it): a toast
// the bridge shows must stay shown. Sonner applies a dismissal late, in two animation frames (one before its store
// tells the toaster, one before the toaster marks the toast), and to whatever toast has the id by then. So a dismissal
// sent just before a toast with the same id is made takes that toast away as it appears.
import { QueryClient } from '@tanstack/react-query';
import { act, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { setTransport, type Transport } from '../api/transport';
import type { CrawlListItem } from '../api/types';
import { AppToaster } from '../feedback/AppToaster';
import { dismissToast } from '../feedback/notify';
import { startLiveBridge } from './liveBridge';
import { LiveStore, type LiveStoreClock } from './liveStore';

vi.mock('../feedback/announce', () => ({ announce: vi.fn() }));

const ID = '0123456789abcdef';
let item: CrawlListItem;

const frame = () => new Promise<void>((resolve) => requestAnimationFrame(() => resolve()));
const wait = (ms: number) => new Promise<void>((resolve) => setTimeout(resolve, ms));

/** The sidecar's answer for the crawl item, a frame after the bridge asks: a request takes that long, and longer. */
const transport: Transport = {
  kind: 'mock',
  send: async () => {
    await frame();
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
  const stop = startLiveBridge({ queryClient: new QueryClient(), openCrawl: vi.fn(), store });
  const status = (value: string, extra: object = {}) =>
    store.ingest({ event: 'crawl', id: '', data: JSON.stringify({ crawlId: ID, status: value, ...extra }) });
  return { stop, status };
}

/** The toast with this title, if it's shown and isn't on its way out. */
const shown = (title: string | RegExp) => {
  const toast = screen.queryByText(title)?.closest('[data-sonner-toast]');
  return toast && toast.getAttribute('data-removed') !== 'true' ? toast : null;
};

/** Long enough for a dismissal to be applied (two frames) and for a toast it removed to have left (200 ms). */
const SETTLED_MS = 400;

beforeEach(() => {
  setTransport(transport);
  item = {
    id: ID,
    path: 'C:\\crawls\\example.com.sicrawl',
    fileName: 'example.com.sicrawl',
    site: 'example.com',
    startUrl: 'https://example.com/',
    status: 'running',
    startedAt: null,
    finishedAt: null,
    updatedAt: null,
    fetchedUrlCount: 4521,
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
  dismissToast(`crawl:${ID}:stopped`);
  dismissToast(`crawl:${ID}:failed`);
});

describe('the live bridge’s toasts, in the toaster', () => {
  it('keeps “Crawl stopped”, with Continue, on screen once the crawl has stopped', async () => {
    render(<AppToaster />);
    const { stop, status } = setup();
    status('running');
    // It runs for a while (its going live dismisses any toast a stop left, which has long been applied by then).
    await act(() => wait(SETTLED_MS));
    // F8: the crawl says it's stopping, then, a few ms later, that it stopped.
    status('stopping');
    item = { ...item, status: 'stopped', fetchedUrlCount: 1234 };
    status('stopped');
    expect(await screen.findByText('Crawl stopped')).toBeInTheDocument();
    await act(() => wait(SETTLED_MS));
    expect(shown('Crawl stopped')).not.toBeNull();
    expect(screen.getByRole('button', { name: 'Continue' })).toBeInTheDocument();
    stop();
  });

  it('keeps “the analysis failed” on screen when an analysis ends in an error', async () => {
    render(<AppToaster />);
    const { stop, status } = setup();
    status('completed');
    status('analyzing');
    await act(() => wait(SETTLED_MS));
    status('completed', { error: 'The crawl file is in use by another program.', errorType: 'crawl-busy' });
    expect(await screen.findByText(/the analysis failed$/)).toBeInTheDocument();
    await act(() => wait(SETTLED_MS));
    expect(shown(/the analysis failed$/)).not.toBeNull();
    stop();
  });

  it('still takes “Crawl stopped” away once the crawl runs again', async () => {
    render(<AppToaster />);
    const { stop, status } = setup();
    status('running');
    await act(() => wait(SETTLED_MS));
    status('stopping');
    item = { ...item, status: 'stopped' };
    status('stopped');
    expect(await screen.findByText('Crawl stopped')).toBeInTheDocument();
    await act(() => wait(SETTLED_MS));
    expect(shown('Crawl stopped')).not.toBeNull();
    status('running');
    await act(() => wait(SETTLED_MS));
    expect(shown('Crawl stopped')).toBeNull();
    stop();
  });
});
