// The Links grid's presets: what people come to a link audit for, each as the filters it applies. A preset is only
// its filters: choosing one puts them in the route, where they show as chips that can be read, changed and removed, and
// the preset reads as chosen exactly while the route's filters are its own. So a preset's count is the grid's count,
// and nothing is filtered that the filter bar doesn't say.
import type { GridFilter, GridSort } from '../../api/grids';
import { ENDS_AT_COLUMN, PIXELS_COLUMN } from './linkWords';

export interface LinkPreset {
  id: string;
  title: string;
  /** What it shows, for its tooltip. */
  hint: string;
  filters: readonly GridFilter[];
  /**
   * The columns it shows, for a preset whose links want their own (Images: the file's size and pixels). Choosing it puts
   * them in the route; choosing another preset after it puts the default columns back. Left out, the columns stay.
   */
  columns?: readonly string[];
  /** The order it shows its links in, put in the route as it's chosen; left out, the view's own order. */
  sort?: readonly GridSort[];
}

/** The Images preset's columns: the image, its file's size and pixels, its alt text, and the page that shows it. */
export const IMAGE_COLUMNS: readonly string[] = [
  'to.url',
  'to.sizeBytes',
  PIXELS_COLUMN,
  'text',
  'from.url',
  'position',
];

/**
 * The Links to redirects preset's columns (the UX round's D-20): the link, how its target answered and where that
 * ends, which is what says why a target that answered 200 is here (a meta refresh), and what to change the link to.
 * Every link here is a hyperlink, so its kind isn't said again.
 */
export const REDIRECT_COLUMNS: readonly string[] = [
  'from.url',
  'to.url',
  'to.statusCode',
  ENDS_AT_COLUMN,
  'text',
  'position',
];

/** The largest image file first. */
export const IMAGE_SORT: readonly GridSort[] = [{ column: 'to.sizeBytes', desc: true }];

/**
 * The links a check counts (the links grid's `check` filter), with hyperlinks alone: exactly the links the link checks
 * count, each on the page that holds it. A target's status class says less: another site's 403 is often only sent to
 * crawlers, and the check lets it go; a target that didn't answer at all (a DNS failure) has no status, and is broken;
 * a URL that redirects to an error is broken too, though it answered 3xx; a temporary redirect isn't one to change a
 * link for; and a redirect's hop is a link of the redirect's, never of a page anyone can edit.
 */
const linksCountedBy = (...checks: string[]): readonly GridFilter[] => [
  { column: 'kind', op: 'in', values: ['hyperlink'] },
  { column: 'check', op: 'in', values: checks },
];

/** Links to broken URLs, on the site and off it: `broken-internal-links` and `broken-external-links`. */
export const BROKEN_LINKS = linksCountedBy('broken-internal-links', 'broken-external-links');

/** Links to the site's URLs that redirect for good to a working page: `links-to-redirects`. */
export const LINKS_TO_REDIRECTS = linksCountedBy('links-to-redirects');

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
    hint: 'Links to URLs that fail or don’t answer, as the broken link checks count them, each on the page that holds it',
    filters: BROKEN_LINKS,
  },
  {
    id: 'redirects',
    title: 'Links to redirects',
    hint: 'Links to the site’s URLs that redirect for good to a working page, as the check counts them, with where each ends',
    filters: LINKS_TO_REDIRECTS,
    columns: REDIRECT_COLUMNS,
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
  {
    id: 'images',
    title: 'Images',
    hint: 'Every image the pages show, the largest file first, with its pixels, its alt text and the page it’s on',
    filters: [{ column: 'kind', op: 'in', values: ['image'] }],
    columns: IMAGE_COLUMNS,
    sort: IMAGE_SORT,
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

/**
 * A view's sort keys whose columns are shown, in their order, and no sort (undefined, as the route has it) once none
 * is. A sort is seen and changed at its column's header: by a hidden column the rows would be in an order nothing on
 * the screen explains, under a view that reads as "All links". Hiding one key's column drops that key alone.
 */
export function sortShown(sort: readonly GridSort[] | undefined, shown: readonly string[]): GridSort[] | undefined {
  const kept = (sort ?? []).filter((key) => shown.includes(key.column));
  // The keys whose columns are still shown stay, in their order: hiding one column drops its key, not the others.
  return kept.length > 0 ? kept : undefined;
}

/** Whether some columns are exactly a preset's own, in its order: the columns it put in the route. */
function presetColumns(cols: readonly string[] | undefined): boolean {
  return (
    cols !== undefined &&
    LINK_PRESETS.some(
      (preset) => preset.columns?.length === cols.length && preset.columns.every((id, index) => cols[index] === id),
    )
  );
}

/**
 * The route a preset is chosen into, from the one it's chosen from: its filters, and its own columns and order where it
 * has them. The mode stays (one row a link, or a target); a preset's order is kept in either mode where it means
 * something there (a column of the target). The columns stay as the user chose them, unless they were another preset's
 * own: a preset's columns go with it.
 */
export function presetSearch(
  preset: LinkPreset,
  previous: { cols?: string[] | undefined; by?: 'target' | undefined },
): { cols?: string[]; filter?: GridFilter[]; sort?: GridSort[]; by?: 'target' } {
  const cols = preset.columns ? [...preset.columns] : presetColumns(previous.cols) ? undefined : previous.cols;
  return {
    ...(cols ? { cols } : {}),
    ...(preset.filters.length > 0 ? { filter: [...preset.filters] } : {}),
    ...(preset.sort ? { sort: [...preset.sort] } : {}),
    ...(previous.by ? { by: previous.by } : {}),
  };
}

/**
 * The Links view of the images one page shows, as the Images preset shows them: what an image check's page opens in
 * Links. Its links to itself are left out, as the inspector leaves them out.
 */
export function imagesOfPage(pageId: number): { cols: string[]; filter: GridFilter[]; sort: GridSort[] } {
  return {
    cols: [...IMAGE_COLUMNS],
    filter: [
      { column: 'from.id', op: 'eq', value: pageId },
      { column: 'selfLink', op: 'is', value: false },
      { column: 'kind', op: 'in', values: ['image'] },
    ],
    sort: [...IMAGE_SORT],
  };
}
