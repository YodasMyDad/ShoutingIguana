// The grid queries (E3a in docs/v2-sidecar-api.md, "URLs", "Facets", "Lookup" and "Issue groups"): a POST with a JSON
// body to one fixed URL per grid. The shapes are the sidecar's. A wrong JSON type on a field whose type is fixed is
// `bad-request` (400); a filter's `value` and `values` are checked against the column, under `invalid-query` (422).
import type { RedirectChain, TargetEnd, UrlTarget } from './inspector';

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
  /** `isNot`: the other value, or unknown (round 5 review R1-2: "Exclude this value" keeps the rows it's unknown in). */
  | { column: string; op: 'is' | 'isNot'; value: boolean }
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
  'isNot',
  'between',
  'in',
  'notIn',
  'isEmpty',
  'isNotEmpty',
];

/**
 * A grid query. A null in `columns`, `filters`, `sort` or `values`, a field the operator doesn't take, a pattern that
 * doesn't compile or is too complex, and a cursor from another query or whose row has changed or gone, are
 * `invalid-query`. Arrays never hold nulls.
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

/** A query of one of the URL grids (`/urls`, a check's `/rows`, `/sitemaps/urls`): the grid query, and E6's `linkEnds`. */
export interface UrlGridQuery extends GridQuery {
  /**
   * E6: only the URLs at one end of the links a view of the links grid matches. `matching` under it alone is `total`
   * of `/links/ends` for the same links. Its errors are keyed under it (`linkEnds.end`, `linkEnds.filters[0].column`).
   * The links grid doesn't take it (`bad-request`), which is why it isn't on `GridQuery`.
   */
  linkEnds?: LinkEnds;
}

/** Which end of a link: `from` is the page it's on, `to` the URL it leads to. */
export type LinkEnd = 'from' | 'to';

/**
 * E6: the URLs at one end of the links a view of the links grid matches: the pages those links are on (`from`), or the
 * URLs they lead to (`to`). `filters` and `search` are the links grid's own; none of either is every link.
 */
export interface LinkEnds {
  end: LinkEnd;
  /** The links grid's filters (`kind`, `text`, `from.<column>`, `to.<column>`...). At most 20. */
  filters?: GridFilter[];
  /** The links grid's search: the link's text, or either end's URL, contains. */
  search?: string;
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
  /** The title's characters, as a reader counts them, white space run together. */
  titleLength?: number | null;
  descriptionLength?: number | null;
  /** The title's width in Google's desktop results, in pixels: what `title-too-long` and `title-too-short` judge. */
  titleWidth?: number | null;
  descriptionWidth?: number | null;
  /**
   * The title as Google's desktop results will likely show it: up to where it's cut, ending "…", or the whole title
   * when it fits. Worked out for the rows read: shown only.
   */
  titleInResults?: string | null;
  descriptionInResults?: string | null;
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
  /**
   * Evidence: the pages audited that refer to the URL by any kind of link (an image, a script, a canonical...), each
   * once: where a file is used, which `inlinks` (hyperlinks only) reads as 0. Needs the URL summary.
   */
  usedOn?: number | null;
  /**
   * Evidence: an image's own size in pixels, read from its file's header. Null for anything else, an SVG included.
   * Shown only: neither sorts nor filters (they're read for the rows of a page, as `redirectChain` is).
   */
  pixelWidth?: number | null;
  pixelHeight?: number | null;
  /**
   * Evidence: the error the crawl recorded for any outcome other than success (a timeout, a connection that failed,
   * robots.txt that couldn't be read); null for a success. Filters, doesn't sort.
   */
  fetchError?: string | null;
  /** Evidence: the URL's redirects hop by hop, as the inspector's `redirects`. Null for a URL that doesn't redirect. */
  redirectChain?: RedirectChain | null;
  /** Evidence: why robots.txt refused the URL. Null for a URL it didn't refuse. */
  robotsRule?: RobotsRule | null;
}

/**
 * Why robots.txt refused a URL, as the inspector's `robotsTxt` test says it: the rule and its line in the stored file
 * (`reason` `rule`), or that the file couldn't be read (`unreachable`), or neither is known (`unknown`).
 */
export interface RobotsRule {
  /** The origin whose robots.txt decides it; null where the crawl has none for it. */
  origin: string | null;
  reason: 'rule' | 'unreachable' | 'unknown';
  /** The file's line that refused it, from 1, for `rule`. */
  line: number | null;
  /** That line's text: "Disallow: /cart/". */
  rule: string | null;
}

