import type { CrawlListItem } from '../api/types';

/** How many crawls the sidebar's switcher lists: the most recent, the open one always among them. */
export const RECENT_CRAWLS = 8;

/** When a crawl last began or changed, for its place in the recent list: never-started files go last. */
const recency = (item: CrawlListItem) => item.startedAt ?? item.updatedAt ?? '';

/**
 * The library's most recent crawls, newest first, for the sidebar's switcher. The open crawl is always listed: past
 * the limit, it takes the last place, so the list says where you are.
 */
export function recentCrawls(
  library: readonly CrawlListItem[],
  openId: string,
  limit = RECENT_CRAWLS,
): CrawlListItem[] {
  const newest = [...library].sort((a, b) => recency(b).localeCompare(recency(a)));
  const recent = newest.slice(0, limit);
  const open = newest.find((item) => item.id === openId);
  if (!open || recent.includes(open)) return recent;
  return [...recent.slice(0, limit - 1), open];
}
