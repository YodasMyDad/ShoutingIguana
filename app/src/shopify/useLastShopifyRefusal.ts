// What the site's last crawl met on Shopify (lane shopify): the new-crawl form opens its Shopify part by itself when the
// site's latest crawl in the library was a Shopify store that refused pages, and says how many, so a signature can be
// added before the crawl starts rather than found missing after it.
import { useQuery } from '@tanstack/react-query';
import { useMemo } from 'react';
import { libraryQuery, shopifyAccessQuery } from '../api/queries';
import type { CrawlListItem } from '../api/types';
import { startHostOf } from '../crawl/options/shopifySignature';
import { withScheme } from '../crawl/options/form';

/** The site as the library keys it: its host without `www.`, and `:port` when it has one of its own. */
export function siteKeyOf(startUrl: string): string | null {
  const host = startHostOf(withScheme(startUrl));
  return host === null ? null : host.replace(/^www\./, '');
}

/** The site's latest crawl in the library, by when it started; null for a site never crawled. */
export function latestCrawlOf(items: readonly CrawlListItem[] | undefined, key: string | null): CrawlListItem | null {
  if (!items || key === null) return null;
  const crawls = items.filter((item) => (item.siteKey ?? item.site) === key && item.startedAt !== null);
  return crawls.sort((a, b) => (b.startedAt ?? '').localeCompare(a.startedAt ?? ''))[0] ?? null;
}

/** How many pages Shopify refused on the site's last crawl, or null when it refused none (or the site is no store). */
export function useLastShopifyRefusal(startUrl: string): number | null {
  const items = useQuery(libraryQuery()).data;
  const key = siteKeyOf(startUrl);
  const last = useMemo(() => latestCrawlOf(items, key), [items, key]);
  const access = useQuery({ ...shopifyAccessQuery(last?.id ?? ''), enabled: last !== null }).data;
  if (!last || !access) return null;
  const refused = access.notCheckedUnsigned + access.notCheckedSigned;
  return refused > 0 ? refused : null;
}
