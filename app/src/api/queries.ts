// Query keys and options for every read, so each screen asks for the same data the same way and the live layer knows
// what to refresh. Freshness comes from events (`library`, `crawl`), not polling: reads stay fresh until told.
import { mutationOptions, queryOptions, type QueryFilters } from '@tanstack/react-query';
import { api } from './client';
import type { FixedQuery, RecheckAsk, RecheckRowsQuery } from './fixLoop';
import { loadFoundOn, loadIssueTargets } from './whereFoundBatch';
import type {
  CheckGroupRef,
  FacetsQuery,
  GridFilter,
  GridPage,
  GridQuery,
  GridSort,
  IssueGroup,
  IssueGroupPage,
  IssueRow,
  RobotsBlockedPage,
  RobotsBlockedQuery,
  RobotsBlockedUrl,
  RobotsOrigin,
  RobotsOriginPage,
  LinkFacetsQuery,
  UrlRow,
} from './grids';
import type { LinkKind, LinkPage, LinkRow } from './inspector';

export const queryKeys = {
  health: ['health'] as const,
  checks: ['checks'] as const,
  crawlOptionDefaults: ['crawl-options', 'defaults'] as const,
  /** The library list. */
  library: ['library'] as const,
  /** Everything about one crawl: its item and what's read from its file. Invalidate this prefix when it changes. */
  crawl: (id: string) => ['crawl', id] as const,
  crawlItem: (id: string) => ['crawl', id, 'item'] as const,
  analysis: (id: string) => ['crawl', id, 'analysis'] as const,
  stats: (id: string) => ['crawl', id, 'stats'] as const,
  /** A Shopify store's crawler access: under the crawl, so an analysis after a recheck reads it afresh. */
  shopifyAccess: (id: string) => ['crawl', id, 'shopify'] as const,
  crawlOptions: (id: string) => ['crawl', id, 'options'] as const,
  alerts: (id: string) => ['crawl', id, 'alerts'] as const,
  history: (id: string) => ['crawl', id, 'history'] as const,
  /** Counts by value, for Overview's breakdowns: under the crawl, so whatever changes it refreshes them. */
  urlFacets: (id: string, query: FacetsQuery) => ['crawl', id, 'facets', query] as const,
  /** The URL the crawl started from, as the crawl found it. */
  startUrlRow: (id: string) => ['crawl', id, 'start-url'] as const,
  issues: (id: string, checkId: string, offset: number, limit: number) =>
    ['crawl', id, 'issues', checkId, offset, limit] as const,
  /** A check's groups, as grid pages (`usePagedRows` adds the page size and number). */
  issueGroupsGrid: (id: string, checkId: string) => ['grid', 'issueGroups', id, checkId] as const,
  /** A check's rows, sorted, as grid pages: the URLs of a check that doesn't group. */
  issueRowsGrid: (id: string, checkId: string, sort: readonly GridSort[]) =>
    ['grid', 'issueRows', id, checkId, { sort }] as const,
  /** The first pages of one of a check's groups, shown under it when it's open, with the columns they carry. */
  issueGroupRows: (id: string, checkId: string, group: CheckGroupRef, limit: number, columns: readonly string[]) =>
    ['crawl', id, 'issues', checkId, 'group', group, limit, columns] as const,
  /** Where one of a check's groups is among its groups, to open it from the route. */
  issueGroupPlace: (id: string, checkId: string, group: CheckGroupRef) =>
    ['crawl', id, 'issues', checkId, 'place', group] as const,
  /**
   * Where a page of URLs is found (their links, pages and sitemaps), and what a page of a check's flagged pages names:
   * under the crawl's prefix, so a crawl continued or analysed again reads them afresh.
   */
  foundOn: (id: string, urlId: number, limit: number) => ['crawl', id, 'found-on', urlId, limit] as const,
  issueTargets: (id: string, checkId: string, urlId: number, limit: number) =>
    ['crawl', id, 'issues', checkId, 'targets', urlId, limit] as const,
  /** One URL in full, for the inspector. Under the crawl's prefix, so whatever changes the crawl refreshes it. */
  urlDetail: (id: string, urlId: number) => ['crawl', id, 'inspect', urlId] as const,
  /** Every URL the inspector has read of one crawl: what a running crawl may change. */
  urlDetails: (id: string) => ['crawl', id, 'inspect'] as const,
  /** A URL's stored HTML. */
  urlSource: (id: string, urlId: number) => ['crawl', id, 'source', urlId] as const,
  /** A URL's inlinks or outlinks, as grid pages: a running crawl adds to them, so they refresh live. */
  urlLinksGrid: (direction: 'inlinks' | 'outlinks', id: string, urlId: number, kinds: readonly LinkKind[]) =>
    ['grid', direction, id, { urlId, kinds }] as const,
  /**
   * Grid pages live under their own prefix, so the page cache can be capped (see queryClient.ts). Every grid's keys
   * are `['grid', <grid>, <crawl id>, <query>, ...]`, so a crawl's pages can be found (gridPagesOf).
   */
  gridPage: (grid: string, id: string, query: Omit<GridQuery, 'offset' | 'limit'>, page: number) =>
    ['grid', grid, id, query, page] as const,
  gridPrefix: ['grid'] as const,
  // ---- Links (E3b)
  linkColumns: ['link-columns'] as const,
  /** The links grid's pages for one view of it: its columns, filters, search and sort. */
  linksGrid: (
    id: string,
    view: { columns: readonly string[]; filters: readonly GridFilter[]; q: string; sort: readonly GridSort[] },
  ) => ['grid', 'links', id, view] as const,
  /**
   * The links counted by value under a view's filters and search. Among the grids' keys, so a running crawl's
   * refresh moves the counts with the rows, and whatever changes the crawl refreshes both.
   */
  linkFacets: (id: string, query: LinkFacetsQuery) => ['grid', 'linkFacets', id, query] as const,
  /** Step 3b: a view of the links grouped by target, as grid pages. Among the grids' keys, so a running crawl refreshes it. */
  linkGroupsGrid: (id: string, view: { filters: readonly GridFilter[]; q: string; sort: readonly GridSort[] }) =>
    ['grid', 'linkGroups', id, view] as const,
  /** Step 3b: the images a page shows, each file once, the largest first: the inspector's Images. */
  urlImages: (id: string, urlId: number) => ['crawl', id, 'urlImages', urlId] as const,
  /**
   * The URL grid's facets, among the grids' reads: while a facet menu is open a running crawl's refresh keeps its
   * counts fresh, and with every menu closed (the query disabled) it only marks them stale.
   */
  urlGridFacets: (id: string, query: FacetsQuery) => ['grid', 'urlFacets', id, query] as const,
  // ---- The fix loop. Under the crawl's prefix, so whatever changes the crawl (a recheck, an analysis) reads them again.
  /** The crawl compared with its previous crawl (`previous`) or with another crawl of its site by id. */
  changes: (id: string, withId = 'previous') => ['crawl', id, 'changes', withId] as const,
  /** What a check flagged in the other crawl and doesn't flag now: a page of rows, or of causes. */
  fixed: (id: string, checkId: string, query: FixedQuery) => ['crawl', id, 'fixed', checkId, query] as const,
  /** What a recheck would do now, and whether it can run. */
  recheckPlan: (id: string, ask: RecheckAsk) => ['crawl', id, 'recheck-plan', ask] as const,
  /** The crawl's rechecks, newest first, and one of them. */
  rechecks: (id: string) => ['crawl', id, 'rechecks'] as const,
  recheck: (id: string, recheckId: number) => ['crawl', id, 'rechecks', recheckId] as const,
  recheckRows: (id: string, recheckId: number, query: RecheckRowsQuery) =>
    ['crawl', id, 'rechecks', recheckId, 'rows', query] as const,
};

