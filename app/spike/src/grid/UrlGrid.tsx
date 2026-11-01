import { memo, useEffect, useLayoutEffect, useMemo, useRef, useState, type CSSProperties } from 'react';
import { queryOptions, useQueries, useQuery, useQueryClient } from '@tanstack/react-query';
import { flexRender, useTable, type Row, type SortingState } from '@tanstack/react-table';
import { fetchUrlPage, type UrlRow } from '../api';
import { useConnection } from '../connection';
import { formatInteger } from '../format';
import { markGridMount, registerGrid, ROW_HEIGHT } from '../perf/probe';
import { createScrollWindow, type ScrollWindow } from './scrollWindow';
import { params } from '../params';
import { columns, features } from './columns';
import { useFixedRows, useTanStackRows } from './useRowWindow';

export const PAGE_SIZE = 200;
const SKELETON_ROWS = 40;
const OVERSCAN = Number(params.get('overscan') ?? 12);
// Variant switches for the memory profile: row positioning, page cache lifetime and size.
const POSITION_WITH_TOP = params.get('pos') === 'top';
const PAGE_GC_MS = Number(params.get('gc') ?? 30_000);
const MAX_PREFETCH_PAGES = 8;
const LOOKAHEAD_SECONDS = 0.4;
// ?recycle=1 keys rows by slot, so React reuses row elements instead of mounting and unmounting them while scrolling.
const recycleRows = params.get('recycle') === '1';
const ROW_SLOTS = 128;
// ?virt=own swaps TanStack Virtual for a fixed-height window that stores nothing per row (chosen once, so hook order is stable).
const useRows = params.get('virt') === 'own' ? useFixedRows : useTanStackRows;

type GridRowModel = Row<typeof features, UrlRow>;

function pageQuery(pageIndex: number, sort: string, enabled: boolean) {
  return queryOptions({
    queryKey: ['urls', sort, pageIndex] as const,
    queryFn: ({ signal }) => fetchUrlPage(pageIndex, PAGE_SIZE, sort, signal),
    enabled,
    staleTime: Infinity,
    gcTime: PAGE_GC_MS,
  });
}

function range(from: number, to: number) {
  const pages: number[] = [];
  for (let page = from; page <= to; page++) pages.push(page);
  return pages;
}

const GridRow = memo(function GridRow({ row, index, start }: { row: GridRowModel; index: number; start: number }) {
  return (
    <div
      role="row"
      aria-rowindex={index + 2}
      className="row"
      data-index={index}
      style={POSITION_WITH_TOP ? { top: start } : { transform: `translateY(${start}px)` }}
    >
      {row.getVisibleCells().map((cell, column) => (
        <div
          key={cell.column.id}
          role="gridcell"
          aria-colindex={column + 1}
          className={cell.column.columnDef.meta?.numeric ? 'cell num' : 'cell'}
        >
          {flexRender(cell.column.columnDef.cell, cell.getContext())}
        </div>
      ))}
    </div>
  );
});

const PlaceholderRow = memo(function PlaceholderRow({ index, start, columnCount }: { index: number; start: number; columnCount: number }) {
  const cells = [];
  for (let column = 0; column < columnCount; column++) {
    cells.push(
      <div key={column} role="gridcell" aria-colindex={column + 1} className="cell">
        <span className="skeleton" />
      </div>,
    );
  }
  return (
    <div
      role="row"
      aria-rowindex={index + 2}
      aria-busy="true"
      className="row placeholder"
      data-index={index}
      style={POSITION_WITH_TOP ? { top: start } : { transform: `translateY(${start}px)` }}
    >
      {cells}
    </div>
  );
});

