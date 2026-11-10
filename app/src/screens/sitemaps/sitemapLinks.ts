// Where each of a sitemap's counts leads: its own URL grid (`/sitemaps/urls`) under the filter that selects exactly the
// URLs counted, as the API says each count is judged (docs/v2-sidecar-api.md, Sitemaps). The same filters are the
// grid's quick filters. Plain data, so each count's link is unit-tested with its number, and the sweep
// (e2e/sitemaps-robots.sweep.spec.ts) checks every one against real crawls.
import type { GridFilter, Sitemap } from '../../api/grids';

/** The counts a sitemap carries, or that follow from them: "not crawled" is what's listed less what was fetched. */
export type SitemapCount = 'listed' | 'crawled' | 'notCrawled' | 'indexable' | 'nonIndexable';

/** The filter that selects exactly what each count counts, in a sitemap's own grid. */
export const COUNT_FILTERS: Record<SitemapCount, readonly GridFilter[]> = {
  listed: [],
  crawled: [{ column: 'fetched', op: 'is', value: true }],
  notCrawled: [{ column: 'fetched', op: 'is', value: false }],
  indexable: [{ column: 'indexable', op: 'is', value: true }],
  nonIndexable: [{ column: 'indexable', op: 'is', value: false }],
};

export const COUNT_WORDS: Record<SitemapCount, string> = {
  listed: 'Listed',
  crawled: 'Crawled',
  notCrawled: 'Not crawled',
  indexable: 'Indexable',
  nonIndexable: 'Non-indexable',
};

export function countOf(sitemap: Pick<Sitemap, 'urls'>, count: SitemapCount): number {
  const { listed, fetched, indexable, nonIndexable } = sitemap.urls;
  switch (count) {
    case 'listed':
      return listed;
    case 'crawled':
      return fetched;
    case 'notCrawled':
      // Never below nothing, whatever a crawl still running says of the two a moment apart.
      return Math.max(0, listed - fetched);
    case 'indexable':
      return indexable;
    case 'nonIndexable':
      return nonIndexable;
  }
}

/**
 * Listed, but redirecting or broken: the URLs that answered with a redirect or an error, or didn't answer. Not the
 * ones robots.txt or a firewall kept the crawl from, nor the ones it didn't check: those aren't known to be broken.
 */
export const REDIRECTING_OR_BROKEN: readonly GridFilter[] = [
  { column: 'outcome', op: 'in', values: ['redirect', 'clientError', 'serverError', 'timeout', 'networkError'] },
];

/** The quick filters over one sitemap's URLs: what people come for. */
export type QuickFilter = 'listed' | 'notCrawled' | 'nonIndexable' | 'broken';

export const QUICK_FILTERS: readonly QuickFilter[] = ['listed', 'notCrawled', 'nonIndexable', 'broken'];

export const QUICK_FILTER_WORDS: Record<QuickFilter, string> = {
  listed: 'All',
  notCrawled: 'Not crawled',
  nonIndexable: 'Non-indexable',
  broken: 'Redirecting or broken',
};

export function quickFilterFilters(quick: QuickFilter): readonly GridFilter[] {
  return quick === 'broken' ? REDIRECTING_OR_BROKEN : COUNT_FILTERS[quick];
}

/** A quick filter's count, where the sitemap carries one: nothing counts the redirecting or broken ones apart. */
export function quickFilterCount(sitemap: Pick<Sitemap, 'urls'>, quick: QuickFilter): number | null {
  return quick === 'broken' ? null : countOf(sitemap, quick);
}

const same = (a: readonly GridFilter[], b: readonly GridFilter[]) => JSON.stringify(a) === JSON.stringify(b);

/** The quick filter the grid's filters are, or null when they're something else (a count's link, a filter removed). */
export function quickFilterOf(filters: readonly GridFilter[]): QuickFilter | null {
  return QUICK_FILTERS.find((quick) => same(quickFilterFilters(quick), filters)) ?? null;
}
