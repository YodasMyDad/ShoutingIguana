import { useNavigate, useParams, useSearch } from '@tanstack/react-router';
import { useCallback, useMemo } from 'react';
import { api } from '../api/client';
import { isProblem } from '../api/errors';
import type { UrlRow } from '../api/grids';
import { ErrorState } from '../feedback/ErrorState';
import { notifyResult } from '../feedback/notify';
import { GridCore, type GridCopyResult } from '../grid/GridCore';
import { useGridMetrics } from '../grid/metrics';
import type { GridColumn } from '../grid/types';
import { usePagedRows } from '../grid/usePagedRows';
import { MAX_COPY_ROWS } from '../grid/tsv';
import { formatBytes, formatCount, formatMilliseconds } from '../lib/format';

/** Words for the indexability column: the reason it isn't indexable, or Indexable. */
const INDEXABILITY: Record<string, string> = {
  none: 'Indexable',
  noindex: 'Noindex',
  canonicalised: 'Canonicalised',
  redirected: 'Redirected',
  clientError: 'Client error',
  serverError: 'Server error',
  blockedByRobots: 'Blocked by robots.txt',
  blocked: 'Blocked',
  unreachable: 'Unreachable',
};

const plain = (value: number | null | undefined) => (value === null || value === undefined ? '' : String(value));
const indexability = (row: UrlRow) => (row.indexability ? (INDEXABILITY[row.indexability] ?? row.indexability) : '');
const status = (row: UrlRow) =>
  row.statusCode === null || row.statusCode === undefined
    ? row.statusClass === 'noResponse'
      ? 'No response'
      : ''
    : String(row.statusCode);

/** Columns use E3a's ids (docs/v2-sidecar-api.md, URLs), so the query asks for exactly these. */
const COLUMNS: readonly GridColumn<UrlRow>[] = [
  { id: 'url', header: 'URL', width: 420, mono: true, cell: (row) => row.url ?? '', text: (row) => row.url ?? '' },
  { id: 'statusCode', header: 'Status', width: 104, numeric: true, cell: (row) => status(row) || '–', text: status },
  {
    id: 'indexability',
    header: 'Indexability',
    width: 168,
    cell: (row) => indexability(row) || '–',
    text: indexability,
  },
  { id: 'title', header: 'Title', width: 320, cell: (row) => row.title ?? '', text: (row) => row.title ?? '' },
  {
    id: 'depth',
    header: 'Depth',
    width: 72,
    numeric: true,
    cell: (row) => formatCount(row.depth),
    text: (row) => plain(row.depth),
  },
  {
    id: 'responseMs',
    header: 'Response',
    width: 104,
    numeric: true,
    cell: (row) => formatMilliseconds(row.responseMs),
    text: (row) => plain(row.responseMs),
  },
  {
    id: 'sizeBytes',
    header: 'Size',
    width: 96,
    numeric: true,
    cell: (row) => formatBytes(row.sizeBytes),
    text: (row) => plain(row.sizeBytes),
  },
  {
    id: 'inlinks',
    header: 'Inlinks',
    width: 88,
    numeric: true,
    cell: (row) => formatCount(row.inlinks),
    text: (row) => plain(row.inlinks),
  },
  {
    id: 'issues',
    header: 'Issues',
    width: 80,
    numeric: true,
    cell: (row) => formatCount(row.issues),
    text: (row) => plain(row.issues),
  },
];

/** What the query asks for: the columns shown, and statusClass, to tell "No response" from "not fetched". */
const QUERY_COLUMNS = [...COLUMNS.map((column) => column.id), 'statusClass'];

/**
 * URLs: the main grid. Foundation version: the grid primitive over the URL query (served by the mock until E3), with
 * sort and quick search from the route. The column presets, chooser, filter bar and saved views are its slice.
 */
