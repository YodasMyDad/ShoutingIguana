// The URL grid's facets (docs/v2-sidecar-api.md, Facets): the URLs a view matches counted by value, as quick filters.
// Each value comes from the engine with its count and the filter that selects exactly those URLs, so adding that
// filter to the view's own lands on that count. This module only words them and puts them in order.
import type { FacetBucket, FacetId, FacetsPage, GridFilter } from '../../api/grids';
import { formatCountOf } from '../../lib/format';
import { sameFilters } from '../links/presets';
import { urlCellValue } from './urlColumns';

/** The facets the bar offers, in its order: what a URL audit narrows by first. */
export const URL_FACET_IDS: readonly FacetId[] = [
  'statusClass',
  'statusCode',
  'indexability',
  'type',
  'depth',
  'responseTime',
  'inSitemap',
  'severity',
];

export const URL_FACET_TITLES: Record<FacetId, string> = {
  statusClass: 'Status',
  statusCode: 'Status code',
  indexability: 'Indexability',
  type: 'Type',
  depth: 'Depth',
  responseTime: 'Response time',
  inSitemap: 'Sitemaps',
  severity: 'Issues',
};

/** The facets that count from the analysis: without a current one the engine leaves them out, and says why. */
export const ANALYSIS_FACETS: ReadonlySet<FacetId> = new Set(['inSitemap', 'severity']);

/**
 * What a facet's unknown value (null) means: each has its own true words. No status class is a URL the crawl has no
 * answer from at all (found and not requested, or not checked because its host had stopped answering); while the crawl
 * runs, some of them may still be.
 */
function noValueWords(facet: FacetId, crawling: boolean): string {
  switch (facet) {
    case 'statusClass':
      return crawling ? 'Not crawled so far' : 'Not crawled';
    case 'statusCode':
      return 'No status';
    case 'indexability':
      return 'Not judged';
    case 'depth':
      return 'Only in a sitemap';
    case 'responseTime':
      return 'No request made';
    case 'inSitemap':
      return 'Not known';
    default:
      return 'Unknown';
  }
}

const SITEMAP: Record<string, string> = { true: 'In a sitemap', false: 'In no sitemap' };
const SEVERITY: Record<string, string> = {
  error: 'With errors',
  warning: 'With warnings',
  notice: 'With notices',
  none: 'No issues',
};

/** A facet's value in words: "4xx", "404", "Client error", "PDF", "Depth 3", "250–499 ms", "With errors". */
export function urlFacetValueWords(
  facet: FacetId,
  bucket: FacetBucket,
  crawling = false,
  titles?: Readonly<Record<string, string>>,
): string {
  const value = bucket.value;
  if (value === null) return noValueWords(facet, crawling);
  switch (facet) {
    case 'statusClass':
    case 'indexability':
    case 'type':
      return urlCellValue(facet, String(value), titles);
    case 'depth':
      return `Depth ${String(value)}`;
    case 'responseTime':
      return bucket.max === undefined ? `${bucket.min ?? 0} ms or more` : `${bucket.min ?? 0}–${bucket.max} ms`;
    case 'inSitemap':
      return SITEMAP[String(value)] ?? String(value);
    case 'severity':
      return SEVERITY[String(value)] ?? String(value);
    default:
      return String(value);
  }
}

export interface UrlFacetValue {
  /** Stable for a list: the bucket's value. */
  key: string;
  words: string;
  count: number;
  /** The engine's filter for exactly these URLs. */
  filter: GridFilter;
  /** The view already has this very filter: choosing it again would change nothing. */
  applied: boolean;
}

export interface UrlFacet {
  id: FacetId;
  title: string;
  values: UrlFacetValue[];
}

/**
 * The facets as lists, in the engine's order of values (a column's own order, the unknown last). `filters` are the
 * view's: a value whose filter the view already has is marked, as its count is every URL in view.
 */
export function buildUrlFacets(
  page: FacetsPage,
  filters: readonly GridFilter[],
  crawling = false,
  /** The engine's words for a column's values (`valueTitles`), by the column's id: the facet's are its column's. */
  titles: (column: string) => Readonly<Record<string, string>> | undefined = () => undefined,
): UrlFacet[] {
  return URL_FACET_IDS.flatMap((id) => {
    const buckets = page.facets[id];
    if (!buckets) return [];
    return [
      {
        id,
        title: URL_FACET_TITLES[id],
        values: buckets.map((bucket) => ({
          key: String(bucket.value),
          words: urlFacetValueWords(id, bucket, crawling, titles(id)),
          count: bucket.count,
          filter: bucket.filter,
          applied: filters.some((filter) => sameFilters([filter], [bucket.filter])),
        })),
      },
    ];
  });
}

/** A value's name for a screen reader and a tooltip: "4xx, 12 URLs". */
export function urlFacetValueName(value: UrlFacetValue): string {
  return `${value.words}, ${formatCountOf(value.count, 'URL')}`;
}
