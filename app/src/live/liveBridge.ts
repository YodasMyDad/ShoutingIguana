// Joins the event stream to the rest of the app: events go into the live store (for rendering, batched), and their
// side effects happen here, at once: the library and crawl reads refresh, status changes are announced, and
// background results become toasts with their action. The shell sends the system notification when the window
// isn't focused; the toast is the in-app half.
import type { QueryClient } from '@tanstack/react-query';
import { ApiError } from '../api/errors';
import { api } from '../api/client';
import { gridPagesOf, liveGridPagesOf, queryKeys } from '../api/queries';
import { getTransport } from '../api/transport';
import type { CrawlEvent, CrawlListItem, CrawlStatus } from '../api/types';
import { isCrawling, isLive } from '../api/types';
import { analyseCrawl, continueCrawl } from '../crawl/actions';
import { crawlName, statusAnnouncement } from '../crawl/status';
import { announce } from '../feedback/announce';
import { crawlToast, dismissToast, notifyError, notifyResult } from '../feedback/notify';
import { formatCountOf } from '../lib/format';
import { getConnection, subscribeSidecarStatus } from '../shell/connection';
import { liveStore, type LiveState, type LiveStore } from './liveStore';
import { startEventStream } from './sse';

/** How long a completed crawl waits for its analysis to begin before its toast says it finished without one. */
const ANALYSIS_GRACE_MS = 1_000;
/** A crawling crawl's open grids (their counts and rows) refresh at most this often. */
export const LIVE_GRID_REFRESH_MS = 1_000;

export interface LiveBridgeOptions {
  queryClient: QueryClient;
  /** Opens a crawl's view from a toast's action. */
  openCrawl: (id: string, view: 'overview' | 'issues') => void;
  store?: LiveStore;
}

