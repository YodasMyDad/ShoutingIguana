// How a crawl's status reads, everywhere: the top bar's pill, the library, announcements. UK English, one word each.
import { isCrawling, isLive, type CrawlListItem, type CrawlStatus } from '../api/types';
import type { LiveCrawl, LiveProgress } from '../live/liveStore';

const LABELS: Record<CrawlStatus, string> = {
  running: 'Running',
  paused: 'Paused',
  stopping: 'Stopping',
  analyzing: 'Analysing',
  stopped: 'Stopped',
  interrupted: 'Interrupted',
  completed: 'Done',
  failed: 'Failed',
  inUse: 'In use',
  unreadable: 'Can’t be read',
};

/** What a screen reader hears when a crawl's status changes. */
const ANNOUNCEMENTS: Record<CrawlStatus, string> = {
  running: 'crawling',
  paused: 'paused',
  stopping: 'stopping',
  analyzing: 'analysing',
  stopped: 'stopped',
  interrupted: 'interrupted',
  completed: 'finished',
  failed: 'failed',
  inUse: 'in use in another window',
  unreadable: 'can’t be read',
};

/** Simple's words where Advanced's are technical (docs/v2-ui-simple.md, Simple's words); the rest are the same. */
const SIMPLE_LABELS: Partial<Record<CrawlStatus, string>> = {
  running: 'Checking',
  stopped: 'Stopped early',
  analyzing: 'Working out the results',
};

/** A status in a word or two: Advanced's, or with `simple`, Simple's. */
export function statusLabel(status: CrawlStatus, mode: 'simple' | 'advanced' = 'advanced'): string {
  return (mode === 'simple' ? SIMPLE_LABELS[status] : undefined) ?? LABELS[status];
}

/** Simple's words for what a screen reader hears, where Advanced's are technical; the rest are the same. */
const SIMPLE_ANNOUNCEMENTS: Partial<Record<CrawlStatus, string>> = {
  running: 'checking',
  analyzing: 'working out the results',
  stopped: 'stopped early',
  failed: 'couldn’t finish',
};

export function statusAnnouncement(
  site: string,
  status: CrawlStatus,
  mode: 'simple' | 'advanced' = 'advanced',
): string {
  return `${site}: ${(mode === 'simple' ? SIMPLE_ANNOUNCEMENTS[status] : undefined) ?? ANNOUNCEMENTS[status]}`;
}

/** The status to show: this sidecar's live one while it's working on the crawl, else the library's. */
export function effectiveStatus(
  item: CrawlListItem | undefined,
  live: Pick<LiveCrawl, 'status'> | undefined,
): CrawlStatus | null {
  if (live?.status && isLive(live.status)) return live.status;
  return item?.status ?? live?.status ?? null;
}

/**
 * The crawl's "N URLs", the same number everywhere: the URLs crawled as the URL grid lists them (`fetched` is true),
 * which is E3c's `crawledUrlCount` at rest and the progress event's `crawled` while it runs. An earlier engine sends
 * neither: its `fetchedUrlCount`, and `fetched` less the URLs that weren't checked, are the nearest it has.
 */
export function crawledUrls(
  item: Pick<CrawlListItem, 'fetchedUrlCount' | 'crawledUrlCount'> | undefined,
  progress?: Pick<LiveProgress, 'fetched' | 'crawled' | 'noResponse'> | null,
): number | null {
  if (progress) {
    if (progress.crawled !== null) return progress.crawled;
    if (progress.fetched !== null)
      return progress.fetched - Math.min(progress.noResponse?.notChecked ?? 0, progress.fetched);
    return null;
  }
  return item ? (item.crawledUrlCount ?? item.fetchedUrlCount) : null;
}

/** The crawl's name in lists and announcements: its site, or its file name when it has none. */
export function crawlName(item: Pick<CrawlListItem, 'site' | 'fileName'> | undefined): string {
  return item?.site ?? item?.fileName ?? 'This crawl';
}

export interface CrawlControls {
  canPause: boolean;
  canResume: boolean;
  canStop: boolean;
  canContinue: boolean;
  canAnalyse: boolean;
}

/** Which controls a crawl offers in this status. */
export function crawlControls(status: CrawlStatus | null, item: CrawlListItem | undefined): CrawlControls {
  return {
    canPause: status === 'running',
    canResume: status === 'paused',
    canStop: status === 'running' || status === 'paused',
    canContinue: status === 'stopped' || status === 'interrupted',
    canAnalyse:
      status !== null &&
      !isCrawling(status) &&
      status !== 'analyzing' &&
      status !== 'interrupted' &&
      status !== 'inUse' &&
      status !== 'unreadable' &&
      item?.missing !== true,
  };
}

/**
 * The heading over the crawls this sidecar is working on, in the sidebar: what each is doing, counted ("1 crawling",
 * "1 paused", "2 crawling, 1 analysing"), never "crawling" for one that's paused or being analysed. A crawl whose
 * status hasn't arrived yet counts as crawling: it's live because it was started.
 */
export function runningHeading(statuses: readonly (CrawlStatus | null | undefined)[]): string {
  const counts = new Map<string, number>();
  for (const status of statuses) {
    const word =
      status === 'paused'
        ? 'paused'
        : status === 'stopping'
          ? 'stopping'
          : status === 'analyzing'
            ? 'analysing'
            : 'crawling';
    counts.set(word, (counts.get(word) ?? 0) + 1);
  }
  return ['crawling', 'paused', 'stopping', 'analysing']
    .filter((word) => counts.has(word))
    .map((word) => `${counts.get(word) ?? 0} ${word}`)
    .join(', ');
}