/** The reads of a crawl's comparison: what an analysis of the other crawl (the one compared with) makes stale. */
export const COMPARISON_READS: readonly string[] = ['changes', 'fixed'];

/** Every crawl's comparison reads (`/changes`, `/fixed`): any crawl analysed again can be another's previous. */
export function comparisonReads(): QueryFilters {
  return {
    queryKey: ['crawl'],
    predicate: (query) => COMPARISON_READS.includes(String(query.queryKey[2])),
  };
}

/** Every page of every grid of one crawl: what a change to the crawl makes stale. */
export function gridPagesOf(crawlId: string): QueryFilters {
  return { queryKey: queryKeys.gridPrefix, predicate: (query) => query.queryKey[2] === crawlId };
}

/**
 * The grids that read the last analysis (a check's groups and rows): a running crawl doesn't change them, so the live
 * refresh leaves them be; the analysis that follows the crawl refreshes them.
 */
export const ANALYSIS_GRIDS: readonly string[] = ['issueGroups', 'issueRows'];

/**
 * The reads a view refreshes itself while a crawl runs, so the once-a-second refresh leaves them be. One origin's
 * robots.txt is read whole (a file of 512 KB is over 2 MB as lines), and changes only when the origin's answer or
 * what it blocked does: its view reads it again then (screens/robots/RobotsFileView.tsx), not every second.
 */
