import { QueryClient, type QueryKey } from '@tanstack/react-query';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { queryKeys } from '../api/queries';
import { setTransport, type Transport } from '../api/transport';
import type { CrawlListItem } from '../api/types';
import type * as NotifyModule from '../feedback/notify';
import { dismissToast, notifyError, notifyResult } from '../feedback/notify';
import { resetConnectionForTests, setDirectConnection } from '../shell/connection';
import { testUrlPage } from '../test/fakeSidecar';
import { LIVE_GRID_REFRESH_MS, startLiveBridge } from './liveBridge';
import { LiveStore, type LiveStoreClock } from './liveStore';

vi.mock('../feedback/notify', async (importOriginal) => ({
  ...(await importOriginal<typeof NotifyModule>()),
  notifyResult: vi.fn(),
  notifyError: vi.fn(),
  dismissToast: vi.fn(),
}));
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

function setup(queryClient = new QueryClient()) {
  const store = new LiveStore(clock);
  const stop = startLiveBridge({ queryClient, openCrawl: vi.fn(), store });
  const send = (event: string, data: object) => store.ingest({ event, id: '', data: JSON.stringify(data) });
  const status = (value: string, extra: object = {}) => send('crawl', { crawlId: ID, status: value, ...extra });
  return { stop, send, status, store };
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
  vi.mocked(dismissToast).mockClear();
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

  it('says "N URLs" in its toasts as the URL grid lists them: the URLs that weren’t checked aren’t crawled', async () => {
    const { stop, status } = setup();
    status('running');
    status('stopping');
    item = { ...item, status: 'stopped', fetchedUrlCount: 6_914, crawledUrlCount: 6_909 };
    status('stopped');
    await settle();
    expect(notifyResult).toHaveBeenCalledWith(
      'Crawl stopped',
      expect.objectContaining({ description: 'example.com: 6,909 URLs. You can continue it later.' }),
    );
    stop();
  });

  it('gives each crawl’s news one toast, and takes Continue away once the crawl runs again', async () => {
    const { stop, status } = setup();
    status('running');
    status('stopping');
    status('stopped');
    await settle();
    expect(notifyResult).toHaveBeenCalledWith('Crawl stopped', expect.objectContaining({ id: `crawl:${ID}:stopped` }));
    vi.mocked(dismissToast).mockClear();
    status('stopped');
    expect(dismissToast).not.toHaveBeenCalledWith(`crawl:${ID}:stopped`);
    status('running'); // continued, here or from the tray
    expect(dismissToast).toHaveBeenCalledWith(`crawl:${ID}:stopped`);
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

describe('what the live bridge refreshes', () => {
  const OTHER = 'fedcba9876543210';
  const gridKey = (crawl: string, page: number): QueryKey => ['grid', 'urls', crawl, { sort: [] }, 200, page];

  /** A cache holding the library, both crawls' reads and a grid page each, all fresh. */
  function cachedClient() {
    const queryClient = new QueryClient();
    queryClient.setQueryData(queryKeys.library, [item]);
    for (const crawl of [ID, OTHER]) {
      queryClient.setQueryData(queryKeys.crawlItem(crawl), { ...item, id: crawl });
      queryClient.setQueryData(queryKeys.stats(crawl), {});
      queryClient.setQueryData(gridKey(crawl, 0), testUrlPage());
      queryClient.setQueryData(gridKey(crawl, 3), testUrlPage());
    }
    const stale = (key: QueryKey) => queryClient.getQueryState(key)?.isInvalidated === true;
    return { queryClient, stale };
  }

  it('reads a crawl’s history again when it starts or is continued, as it has a new run, and not on a pause', () => {
    const { queryClient, stale } = cachedClient();
    queryClient.setQueryData(queryKeys.history(ID), { crawlId: ID, startedAt: '', liveFrom: null, points: [] });
    const { stop, status } = setup(queryClient);
    status('stopped');
    queryClient.setQueryData(queryKeys.history(ID), { crawlId: ID, startedAt: '', liveFrom: null, points: [] });
    expect(stale(queryKeys.history(ID))).toBe(false);
    status('running');
    expect(stale(queryKeys.history(ID))).toBe(true);
    queryClient.setQueryData(queryKeys.history(ID), { crawlId: ID, startedAt: '', liveFrom: null, points: [] });
    status('paused');
    status('running');
    expect(stale(queryKeys.history(ID))).toBe(false);
    stop();
  });

  it('fetches the library again when the sidecar says it changed', () => {
    const { queryClient, stale } = cachedClient();
    const { stop, send } = setup(queryClient);
    send('library', {});
    expect(stale(queryKeys.library)).toBe(true);
    expect(stale(queryKeys.crawlItem(ID))).toBe(false);
    stop();
  });

  it('fetches everything again when a new sidecar takes over', () => {
    setDirectConnection({ baseUrl: 'http://127.0.0.1:1', token: 't', generation: 1, version: 'x' });
    const { queryClient, stale } = cachedClient();
    const { stop } = setup(queryClient);
    expect(stale(queryKeys.crawlItem(OTHER))).toBe(false);
    setDirectConnection({ baseUrl: 'http://127.0.0.1:2', token: 'u', generation: 2, version: 'x' });
    expect(stale(queryKeys.library)).toBe(true);
    expect(stale(queryKeys.crawlItem(OTHER))).toBe(true);
    expect(stale(gridKey(OTHER, 3))).toBe(true);
    stop();
  });

  it('after a restart, fetches the real status and reads of a crawl that was live and isn’t any more', () => {
    setDirectConnection({ baseUrl: 'http://127.0.0.1:1', token: 't', generation: 1, version: 'x' });
    const { queryClient, stale } = cachedClient();
    const { stop, send, store } = setup(queryClient);
    send('hello', { live: [ID] });
    send('progress', { crawlId: ID, status: 'running', fetched: 10 });
    send('crawl', { crawlId: OTHER, status: 'completed' });
    queryClient.setQueryData(queryKeys.crawlItem(ID), { ...item, status: 'running' });
    queryClient.setQueryData(queryKeys.stats(ID), {});
    queryClient.setQueryData(gridKey(ID, 0), testUrlPage());
    queryClient.setQueryData(queryKeys.crawlItem(OTHER), { ...item, id: OTHER, status: 'completed' });
    // The sidecar crashed; the shell's new one has nothing live.
    setDirectConnection({ baseUrl: 'http://127.0.0.1:2', token: 'u', generation: 2, version: 'x' });
    queryClient.setQueryData(queryKeys.crawlItem(ID), { ...item, status: 'running' });
    queryClient.setQueryData(queryKeys.crawlItem(OTHER), { ...item, id: OTHER, status: 'completed' });
    send('hello', { live: [] });
    expect(store.getState().crawls[ID]?.status ?? null).toBeNull();
    expect(stale(queryKeys.crawlItem(ID))).toBe(true);
    // A crawl that wasn't live has nothing new to say.
    expect(stale(queryKeys.crawlItem(OTHER))).toBe(false);
    stop();
  });

  it('after the same sidecar drops the stream, fetches every crawl’s reads again: events may have been lost', () => {
    setDirectConnection({ baseUrl: 'http://127.0.0.1:1', token: 't', generation: 1, version: 'x' });
    const { queryClient, stale } = cachedClient();
    const { stop, send } = setup(queryClient);
    send('hello', { live: [] });
    expect(stale(queryKeys.crawlItem(OTHER))).toBe(false);
    send('hello', { live: [] });
    expect(stale(queryKeys.crawlItem(OTHER))).toBe(true);
    expect(stale(queryKeys.stats(ID))).toBe(true);
    expect(stale(gridKey(OTHER, 0))).toBe(true);
    stop();
  });

  it('hears a crawl continued after a restart as news: its status, reads and announcement follow', async () => {
    setDirectConnection({ baseUrl: 'http://127.0.0.1:1', token: 't', generation: 1, version: 'x' });
    const { queryClient, stale } = cachedClient();
    const { stop, send, status } = setup(queryClient);
    send('hello', { live: [] });
    status('running');
    await settle();
    send('hello', { live: [] }); // the stream dropped while it ran
    queryClient.setQueryData(queryKeys.crawlItem(ID), { ...item, status: 'interrupted' });
    status('running'); // continued
    expect(stale(queryKeys.crawlItem(ID))).toBe(true);
    expect(queryClient.getQueryData<CrawlListItem>(queryKeys.crawlItem(ID))?.status).toBe('running');
    stop();
  });

  it('refreshes a crawling crawl’s grids and inspected URL at most once a second, and nothing else', async () => {
    const { queryClient, stale } = cachedClient();
    for (const crawl of [ID, OTHER]) queryClient.setQueryData(queryKeys.urlDetail(crawl, 7), {});
    queryClient.setQueryData(queryKeys.urlSource(ID, 7), {});
    const { stop, send } = setup(queryClient);
    const tick = (fetched: number, status = 'running') => send('progress', { crawlId: ID, status, fetched });
    tick(1);
    expect(stale(gridKey(ID, 0))).toBe(true);
    expect(stale(gridKey(ID, 3))).toBe(true);
    expect(stale(gridKey(OTHER, 0))).toBe(false);
    expect(stale(queryKeys.crawlItem(ID))).toBe(false);
    // The URL the inspector shows may have been fetched since; its HTML, once stored, doesn't change.
    expect(stale(queryKeys.urlDetail(ID, 7))).toBe(true);
    expect(stale(queryKeys.urlDetail(OTHER, 7))).toBe(false);
    expect(stale(queryKeys.urlSource(ID, 7))).toBe(false);
    queryClient.setQueryData(gridKey(ID, 0), testUrlPage());
    await vi.advanceTimersByTimeAsync(LIVE_GRID_REFRESH_MS / 2);
    tick(2);
    expect(stale(gridKey(ID, 0))).toBe(false);
    await vi.advanceTimersByTimeAsync(LIVE_GRID_REFRESH_MS / 2);
    tick(3);
    expect(stale(gridKey(ID, 0))).toBe(true);
    // Paused, nothing changes: no refresh.
    queryClient.setQueryData(gridKey(ID, 0), testUrlPage());
    await vi.advanceTimersByTimeAsync(LIVE_GRID_REFRESH_MS * 2);
    tick(3, 'paused');
    expect(stale(gridKey(ID, 0))).toBe(false);
    stop();
  });

  it('reads nothing for a window that is hidden, and refreshes once when it is shown again', async () => {
    const { queryClient, stale } = cachedClient();
    for (const crawl of [ID, OTHER]) queryClient.setQueryData(queryKeys.urlDetail(crawl, 7), {});
    const { stop, send } = setup(queryClient);
    const tick = (fetched: number) => send('progress', { crawlId: ID, status: 'running', fetched });
    const refreshes = vi.spyOn(queryClient, 'invalidateQueries');
    let hidden = true;
    const visibility = vi.spyOn(document, 'hidden', 'get').mockImplementation(() => hidden);
    const show = (shown: boolean) => {
      hidden = !shown;
      document.dispatchEvent(new Event('visibilitychange'));
    };
    try {
      // Hidden from the start: the crawl ticks for three seconds, and nothing is marked to read.
      for (const fetched of [1, 2, 3]) {
        tick(fetched);
        await vi.advanceTimersByTimeAsync(LIVE_GRID_REFRESH_MS);
      }
      expect(refreshes).not.toHaveBeenCalled();
      expect(stale(gridKey(ID, 0))).toBe(false);
      expect(stale(queryKeys.urlDetail(ID, 7))).toBe(false);

      // Shown again: the crawl's grids and inspected URL refresh at once, without waiting for its next tick.
      show(true);
      expect(stale(gridKey(ID, 0))).toBe(true);
      expect(stale(gridKey(ID, 3))).toBe(true);
      expect(stale(queryKeys.urlDetail(ID, 7))).toBe(true);
      // Once, and only the crawl that ticked.
      expect(refreshes).toHaveBeenCalledTimes(2);
      expect(stale(gridKey(OTHER, 0))).toBe(false);
      expect(stale(queryKeys.urlDetail(OTHER, 7))).toBe(false);

      // Hidden and shown again with no tick between: nothing was missed, so nothing is read.
      queryClient.setQueryData(gridKey(ID, 0), testUrlPage());
      show(false);
      show(true);
      expect(stale(gridKey(ID, 0))).toBe(false);
      expect(refreshes).toHaveBeenCalledTimes(2);

      // Shown, the ticks refresh as they did, at most once a second from the refresh on showing.
      tick(4);
      expect(stale(gridKey(ID, 0))).toBe(false);
      await vi.advanceTimersByTimeAsync(LIVE_GRID_REFRESH_MS);
      tick(5);
      expect(stale(gridKey(ID, 0))).toBe(true);

      // A crawl that ends while the window is hidden is read in full then: showing the window adds no second read.
      queryClient.setQueryData(gridKey(ID, 0), testUrlPage());
      show(false);
      await vi.advanceTimersByTimeAsync(LIVE_GRID_REFRESH_MS);
      tick(6);
      expect(stale(gridKey(ID, 0))).toBe(false);
      send('crawl', { crawlId: ID, status: 'stopped' });
      expect(stale(gridKey(ID, 0))).toBe(true);
      queryClient.setQueryData(gridKey(ID, 0), testUrlPage());
      show(true);
      expect(stale(gridKey(ID, 0))).toBe(false);

      // Stopped, the bridge no longer listens for the window.
      show(false);
      tick(7);
      stop();
      show(true);
      expect(stale(gridKey(ID, 0))).toBe(false);
    } finally {
      visibility.mockRestore();
      stop();
    }
  });
});