export function UrlsScreen() {
  const { crawl: crawlId } = useParams({ from: '/crawls/$crawl/urls' });
  const search = useSearch({ from: '/crawls/$crawl/urls' });
  const navigate = useNavigate({ from: '/crawls/$crawl/urls' });
  const { rowHeight, headerHeight } = useGridMetrics();
  const sort = useMemo(() => search.sort ?? [], [search.sort]);
  const filters = useMemo(() => search.filter ?? [], [search.filter]);
  const q = search.q;

  const queryKey = useMemo(
    () => ['grid', 'urls', crawlId, { sort, filters, q: q ?? '' }] as const,
    [crawlId, sort, filters, q],
  );
  const fetchPage = useCallback(
    (offset: number, limit: number, signal: AbortSignal) =>
      api.urls(crawlId, { columns: QUERY_COLUMNS, offset, limit, sort, filters, ...(q ? { search: q } : {}) }, signal),
    [crawlId, sort, filters, q],
  );
  const rows = usePagedRows<UrlRow>({ queryKey, fetchPage, rowHeight });

  const columns = useMemo(
    () =>
      COLUMNS.map((column) => {
        const sorted = sort.find((entry) => entry.column === column.id);
        return sorted ? { ...column, sort: sorted.desc ? ('descending' as const) : ('ascending' as const) } : column;
      }),
    [sort],
  );

  const onCopy = (result: GridCopyResult) => {
    if (result.truncated) {
      notifyResult(`Copied the first ${formatCount(MAX_COPY_ROWS)} rows`, {
        description: 'Export the view for all of them.',
      });
    }
  };

  const count =
    rows.rowCount === null || rows.unfilteredTotal === null
      ? null
      : rows.rowCount === rows.unfilteredTotal
        ? `${formatCount(rows.rowCount)} URLs`
        : `${formatCount(rows.rowCount)} of ${formatCount(rows.unfilteredTotal)} URLs`;

  return (
    <div className="flex h-full min-h-0 flex-col">
      <header className="flex items-center gap-3 px-4 py-2">
        <h1 className="text-16 font-semibold">URLs</h1>
        <span className="figures text-13 text-text-muted" aria-live="polite">
          {count}
        </span>
        <input
          key={q ?? ''}
          type="search"
          aria-label="Search URLs and titles"
          placeholder="Search URLs and titles"
          defaultValue={q ?? ''}
          onKeyDown={(event) => {
            if (event.key !== 'Enter') return;
            const value = event.currentTarget.value.trim();
            void navigate({ search: (previous) => ({ ...previous, q: value || undefined }) });
          }}
          className="ml-auto h-(--control-height-sm) w-(--search-width) rounded-sm border border-border-control bg-surface px-2.5 text-13"
        />
      </header>
      {rows.error && rows.rowCount === null ? (
        isProblem(rows.error, 'not-found') ? (
          <ErrorState error={rows.error} title="The engine doesn’t serve the URL grid yet: it arrives with E3." />
        ) : (
          <ErrorState error={rows.error} onRetry={rows.refetch} />
        )
      ) : (
        <GridCore
          key={JSON.stringify(queryKey)}
          label="URLs"
          columns={columns}
          rowCount={rows.rowCount}
          getRow={rows.getRow}
          rowHeight={rowHeight}
          headerHeight={headerHeight}
          onRangeChange={rows.onRangeChange}
          onScrollOffset={rows.onScrollOffset}
          fetchRows={rows.fetchRows}
          onCopy={onCopy}
          onActivate={(_index, row) => {
            if (row) void navigate({ search: (previous) => ({ ...previous, inspect: row.id }) });
          }}
          busy={rows.isRefreshing}
          emptyState={
            <div className="flex items-center gap-3 p-6 text-14 text-text-secondary">
              No URLs match these filters.
              <button
                type="button"
                className="text-accent-text underline underline-offset-(--underline-offset)"
                onClick={() => void navigate({ search: {} })}
              >
                Clear them
              </button>
            </div>
          }
          className="mx-4 mb-4 flex-1 rounded-lg border border-border"
        />
      )}
    </div>
  );
}
