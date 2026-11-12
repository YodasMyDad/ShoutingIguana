// Where other screens land in the URL grid: the route's filters for a set of URLs, so a link elsewhere opens exactly
// those URLs here.
import type { GridFilter } from '../../api/grids';

/** The URL grid narrowed to one URL, by its text: a chip reads it as "URL is /about/". */
export function oneUrlFilter(url: string): GridFilter[] {
  return [{ column: 'url', op: 'eq', value: url }];
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