/** A row of a check's grid (`/issues/{checkId}/rows`): the URL's columns, and the issue's own. */
export interface IssueRow extends UrlRow {
  detail?: string | null;
  relatedUrlId?: number | null;
  relatedUrl?: string | null;
  relatedStatusCode?: number | null;
}

/**
 * `object` (evidence) is a value that is a list or a record (`redirectChain`, `robotsRule`): shown only, never sorted
 * or filtered by.
 */
export type UrlColumnType = 'number' | 'text' | 'enum' | 'boolean' | 'object';

/** `GET /api/url-columns`: the columns, for the column chooser and the filter bar. */
export interface UrlColumn {
  id: string;
  type: UrlColumnType;
  /** Read from the URL summary: unavailable without a current analysis. */
  needsAnalysis: boolean;
  /**
   * False for columns read per response (`outcome`, `noindex`, `nofollow`, `h1Count`, `ttfbMs`,
   * `redirectTargetStatusCode`) and for text other than `url`. Go by this flag.
   */
  sortable: boolean;
  /** False for `metaRobots` and `xRobotsTag`, shown only. */
  filterable: boolean;
  /** An enum's values, in order; empty otherwise. */
  values: string[];
  /** A check's own columns (`detail`, `relatedUrl`...): only its rows have them. */
  issueRowsOnly: boolean;
  /** E5: the column's name in words ("Status", "Target status"), as a header shows it and an export writes it. */
  title?: string;
  /** E5: the unit of its numbers (`ms`, `bytes`), which an export's header carries after the title. */
  unit?: string | null;
  /** E5: an enum's values in words, by value (`clientError` is "Client error"); empty otherwise. */
  valueTitles?: Record<string, string>;
}

// ---------------------------------------------------------------------------------------------------------- Facets

export type FacetId =
  'statusClass' | 'statusCode' | 'indexability' | 'type' | 'depth' | 'responseTime' | 'inSitemap' | 'severity';

export interface FacetsQuery {
  filters?: GridFilter[];
  search?: string;
  /** Every facet when left out. */
  facets?: FacetId[];
  /** E6: only the URLs at one end of the links a links view matches, as the grid's `linkEnds`. */
  linkEnds?: LinkEnds;
}

/**
 * A value's count, and the filter that selects exactly those URLs. The `2000+` response-time bucket filters with `gte`,
 * not `between`. The severity buckets filter on `errors`, `warnings` and `notices` (`gte` 1), and on `issues` (`eq` 0)
 * for `none`.
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

/** A URL of the crawl, by its id: what the inspector links to. */
export interface UrlRef {
  id: number;
  url: string;
}

/** A URL found by its text (`POST /urls/lookup`). */
export interface UrlLookup extends UrlRef {
  /**
   * The URL the text stood for, normalised, when the crawl has it only on another version of the site (www or not, http
   * or https). Null when `url` is the one asked for.
   */
  requested: string | null;
}

// ---------------------------------------------------------------------------------------------------- Issue groups

/** How a check's issues group: `GET /api/checks` gives it as each check's `grouping`. */
export type CheckGrouping = 'detail' | 'relatedUrl' | 'none';

export interface IssueGroup {
  /** Null for a `relatedUrl` check's groups. */
  detail: string | null;
  relatedUrlId: number | null;
  relatedUrl: string | null;
  /** How many URLs the group holds. Kept for older callers: `urls` is the same number under a name true of every check. */
  pages: number;
  /** E6: how many URLs the group holds, pages or not: the name to read. Absent from a sidecar built before E6. */
  urls?: number;
  /** The group's part of the check's flagged URLs, 0 to 1, unrounded. */
  share: number;
  /** One fix probably clears it: it's in the site's template. */
  likelyTemplate: boolean;
  /**
   * Evidence: what the group's related URL is (its status, size, pixels, where its redirects end, why it isn't
   * indexable), for every group that has one; null for one without. The check's `targetFacts` say which to show.
   * Left out by a sidecar from before it.
   */
  related?: RelatedFacts | null;
  /**
   * Evidence: what a duplicate cluster's pages share (the title, the meta description, the H1), as its first page
   * writes it. Null for every other group.
   */
  sharedText?: string | null;
}

