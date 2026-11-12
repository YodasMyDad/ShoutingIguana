import { queryOptions, useQuery } from '@tanstack/react-query';
import { api } from '../../api/client';
import { queryKeys, urlFacetsQuery } from '../../api/queries';
import { INDEXABILITY_FACETS, PAGE_FACETS } from './answer';
import { NOT_CHECKED_FACETS, PROBE_ROWS, STATUS_FACETS, probeCountsOf, type StatusFacets } from './statusSplit';

/**
 * The status breakdown's two reads as one: the classes of what wasn't blocked, and how many URLs weren't checked. One
 * query, so the card never shows the first without the second (its "found but not crawled" would count the URLs that
 * weren't checked for a moment), and it loads, fails and tries again as one. Its key is under the crawl's facets, so
 * whatever refreshes them refreshes it.
 */
export const statusFacetsQuery = (crawlId: string) => {
  const status = { filters: STATUS_FACETS.filters, facets: [...STATUS_FACETS.facets] };
  const notChecked = { filters: NOT_CHECKED_FACETS.filters, facets: [...NOT_CHECKED_FACETS.facets] };
  return queryOptions({
    queryKey: [...queryKeys.urlFacets(crawlId, status), notChecked] as const,
    queryFn: async ({ signal }): Promise<StatusFacets> => {
      const [facets, apart, probes] = await Promise.all([
        api.urlFacets(crawlId, status, signal),
        api.urlFacets(crawlId, notChecked, signal),
        api.urls(
          crawlId,
          { ...PROBE_ROWS, filters: [...PROBE_ROWS.filters], columns: [...PROBE_ROWS.columns], sort: [] },
          signal,
        ),
      ]);
      // The probes are a handful on any site; more than a page of them, and they're left in rather than half taken out.
      const told = probes.matching !== null && probes.matching <= probes.rows.length;
      return { ...facets, notChecked: apart.matching, probes: told ? probeCountsOf(probes.rows) : null };
    },
    staleTime: Infinity,
  });
};

/** The reads behind the crawl's breakdowns, shared by the cards and by the screen's "refreshing" line. */
export function useCrawlFacets(crawlId: string, enabled: boolean) {
  const status = useQuery({ ...statusFacetsQuery(crawlId), enabled });
  const indexability = useQuery({
    ...urlFacetsQuery(crawlId, { filters: INDEXABILITY_FACETS.filters, facets: [...INDEXABILITY_FACETS.facets] }),
    enabled,
  });
  const pages = useQuery({
    ...urlFacetsQuery(crawlId, { filters: PAGE_FACETS.filters, facets: [...PAGE_FACETS.facets] }),
    enabled,
  });
  return { status, indexability, pages };
}

export type CrawlFacets = ReturnType<typeof useCrawlFacets>;