export const SELF_REFRESHED_GRIDS: readonly string[] = ['robotsFile'];

/** The pages of one crawl's grids that a running crawl changes: every grid but the analysis's and the above. */
export function liveGridPagesOf(crawlId: string): QueryFilters {
  return {
    queryKey: queryKeys.gridPrefix,
    predicate: (query) =>
      query.queryKey[2] === crawlId &&
      !ANALYSIS_GRIDS.includes(String(query.queryKey[1])) &&
      !SELF_REFRESHED_GRIDS.includes(String(query.queryKey[1])),
  };
}

export const healthQuery = () =>
  queryOptions({ queryKey: queryKeys.health, queryFn: ({ signal }) => api.health(signal), staleTime: Infinity });

/** The catalog never changes while the sidecar runs. */
export const checksQuery = () =>
  queryOptions({ queryKey: queryKeys.checks, queryFn: ({ signal }) => api.checks(signal), staleTime: Infinity });

export const crawlOptionDefaultsQuery = () =>
  queryOptions({
    queryKey: queryKeys.crawlOptionDefaults,
    queryFn: ({ signal }) => api.crawlOptionDefaults(signal),
    staleTime: Infinity,
  });

export const libraryQuery = () =>
  queryOptions({ queryKey: queryKeys.library, queryFn: ({ signal }) => api.crawls(signal), staleTime: Infinity });

export const crawlQuery = (id: string) =>
  queryOptions({
    queryKey: queryKeys.crawlItem(id),
    queryFn: ({ signal }) => api.crawl(id, signal),
    staleTime: Infinity,
  });

export const analysisQuery = (id: string) =>
  queryOptions({
    queryKey: queryKeys.analysis(id),
    queryFn: ({ signal }) => api.analysis(id, signal),
    staleTime: Infinity,
  });

export const shopifyAccessQuery = (id: string) =>
  queryOptions({
    queryKey: queryKeys.shopifyAccess(id),
    queryFn: ({ signal }) => api.shopifyAccess(id, signal),
    staleTime: Infinity,
  });

export const statsQuery = (id: string) =>
  queryOptions({ queryKey: queryKeys.stats(id), queryFn: ({ signal }) => api.stats(id, signal), staleTime: Infinity });

export const crawlOptionsQuery = (id: string) =>
  queryOptions({
    queryKey: queryKeys.crawlOptions(id),
    queryFn: ({ signal }) => api.crawlOptions(id, signal),
    staleTime: Infinity,
  });

