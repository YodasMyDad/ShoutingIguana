// The grid queries (E3a in docs/v2-sidecar-api.md, "URLs"): a POST with a JSON body to one fixed URL per grid. The
// engine doesn't serve them yet; the mock transport does, to this shape, so the grids are built against the contract.

export interface GridSort {
  column: string;
  desc: boolean;
}

/** A check's group (E3a, Issue groups), to open one group's URLs. */
export interface CheckGroupRef {
  detail: string;
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

export interface GridQuery {
  /** What each row carries; `id` always comes first. */
  columns?: string[];
  filters: GridFilter[];
  /** URL or title contains, ignoring the case of ASCII letters. */
  search?: string;
  /** Up to 3; crawl order breaks ties. */
  sort: GridSort[];
  /** Give `offset` or `after`, not both. */
  offset?: number;
  /** A page's `next` cursor, to read on from there (exports). */
  after?: string;
  /** 1 to 1,000. */
  limit: number;
}

export type SummaryReason = 'crawling' | 'notAnalyzed' | 'engineChanged' | 'crawlChanged';

export interface GridPage<Row> {
  columns: string[];
  rows: Row[];
  offset: number;
  /** Rows the filters and search match: the "3" of "3 of 6,286 URLs". */
  matching: number;
  /** Every URL in the crawl: the "6,286". */
  total: number;
  /** The cursor for the row after the last, or null at the end. */
  next: string | null;
  /** Whether the URL summary describes the crawl; without it, the analysis's columns have no values. */
  summary: { available: boolean; reason: SummaryReason | null };
  /** Columns asked for that have no values now: left out of every row. */
  unavailable: string[];
}

export type StatusClass = '2xx' | '3xx' | '4xx' | '5xx' | 'other' | 'noResponse';

/** A row of the URL grid. It carries only the columns asked for, so every column but `id` is optional. */
export interface UrlRow {
  id: number;
  url?: string;
  fetched?: boolean | null;
  isInternal?: boolean | null;
  source?: 'seed' | 'link' | 'sitemap' | 'siteVersion' | null;
  depth?: number | null;
  type?: string | null;
  statusCode?: number | null;
  statusClass?: StatusClass | null;
  outcome?: string | null;
  redirectTarget?: string | null;
  redirectTargetStatusCode?: number | null;
  indexable?: boolean | null;
  indexability?: string | null;
  noindex?: boolean | null;
  nofollow?: boolean | null;
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
  issues?: number | null;
  errors?: number | null;
  warnings?: number | null;
  notices?: number | null;
}

/** Rows a page of the grids asks for: two screenfuls of 1M-row scrolling per request, as the spike measured. */
export const GRID_PAGE_SIZE = 200;
