import { useQuery } from '@tanstack/react-query';
import { crawlQuery } from '../api/queries';
import { isCrawling, type CrawlListItem, type CrawlStatus } from '../api/types';
import { useLive } from '../live/liveStore';
import { siteOriginOf } from '../ui/urlText';
import { crawlControls, effectiveStatus } from './status';

export interface CrawlState {
  item: CrawlListItem | undefined;
  status: CrawlStatus | null;
  /** Running, paused or stopping: what it hasn't fetched is still on its way, and its figures move. */
  crawling: boolean;
  analysing: boolean;
  canAnalyse: boolean;
  canContinue: boolean;
  /** The crawl's own scheme and host: URLs on it show their path. */
  siteOrigin: string | null;
}

/**
 * What a screen needs to know of the crawl it shows: its status as it is now, and what can be done with it. It reads
 * only the live crawl's status, never the whole of it, which changes on every progress event (4 a second while it
 * runs) and would draw the screen again each time.
 */
export function useCrawlState(crawlId: string): CrawlState {
  const item = useQuery(crawlQuery(crawlId)).data;
  const liveStatus = useLive((state) => state.crawls[crawlId]?.status);
  const status = effectiveStatus(item, liveStatus === undefined ? undefined : { status: liveStatus });
  const controls = crawlControls(status, item);
  return {
    item,
    status,
    crawling: status !== null && isCrawling(status),
    analysing: status === 'analyzing',
    canAnalyse: controls.canAnalyse,
    canContinue: controls.canContinue,
    siteOrigin: siteOriginOf(item?.startUrl),
  };
}