/** Evidence: what a group's related URL is: a target's facts less the page's link to it, which is each page's own. */
export interface RelatedFacts {
  statusCode: number | null;
  outcome: UrlOutcome | null;
  contentType: string | null;
  sizeBytes: number | null;
  pixelWidth: number | null;
  pixelHeight: number | null;
  /** Where its redirects end; null when it doesn't redirect. */
  final: TargetEnd | null;
  indexability: IndexabilityReason | null;
  /** The URL it's canonicalised to; null when its canonical doesn't name another. */
  canonical: UrlTarget | null;
}

/** How a check's groups are ordered: the most URLs first, or (evidence) the largest file first. */
export type IssueGroupSort = 'pages' | 'size';

/** `POST /issues/{checkId}/groups`: a page of the groups, in the order asked for (the check's own without `sort`). */
export interface IssueGroupsQuery {
  offset: number;
  limit: number;
  sort?: IssueGroupSort;
}

export interface IssueGroupPage {
  checkId: string;
  grouping: CheckGrouping;
  /** The check's flagged URLs; null for a skipped check, never 0. */
  total: number | null;
  /** Null for a skipped check, never 0. 0 for a check that doesn't group (`none`). */
  groupCount: number | null;
  offset: number;
  groups: IssueGroup[];
  /** Evidence: the order used: `size` for `image-too-large` unless asked otherwise. Left out by an earlier sidecar. */
  sort?: IssueGroupSort;
  /** Evidence: the check's `counts[].likelyTemplate`, for a check that doesn't group. */
  likelyTemplate?: boolean;
}

/**
 * The group an inspected issue is in (E3b): the inspector's `issues[].group`, the key to pass as a check's rows' `group`
 * or the check filter's. Null for a check that doesn't group. It's the group's key, not the issue's own detail and
 * related URL: a `relatedUrl` check's has no detail, and a fault's has the fault.
 */
export type IssueGroupOfUrl = CheckGroupRef | null;

// ------------------------------------------------------------------------------------------------------ Links (E3b)

// The links grid (docs/v2-sidecar-api.md, "Links"): every stored link a row. It takes `GridQuery` and answers with
// `GridPage<LinkRow>`, over its own columns: a link's own, and the URL grid's for either end as `from.<column>` and
// `to.<column>`. It sorts by one key: `id`, or a column of the target that the URL grid sorts by (`to.url`...).

export type LinkKind =
  | 'hyperlink'
  | 'image'
  | 'script'
  | 'stylesheet'
  | 'iframe'
  | 'media'
  | 'icon'
  | 'canonical'
  | 'hreflang'
  | 'pagination'
  | 'metaRefresh'
  | 'redirect';

export type LinkPosition = 'content' | 'navigation' | 'header' | 'footer' | 'sidebar' | 'head';

/**
 * A row of the links grid. Only `id` is always there; the rest are the columns asked for. A value is null when unknown
 * (a kind this engine has no name for, `followed` for anything but a hyperlink), never 0. Either end's URL grid columns
 * are keyed `from.<column>` and `to.<column>`, with the URL grid's own types (`UrlRow`).
 */
export interface LinkRow {
  /** The link's id: the order the crawl stored links in. */
  id: number;
  kind?: LinkKind | null;
  /** The anchor text of a hyperlink, the alt text of an image, or the language of an hreflang link. */
  text?: string | null;
  position?: LinkPosition | null;
  noFollow?: boolean;
  sponsored?: boolean;
  ugc?: boolean;
  /** A hyperlink around an image, whose text is the image's alt text. */
  imageLink?: boolean;
  /** The link leads to the page it's on. */
  selfLink?: boolean;
  /** Whether search engines follow a hyperlink: no nofollow on it, nor on its page. Null for anything else. */
  followed?: boolean | null;
  fragment?: string | null;
  'from.id'?: number;
  'from.url'?: string;
  'to.id'?: number;
  'to.url'?: string;
  /** Any other column of either end: `to.statusCode`, `from.depth`... as the URL grid types it. */
  [endColumn: `from.${string}` | `to.${string}`]: string | number | boolean | null | undefined;
}

/** `GET /api/link-columns`: a link's own columns, then each end's (`from.`, `to.`). Only `id` and the target's sort. */
export type LinkColumn = Omit<UrlColumn, 'issueRowsOnly'>;

export type LinkFacetId = 'kind' | 'position' | 'follow' | 'to.statusClass' | 'to.isInternal';