export const alertsQuery = (id: string) =>
  queryOptions({
    queryKey: queryKeys.alerts(id),
    queryFn: ({ signal }) => api.alerts(id, signal),
    staleTime: Infinity,
  });

/** E4: the crawl's progress since it started. A crawl running here carries it on from its progress events. */
export const historyQuery = (id: string) =>
  queryOptions({
    queryKey: queryKeys.history(id),
    queryFn: ({ signal }) => api.history(id, signal),
    staleTime: Infinity,
  });

/** The crawl's URLs counted by value (docs/v2-sidecar-api.md, Facets), each count with the filter that opens it. */
export const urlFacetsQuery = (id: string, query: FacetsQuery) =>
  queryOptions({
    queryKey: queryKeys.urlFacets(id, query),
    queryFn: ({ signal }) => api.urlFacets(id, query, signal),
    staleTime: Infinity,
  });

/** The URL grid's facets, read while one of its menus is open (`queryKeys.urlGridFacets`). */
export const urlGridFacetsQuery = (id: string, query: FacetsQuery) =>
  queryOptions({
    queryKey: queryKeys.urlGridFacets(id, query),
    queryFn: ({ signal }) => api.urlFacets(id, query, signal),
    staleTime: Infinity,
  });

// ---- Links (E3b)

/** The links grid's columns never change while the sidecar runs. */
export const linkColumnsQuery = () =>
  queryOptions({
    queryKey: queryKeys.linkColumns,
    queryFn: ({ signal }) => api.linkColumns(signal),
    staleTime: Infinity,
  });

/** The links a view matches, counted by value (docs/v2-sidecar-api.md, Link facets): each count with its filter. */
export const linkFacetsQuery = (id: string, query: LinkFacetsQuery) =>
  queryOptions({
    queryKey: queryKeys.linkFacets(id, query),
    queryFn: ({ signal }) => api.linkFacets(id, query, signal),
    staleTime: Infinity,
  });

/** What the start URL asked for: its row columns. */
export const START_URL_COLUMNS = ['url', 'statusCode', 'outcome', 'redirectTarget', 'contentType'] as const;

/**
 * The URL the crawl started from, with how it answered, or null when the crawl has none: what a crawl that found
 * nothing has to say why (the start URL answered 403, redirected away, wasn't a page).
 */
export const startUrlQuery = (id: string) =>
  queryOptions({
    queryKey: queryKeys.startUrlRow(id),
    queryFn: async ({ signal }): Promise<UrlRow | null> => {
      const page = await api.urls(
        id,
        {
          columns: [...START_URL_COLUMNS],
          filters: [{ column: 'source', op: 'in', values: ['seed'] }],
          sort: [],
          offset: 0,
          limit: 1,
        },
        signal,
      );
      return page.rows[0] ?? null;
    },
    staleTime: Infinity,
  });

/** One URL in full: the inspector's summary, its issues, its redirects, headers, structured data and hreflang. */
/**
 * Step 3b: the images a page shows, each file once, the largest first, the first `limit` of them: the inspector's Images
 * and its tab's count, one read for both.
 */
export const urlImagesQuery = (id: string, urlId: number, limit: number) =>
  queryOptions({
    queryKey: [...queryKeys.urlImages(id, urlId), limit] as const,
    queryFn: ({ signal }) => api.urlImages(id, urlId, limit, signal),
    staleTime: Infinity,
  });

export const urlDetailQuery = (id: string, urlId: number) =>
  queryOptions({
    queryKey: queryKeys.urlDetail(id, urlId),
    queryFn: ({ signal }) => api.urlDetail(id, urlId, signal),
    staleTime: Infinity,
  });

/**
 * Where one URL is found, read with the other URLs shown at the same moment in one request (whereFoundBatch.ts): a
 * grid's rows each ask for their own, and a page of them costs one read. Null for a URL the crawl doesn't have.
 */
