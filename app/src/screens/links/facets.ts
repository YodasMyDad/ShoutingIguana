// The Links grid's facets (docs/v2-sidecar-api.md, Link facets): the links a view matches counted by value, as quick
// filters. Each value comes from the engine with its count and the filter that selects exactly those links, so adding
// that filter to the view's own lands on that count. This module only words them and puts them in order.
import type { FacetBucket, GridFilter, LinkFacetId, LinkFacetsPage } from '../../api/grids';
import { formatCountOf } from '../../lib/format';
import { FOLLOW_WORDS, linkValueName } from './linkWords';
import { sameFilters } from './presets';

export const LINK_FACET_IDS: readonly LinkFacetId[] = ['kind', 'position', 'follow', 'to.statusClass', 'to.isInternal'];

export const FACET_TITLES: Record<LinkFacetId, string> = {
  kind: 'Kind',
  position: 'Position',
  follow: 'Follow',
  'to.statusClass': 'Target status',
  'to.isInternal': 'Target site',
};

/** What a facet's unknown value (null) means: each has its own true words. */
const NO_VALUE: Record<LinkFacetId, string> = {
  kind: 'A kind this version doesn’t know',
  position: 'A position this version doesn’t know',
  follow: 'Unknown',
  // No status class is a target the crawl has no answer from at all: queued, never reached, not followed, or not
  // checked. "Not fetched" is true of every one of them.
  'to.statusClass': 'Not fetched',
  'to.isInternal': 'Unknown',
};

const SITE: Record<string, string> = { true: 'This site', false: 'Another site' };

/** A facet's value in words: "Hyperlink", "Not followed", "4xx", "Another site". */
export function facetValueWords(facet: LinkFacetId, value: FacetBucket['value']): string {
  if (value === null) return NO_VALUE[facet];
  if (facet === 'follow') return FOLLOW_WORDS[String(value)] ?? String(value);
  if (facet === 'to.isInternal') return SITE[String(value)] ?? String(value);
  return (typeof value === 'boolean' ? null : linkValueName(facet, value)) ?? String(value);
}

export interface FacetValue {
  /** Stable for a list: the bucket's value. */
  key: string;
  words: string;
  count: number;
  /** The engine's filter for exactly these links. */
  filter: GridFilter;
  /** The view already has this very filter: choosing it again would change nothing. */
  applied: boolean;
  /** What a screen reader and a tooltip say: "Hyperlink, 17,641 links". */
  name: string;
}

export interface Facet {
  id: LinkFacetId;
  title: string;
  values: FacetValue[];
}

/**
 * The facets as lists, in the engine's order of values (a column's own order, the unknown last). `filters` are the
 * view's: a value whose filter the view already has is marked, as its count is every link in view.
 */
export function buildFacets(page: LinkFacetsPage, filters: readonly GridFilter[]): Facet[] {
  return LINK_FACET_IDS.flatMap((id) => {
    const buckets = page.facets[id];
    if (!buckets) return [];
    return [
      {
        id,
        title: FACET_TITLES[id],
        values: buckets.map((bucket) => {
          const words = facetValueWords(id, bucket.value);
          return {
            key: String(bucket.value),
            words,
            count: bucket.count,
            filter: bucket.filter,
            applied: filters.some((filter) => sameFilters([filter], [bucket.filter])),
            name: `${words}, ${formatCountOf(bucket.count, 'link')}`,
          };
        }),
      },
    ];
  });
}

/**
 * The view's filters with a facet's value added. The values were counted under the view's filters, so the value's own
 * filter beside them leaves exactly its count. Only where it says all the old one did (the old filter is a list of
 * values the new one's are among: "4xx or 5xx", then "4xx") does it take the old one's place, so the chips don't say
 * the same thing twice; a filter it narrows another way stays ("Response time is over 300 ms" and "is 250-499 ms",
 * which is 301-499 ms, the count the menu showed).
 */
export function withFacetValue(filters: readonly GridFilter[], value: GridFilter): GridFilter[] {
  const narrows = (filter: GridFilter) =>
    filter.column === value.column &&
    filter.op === 'in' &&
    value.op === 'in' &&
    !('group' in filter) &&
    value.values.every((item) => filter.values.includes(item));
  return [...filters.filter((filter) => !narrows(filter)), value];
}