export interface LinkFacetsQuery {
  filters?: GridFilter[];
  /** The link's text, or either end's URL, contains. */
  search?: string;
  /** Every facet when left out. */
  facets?: LinkFacetId[];
}

/** The links a query matches, counted by value; each bucket's `filter` opens exactly its links. */
export interface LinkFacetsPage {
  matching: number;
  total: number;
  summary: SummaryState;
  /** Always empty: no link facet needs the URL summary. */
  unavailable: LinkFacetId[];
  facets: Partial<Record<LinkFacetId, FacetBucket[]>>;
}

/** E6, `POST /crawls/{id}/links/ends`: one page of the URLs at one end of the links a view matches. */
export interface LinkEndsQuery {
  end: LinkEnd;
  /** The links grid's filters; none is every link. */
  filters?: GridFilter[];
  /** The links grid's search. */
  search?: string;
  offset?: number;
  /** 1 to 1,000; 100 when left out. */
  limit?: number;
}

/** A URL at one end of a view's links, with how many of those links have it at that end. */
export interface LinkEndRow extends UrlRef {
  links: number;
}

/**
 * The ends of a links view, each URL once, in the order the crawl found the URLs: its targets, or the pages its links
 * are on. The targets are read from what the link summary counts; the pages take a pass over every link, once per set
 * of filters (inside the budget where the sidecar has read slots free, over it on one connection).
 */
export interface LinkEndsPage {
  end: LinkEnd;
  /** How many URLs there are at that end: the URL grid's `matching` under the same `linkEnds`. */
  total: number;
  /** The links the view matches: the links grid's `matching`. The rows' `links` add up to it. */
  links: number;
  offset: number;
  rows: LinkEndRow[];
  summary: SummaryState;
}

// --------------------------------------------------------------------------------------------------- Sitemaps (E3b)

/** How far the crawl got with its sitemaps: `unfinished` means the list isn't all there is. */
export type SitemapDiscovery = 'off' | 'running' | 'unfinished' | 'done';

/** How the crawl came by a sitemap. */
export type SitemapSource = 'robotsTxt' | 'options' | 'defaultLocation' | 'index';

/** `failed`: `error` says why. `notRead`: left unread at the crawler's limits. `pending`: found, not read yet. */
export type SitemapState = 'read' | 'failed' | 'notRead' | 'pending';

export interface Sitemap {
  id: number;
  /** The URL it was found at; where that redirects to isn't stored. */
  url: string;
  /** The sitemap index that lists it, or null. */
  parentId: number | null;
  /** Null when neither the stored robots.txt nor the crawl's options name it any more. */
  foundVia: SitemapSource | null;
  state: SitemapState;
  statusCode: number | null;
  isIndex: boolean;
  /** URL entries the crawl read in the file, repeats included. */
  entries: number;
  /** The sitemaps an index lists that the crawl has. */
  sitemaps: number;
  /**
   * E6: how many sitemaps the file itself lists, as the crawl read it (every `<sitemap>` entry, repeats included), so
   * an index says how many it names beside how many the crawl has; 0 for a sitemap that lists only URLs. Null when the
   * file's body was never read, and for a crawl made before E6. Absent from a sidecar built before E6.
   */
  listedSitemaps?: number | null;
  error: string | null;
  /**
   * The URLs it lists, each once (an index: those of every sitemap under it), as the URL grid's `fetched` and
   * `indexable` columns judge them: each count opens its rows in `/sitemaps/urls` under that filter.
   *
   * From the E3c review's engine on, two more, by the URL grid's `outcome`, each its rows in `/sitemaps/urls` under
   * the filter named (absent from a sidecar built before it):
   * - `robotsDisallowed`: those robots.txt kept the crawl from (`outcome` in `robotsDisallowed`);
   * - `redirectingOrBroken`: those that answered with a redirect or an error, or didn't answer (`outcome` in
   *   `redirect`, `clientError`, `serverError`, `timeout`, `networkError`). Not the ones robots.txt or a firewall kept
   *   the crawl from, nor those not checked.
   */
  urls: {
    listed: number;
    /** Every listed URL the crawl is done with: those it requested, and those robots.txt refused. */
    fetched: number;
    indexable: number;
    nonIndexable: number;
    /**
     * The listed URLs robots.txt refused (`outcome` `robotsDisallowed`), which `fetched` counts in (the E3c
     * review). Left out by a sidecar from before it: not known then, never 0.
     */
    robotsDisallowed?: number;
    /**
     * The listed URLs that answered with a redirect or an error, or didn't answer (`outcome` `redirect`,
     * `clientError`, `serverError`, `timeout` or `networkError`). Left out by a sidecar from before it.
     */
    redirectingOrBroken?: number;
  };
}