export const foundOnQuery = (id: string, urlId: number, limit: number) =>
  queryOptions({
    queryKey: queryKeys.foundOn(id, urlId, limit),
    queryFn: () => loadFoundOn(id, urlId, limit),
    staleTime: Infinity,
  });

/** What one of a check's flagged pages names, batched as `foundOnQuery` is. Null for a page the check didn't flag. */
export const issueTargetsQuery = (id: string, checkId: string, urlId: number, limit: number) =>
  queryOptions({
    queryKey: queryKeys.issueTargets(id, checkId, urlId, limit),
    queryFn: () => loadIssueTargets(id, checkId, urlId, limit),
    staleTime: Infinity,
  });

/** How long a URL's HTML is kept once nothing shows it: a page can be 16 MB, so not for long. */
export const SOURCE_GC_MS = 30_000;

/** A URL's stored HTML: read when the Source section is opened, never before. */
export const urlSourceQuery = (id: string, urlId: number) =>
  queryOptions({
    queryKey: queryKeys.urlSource(id, urlId),
    queryFn: ({ signal }) => api.urlSource(id, urlId, signal),
    staleTime: Infinity,
    gcTime: SOURCE_GC_MS,
  });

/** A page of a URL's links in the grid's shape: the links of the kinds asked for are what "match". */
export function linksAsGridPage(page: LinkPage): GridPage<LinkRow> {
  return {
    columns: [],
    rows: page.rows,
    offset: page.offset,
    matching: page.total,
    total: page.total,
    next: page.next,
    summary: { available: true, reason: null },
    unavailable: [],
  };
}

/** A page of a check's groups in the grid's shape: a group is a row, and the groups are what "match". */
export function groupsAsGridPage(page: IssueGroupPage): GridPage<IssueGroup> {
  return {
    columns: [],
    rows: page.groups,
    offset: page.offset,
    matching: page.groupCount,
    total: page.total,
    next: null,
    summary: { available: true, reason: null },
    unavailable: [],
  };
}

/** What a group's pages carry without a check's evidence: the URL, the detail and the other URL. */
export const GROUP_ROW_COLUMNS: readonly string[] = ['url', 'detail', 'relatedUrl'];

/**
 * The first rows of one of a check's groups, with what the tree shows of them: by default the URL, the detail and the
 * other URL; a check's evidence columns after them where it has some (screens/issues/evidenceColumns.ts).
 */
export const issueGroupRowsQuery = (
  id: string,
  checkId: string,
  group: CheckGroupRef,
  limit: number,
  columns: readonly string[] = GROUP_ROW_COLUMNS,
) =>
  queryOptions({
    queryKey: queryKeys.issueGroupRows(id, checkId, group, limit, columns),
    queryFn: ({ signal }): Promise<GridPage<IssueRow>> =>
      api.issueRows(id, checkId, { group, columns: [...columns], filters: [], sort: [], offset: 0, limit }, signal),
    staleTime: Infinity,
  });

/** Where a check's groups are looked for, from the largest: pages of 1,000, as far as 50,000 groups. */
export const LOCATE_GROUP_PAGE = 1_000;
export const LOCATE_GROUP_LIMIT = 50_000;

/**
 * Where one of a check's groups is among its groups (largest first), or why it isn't placed: `gone`, the check has no
 * such group (it was analysed again since the link was made); `beyond`, it isn't among the groups looked through.
 */
export type GroupPlace = { index: number; group: IssueGroup } | { index: null; missing: 'gone' | 'beyond' };

/** Finds a group the route names among the check's groups. A group opened from the grid is placed at once instead. */
export const issueGroupPlaceQuery = (id: string, checkId: string, group: CheckGroupRef) =>
  queryOptions({
    queryKey: queryKeys.issueGroupPlace(id, checkId, group),
    queryFn: async ({ signal }): Promise<GroupPlace> => {
      for (let offset = 0; offset < LOCATE_GROUP_LIMIT; offset += LOCATE_GROUP_PAGE) {
        const answer = await api.issueGroups(id, checkId, { offset, limit: LOCATE_GROUP_PAGE }, signal);
        const at = answer.groups.findIndex(
          (candidate) => candidate.detail === group.detail && candidate.relatedUrlId === group.relatedUrlId,
        );
        const found = answer.groups[at];
        if (found) return { index: offset + at, group: found };
        if (answer.groups.length < LOCATE_GROUP_PAGE) return { index: null, missing: 'gone' };
      }
      return { index: null, missing: 'beyond' };
    },
    staleTime: Infinity,
  });

