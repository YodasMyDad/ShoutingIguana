// Server-side paging for the grid primitive, with TanStack Query: the pages in view are queries, pages ahead are
// prefetched in the direction of travel (further the faster the scroll), and the cache holds at most 20 pages for
// 5 s each (queryClient.ts). A row's page is read from the cache, so a prefetched page shows without waiting.
import { useQueries, useQuery, useQueryClient, type QueryKey } from '@tanstack/react-query';
import { useCallback, useMemo, useRef, useState } from 'react';
import { GRID_PAGE_GC_MS } from '../app/queryClient';
import { GRID_PAGE_SIZE, type GridPage } from '../api/grids';
import type { RowFetcher } from './types';

/** At most this many pages ahead, enough for the next 0.4 s at the current speed (the spike's settings). */
const MAX_PREFETCH_PAGES = 8;
const LOOKAHEAD_SECONDS = 0.4;
const FETCH_CONCURRENCY = 4;

/** A prefetch that fails is fetched again when its rows come into view, and shows its error then. */
const ignore = () => undefined;

export interface PagedRowsOptions<Row> {
  /** Identifies the rows: the grid, the crawl, the sort, filters and search. Must start with 'grid'. */
  queryKey: readonly ['grid', ...unknown[]];
  fetchPage: (offset: number, limit: number, signal: AbortSignal) => Promise<GridPage<Row>>;
  rowHeight: number;
  pageSize?: number;
  enabled?: boolean;
}

export interface PagedRows<Row> {
  /** Null until the first page says how many rows match. */
  rowCount: number | null;
  /** Rows before any filter. */
  unfilteredTotal: number | null;
  getRow: (index: number) => Row | undefined;
  onRangeChange: (range: { first: number; last: number }) => void;
  onScrollOffset: (offset: number) => void;
  fetchRows: RowFetcher<Row>;
  error: unknown;
  /** The first page is loading. */
  isLoading: boolean;
  /** Rows already shown are being fetched again (after a change): the thin progress line, nothing blanks. */
  isRefreshing: boolean;
  refetch: () => void;
}

export function usePagedRows<Row>({
  queryKey,
  fetchPage,
  rowHeight,
  pageSize = GRID_PAGE_SIZE,
  enabled = true,
}: PagedRowsOptions<Row>): PagedRows<Row> {
  const queryClient = useQueryClient();
  const pageKey = useCallback((page: number): QueryKey => [...queryKey, pageSize, page], [queryKey, pageSize]);
  const pageQuery = useCallback(
    (page: number) => ({
      queryKey: [...queryKey, pageSize, page],
      queryFn: ({ signal }: { signal: AbortSignal }) => fetchPage(page * pageSize, pageSize, signal),
      staleTime: Infinity,
      gcTime: GRID_PAGE_GC_MS,
    }),
    [queryKey, fetchPage, pageSize],
  );

  const first = useQuery({ ...pageQuery(0), enabled });
  // `matching` rows exist for this query: the grid's row count. `total` is every URL, for "3 of 6,286".
  const total = first.data?.matching ?? null;
  const [pages, setPages] = useState({ first: 0, last: 0 });
  const visible = useMemo(() => {
    const list: number[] = [];
    for (let page = pages.first; page <= pages.last; page++) if (page > 0) list.push(page);
    return list;
  }, [pages]);
  const results = useQueries({
    queries: visible.map((page) => ({ ...pageQuery(page), enabled: enabled && total !== null })),
  });

  // Scroll speed in rows per second, signed and smoothed; a jump resets it.
  const motion = useRef({ offset: 0, at: 0, rowsPerSecond: 0 });
  const onScrollOffset = useCallback(
    (offset: number) => {
      const now = performance.now();
      const state = motion.current;
      const elapsed = now - state.at;
      const moved = offset - state.offset;
      if (elapsed > 0 && elapsed < 250 && Math.abs(moved) < rowHeight * 200) {
        state.rowsPerSecond = state.rowsPerSecond * 0.6 + (moved / rowHeight / (elapsed / 1_000)) * 0.4;
      } else {
        state.rowsPerSecond = 0;
      }
      state.offset = offset;
      state.at = now;
    },
    [rowHeight],
  );

  const onRangeChange = useCallback(
    ({ first: firstRow, last: lastRow }: { first: number; last: number }) => {
      const firstPage = Math.floor(firstRow / pageSize);
      const lastPage = Math.floor(lastRow / pageSize);
      setPages((current) =>
        current.first === firstPage && current.last === lastPage ? current : { first: firstPage, last: lastPage },
      );
      if (total === null) return;
      const lastIndex = Math.floor(Math.max(0, total - 1) / pageSize);
      const speed = motion.current.rowsPerSecond;
      const forward = speed >= 0;
      const ahead = Math.min(MAX_PREFETCH_PAGES, 1 + Math.ceil((Math.abs(speed) * LOOKAHEAD_SECONDS) / pageSize));
      const wanted: number[] = [];
      for (let k = 1; k <= ahead; k++) wanted.push(forward ? lastPage + k : firstPage - k);
      wanted.push(forward ? firstPage - 1 : lastPage + 1);
      for (const page of wanted) {
        if (page >= 0 && page <= lastIndex) queryClient.query(pageQuery(page)).catch(ignore);
      }
    },
    [pageSize, total, queryClient, pageQuery],
  );

  // Rows come from whatever is cached; the visible queries' results only make React re-render when they land.
  const loadedKey = results.map((result) => result.dataUpdatedAt).join(',') + `|${first.dataUpdatedAt}`;
  const getRow = useCallback(
    (index: number): Row | undefined => {
      const page = Math.floor(index / pageSize);
      const data = queryClient.getQueryData<GridPage<Row>>(pageKey(page));
      return data?.rows[index - page * pageSize];
    },
    // loadedKey: a page landing must give a new getRow, so the grid renders it.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [queryClient, pageKey, pageSize, loadedKey],
  );

  const fetchRows = useCallback<RowFetcher<Row>>(
    async (indices) => {
      const needed = [...new Set(indices.map((index) => Math.floor(index / pageSize)))];
      const fetched = new Map<number, GridPage<Row>>();
      for (let i = 0; i < needed.length; i += FETCH_CONCURRENCY) {
        await Promise.all(
          needed.slice(i, i + FETCH_CONCURRENCY).map(async (page) => {
            fetched.set(page, await queryClient.query(pageQuery(page)));
          }),
        );
      }
      return indices.map((index) => {
        const page = Math.floor(index / pageSize);
        return fetched.get(page)?.rows[index - page * pageSize];
      });
    },
    [queryClient, pageQuery, pageSize],
  );

  const error = first.error ?? results.find((result) => result.error)?.error ?? null;
  return {
    rowCount: total,
    unfilteredTotal: first.data?.total ?? null,
    getRow,
    onRangeChange,
    onScrollOffset,
    fetchRows,
    error,
    isLoading: first.isPending && enabled,
    isRefreshing: first.isFetching && first.data !== undefined,
    refetch: () => void first.refetch(),
  };
}