/** `GET /crawls/{id}/sitemaps`: every sitemap the crawl looked for, in the order it found them. */
export interface SitemapList {
  discovery: SitemapDiscovery;
  sitemaps: Sitemap[];
  /** Whether the URL counts are the analysis's judgement or, without a URL summary, the crawl's own. */
  summary: SummaryState;
}

// ------------------------------------------------------------------------------------------------- robots.txt (E3b)

/**
 * What an origin's answer meant to the crawl. `read`: its rules applied. `missing`: there were none, so everything was
 * allowed. `unreachable`: it couldn't be read (a 5xx, a 429, or no answer from the start URL's own origin), so the crawl
 * refused the origin's URLs until it could. `unanswered`: another origin than the start URL's gave no answer at all, so
 * the crawl gave no verdict and went ahead: its URLs show their own errors.
 */
export type RobotsState = 'read' | 'missing' | 'unreachable' | 'unanswered';

export interface RobotsOrigin {
  /** Scheme, host and port unless it's the scheme's default: `https://www.example.com`. */
  origin: string;
  isInternal: boolean;
  /** Null when the crawl kept no robots.txt for an origin it stored refused URLs on. */
  state: RobotsState | null;
  statusCode: number | null;
  /** When it was last read; a crawl continued later reads each again and keeps the last. */
  fetchedAt: string | null;
  /** How it failed, when there was no status. */
  error: string | null;
  /** The size of the text kept, or null when none was. */
  bytes: number | null;
  /** The origin's URLs the crawl stored as refused by robots.txt. */
  blocked: number;
}

/** E6, `POST /crawls/{id}/robots`: a page of origins, searched and filtered. Every field is optional. */
export interface RobotsOriginsQuery {
  offset?: number;
  /** 1 to 1,000; 100 when left out. */
  limit?: number;
  /** Text in the origin's name, case ignored. At most 1,000 characters. */
  search?: string;
  /** True for only the origins that blocked something, false for only those that blocked nothing. */
  blocked?: boolean;
}

/** `POST /crawls/{id}/robots`: the site's own origins first, then the rest by the URLs they blocked. */
export interface RobotsOriginPage {
  /** Every origin of the crawl, whatever is searched or filtered. */
  total: number;
  /** E6: the origins the search and the filter match, which the page is of. Absent from a sidecar built before E6. */
  matching?: number;
  /**
   * E6: what a filter bar shows, of the origins the `search` finds: all of them, those that blocked something, and
   * those that blocked nothing. Each is `matching` under that filter. Absent from a sidecar built before E6.
   */
  counts?: { all: number; blocked: number; notBlocked: number };
  offset: number;
  /** False for a crawl told to ignore robots.txt, which blocks nothing. */
  respected: boolean;
  /** The name the crawl looked for its group by. */
  userAgent: string;
  /** Every URL the crawl stored as refused by robots.txt. */
  blocked: number;
  origins: RobotsOrigin[];
}

export type RobotsLineKind =
  'userAgent' | 'allow' | 'disallow' | 'sitemap' | 'crawlDelay' | 'comment' | 'blank' | 'other' | 'invalid';

export type RobotsLineProblem = 'noUserAgent' | 'invalidValue' | 'unknownDirective' | 'notADirective';

export interface RobotsLine {
  /** From 1. */
  number: number;
  text: string;
  kind: RobotsLineKind;
  /** Whether it's part of the group the crawl obeyed; a sitemap line applies to everyone. Null for other kinds. */
  applies: boolean | null;
  /** On a disallow line that applies: how many of the origin's blocked URLs it refused. Null on every other line. */
  blocked: number | null;
  /** Why the crawler made nothing of the line, or null. */
  problem: RobotsLineProblem | null;
}

