// Server-side paging for the grid primitive, with TanStack Query: the pages in view are queries, pages ahead are
// prefetched in the direction of travel (further the faster the scroll), and the cache holds at most 20 pages for
// 5 s each (queryClient.ts). A row's page is read from the cache, so a prefetched page shows without waiting.
//
// What keeps a 1M-row grid from flooding the sidecar:
// - the pages in view are asked for first, then the ones ahead, then one behind;
// - a page fetch nobody wants any more (scrolled past before it came) is cancelled, so its request is aborted;
// - while the scrollbar's thumb is dragged, rows fly past: nothing is prefetched, and the pages in view are asked for
//   at most every SCRUB_INTERVAL_MS, and at once when the thumb is let go.
import { useQueries, useQuery, useQueryClient, type Query, type QueryKey } from '@tanstack/react-query';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { GRID_PAGE_GC_MS } from '../app/queryClient';
import { GRID_PAGE_SIZE, type GridPage } from '../api/grids';
import type { GridRange, RowFetcher } from './types';

/** At most this many pages ahead, enough for the next 0.4 s at the current speed (the spike's settings). */
const MAX_PREFETCH_PAGES = 8;
const LOOKAHEAD_SECONDS = 0.4;
const FETCH_CONCURRENCY = 4;
/** While the thumb is dragged, the pages in view are asked for at most this often. */
export const SCRUB_INTERVAL_MS = 150;

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
  /** The first page, for what it says about the rows: which columns have no values now, and why. */
  firstPage: GridPage<Row> | undefined;
  getRow: (index: number) => Row | undefined;
  onRangeChange: (range: GridRange) => void;
  onScrollOffset: (offset: number) => void;
  fetchRows: RowFetcher<Row>;
  error: unknown;
  /** The first page is loading. */
  isLoading: boolean;
  /** Rows already shown are being fetched again (after a change): the thin progress line, nothing blanks. */
  isRefreshing: boolean;
  refetch: () => void;
}

/** The pages a range of rows covers. */
function pagesOf(range: { first: number; last: number }, pageSize: number) {
  return { first: Math.floor(range.first / pageSize), last: Math.floor(range.last / pageSize) };
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

  /** Pages a copy is fetching: wanted until the copy has them, wherever the grid scrolls meanwhile. */
  const copying = useRef(new Map<number, number>());

  /** Cancels this grid's page fetches that nobody waits for and that aren't wanted now: their requests abort. */
  const cancelUnwanted = useCallback(
    (wanted: ReadonlySet<number>) => {
      const isUnwanted = (query: Query) => {
        const page = query.queryKey[query.queryKey.length - 1];
        return (
          typeof page === 'number' &&
          page > 0 &&
          !wanted.has(page) &&
          !copying.current.has(page) &&
          query.state.fetchStatus === 'fetching' &&
          query.getObserversCount() === 0
        );
      };
      void queryClient.cancelQueries({ queryKey: [...queryKey, pageSize], predicate: isUnwanted });
    },
    [queryClient, queryKey, pageSize],
  );

  /** Asks for the pages in view, then the ones ahead in the direction of travel, then one behind. */
  const fetchAround = useCallback(
    (firstPage: number, lastPage: number) => {
      if (total === null) return;
      const lastIndex = Math.floor(Math.max(0, total - 1) / pageSize);
      const speed = motion.current.rowsPerSecond;
      const forward = speed >= 0;
      const ahead = Math.min(MAX_PREFETCH_PAGES, 1 + Math.ceil((Math.abs(speed) * LOOKAHEAD_SECONDS) / pageSize));
      const wanted: number[] = [];
      for (let page = firstPage; page <= lastPage; page++) wanted.push(page);
      for (let k = 1; k <= ahead; k++) wanted.push(forward ? lastPage + k : firstPage - k);
      wanted.push(forward ? firstPage - 1 : lastPage + 1);
      const inRange = wanted.filter((page) => page >= 0 && page <= lastIndex);
      // What's still coming within prefetch reach stays: the lookahead grows and shrinks with the smoothed speed, and
      // cancelling a page one step past it would only ask for it again a frame later. Pages beyond reach go.
      const reach = new Set<number>();
      for (let page = firstPage - MAX_PREFETCH_PAGES; page <= lastPage + MAX_PREFETCH_PAGES; page++) reach.add(page);
      cancelUnwanted(reach);
      for (const page of inRange) queryClient.query(pageQuery(page)).catch(ignore);
    },
    [total, pageSize, queryClient, pageQuery, cancelUnwanted],
  );

  const showPages = useCallback((next: { first: number; last: number }) => {
    setPages((current) => (current.first === next.first && current.last === next.last ? current : next));
  }, []);

  // While scrubbing: when the pages in view were last asked for, and the call waiting to ask again.
  const scrub = useRef<{ shownAt: number; timer: ReturnType<typeof setTimeout> | null }>({
    shownAt: Number.NEGATIVE_INFINITY,
    timer: null,
  });
  useEffect(
    () => () => {
      if (scrub.current.timer !== null) clearTimeout(scrub.current.timer);
    },
    [],
  );

  const onRangeChange = useCallback(
    (range: GridRange) => {
      const next = pagesOf(range, pageSize);
      const state = scrub.current;
      if (state.timer !== null) {
        clearTimeout(state.timer);
        state.timer = null;
      }
      if (!range.scrubbing) {
        // The visible pages' requests go out before any prefetch: the screen waits for them.
        fetchAround(next.first, next.last);
        showPages(next);
        return;
      }
      // Nothing ahead is worth fetching while the thumb flies: drop what's still coming for rows long gone.
      cancelUnwanted(new Set());
      const show = () => {
        state.timer = null;
        state.shownAt = performance.now();
        showPages(next);
      };
      const wait = state.shownAt + SCRUB_INTERVAL_MS - performance.now();
      if (wait <= 0) show();
      else state.timer = setTimeout(show, wait);
    },
    [pageSize, fetchAround, showPages, cancelUnwanted],
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
      const pin = (page: number, by: number) => {
        const count = (copying.current.get(page) ?? 0) + by;
        if (count > 0) copying.current.set(page, count);
        else copying.current.delete(page);
      };
      needed.forEach((page) => pin(page, 1));
      try {
        for (let i = 0; i < needed.length; i += FETCH_CONCURRENCY) {
          await Promise.all(
            needed.slice(i, i + FETCH_CONCURRENCY).map(async (page) => {
              fetched.set(page, await queryClient.query(pageQuery(page)));
            }),
          );
        }
      } finally {
        needed.forEach((page) => pin(page, -1));
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
    firstPage: first.data,
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
