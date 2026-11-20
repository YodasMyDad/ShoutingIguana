// The URL grid's facets that are about pages (the UX audit of 2026-11-19, D-10 and D-11). Indexability takes several
// values at once and answers with pages: "Not indexable" is one list, and "Indexable" is never 4,000 images. Titles,
// descriptions and H1s list what the checks already found (missing, duplicate, too long), each in one choice.
import type { GridFilter } from '../../api/grids';
import { sameFilters } from '../links/presets';

const INDEXABILITY = 'indexability';
/** The value of a page nothing stops from being indexed. */
export const INDEXABLE = 'none';

/** A page, for a view that doesn't say which types it lists: an indexability is a page's. */
export const PAGE_TYPE: GridFilter = { column: 'type', op: 'in', values: ['page'] };

const isOwn = (filter: GridFilter) => filter.column === INDEXABILITY && filter.op === 'in' && !('group' in filter);
const namesType = (filters: readonly GridFilter[]) => filters.some((filter) => filter.column === 'type');

/** The indexability values the view is narrowed to, by a filter the facet made or one like it. */
export function tickedIndexability(filters: readonly GridFilter[]): string[] {
  const own = filters.find(isOwn);
  return own && 'values' in own ? own.values.map(String) : [];
}

/**
 * What the facet's values are counted in: the view without its own indexability filter, so every value still shows
 * with the count choosing it would add, and narrowed to pages where the view names no type, as choosing a value does.
 */
export function indexabilityCountFilters(filters: readonly GridFilter[]): GridFilter[] {
  const others = filters.filter((filter) => !isOwn(filter));
  return namesType(others) ? others : [...others, PAGE_TYPE];
}

/**
 * The view with these indexability values ticked (none: the filter gone). With a value ticked, a view that names no
 * type is narrowed to pages too, so "Indexable" is the pages that can be indexed, not every image that answered.
 */
export function withIndexability(filters: readonly GridFilter[], values: readonly string[]): GridFilter[] {
  const others = filters.filter((filter) => !isOwn(filter));
  if (values.length === 0) return others;
  const own: GridFilter = { column: INDEXABILITY, op: 'in', values: [...values] };
  return namesType(others) ? [...others, own] : [...others, PAGE_TYPE, own];
}

/** Every reason a page isn't indexable, among the values the view has: all of them but "Indexable". */
export function notIndexableValues(values: readonly string[]): string[] {
  return values.filter((value) => value !== INDEXABLE && value !== 'null');
}

/** Whether the ticked values are exactly every reason there is: "Not indexable (any reason)" is what's chosen. */
export function isNotIndexable(ticked: readonly string[], values: readonly string[]): boolean {
  const reasons = notIndexableValues(values);
  return reasons.length > 0 && reasons.length === ticked.length && reasons.every((value) => ticked.includes(value));
}

// --------------------------------------------------------------------------------------- Titles, descriptions, H1s

export interface ContentFacet {
  id: 'title' | 'description' | 'h1';
  title: string;
  /** The checks whose pages it lists, with the word each is chosen by. */
  values: readonly { check: string; words: string }[];
}

/** What the checks already found about each page's title, description and main heading, as quick filters. */
export const CONTENT_FACETS: readonly ContentFacet[] = [
  {
    id: 'title',
    title: 'Title',
    values: [
      { check: 'title-missing', words: 'Missing' },
      { check: 'title-duplicate', words: 'Duplicate' },
      { check: 'title-too-long', words: 'Too long' },
      { check: 'title-too-short', words: 'Too short' },
      { check: 'title-multiple', words: 'More than one' },
    ],
  },
  {
    id: 'description',
    title: 'Description',
    values: [
      { check: 'description-missing', words: 'Missing' },
      { check: 'description-duplicate', words: 'Duplicate' },
      { check: 'description-too-long', words: 'Too long' },
      { check: 'description-too-short', words: 'Too short' },
      { check: 'description-multiple', words: 'More than one' },
    ],
  },
  {
    id: 'h1',
    title: 'H1',
    values: [
      { check: 'h1-missing', words: 'Missing' },
      { check: 'h1-multiple', words: 'Several' },
      { check: 'h1-duplicate', words: 'Duplicate' },
    ],
  },
];

export interface ContentFacetValue {
  key: string;
  words: string;
  count: number;
  filter: GridFilter;
  applied: boolean;
}

/** The pages a check flagged: the check's own filter, so the grid holds exactly its pages. */
export const checkFilter = (check: string): GridFilter => ({ column: 'check', op: 'in', values: [check] });

/**
 * A content facet's values with the analysis's counts: every check the analysis counted, a zero included (a title is
 * never missing: said, not hidden). Null until the counts are read.
 */
export function contentFacetValues(
  facet: ContentFacet,
  counts: readonly { checkId: string; count: number }[] | undefined,
  filters: readonly GridFilter[],
): ContentFacetValue[] | null {
  if (!counts) return null;
  const byCheck = new Map(counts.map((count) => [count.checkId, count.count]));
  return facet.values.map((value) => {
    const filter = checkFilter(value.check);
    return {
      key: value.check,
      words: value.words,
      count: byCheck.get(value.check) ?? 0,
      filter,
      applied: filters.some((other) => sameFilters([other], [filter])),
    };
  });
}
