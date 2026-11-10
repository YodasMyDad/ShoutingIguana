// Where Overview's figures lead (docs/v2-ui-brief.md, "Every number leads to its URLs"): the URL grid filtered to
// exactly what a figure counts, or Issues at a check, a severity or a category. Plain data, so every figure's link is
// unit-tested with its number.
import type { GridFilter } from '../../api/grids';
import type { Severity } from '../../api/types';

/** The URL grid with these filters: the grid then shows exactly the URLs the figure counts. */
export interface UrlsLink {
  to: 'urls';
  filters: GridFilter[];
}

/** Issues: at one check, or listing one severity's checks, one category's (by its id), or the checks a text matches. */
export interface IssuesLink {
  to: 'issues';
  check?: string;
  severity?: Severity;
  category?: string;
  q?: string;
}

export type OverviewLink = UrlsLink | IssuesLink;

export const urlsLink = (filters: readonly GridFilter[]): UrlsLink => ({ to: 'urls', filters: [...filters] });

/** Everything a firewall or bot challenge answered counts as blocked, whatever its status code. */
export const NOT_BLOCKED: GridFilter = { column: 'outcome', op: 'notIn', values: ['blocked'] };
export const BLOCKED: GridFilter = { column: 'outcome', op: 'in', values: ['blocked'] };

/**
 * URLs the crawl never asked, their host having stopped answering: unknown, not broken. The grid has them as not
 * fetched, with no status class, so no class of the breakdown counts them.
 */
export const NOT_CHECKED: GridFilter = { column: 'outcome', op: 'in', values: ['notChecked'] };
/** URLs with no outcome at all: only found (not followed, past a limit, still queued), never asked or looked at. */
export const NO_OUTCOME: GridFilter = { column: 'outcome', op: 'isEmpty' };

/**
 * The site's own URLs that are pages, by their content type or, without one, their extension. Not the crawler's own
 * probes of the site's other versions (http or https, www or not), which nothing on the site leads to: they're on the
 * site and look like pages, but the engine leaves them out of every count of the site's pages (`/stats`'s
 * `nonIndexableReasons`, the checks), and that the https version of an http site doesn't answer says nothing of its
 * pages.
 */
export const SITE_PAGES: readonly GridFilter[] = [
  { column: 'isInternal', op: 'is', value: true },
  { column: 'type', op: 'in', values: ['page'] },
  { column: 'source', op: 'notIn', values: ['siteVersion'] },
];

/** The site's page URLs the crawl asked for: what indexability is judged on, errors and redirects included. */
export const CRAWLED_SITE_PAGES: readonly GridFilter[] = [...SITE_PAGES, { column: 'fetched', op: 'is', value: true }];

/**
 * The site's pages: its URLs that answered 2xx with HTML the crawl read. What depth and response times are about, and
 * what "pages" counts everywhere (the library's `pageCount`, a running crawl's `htmlPages`), by the engine's own
 * definition, which goes by the word count: a URL whose status alone was checked (an iframe's target) has none, and
 * isn't a page though it answered 2xx with HTML.
 */
export const ANSWERED_SITE_PAGES: readonly GridFilter[] = [
  ...SITE_PAGES,
  { column: 'statusClass', op: 'in', values: ['2xx'] },
  { column: 'wordCount', op: 'isNotEmpty' },
];