export function startLiveBridge({ queryClient, openCrawl, store = liveStore }: LiveBridgeOptions): () => void {
  /** Crawls seen crawling in this session, so their end reads "Crawl finished" rather than "Analysis finished". */
  const crawledHere = new Set<string>();
  /** Crawls whose crawl completed and whose analysis then began: their toast waits for the analysis. */
  const analysingAfterCrawl = new Set<string>();
  /** A completed crawl, waiting a moment to see whether an analysis follows. */
  const finishing = new Map<string, ReturnType<typeof setTimeout>>();
  /** When each running analysis's crawl was last analysed: an analysis that ends with it unchanged was cancelled. */
  const analysedBefore = new Map<string, string | null>();
  const lastStatus = new Map<string, CrawlStatus>();
  /** When each crawling crawl's grids last refreshed. */
  const gridRefreshedAt = new Map<string, number>();
  /** The sidecar generation the last `hello` came from; undefined before the first. */
  let helloGeneration: number | null | undefined;

  const stream = startEventStream({
    open: (signal) =>
      getTransport().send({ method: 'GET', path: '/api/events', headers: { Accept: 'text/event-stream' }, signal }),
    generation: () => getConnection()?.generation ?? null,
    subscribe: subscribeSidecarStatus,
    onMessage: (message) => store.ingest(message),
    onState: (state) => store.setStreamState(state),
  });

  const offEvents = store.onEvent((event, previous) => {
    switch (event.type) {
      case 'library':
        void queryClient.invalidateQueries({ queryKey: queryKeys.library, exact: true });
        break;
      case 'hello':
        // A crawl being analysed here is still this sidecar's, as one being crawled is.
        onHello([...event.data.live, ...(event.data.analyzing ?? [])], previous);
        break;
      case 'progress':
        if (isCrawling(event.data.status)) {
          crawledHere.add(event.data.crawlId);
          if (event.data.status !== 'paused') refreshLiveGrids(event.data.crawlId);
        }
        break;
      case 'crawl':
        onCrawlEvent(event.data);
        break;
      default:
        break;
    }
  });

  /**
   * A new stream: `hello` says what's live now, whatever the app believed. A crawl that was live and isn't any more
   * (its sidecar crashed and the shell started another, or it ended while the stream was down) has its status and
   * reads fetched again, so the screens show what the sidecar says now: `interrupted`, with Continue. A reconnect to
   * the same sidecar may also have missed events (the sidecar drops a client that falls far behind), so every
   * crawl's reads refresh then. A new sidecar's reads were all refreshed when it came (below).
   */
  function onHello(live: readonly string[], previous: LiveState) {
    void queryClient.invalidateQueries({ queryKey: queryKeys.library, exact: true });
    const generation = getConnection()?.generation ?? null;
    const reconnected = helloGeneration !== undefined && helloGeneration === generation;
    helloGeneration = generation;
    const wasLive = new Set(previous.live);
    for (const crawl of Object.values(previous.crawls)) {
      if (crawl.status !== null && isLive(crawl.status)) wasLive.add(crawl.id);
    }
    for (const crawlId of wasLive) {
      if (live.includes(crawlId)) continue;
      // Its next status is news, whatever the old sidecar last said.
      lastStatus.delete(crawlId);
      gridRefreshedAt.delete(crawlId);
      analysingAfterCrawl.delete(crawlId);
      if (!reconnected) {
        void queryClient.invalidateQueries({ queryKey: queryKeys.crawl(crawlId) }, { cancelRefetch: false });
        void queryClient.invalidateQueries(gridPagesOf(crawlId), { cancelRefetch: false });
      }
    }
    if (reconnected) {
      void queryClient.invalidateQueries({ queryKey: ['crawl'] }, { cancelRefetch: false });
      void queryClient.invalidateQueries({ queryKey: queryKeys.gridPrefix }, { cancelRefetch: false });
    }
  }

  /**
   * While a crawl runs, its grids' counts and rows refresh in place (the IA's "12,345 of 101,107 URLs, live while
   * crawling"): at most once a second, never cancelling a page that's still on its way, so a slow page isn't fetched
   * forever. Only grids on screen fetch; the rest are marked stale for when they're next shown.
   */
  function refreshLiveGrids(crawlId: string) {
    const now = Date.now();
    const last = gridRefreshedAt.get(crawlId);
    if (last !== undefined && now - last < LIVE_GRID_REFRESH_MS) return;
    gridRefreshedAt.set(crawlId, now);
    void queryClient.invalidateQueries(liveGridPagesOf(crawlId), { cancelRefetch: false });
  }

  function onCrawlEvent(event: CrawlEvent) {
    const { crawlId, status } = event;
    const previous = lastStatus.get(crawlId);
    lastStatus.set(crawlId, status);
    if (isCrawling(status)) crawledHere.add(crawlId);

    // The status pill and the library change at once; the rest of the item follows from the sidecar.
    queryClient.setQueryData<CrawlListItem[]>(queryKeys.library, (list) =>
      list?.map((item) => (item.id === crawlId ? { ...item, status } : item)),
    );
    queryClient.setQueryData<CrawlListItem>(queryKeys.crawlItem(crawlId), (item) =>
      item ? { ...item, status } : item,
    );
    if (status === previous) return;
    dismissStaleToasts(crawlId, status);
    if (status === 'completed' || status === 'stopped' || status === 'failed') {
      gridRefreshedAt.delete(crawlId);
      void queryClient.invalidateQueries({ queryKey: queryKeys.crawl(crawlId) });
      void queryClient.invalidateQueries(gridPagesOf(crawlId));
    } else {
      void queryClient.invalidateQueries({ queryKey: queryKeys.crawlItem(crawlId) });
    }
    void announceStatus(crawlId, status);
    decideToast(event, previous);
  }

  /** A crawl's toast whose action no longer fits its status goes: no Continue for a crawl that runs again. */
  function dismissStaleToasts(crawlId: string, status: CrawlStatus) {
    if (status !== 'stopped' && status !== 'interrupted') dismissToast(crawlToast(crawlId, 'stopped'));
    if (status !== 'failed') dismissToast(crawlToast(crawlId, 'failed'));
  }

  /**
   * The crawl's item as it is now, read straight from the sidecar: a cached read could join a fetch already in
   * flight from an earlier event and say what was true before. The cache gets the fresh item too.
   */
  async function itemOf(crawlId: string): Promise<CrawlListItem | undefined> {
    try {
      const item = await api.crawl(crawlId);
      queryClient.setQueryData(queryKeys.crawlItem(crawlId), item);
      return item;
    } catch {
      return queryClient.getQueryData<CrawlListItem>(queryKeys.crawlItem(crawlId));
    }
  }

  async function announceStatus(crawlId: string, status: CrawlStatus) {
    announce(statusAnnouncement(crawlName(await itemOf(crawlId)), status));
  }

  /**
   * Which toast a status change earns, decided as the events arrive. The sidecar reports a crawl completed and starts
   * its analysis in the same breath, so a completed crawl waits a moment: if the analysis begins, the toast waits
   * for it and then says "Crawl finished" with the issue counts.
   */
  function decideToast(event: CrawlEvent, previous: CrawlStatus | undefined) {
    const { crawlId, status } = event;
    if (status === 'analyzing') {
      // What the screens already hold, read now: a fetch could land after a quick analysis ended.
      const known =
        queryClient.getQueryData<CrawlListItem>(queryKeys.crawlItem(crawlId)) ??
        queryClient.getQueryData<CrawlListItem[]>(queryKeys.library)?.find((entry) => entry.id === crawlId);
      if (known) analysedBefore.set(crawlId, known.analyzedAt);
      const waiting = finishing.get(crawlId);
      if (waiting !== undefined) {
        clearTimeout(waiting);
        finishing.delete(crawlId);
        analysingAfterCrawl.add(crawlId);
      }
      return;
    }
    if (status === 'failed' || event.error) {
      crawledHere.delete(crawlId);
      analysingAfterCrawl.delete(crawlId);
      const retry = event.errorType === 'crawl-busy' ? () => void analyseCrawl(crawlId) : undefined;
      void itemOf(crawlId).then((item) =>
        notifyError(new CrawlFailure(event.error ?? 'The engine didn’t say why.', retry !== undefined), {
          id: crawlToast(crawlId, 'failed'),
          title:
            status === 'failed' ? `${crawlName(item)}: the crawl failed` : `${crawlName(item)}: the analysis failed`,
          ...(retry ? { retry } : {}),
        }),
      );
      return;
    }
    if (status === 'completed') {
      if (analysingAfterCrawl.delete(crawlId)) {
        void toastFinished(crawlId, true);
      } else if (crawledHere.delete(crawlId)) {
        finishing.set(
          crawlId,
          setTimeout(() => {
            finishing.delete(crawlId);
            void toastFinished(crawlId, true);
          }, ANALYSIS_GRACE_MS),
        );
      } else if (previous === 'analyzing') {
        void toastFinished(crawlId, false);
      }
      return;
    }
    if (status === 'stopped') {
      const crawled = crawledHere.delete(crawlId);
      analysingAfterCrawl.delete(crawlId);
      if (crawled && previous === 'stopping') void toastStopped(crawlId);
    }
  }

  async function toastFinished(crawlId: string, crawled: boolean) {
    const before = analysedBefore.get(crawlId);
    analysedBefore.delete(crawlId);
    const item = await itemOf(crawlId);
    // An analysis that stored nothing was cancelled: nothing to announce.
    if (!crawled && before !== undefined && item?.analyzedAt === before) return;
    const name = crawlName(item);
    const checks = item?.issues ? item.issues.error + item.issues.warning + item.issues.notice : null;
    const found =
      checks === null
        ? null
        : checks === 0
          ? 'no checks found issues'
          : `${formatCountOf(checks, 'check')} found issues`;
    const urls = crawled && item ? formatCountOf(item.fetchedUrlCount, 'URL') : null;
    notifyResult(crawled ? 'Crawl finished' : 'Analysis finished', {
      id: crawlToast(crawlId, 'finished'),
      description: [name, [urls, found].filter(Boolean).join(', ')].filter(Boolean).join(': '),
      action: { label: 'View issues', onClick: () => openCrawl(crawlId, 'issues') },
    });
  }

  async function toastStopped(crawlId: string) {
    const item = await itemOf(crawlId);
    const name = crawlName(item);
    notifyResult('Crawl stopped', {
      id: crawlToast(crawlId, 'stopped'),
      description: item ? `${name}: ${formatCountOf(item.fetchedUrlCount, 'URL')}. You can continue it later.` : name,
      action: { label: 'Continue', onClick: () => void continueCrawl(crawlId) },
    });
  }

  // A new sidecar: anything could have changed while it was away.
  let generation = getConnection()?.generation ?? null;
  const offStatus = subscribeSidecarStatus(() => {
    const next = getConnection()?.generation ?? null;
    if (next !== null && generation !== null && next !== generation) void queryClient.invalidateQueries();
    if (next !== null) generation = next;
  });

  return () => {
    finishing.forEach((timer) => clearTimeout(timer));
    stream.stop();
    offEvents();
    offStatus();
  };
}

/** The engine's own words for a failure (written for users), shown under the app's plain title. */
class CrawlFailure extends ApiError {
  readonly title: string;
  readonly retryable: boolean;

  constructor(message: string, retryable: boolean) {
    super(message);
    this.title = message;
    this.retryable = retryable;
  }
}
