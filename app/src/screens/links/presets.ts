// The Links grid's presets: what people come to a link audit for, each as the filters it applies. A preset is only
// its filters: choosing one puts them in the route, where they show as chips that can be read, changed and removed, and
// the preset reads as chosen exactly while the route's filters are its own. So a preset's count is the grid's count,
// and nothing is filtered that the filter bar doesn't say.
import type { GridFilter } from '../../api/grids';

export interface LinkPreset {
  id: string;
  title: string;
  /** What it shows, for its tooltip. */
  hint: string;
  filters: readonly GridFilter[];
}

export const ALL_LINKS: LinkPreset = {
  id: 'all',
  title: 'All links',
  hint: 'Every link the crawl stored, with no filter',
  filters: [],
};

export const LINK_PRESETS: readonly LinkPreset[] = [
  ALL_LINKS,
  {
    id: 'broken',
    title: 'Broken links',
    hint: 'Links whose target answered 4xx or 5xx',
    filters: [{ column: 'to.statusClass', op: 'in', values: ['4xx', '5xx'] }],
  },
  {
    id: 'redirects',
    title: 'Links to redirects',
    hint: 'Links whose target answered 3xx',
    filters: [{ column: 'to.statusClass', op: 'in', values: ['3xx'] }],
  },
  {
    id: 'nofollow',
    title: 'Nofollow links',
    hint: 'Links whose own rel says nofollow',
    filters: [{ column: 'noFollow', op: 'is', value: true }],
  },
  {
    id: 'no-text',
    title: 'No anchor text',
    hint: 'Hyperlinks with no text of their own, nor an image’s alt text',
    filters: [
      { column: 'kind', op: 'in', values: ['hyperlink'] },
      { column: 'text', op: 'isEmpty' },
    ],
  },
  {
    id: 'external',
    title: 'External links',
    hint: 'Links to other sites',
    filters: [{ column: 'to.isInternal', op: 'is', value: false }],
  },
];

/** A filter in one form, whatever order its fields and values came in, so two that select the same links match. */
function canonical(filter: GridFilter): string {
  const entries = Object.entries(filter as Record<string, unknown>).map(([key, value]): [string, unknown] => [
    key,
    Array.isArray(value) ? [...(value as (string | number)[])].sort() : value,
  ]);
  return JSON.stringify(entries.sort(([a], [b]) => a.localeCompare(b)));
}

/** Whether two filter sets are the same filters, in any order. */
export function sameFilters(a: readonly GridFilter[], b: readonly GridFilter[]): boolean {
  if (a.length !== b.length) return false;
  const left = a.map(canonical).sort();
  const right = b.map(canonical).sort();
  return left.every((filter, index) => filter === right[index]);
}

/**
 * The preset the view is on: the one whose filters are exactly the route's, with no search. Null for a view of the
 * user's own making, which no preset's name would describe.
 */
export function activePreset(filters: readonly GridFilter[], search: string | undefined): LinkPreset | null {
  if (search) return null;
  return LINK_PRESETS.find((preset) => sameFilters(preset.filters, filters)) ?? null;
}
