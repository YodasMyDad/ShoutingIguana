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

/** The site's own URLs that are pages, by their content type or, without one, their extension. */
export const SITE_PAGES: readonly GridFilter[] = [
  { column: 'isInternal', op: 'is', value: true },
  { column: 'type', op: 'in', values: ['page'] },
];

/** The site's page URLs the crawl asked for: what indexability is judged on, errors and redirects included. */
export const CRAWLED_SITE_PAGES: readonly GridFilter[] = [...SITE_PAGES, { column: 'fetched', op: 'is', value: true }];

/** The site's pages that answered 2xx: what depth and response times are about. */
export const ANSWERED_SITE_PAGES: readonly GridFilter[] = [
  ...SITE_PAGES,
  { column: 'statusClass', op: 'in', values: ['2xx'] },
];
