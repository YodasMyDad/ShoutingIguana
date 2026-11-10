// Query keys and options for every read, so each screen asks for the same data the same way and the live layer knows
// what to refresh. Freshness comes from events (`library`, `crawl`), not polling: reads stay fresh until told.
import { queryOptions, type QueryFilters } from '@tanstack/react-query';
import { api } from './client';
import type {
  CheckGroupRef,
  FacetsQuery,
  GridPage,
  GridQuery,
  GridSort,
  IssueGroup,
  IssueGroupPage,
  IssueRow,
  UrlRow,
} from './grids';

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
  /** The first pages of one of a check's groups, shown under it when it's open. */
  issueGroupRows: (id: string, checkId: string, group: CheckGroupRef, limit: number) =>
    ['crawl', id, 'issues', checkId, 'group', group, limit] as const,
  /** Where one of a check's groups is among its groups, to open it from the route. */
  issueGroupPlace: (id: string, checkId: string, group: CheckGroupRef) =>
    ['crawl', id, 'issues', checkId, 'place', group] as const,
  /**
   * Grid pages live under their own prefix, so the page cache can be capped (see queryClient.ts). Every grid's keys
   * are `['grid', <grid>, <crawl id>, <query>, ...]`, so a crawl's pages can be found (gridPagesOf).
   */
  gridPage: (grid: string, id: string, query: Omit<GridQuery, 'offset' | 'limit'>, page: number) =>
    ['grid', grid, id, query, page] as const,
  gridPrefix: ['grid'] as const,
};

/** Every page of every grid of one crawl: what a change to the crawl makes stale. */
export function gridPagesOf(crawlId: string): QueryFilters {
  return { queryKey: queryKeys.gridPrefix, predicate: (query) => query.queryKey[2] === crawlId };
}

/**
 * The grids that read the last analysis (a check's groups and rows): a running crawl doesn't change them, so the live
 * refresh leaves them be; the analysis that follows the crawl refreshes them.
 */
export const ANALYSIS_GRIDS: readonly string[] = ['issueGroups', 'issueRows'];

/** The pages of one crawl's grids that a running crawl changes: every grid but the analysis's. */
export function liveGridPagesOf(crawlId: string): QueryFilters {
  return {
    queryKey: queryKeys.gridPrefix,
    predicate: (query) => query.queryKey[2] === crawlId && !ANALYSIS_GRIDS.includes(String(query.queryKey[1])),
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

/** The first rows of one of a check's groups, with what the tree shows of them. */
export const issueGroupRowsQuery = (id: string, checkId: string, group: CheckGroupRef, limit: number) =>
  queryOptions({
    queryKey: queryKeys.issueGroupRows(id, checkId, group, limit),
    queryFn: ({ signal }): Promise<GridPage<IssueRow>> =>
      api.issueRows(
        id,
        checkId,
        { group, columns: ['url', 'detail', 'relatedUrl'], filters: [], sort: [], offset: 0, limit },
        signal,
      ),
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

export const issuesQuery = (id: string, checkId: string, offset: number, limit: number) =>
  queryOptions({
    queryKey: queryKeys.issues(id, checkId, offset, limit),
    queryFn: ({ signal }) => api.issues(id, checkId, { offset, limit }, signal),
    staleTime: Infinity,
  });
