// Where other screens land in the URL grid: the route's filters for a set of URLs, so a link elsewhere opens exactly
// those URLs here.
import type { GridFilter } from '../../api/grids';

/** The longest text a filter's value holds in the route (routing/search.ts): a longer URL would be cut, and match none. */
const MAX_FILTER_TEXT = 1_000;

/**
 * The URL grid narrowed to one URL: by its text, which a chip reads as "URL is /about/", or, for a URL longer than a
 * filter's value can be, by its id, which the chip reads as the URL too.
 */
export function oneUrlFilter(url: string, id: number): GridFilter[] {
  return url.length > MAX_FILTER_TEXT
    ? [{ column: 'id', op: 'eq', value: id }]
    : [{ column: 'url', op: 'eq', value: url }];
}

/** The URL grid with no URL at all: while the crawl runs, none found yet; once it has ended, none found. */
export function noUrlsWords(crawling: boolean): { title: string; why: string } {
  return crawling
    ? { title: 'No URLs yet', why: 'They arrive as the crawl finds them.' }
    : { title: 'No URLs', why: 'The crawl found none.' };
}

/** What nothing matched: the search, the filters, or both. */
export function noUrlsMatchWords(search: string | undefined, filtered: boolean): string {
  if (search && filtered) return `No URLs match “${search}” with these filters.`;
  if (search) return `No URLs match “${search}”.`;
  return 'No URLs match these filters.';
}
