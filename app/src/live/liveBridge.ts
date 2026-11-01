// Joins the event stream to the rest of the app: events go into the live store (for rendering, batched), and their
// side effects happen here, at once: the library and crawl reads refresh, status changes are announced, and
// background results become toasts with their action. The shell sends the system notification when the window
// isn't focused; the toast is the in-app half.
import type { QueryClient } from '@tanstack/react-query';
import { ApiError } from '../api/errors';
import { api } from '../api/client';
import { queryKeys } from '../api/queries';
import { getTransport } from '../api/transport';
import type { CrawlEvent, CrawlListItem, CrawlStatus } from '../api/types';
import { isCrawling } from '../api/types';
import { analyseCrawl, continueCrawl } from '../crawl/actions';
import { crawlName, statusAnnouncement } from '../crawl/status';
import { announce } from '../feedback/announce';
import { notifyError, notifyResult } from '../feedback/notify';
import { formatCount, formatCountOf } from '../lib/format';
import { getConnection, subscribeSidecarStatus } from '../shell/connection';
import { liveStore, type LiveStore } from './liveStore';
import { startEventStream } from './sse';

/** How long a completed crawl waits for its analysis to begin before its toast says it finished without one. */
const ANALYSIS_GRACE_MS = 1_000;

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

  const stream = startEventStream({
    open: (signal) =>
      getTransport().send({ method: 'GET', path: '/api/events', headers: { Accept: 'text/event-stream' }, signal }),
    generation: () => getConnection()?.generation ?? null,
    subscribe: subscribeSidecarStatus,
    onMessage: (message) => store.ingest(message),
    onState: (state) => store.setStreamState(state),
  });

  const offEvents = store.onEvent((event) => {
    switch (event.type) {
      case 'library':
        void queryClient.invalidateQueries({ queryKey: queryKeys.library, exact: true });
        break;
      case 'hello':
        // A new stream: whatever happened while it was down, the library says now.
        void queryClient.invalidateQueries({ queryKey: queryKeys.library, exact: true });
        break;
      case 'progress':
        if (isCrawling(event.data.status)) crawledHere.add(event.data.crawlId);
        break;
      case 'crawl':
        onCrawlEvent(event.data);
        break;
      default:
        break;
    }
  });

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
    if (status === 'completed' || status === 'stopped' || status === 'failed') {
      void queryClient.invalidateQueries({ queryKey: queryKeys.crawl(crawlId) });
      void queryClient.invalidateQueries({ queryKey: [...queryKeys.gridPrefix] });
    } else {
      void queryClient.invalidateQueries({ queryKey: queryKeys.crawlItem(crawlId) });
    }
    void announceStatus(crawlId, status);
    decideToast(event, previous);
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
      description: [name, [urls, found].filter(Boolean).join(', ')].filter(Boolean).join(': '),
      action: { label: 'View issues', onClick: () => openCrawl(crawlId, 'issues') },
    });
  }

  async function toastStopped(crawlId: string) {
    const item = await itemOf(crawlId);
    const name = crawlName(item);
    notifyResult('Crawl stopped', {
      description: item ? `${name}: ${formatCount(item.fetchedUrlCount)} URLs. You can continue it later.` : name,
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
