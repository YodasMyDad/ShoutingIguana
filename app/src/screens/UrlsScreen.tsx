import { useQuery } from '@tanstack/react-query';
import { useNavigate, useParams, useSearch } from '@tanstack/react-router';
import { useCallback, useMemo, type ReactNode } from 'react';
import { api } from '../api/client';
import { isProblem, type ProblemError } from '../api/errors';
import type { SummaryReason, UrlRow } from '../api/grids';
import { crawlQuery } from '../api/queries';
import { isCrawling } from '../api/types';
import { analyseCrawl } from '../crawl/actions';
import { crawlControls, effectiveStatus } from '../crawl/status';
import { ErrorState } from '../feedback/ErrorState';
import { notifyResult } from '../feedback/notify';
import { GridCore, type GridCopyResult } from '../grid/GridCore';
import { useGridMetrics } from '../grid/metrics';
import type { GridColumn } from '../grid/types';
import { usePagedRows } from '../grid/usePagedRows';
import { MAX_COPY_ROWS } from '../grid/tsv';
import { formatBytes, formatCount, formatCountOf, formatMilliseconds } from '../lib/format';
import { useLiveCrawl } from '../live/liveStore';
import { Button } from '../ui/Button';

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

/** Why the analysis's columns are empty now, after the columns' names: the summary's reason, in plain words. */
const UNAVAILABLE_BECAUSE: Record<SummaryReason, string> = {
  crawling: 'fill in once the crawl has finished and been analysed',
  notAnalyzed: 'need an analysis: this crawl hasn’t been analysed yet',
  engineChanged: 'need this crawl analysed again: another version of Shouting Iguana analysed it',
  crawlChanged: 'need this crawl analysed again: it was continued after its last analysis',
};

/** "Inlinks", "Inlinks and Issues", "Title, Inlinks and Issues". */
function namesOf(ids: readonly string[]): string {
  const names = COLUMNS.filter((column) => ids.includes(column.id)).map((column) => column.header);
  return names.length > 1 ? `${names.slice(0, -1).join(', ')} and ${names.at(-1) ?? ''}` : (names[0] ?? '');
}

/**
 * URLs: the main grid. Foundation version: the grid primitive over the URL query, with sort and quick search from the
 * route. The column presets, chooser, filter bar and saved views are its slice. While the crawl runs, the count and
 * rows refresh in place (the live bridge refreshes them at most once a second), and the analysis's columns say why
 * they're empty.
 */
export function UrlsScreen() {
  const { crawl: crawlId } = useParams({ from: '/crawls/$crawl/urls' });
  const search = useSearch({ from: '/crawls/$crawl/urls' });
  const navigate = useNavigate({ from: '/crawls/$crawl/urls' });
  const { rowHeight, headerHeight } = useGridMetrics();
  const sort = useMemo(() => search.sort ?? [], [search.sort]);
  const filters = useMemo(() => search.filter ?? [], [search.filter]);
  const q = search.q;

  const item = useQuery(crawlQuery(crawlId)).data;
  const live = useLiveCrawl(crawlId);
  const crawlStatus = effectiveStatus(item, live);
  const crawling = crawlStatus !== null && isCrawling(crawlStatus);
  const canAnalyse = crawlControls(crawlStatus, item).canAnalyse;

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
        ? formatCountOf(rows.rowCount, 'URL')
        : `${formatCount(rows.rowCount)} of ${formatCountOf(rows.unfilteredTotal, 'URL')}`;

  const summary = rows.firstPage?.summary;
  const unavailable = (rows.firstPage?.unavailable ?? []).filter((id) => COLUMNS.some((column) => column.id === id));
  const analyse = (label: string) =>
    canAnalyse ? (
      <Button size="sm" command="crawl.analyse" onClick={() => void analyseCrawl(crawlId)}>
        {label}
      </Button>
    ) : null;
  const clearView = () => void navigate({ search: {} });

  return (
    <div className="flex h-full min-h-0 flex-col">
      <header className="flex items-center gap-3 px-4 py-2">
        <h1 className="text-16 font-semibold">URLs</h1>
        <span className="figures text-13 text-text-muted" aria-live="polite">
          {count}
        </span>
        {crawling ? (
          <span className="inline-flex items-center gap-1.5 text-12 font-medium text-text-secondary">
            <span aria-hidden="true" className="size-(--mark-size) rounded-full bg-accent motion-safe:animate-pulse" />
            Live
          </span>
        ) : null}
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
      {summary && !summary.available && unavailable.length > 0 ? (
        <p role="status" className="mx-4 mb-2 flex items-center gap-3 text-13 text-text-secondary">
          {namesOf(unavailable)} {UNAVAILABLE_BECAUSE[summary.reason ?? 'notAnalyzed']}.
          {summary.reason === 'crawling'
            ? null
            : analyse(summary.reason === 'notAnalyzed' ? 'Analyse it' : 'Analyse again')}
        </p>
      ) : null}
      {rows.error && rows.rowCount === null ? (
        isProblem(rows.error, 'needs-analysis') ? (
          <NeedsAnalysis error={rows.error} action={analyse('Analyse it')} onClear={clearView} />
        ) : isProblem(rows.error, 'invalid-query') ? (
          <ErrorState
            error={rows.error}
            actions={
              <Button size="sm" onClick={clearView}>
                Reset the view
              </Button>
            }
          />
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
          // Live refreshes update in place: the progress line is for a refresh the user asked for.
          busy={rows.isRefreshing && !crawling}
          emptyState={
            <div className="flex items-center gap-3 p-6 text-14 text-text-secondary">
              No URLs match these filters.
              <button
                type="button"
                className="text-accent-text underline underline-offset-(--underline-offset)"
                onClick={clearView}
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

/**
 * A sort or filter on a column the analysis fills, with no analysis to read: the engine's plain sentence says why, and
 * the view offers what can work: analysing the crawl, or dropping the sort and filters. Trying again can't.
 */
function NeedsAnalysis({ error, action, onClear }: { error: ProblemError; action: ReactNode; onClear: () => void }) {
  return (
    <ErrorState
      error={error}
      actions={
        <>
          {error.reason === 'crawling' ? null : action}
          <Button size="sm" onClick={onClear}>
            Clear the sort and filters
          </Button>
        </>
      }
    />
  );
}
