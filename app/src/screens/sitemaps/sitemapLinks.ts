// Where each of a sitemap's counts leads: its own URL grid (`/sitemaps/urls`) under the filter that selects exactly the
// URLs counted, as the API says each count is judged (docs/v2-sidecar-api.md, Sitemaps). The same filters are the
// grid's quick filters. Plain data, so each count's link is unit-tested with its number, and the sweep
// (e2e/sitemaps-robots.sweep.spec.ts) checks every one against real crawls.
//
// One meaning per word. "Crawled" here means the crawl requested the URL, which is what a person reading a sitemap's
// row takes it to mean. The engine's `fetched` is wider: it's true of every URL the crawl is done with, the ones
// robots.txt refused included, which were never requested. So:
//
//   Listed = Crawled + Blocked by robots.txt + Not crawled
//
// - Crawled: `fetched`, less the URLs robots.txt refused. One that was requested and didn't answer (a timeout, a
//   network error) is crawled, and its Outcome says what happened.
// - Blocked by robots.txt: the URLs whose outcome is `robotsDisallowed`.
// - Not crawled: what's listed and not `fetched`: still to come, never reached, or not checked.
import type { GridFilter, Sitemap } from '../../api/grids';

/** The counts a sitemap carries, or that follow from them. */
export type SitemapCount = 'listed' | 'crawled' | 'blocked' | 'notCrawled' | 'indexable' | 'nonIndexable';

/** The URL grid's filter for the URLs robots.txt kept the crawl from: the engine's own word for them. */
const REFUSED_BY_ROBOTS = ['robotsDisallowed'];

/** The filter that selects exactly what each count counts, in a sitemap's own grid. */
export const COUNT_FILTERS: Record<SitemapCount, readonly GridFilter[]> = {
  listed: [],
  crawled: [
    { column: 'fetched', op: 'is', value: true },
    { column: 'outcome', op: 'notIn', values: REFUSED_BY_ROBOTS },
  ],
  blocked: [{ column: 'outcome', op: 'in', values: REFUSED_BY_ROBOTS }],
  notCrawled: [{ column: 'fetched', op: 'is', value: false }],
  indexable: [{ column: 'indexable', op: 'is', value: true }],
  nonIndexable: [{ column: 'indexable', op: 'is', value: false }],
};

export const COUNT_WORDS: Record<SitemapCount, string> = {
  listed: 'Listed',
  crawled: 'Crawled',
  blocked: 'Blocked by robots.txt',
  notCrawled: 'Not crawled',
  indexable: 'Indexable',
  nonIndexable: 'Non-indexable',
};

// ------------------------------------------------------------------------- The two counts the E3c review added
//
// `GET /sitemaps` counts, for each sitemap, the listed URLs robots.txt refused (`urls.robotsDisallowed`) and those
// that redirect or are broken (`urls.redirectingOrBroken`), each landing on itself under the filter of the same
// name below (docs/v2-sidecar-api.md, Sitemaps). They're read here and nowhere else. A sidecar from before the
// review sends neither: that's "not known", never 0.

/** The listed URLs robots.txt refused, or null from an engine that doesn't count them for the list. */
function refusedOf(sitemap: Pick<Sitemap, 'urls'>): number | null {
  return sitemap.urls.robotsDisallowed ?? null;
}

/** The listed URLs that redirect or are broken, or null from an engine that doesn't count them for the list. */
function brokenOf(sitemap: Pick<Sitemap, 'urls'>): number | null {
  return sitemap.urls.redirectingOrBroken ?? null;
}

/**
 * Said where the list can't give "Crawled" or "Blocked by robots.txt": this crawl's engine counts what it's done with
 * (`fetched`), robots.txt's refusals included, and showing that as "Crawled" would mislead.
 */
export const ENGINE_DOESNT_SAY =
  'This version of the engine doesn’t count, for each sitemap, the URLs robots.txt refused, so the list can’t say how many were crawled. Open the sitemap: its own page counts them.';

/**
 * A count of a sitemap's URLs. Null when the engine doesn't say: "Crawled" and "Blocked by robots.txt" both need its
 * count of the URLs robots.txt refused.
 */
export function countOf(sitemap: Pick<Sitemap, 'urls'>, count: SitemapCount): number | null {
  const { listed, fetched, indexable, nonIndexable } = sitemap.urls;
  const refused = refusedOf(sitemap);
  switch (count) {
    case 'listed':
      return listed;
    case 'crawled':
      // Never below nothing, whatever a crawl still running says of the two a moment apart.
      return refused === null ? null : Math.max(0, fetched - refused);
    case 'blocked':
      return refused;
    case 'notCrawled':
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

/**
 * The quick filters over one sitemap's URLs: every count of the list's row, so each lands on a filter with a name,
 * and what people come for besides (listed but redirecting or broken).
 */
export type QuickFilter = 'listed' | 'crawled' | 'blocked' | 'notCrawled' | 'nonIndexable' | 'broken';

export const QUICK_FILTERS: readonly QuickFilter[] = [
  'listed',
  'crawled',
  'blocked',
  'notCrawled',
  'nonIndexable',
  'broken',
];

export const QUICK_FILTER_WORDS: Record<QuickFilter, string> = {
  listed: 'All',
  crawled: 'Crawled',
  blocked: 'Blocked by robots.txt',
  notCrawled: 'Not crawled',
  nonIndexable: 'Non-indexable',
  broken: 'Redirecting or broken',
};

export function quickFilterFilters(quick: QuickFilter): readonly GridFilter[] {
  return quick === 'broken' ? REDIRECTING_OR_BROKEN : COUNT_FILTERS[quick];
}

/** A quick filter's count, where the list carries one: null when the engine doesn't count it for the list. */
export function quickFilterCount(sitemap: Pick<Sitemap, 'urls'>, quick: QuickFilter): number | null {
  return quick === 'broken' ? brokenOf(sitemap) : countOf(sitemap, quick);
}

const same = (a: readonly GridFilter[], b: readonly GridFilter[]) => JSON.stringify(a) === JSON.stringify(b);

/** The quick filter the grid's filters are, or null when they're something else (a filter removed, a link made by hand). */
export function quickFilterOf(filters: readonly GridFilter[]): QuickFilter | null {
  return QUICK_FILTERS.find((quick) => same(quickFilterFilters(quick), filters)) ?? null;
}
