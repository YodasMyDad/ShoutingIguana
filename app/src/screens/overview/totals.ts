// Overview's totals row (docs/v2-comparison.md, "What to take from Screaming Frog": "A row of totals before anything
// else. Their default Dashboard: Crawled, Internal, External"). Each total is the URL grid's own count under the filter
// its link opens, so every figure lands on itself, and the four agree with the library's "N URLs" and "pages"
// (docs/v2-ui-ia.md, "One number everywhere").
import { queryOptions } from '@tanstack/react-query';
import { api } from '../../api/client';
import { queryKeys } from '../../api/queries';
import type { GridFilter } from '../../api/grids';
import { ANSWERED_SITE_PAGES } from './links';

/** The URLs crawled: the grid's Crawled, and the library's "N URLs". */
export const CRAWLED: readonly GridFilter[] = [{ column: 'fetched', op: 'is', value: true }];
/** Of them, on the site: its host, and the crawler's probes of the site's other versions, which are on the site too. */
export const CRAWLED_INTERNAL: readonly GridFilter[] = [...CRAWLED, { column: 'isInternal', op: 'is', value: true }];
/** Of them, on other sites: the links out the crawl checked. */
export const CRAWLED_EXTERNAL: readonly GridFilter[] = [...CRAWLED, { column: 'isInternal', op: 'is', value: false }];

export interface CrawlTotals {
  /** Every URL the crawl found, crawled or not: the URL grid's total. */
  found: number;
  crawled: number;
  internal: number;
  external: number;
  /** The site's pages the crawl read, as the library's `pageCount` and the analysis's `pagesAnalyzed` count them. */
  pages: number;
}

/** How many URLs a filter set selects, and how many the crawl has in all: one page of one row, for its counts. */
const count = async (crawlId: string, filters: readonly GridFilter[], signal: AbortSignal) => {
  const page = await api.urls(
    crawlId,
    { columns: ['url'], filters: [...filters], sort: [], offset: 0, limit: 1 },
    signal,
  );
  return { matching: page.matching ?? 0, total: page.total ?? 0 };
};

/** The totals, read together so they're of one moment. Under the crawl's key, so whatever changes it refreshes them. */
export const crawlTotalsQuery = (crawlId: string) =>
  queryOptions({
    queryKey: [...queryKeys.crawl(crawlId), 'totals'] as const,
    queryFn: async ({ signal }): Promise<CrawlTotals> => {
      const [crawled, internal, external, pages] = await Promise.all([
        count(crawlId, CRAWLED, signal),
        count(crawlId, CRAWLED_INTERNAL, signal),
        count(crawlId, CRAWLED_EXTERNAL, signal),
        count(crawlId, ANSWERED_SITE_PAGES, signal),
      ]);
      return {
        found: crawled.total,
        crawled: crawled.matching,
        internal: internal.matching,
        external: external.matching,
        pages: pages.matching,
      };
    },
    staleTime: Infinity,
  });
