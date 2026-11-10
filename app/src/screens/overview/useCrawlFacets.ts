import { useQuery } from '@tanstack/react-query';
import { urlFacetsQuery } from '../../api/queries';
import { INDEXABILITY_FACETS, PAGE_FACETS } from './answer';
import { STATUS_FACETS } from './statusSplit';

/** The three reads behind the crawl's breakdowns, shared by the cards and by the screen's "refreshing" line. */
export function useCrawlFacets(crawlId: string, enabled: boolean) {
  const status = useQuery({
    ...urlFacetsQuery(crawlId, { filters: STATUS_FACETS.filters, facets: [...STATUS_FACETS.facets] }),
    enabled,
  });
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
