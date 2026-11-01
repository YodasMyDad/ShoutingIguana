// Query keys and options for every read, so each screen asks for the same data the same way and the live layer knows
// what to refresh. Freshness comes from events (`library`, `crawl`), not polling: reads stay fresh until told.
import { queryOptions } from '@tanstack/react-query';
import { api } from './client';
import type { GridQuery } from './grids';

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
  issues: (id: string, checkId: string, offset: number, limit: number) =>
    ['crawl', id, 'issues', checkId, offset, limit] as const,
  /** Grid pages live under their own prefix, so the page cache can be capped (see queryClient.ts). */
  gridPage: (grid: string, id: string, query: Omit<GridQuery, 'offset' | 'limit'>, page: number) =>
    ['grid', grid, id, query, page] as const,
  gridPrefix: ['grid'] as const,
};

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

export const issuesQuery = (id: string, checkId: string, offset: number, limit: number) =>
  queryOptions({
    queryKey: queryKeys.issues(id, checkId, offset, limit),
    queryFn: ({ signal }) => api.issues(id, checkId, { offset, limit }, signal),
    staleTime: Infinity,
  });
