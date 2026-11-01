// The one QueryClient. Reads stay fresh until an event says otherwise, nothing refetches on focus, and queries run
// whatever navigator.onLine says: the sidecar is on this machine, so "offline" never applies.
import { QueryCache, QueryClient, type Query } from '@tanstack/react-query';
import { ApiError, isProblem } from '../api/errors';
import { queryKeys } from '../api/queries';

/** Grid pages kept in memory, and how long an unused one lingers (the spike's memory profile: 20 pages, 5 s). */
export const GRID_PAGE_CACHE_LIMIT = 20;
export const GRID_PAGE_GC_MS = 5_000;

/** Retry only what might work a moment later: never a problem the engine meant, such as a 404 or a 422. */
export function shouldRetry(failureCount: number, error: unknown): boolean {
  if (failureCount >= 2) return false;
  if (isProblem(error)) return error.retryable;
  return error instanceof ApiError ? error.retryable : false;
}

export function createQueryClient(): QueryClient {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: {
        networkMode: 'always',
        staleTime: Infinity,
        gcTime: 5 * 60_000,
        refetchOnWindowFocus: false,
        refetchOnReconnect: false,
        retry: shouldRetry,
        retryDelay: (attempt) => Math.min(250 * 2 ** attempt, 2_000),
      },
      mutations: { networkMode: 'always', retry: false },
    },
    queryCache: new QueryCache(),
  });
  capGridPages(queryClient.getQueryCache());
  return queryClient;
}

/**
 * Keeps at most GRID_PAGE_CACHE_LIMIT grid pages: when one is added past the limit, the oldest pages nobody is
 * looking at go first. A small live heap lets V8 collect the DOM garbage of fast scrolling sooner (the spike's
 * biggest memory win: the peak after a 50k-row fling fell from 428 to 266 MB).
 */
export function capGridPages(cache: QueryCache, limit = GRID_PAGE_CACHE_LIMIT): () => void {
  return cache.subscribe((event) => {
    if (event.type !== 'added') return;
    const key: unknown = event.query.queryKey;
    if (!Array.isArray(key) || key[0] !== queryKeys.gridPrefix[0]) return;
    queueMicrotask(() => {
      const pages = cache.findAll({ queryKey: queryKeys.gridPrefix });
      if (pages.length <= limit) return;
      const idle = pages
        .filter((query: Query) => query.getObserversCount() === 0 && query.state.fetchStatus === 'idle')
        .sort((a, b) => a.state.dataUpdatedAt - b.state.dataUpdatedAt);
      for (const query of idle.slice(0, pages.length - limit)) cache.remove(query);
    });
  });
}
