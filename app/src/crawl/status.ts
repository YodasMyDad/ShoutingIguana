// How a crawl's status reads, everywhere: the top bar's pill, the library, announcements. UK English, one word each.
import { isCrawling, isLive, type CrawlListItem, type CrawlStatus } from '../api/types';
import type { LiveCrawl } from '../live/liveStore';

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

export function statusLabel(status: CrawlStatus): string {
  return LABELS[status];
}

export function statusAnnouncement(site: string, status: CrawlStatus): string {
  return `${site}: ${ANNOUNCEMENTS[status]}`;
}

/** The status to show: this sidecar's live one while it's working on the crawl, else the library's. */
export function effectiveStatus(item: CrawlListItem | undefined, live: LiveCrawl | undefined): CrawlStatus | null {
  if (live?.status && isLive(live.status)) return live.status;
  return item?.status ?? live?.status ?? null;
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