// ---------------------------------------------------------------------------------- Sitemaps and robots.txt (E3b)
//
// Their reads are keyed as grids (`['grid', <grid>, <crawl id>, ...]`), the single reads included (the list of
// sitemaps, one origin's file): a running crawl changes every one of them, and the live bridge refreshes a crawl's
// grids in place once a second, with nothing blanked. All but an origin's file, which can be megabytes: its view
// reads it again when what it blocked changes (SELF_REFRESHED_GRIDS).

export const sitemapKeys = {
  /** The list of sitemaps, with each one's counts. */
  list: (id: string) => ['grid', 'sitemaps', id] as const,
  /** One sitemap's URLs, as grid pages. */
  urls: (id: string, sitemapId: number, query: { sort: readonly GridSort[]; filters: readonly unknown[]; q: string }) =>
    ['grid', 'sitemapUrls', id, { sitemapId, ...query }] as const,
  /** How many of one sitemap's URLs a filter selects: a count the list doesn't carry, read by the sitemap's page. */
  count: (id: string, sitemapId: number, filters: readonly unknown[]) =>
    ['grid', 'sitemapCount', id, { sitemapId, filters }] as const,
};

export const robotsKeys = {
  /** The origins, as grid pages. */
  origins: (id: string) => ['grid', 'robotsOrigins', id] as const,
  /** Whether the crawl obeyed robots.txt at all: its settings', which no run of it changes. */
  respected: (id: string) => ['crawl', id, 'robots', 'respected'] as const,
  /** One origin's file. */
  file: (id: string, origin: string) => ['grid', 'robotsFile', id, origin] as const,
  /** An origin's blocked URLs, narrowed to a line or a reason, as grid pages. */
  blocked: (id: string, query: Omit<RobotsBlockedQuery, 'offset' | 'limit'>) =>
    ['grid', 'robotsBlocked', id, query] as const,
};

/** The URL grid's columns as the engine has them: which sort, which filter. They never change while it runs. */
export const urlColumnsQuery = () =>
  queryOptions({
    queryKey: ['url-columns'] as const,
    queryFn: ({ signal }) => api.urlColumns(signal),
    staleTime: Infinity,
  });

/** Every sitemap the crawl looked for. A crawl with none answers with an empty list, never an error. */
export const sitemapsQuery = (id: string) =>
  queryOptions({
    queryKey: sitemapKeys.list(id),
    queryFn: ({ signal }) => api.sitemaps(id, signal),
    staleTime: Infinity,
  });

/**
 * How many of a sitemap's URLs a filter selects: the grid's own read, for its count alone. Keyed as a grid, so a
 * running crawl refreshes it in place with the rows it counts.
 */
export const sitemapCountQuery = (id: string, sitemapId: number, filters: readonly GridFilter[]) =>
  queryOptions({
    queryKey: sitemapKeys.count(id, sitemapId, filters),
    queryFn: async ({ signal }) =>
      (
        await api.sitemapUrls(
          id,
          sitemapId,
          { columns: ['url'], filters: [...filters], sort: [], offset: 0, limit: 1 },
          signal,
        )
      ).matching,
    staleTime: Infinity,
  });

/** One origin's robots.txt, line by line. `origin-not-found` for an origin the crawl has none for. */
export const robotsFileQuery = (id: string, origin: string) =>
  queryOptions({
    queryKey: robotsKeys.file(id, origin),
    queryFn: ({ signal }) => api.robotsFile(id, origin, signal),
    staleTime: Infinity,
  });

