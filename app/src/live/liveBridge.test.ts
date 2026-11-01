import { QueryClient } from '@tanstack/react-query';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { setTransport, type Transport } from '../api/transport';
import type { CrawlListItem } from '../api/types';
import { notifyError, notifyResult } from '../feedback/notify';
import { resetConnectionForTests } from '../shell/connection';
import { startLiveBridge } from './liveBridge';
import { LiveStore, type LiveStoreClock } from './liveStore';

vi.mock('../feedback/notify', () => ({ notifyResult: vi.fn(), notifyError: vi.fn() }));
vi.mock('../feedback/announce', () => ({ announce: vi.fn() }));

const ID = '0123456789abcdef';
let item: CrawlListItem;

/** The sidecar's answer for the crawl item, as it stands when the bridge asks. */
const transport: Transport = {
  kind: 'mock',
  send: () => Promise.resolve(Response.json(item)),
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
  const send = (event: string, data: object) => store.ingest({ event, id: '', data: JSON.stringify(data) });
  const status = (value: string, extra: object = {}) => send('crawl', { crawlId: ID, status: value, ...extra });
  return { stop, send, status };
}

const settle = () => vi.advanceTimersByTimeAsync(0);

beforeEach(() => {
  vi.useFakeTimers();
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
  vi.useRealTimers();
  vi.mocked(notifyResult).mockClear();
  vi.mocked(notifyError).mockClear();
  resetConnectionForTests();
});

describe('the live bridge’s toasts', () => {
  it('says "Crawl finished" once, after the analysis that follows the crawl, with its counts', async () => {
    const { stop, status } = setup();
    status('running');
    status('completed');
    status('analyzing'); // the sidecar starts it in the same breath
    await settle();
    expect(notifyResult).not.toHaveBeenCalled();
    item = {
      ...item,
      status: 'completed',
      analyzedAt: '2026-11-01T12:00:00Z',
      issues: { error: 2, warning: 6, notice: 11 },
    };
    status('completed');
    await vi.advanceTimersByTimeAsync(2_000);
    expect(notifyResult).toHaveBeenCalledOnce();
    expect(notifyResult).toHaveBeenCalledWith(
      'Crawl finished',
      expect.objectContaining({ description: 'example.com: 4,521 URLs, 19 checks found issues' }),
    );
    stop();
  });

  it('says "Crawl finished" a moment after a crawl that isn’t analysed', async () => {
    const { stop, status } = setup();
    status('running');
    item = { ...item, status: 'completed' };
    status('completed');
    await settle();
    expect(notifyResult).not.toHaveBeenCalled();
    await vi.advanceTimersByTimeAsync(1_000);
    expect(notifyResult).toHaveBeenCalledWith(
      'Crawl finished',
      expect.objectContaining({ description: 'example.com: 4,521 URLs' }),
    );
    stop();
  });

  it('says "Analysis finished" for an analysis on its own', async () => {
    item = { ...item, status: 'completed', analyzedAt: '2026-10-01T00:00:00Z' };
    const { stop, status } = setup();
    status('analyzing');
    item = { ...item, analyzedAt: '2026-11-01T12:00:00Z', issues: { error: 0, warning: 1, notice: 0 } };
    status('completed');
    await vi.advanceTimersByTimeAsync(2_000);
    expect(notifyResult).toHaveBeenCalledOnce();
    expect(notifyResult).toHaveBeenCalledWith(
      'Analysis finished',
      expect.objectContaining({ description: 'example.com: 1 check found issues' }),
    );
    stop();
  });

  it('stays quiet for an analysis that was cancelled and stored nothing', async () => {
    item = { ...item, status: 'completed', analyzedAt: '2026-10-01T00:00:00Z' };
    const { stop, status } = setup();
    // The screen showing the crawl holds its item, as it was when the analysis began.
    status('completed');
    await settle();
    status('analyzing');
    await settle();
    status('completed');
    await vi.advanceTimersByTimeAsync(2_000);
    expect(notifyResult).not.toHaveBeenCalled();
    stop();
  });

  it('says a stopped crawl can be continued', async () => {
    const { stop, status } = setup();
    status('running');
    status('stopping');
    item = { ...item, status: 'stopped', fetchedUrlCount: 1234 };
    status('stopped');
    await settle();
    expect(notifyResult).toHaveBeenCalledWith(
      'Crawl stopped',
      expect.objectContaining({ description: 'example.com: 1,234 URLs. You can continue it later.' }),
    );
    stop();
  });

  it('reports a failure with the engine’s words, and offers a retry when the file was only busy', async () => {
    const { stop, status } = setup();
    status('analyzing');
    status('completed', { error: 'Someone is reading the file.', errorType: 'crawl-busy' });
    await settle();
    expect(notifyError).toHaveBeenCalledWith(
      expect.objectContaining({ title: 'Someone is reading the file.', retryable: true }),
      expect.objectContaining({ title: 'example.com: the analysis failed', retry: expect.any(Function) as unknown }),
    );
    expect(notifyResult).not.toHaveBeenCalled();
    stop();
  });
});
