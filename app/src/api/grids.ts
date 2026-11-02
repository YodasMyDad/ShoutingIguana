// The grid queries (E3a in docs/v2-sidecar-api.md, "URLs", "Facets", "Lookup" and "Issue groups"): a POST with a JSON
// body to one fixed URL per grid. The shapes are the sidecar's at 46a2703; where its code and the contract's text
// differ, the code is what's typed here, and the difference is noted.

export interface GridSort {
  column: string;
  /** False when left out. */
  desc: boolean;
}

/**
 * A check's group (E3a, Issue groups), to open one group's URLs. Null matches null: a `relatedUrl` check's groups have
 * no detail, a `detail` check's groups may have no related URL.
 */
export interface CheckGroupRef {
  detail: string | null;
  relatedUrlId: number | null;
}

/** Each filter's operator decides what else it carries. Every filter must match. */
export type GridFilter =
  | {
      column: string;
      op:
        | 'eq'
        | 'ne'
        | 'lt'
        | 'lte'
        | 'gt'
        | 'gte'
        | 'contains'
        | 'notContains'
        | 'startsWith'
        | 'endsWith'
        | 'regex'
        | 'notRegex';
      value: string | number;
    }
  | { column: string; op: 'is'; value: boolean }
  | { column: string; op: 'between'; min?: number; max?: number }
  | { column: string; op: 'in' | 'notIn'; values: (string | number)[]; group?: CheckGroupRef }
  | { column: string; op: 'isEmpty' | 'isNotEmpty' };

export type GridFilterOp = GridFilter['op'];

export const GRID_FILTER_OPS: readonly GridFilterOp[] = [
  'eq',
  'ne',
  'lt',
  'lte',
  'gt',
  'gte',
  'contains',
  'notContains',
  'startsWith',
  'endsWith',
  'regex',
  'notRegex',
  'is',
  'between',
  'in',
  'notIn',
  'isEmpty',
  'isNotEmpty',
];

/**
 * A grid query. The sidecar answers a wrong JSON type anywhere but a filter's `value` or `values` with `bad-request`
 * (400), not `invalid-query`: the contract's text says 422 for both (the code at 46a2703 wins). Arrays never hold
 * nulls.
 */
export interface GridQuery {
  /** What each row carries; `id` always comes first. The sidecar's default is `["url"]`. */
  columns?: string[];
  /** At most 20. */
  filters: GridFilter[];
  /** URL or title contains, ignoring the case of ASCII letters. At most 1,000 characters. */
  search?: string;
  /** Up to 3, each column once, sortable columns only; crawl order breaks ties. */
  sort: GridSort[];
  /** Give `offset` or `after`; an `offset` above 0 with `after` is refused. */
  offset?: number;
  /** A page's `next` cursor, to read on from there (exports). Tied to the filters, search and sort. */
  after?: string;
  /** 1 to 1,000. */
  limit: number;
  /** A check's rows only (`/issues/{checkId}/rows`): one of its groups. `/urls` refuses it with `bad-request`. */
  group?: CheckGroupRef;
}

/** Why the URL summary isn't there to read (`summary.reason`, and `needs-analysis`'s `reason`). */
export type SummaryReason = 'crawling' | 'notAnalyzed' | 'engineChanged' | 'crawlChanged';

/** Whether the URL summary describes the crawl: without it, the analysis's columns have no values. */
export interface SummaryState {
  available: boolean;
  /** Null while it's available. */
  reason: SummaryReason | null;
}

export interface GridPage<Row> {
  /** What each row carries, `id` first: the columns asked for, less the unavailable ones. */
  columns: string[];
  rows: Row[];
  /** Null when the page was read on from a cursor (`after`). */
  offset: number | null;
  /** Rows the filters and search match: the "3" of "3 of 6,286 URLs". Null only for a skipped check's rows. */
  matching: number | null;
  /** Every URL in the crawl (a check's flagged URLs for its rows): the "6,286". Null for a skipped check's rows. */
  total: number | null;
  /** The cursor for the row after the last, or null at the end. */
  next: string | null;
  summary: SummaryState;
  /** Columns asked for that have no values now: left out of `columns` and of every row (not sent as null). */
  unavailable: string[];
}

export type StatusClass = '2xx' | '3xx' | '4xx' | '5xx' | 'other' | 'noResponse';
export type UrlSource = 'seed' | 'link' | 'sitemap' | 'siteVersion';
export type UrlType = 'page' | 'image' | 'script' | 'stylesheet' | 'font' | 'media' | 'pdf' | 'other';
export type UrlOutcome =
  | 'success'
  | 'redirect'
  | 'clientError'
  | 'serverError'
  | 'blocked'
  | 'robotsDisallowed'
  | 'timeout'
  | 'networkError'
  | 'processingError'
  | 'notChecked';
/** Why a URL isn't indexable; `none` when it is. */
export type IndexabilityReason =
  | 'none'
  | 'noindex'
  | 'canonicalised'
  | 'redirected'
  | 'clientError'
  | 'serverError'
  | 'blockedByRobots'
  | 'blocked'
  | 'unreachable';