/** A page of origins as a grid's page, with what it says of the whole crawl: one read, so they agree. */
export interface OriginsGridPage extends GridPage<RobotsOrigin> {
  total: number;
  respected: boolean;
  userAgent: string;
  /** Every URL the crawl stored as refused by robots.txt. */
  blocked: number;
}

/** A page of the origins in the grid's shape: an origin is a row, and every origin "matches". */
export function originsAsGridPage(page: RobotsOriginPage): OriginsGridPage {
  return {
    columns: [],
    rows: page.origins,
    offset: page.offset,
    matching: page.total,
    total: page.total,
    next: null,
    summary: { available: true, reason: null },
    unavailable: [],
    respected: page.respected,
    userAgent: page.userAgent,
    blocked: page.blocked,
  };
}

/** A page of an origin's blocked URLs in the grid's shape: `matching` is what the line or reason narrows them to. */
export function blockedAsGridPage(page: RobotsBlockedPage): GridPage<RobotsBlockedUrl> {
  return {
    columns: [],
    rows: page.rows,
    offset: page.offset,
    matching: page.matching,
    total: page.total,
    next: null,
    summary: { available: true, reason: null },
    unavailable: [],
  };
}

export const issuesQuery = (id: string, checkId: string, offset: number, limit: number) =>
  queryOptions({
    queryKey: queryKeys.issues(id, checkId, offset, limit),
    queryFn: ({ signal }) => api.issues(id, checkId, { offset, limit }, signal),
    staleTime: Infinity,
  });

// ---------------------------------------------------------------------------------------------------- The fix loop

/** The crawl compared with its previous crawl, or `withId`: `comparisonOf` reads a first crawl's answer as null. */
export const changesQuery = (id: string, withId?: string) =>
  queryOptions({
    queryKey: queryKeys.changes(id, withId),
    queryFn: ({ signal }) => api.changes(id, withId, signal),
    staleTime: Infinity,
  });

/** A page of what a check no longer flags since the other crawl. */
export const fixedQuery = (id: string, checkId: string, query: FixedQuery = {}) =>
  queryOptions({
    queryKey: queryKeys.fixed(id, checkId, query),
    queryFn: ({ signal }) => api.fixed(id, checkId, query, signal),
    staleTime: Infinity,
  });

/**
 * What a recheck would do: read afresh whenever it's shown (staleTime 0), as whether it can run (`refused`) moves with
 * every crawl of the site, here or not.
 */
export const recheckPlanQuery = (id: string, ask: RecheckAsk) =>
  queryOptions({
    queryKey: queryKeys.recheckPlan(id, ask),
    queryFn: ({ signal }) => api.recheckPlan(id, ask, signal),
    staleTime: 0,
  });

export const rechecksQuery = (id: string) =>
  queryOptions({
    queryKey: queryKeys.rechecks(id),
    queryFn: ({ signal }) => api.rechecks(id, signal),
    staleTime: Infinity,
  });

export const recheckQuery = (id: string, recheckId: number) =>
  queryOptions({
    queryKey: queryKeys.recheck(id, recheckId),
    queryFn: ({ signal }) => api.recheck(id, recheckId, signal),
    staleTime: Infinity,
  });

export const recheckRowsQuery = (id: string, recheckId: number, query: RecheckRowsQuery = {}) =>
  queryOptions({
    queryKey: queryKeys.recheckRows(id, recheckId, query),
    queryFn: ({ signal }) => api.recheckRows(id, recheckId, query, signal),
    staleTime: Infinity,
  });

/** Round 5: the proxy test, a fetch through the proxy the user typed. Never cached: each press tests again. */
export const proxyTestMutation = () =>
  mutationOptions({
    mutationKey: ['proxy-test'] as const,
    mutationFn: ({ proxyUrl, url }: { proxyUrl: string; url: string }) => api.testProxy(proxyUrl, url),
  });