/** `POST /crawls/{id}/robots/file`: one origin's file, line by line; `lines` is empty unless `state` is `read`. */
export interface RobotsFile extends RobotsOrigin {
  /** E6: false for a crawl told to ignore robots.txt, which blocked nothing. Absent from a sidecar built before E6. */
  respected?: boolean;
  userAgent: string;
  lines: RobotsLine[];
  /** Blocked URLs no line accounts for: with the lines' `blocked`, they add up to the origin's. */
  unmatched: { unreachable: number; unknown: number };
}

/** `rule`: a line refused it. `unreachable`: robots.txt couldn't be read. `unknown`: the file kept doesn't refuse it. */
export type RobotsBlockReason = 'rule' | 'unreachable' | 'unknown';

export interface RobotsBlockedUrl extends UrlRef {
  reason: RobotsBlockReason;
  /** The line that refused it, and its text, for `rule`. */
  line: number | null;
  rule: string | null;
  /** How robots.txt failed, for `unreachable`, when stored. */
  error: string | null;
  // Step 3b: what blocking it costs, as the analysis counted it. Each is null while the crawl has changed since its
  // analysis (or was never analysed), and absent from a sidecar built before step 3b.
  /** The pages that link to it, a page's links to itself left out. */
  linkingPages?: number | null;
  /** A sitemap lists it; false only when every sitemap was read and none does. */
  inSitemap?: boolean | null;
  /** How many sitemaps list it, where one does. */
  sitemaps?: number | null;
  /** The first sitemap that lists it, by the id the Sitemaps screen opens. */
  sitemap?: { id: number; url: string } | null;
  /** A page's menu (a navigation hyperlink) links to it. */
  inMenu?: boolean | null;
  /** The site asks for it and blocks it: a sitemap lists it, or a menu links to it. */
  conflict?: boolean | null;
}

export interface RobotsBlockedQuery {
  origin: string;
  /** Only the URLs this line refused. */
  line?: number;
  reason?: RobotsBlockReason;
  /** E6: only this URL: its row and `matching` 1, or no rows and `matching` 0 for a URL the origin didn't block. */
  urlId?: number;
  /** Step 3b: only the URLs a sitemap lists or a menu links to (true), or only the others (false). */
  conflict?: boolean;
  offset?: number;
  /** 1 to 1,000. */
  limit?: number;
}

/** `POST /crawls/{id}/robots/blocked`: an origin's blocked URLs, in crawl order. */
export interface RobotsBlockedPage {
  origin: string;
  /** The origin's blocked URLs. */
  total: number;
  /** Those the line or reason narrows them to. */
  matching: number;
  offset: number;
  rows: RobotsBlockedUrl[];
  /**
   * Step 3b: the origin's blocked URLs that conflict (a sitemap lists them or a menu links to them), whatever is
   * narrowed to; null while the crawl has changed since its analysis. Absent from a sidecar built before step 3b.
   */
  conflicts?: number | null;
}

// ---------------------------------------------------------------------------------------------------- Search (E6)

/**
 * How well a search result matches, best first. `exact`: the text is the URL (with or without its scheme) or its path.
 * `start`: it starts a segment of the path, the host name, or a word of the title. `anywhere`: it's somewhere else.
 */
export type SearchRank = 'exact' | 'start' | 'anywhere';

/** One URL a search found: what a list of results shows, and its `id` to open it by. */
export interface SearchResult extends UrlRef {
  /** The page's first title; null without one. */
  title: string | null;
  isInternal: boolean;
  /** As the URL grid's columns have them, read as they now stand: null when not fetched (a status also with no answer). */
  statusCode: number | null;
  outcome: UrlOutcome | null;
  rank: SearchRank;
  /** Where the text was found: the match the rank is for, the URL's when both match as well. */
  matchedIn: 'url' | 'title';
}

/**
 * `POST /crawls/{id}/search`: search as it's typed, for the palette. The best few of the crawl's URLs whose URL or title
 * contains the text, the page a person means first. A keystroke takes a few milliseconds once a crawl's text is read,
 * which its first search does (tens of milliseconds): ask an empty text as the palette opens, and the first letter
 * typed is answered as fast as the rest.
 */
export interface SearchResults {
  /** Empty for an empty text. */
  results: SearchResult[];
  /** The URL grid's `matching` under `{ search: text }`; equal to `total` for an empty text. */
  matching: number;
  /** Every URL in the crawl. */
  total: number;
}

/** Rows a page of the grids asks for: two screenfuls of 1M-row scrolling per request, as the spike measured. */
export const GRID_PAGE_SIZE = 200;