/**
 * A row of the URL grid. It carries only the columns asked for and available, so every column but `id` is optional; a
 * value the crawl doesn't know is null, never 0.
 */
export interface UrlRow {
  id: number;
  url?: string;
  fetched?: boolean | null;
  isInternal?: boolean | null;
  source?: UrlSource | null;
  depth?: number | null;
  type?: UrlType | null;
  statusCode?: number | null;
  statusClass?: StatusClass | null;
  outcome?: UrlOutcome | null;
  redirectTarget?: string | null;
  redirectTargetStatusCode?: number | null;
  indexable?: boolean | null;
  indexability?: IndexabilityReason | null;
  noindex?: boolean | null;
  nofollow?: boolean | null;
  metaRobots?: string | null;
  xRobotsTag?: string | null;
  canonicalUrl?: string | null;
  contentType?: string | null;
  title?: string | null;
  description?: string | null;
  h1?: string | null;
  h1Count?: number | null;
  wordCount?: number | null;
  outlinks?: number | null;
  sizeBytes?: number | null;
  responseMs?: number | null;
  ttfbMs?: number | null;
  titleLength?: number | null;
  descriptionLength?: number | null;
  finalUrl?: string | null;
  finalStatusCode?: number | null;
  inSitemap?: boolean | null;
  inlinks?: number | null;
  followedInlinks?: number | null;
  nofollowInlinks?: number | null;
  linkingPages?: number | null;
  issues?: number | null;
  errors?: number | null;
  warnings?: number | null;
  notices?: number | null;
}

/** A row of a check's grid (`/issues/{checkId}/rows`): the URL's columns, and the issue's own. */
export interface IssueRow extends UrlRow {
  detail?: string | null;
  relatedUrlId?: number | null;
  relatedUrl?: string | null;
  relatedStatusCode?: number | null;
}

export type UrlColumnType = 'number' | 'text' | 'enum' | 'boolean';

/** `GET /api/url-columns`: the columns, for the column chooser and the filter bar. */
export interface UrlColumn {
  id: string;
  type: UrlColumnType;
  /** Read from the URL summary: unavailable without a current analysis. */
  needsAnalysis: boolean;
  /**
   * The contract says every number, enum and boolean column sorts; at 46a2703 `outcome`, `noindex`, `nofollow`,
   * `h1Count` and `ttfbMs` don't. Go by this flag.
   */
  sortable: boolean;
  /** False for `metaRobots` and `xRobotsTag`, shown only. */
  filterable: boolean;
  /** An enum's values, in order; empty otherwise. */
  values: string[];
  /** A check's own columns (`detail`, `relatedUrl`...): only its rows have them. */
  issueRowsOnly: boolean;
}

// ---------------------------------------------------------------------------------------------------------- Facets

export type FacetId =
  'statusClass' | 'statusCode' | 'indexability' | 'type' | 'depth' | 'responseTime' | 'inSitemap' | 'severity';

export interface FacetsQuery {
  filters?: GridFilter[];
  search?: string;
  /** Every facet when left out. */
  facets?: FacetId[];
}

/**
 * A value's count, and the filter that selects exactly those URLs. At 46a2703 the `2000+` response-time bucket filters
 * with `gte`, not `between`, and the severity buckets filter on `errors`, `warnings` and `notices` (`gte` 1) and on
 * `issues` (`eq` 0) for `none`.
 */
export interface FacetBucket {
  value: string | number | boolean | null;
  count: number;
  filter: GridFilter;
  /** A response-time bucket's bounds; the last has no `max`. */
  min?: number;
  max?: number;
}

export interface FacetsPage {
  matching: number;
  total: number;
  summary: SummaryState;
  /** Facets that need the URL summary, when it isn't there (`inSitemap`, `severity`): left out of `facets`. */
  unavailable: FacetId[];
  facets: Partial<Record<FacetId, FacetBucket[]>>;
}

/** A URL of the crawl, by its id: what lookup gives, and what the inspector links to. */
export interface UrlRef {
  id: number;
  url: string;
}

// ---------------------------------------------------------------------------------------------------- Issue groups

/** How a check's issues group: `GET /api/checks` gives it as each check's `grouping`. */
export type CheckGrouping = 'detail' | 'relatedUrl' | 'none';

export interface IssueGroup {
  /** Null for a `relatedUrl` check's groups. */
  detail: string | null;
  relatedUrlId: number | null;
  relatedUrl: string | null;
  pages: number;
  /** The group's part of the check's flagged URLs, 0 to 1, unrounded. */
  share: number;
  /** One fix probably clears it: it's in the site's template. */
  likelyTemplate: boolean;
}

export interface IssueGroupPage {
  checkId: string;
  grouping: CheckGrouping;
  /** The check's flagged URLs; null for a skipped check, never 0. */
  total: number | null;
  /** 0 for a `none` check and a skipped one. */
  groupCount: number;
  offset: number;
  groups: IssueGroup[];
}

/** Rows a page of the grids asks for: two screenfuls of 1M-row scrolling per request, as the spike measured. */
export const GRID_PAGE_SIZE = 200;