export function UrlGrid() {
  const connection = useConnection();
  const connected = connection !== null;
  const queryClient = useQueryClient();
  const scrollRef = useRef<HTMLDivElement>(null);
  const [sorting, setSorting] = useState<SortingState>([]);
  const sortKey = sorting.map((s) => `${s.id}:${s.desc ? 'desc' : 'asc'}`).join(',') || 'n:asc';

  const firstPage = useQuery(pageQuery(0, sortKey, connected));
  const total = firstPage.data?.total;
  const rowCount = total ?? SKELETON_ROWS;
  const rowCountRef = useRef(rowCount);
  rowCountRef.current = rowCount;
  const scrollWindowRef = useRef<ScrollWindow | null>(null);
  scrollWindowRef.current ??= createScrollWindow();
  const scrollWindow = scrollWindowRef.current;
  scrollWindow.total = rowCount * ROW_HEIGHT;

  // Scroll velocity in rows per second (signed), smoothed; a jump resets it.
  const velocity = useRef({ offset: 0, at: 0, rowsPerSecond: 0 });
  const rows = useRows({
    count: rowCount,
    overscan: OVERSCAN,
    scrollRef,
    scrollWindow,
    onOffset: (offset) => {
      const now = performance.now();
      const state = velocity.current;
      const elapsed = now - state.at;
      const moved = offset - state.offset;
      const viewport = scrollRef.current?.clientHeight ?? 800;
      if (elapsed > 0 && elapsed < 250 && Math.abs(moved) < viewport * 2) {
        const instant = moved / ROW_HEIGHT / (elapsed / 1_000);
        state.rowsPerSecond = state.rowsPerSecond * 0.6 + instant * 0.4;
      } else {
        state.rowsPerSecond = 0;
      }
      state.offset = offset;
      state.at = now;
    },
  });

  const items = rows.items;
  const firstPageIndex = items.length ? Math.floor(items[0].index / PAGE_SIZE) : 0;
  const lastPageIndex = items.length ? Math.floor(items[items.length - 1].index / PAGE_SIZE) : 0;
  const visiblePages = useMemo(() => range(firstPageIndex, lastPageIndex), [firstPageIndex, lastPageIndex]);
  const pageResults = useQueries({ queries: visiblePages.map((page) => pageQuery(page, sortKey, connected && total !== undefined)) });

  // Page data never changes for a given sort and page, so which pages have loaded is a complete memo key.
  const loadedKey = sortKey + '|' + pageResults.map((result, i) => (result.data ? visiblePages[i] : '-')).join(',');
  const data = useMemo(
    () => pageResults.flatMap((result) => result.data?.rows ?? []),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [loadedKey],
  );

  const table = useTable(
    {
      features,
      columns,
      data,
      getRowId: (row) => String(row.index),
      manualSorting: true,
      state: { sorting },
      onSortingChange: setSorting,
    },
    (state) => ({ sorting: state.sorting }),
  );

  // Prefetch in the direction of travel, further ahead the faster the scroll.
  useEffect(() => {
    if (!connected || total === undefined) return;
    const speed = velocity.current.rowsPerSecond;
    const forward = speed >= 0;
    const ahead = Math.min(MAX_PREFETCH_PAGES, 1 + Math.ceil((Math.abs(speed) * LOOKAHEAD_SECONDS) / PAGE_SIZE));
    const lastPage = Math.floor((total - 1) / PAGE_SIZE);
    const wanted: number[] = [];
    for (let k = 1; k <= ahead; k++) wanted.push(forward ? lastPageIndex + k : firstPageIndex - k);
    wanted.push(forward ? firstPageIndex - 1 : lastPageIndex + 1);
    for (const page of wanted) {
      if (page >= 0 && page <= lastPage) void queryClient.prefetchQuery(pageQuery(page, sortKey, true));
    }
  }, [connected, total, firstPageIndex, lastPageIndex, sortKey, queryClient]);

  useLayoutEffect(() => {
    markGridMount();
    registerGrid({
      scroller: scrollRef.current!,
      rowCount: () => rowCountRef.current,
      scrollOffset: () => scrollWindow.base + scrollRef.current!.scrollTop,
      scrollToIndex: rows.scrollToIndex,
    });
    return () => registerGrid(null);
  }, [rows.scrollToIndex, scrollWindow]);

  const rowsById = table.getRowModel().rowsById;
  const visibleColumns = table.getVisibleLeafColumns();
  const template = visibleColumns.map((column) => `${column.getSize()}px`).join(' ');
  const width = visibleColumns.reduce((sum, column) => sum + column.getSize(), 0);

  return (
    <div className="grid-frame">
      <div
        ref={scrollRef}
        className="grid"
        role="grid"
        aria-label="URLs"
        aria-rowcount={total === undefined ? -1 : total + 1}
        aria-colcount={visibleColumns.length}
        aria-busy={total === undefined}
        data-contain={params.get('contain') ?? 'default'}
        tabIndex={0}
        style={{ '--grid-cols': template, '--grid-width': `${width}px` } as CSSProperties}
      >
        <div className="grid-head" role="rowgroup">
          {table.getHeaderGroups().map((group) => (
            <div key={group.id} role="row" aria-rowindex={1} className="head-row">
              {group.headers.map((header, column) => {
                const sorted = header.column.getIsSorted();
                return (
                  <div
                    key={header.id}
                    role="columnheader"
                    aria-colindex={column + 1}
                    aria-sort={sorted === 'asc' ? 'ascending' : sorted === 'desc' ? 'descending' : 'none'}
                    className={header.column.columnDef.meta?.numeric ? 'head-cell num' : 'head-cell'}
                    onClick={header.column.getToggleSortingHandler()}
                  >
                    {flexRender(header.column.columnDef.header, header.getContext())}
                    {sorted ? (sorted === 'asc' ? ' â–²' : ' â–¼') : null}
                  </div>
                );
              })}
            </div>
          ))}
        </div>
        <div
          className="grid-body"
          role="rowgroup"
          style={{ height: scrollWindow.domHeight() }}
        >
          {items.map((item) => {
            const row = rowsById[String(item.index)];
            return row ? (
              <GridRow key={recycleRows ? item.index % ROW_SLOTS : item.key} row={row} index={item.index} start={item.start - scrollWindow.base} />
            ) : (
              <PlaceholderRow key={recycleRows ? item.index % ROW_SLOTS : item.key} index={item.index} start={item.start - scrollWindow.base} columnCount={visibleColumns.length} />
            );
          })}
        </div>
      </div>
      <div className="grid-status" aria-live="off">
        {total === undefined ? 'Loading URLsâ€¦' : `${formatInteger(total)} URLs`}
        {firstPage.isError ? ' Â· could not load' : null}
      </div>
    </div>
  );
}
