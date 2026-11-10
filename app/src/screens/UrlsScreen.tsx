import { useQuery } from '@tanstack/react-query';
import { useNavigate, useParams, useSearch } from '@tanstack/react-router';
import { RefreshCw } from 'lucide-react';
import { useCallback, useMemo } from 'react';
import { api } from '../api/client';
import { isProblem } from '../api/errors';
import type { GridFilter, UrlRow } from '../api/grids';
import { checksQuery, crawlQuery } from '../api/queries';
import { isCrawling } from '../api/types';
import { analyseCrawl } from '../crawl/actions';
import { crawlControls, effectiveStatus } from '../crawl/status';
import { ErrorState } from '../feedback/ErrorState';
import { notifyResult } from '../feedback/notify';
import { useAnnounceResults } from '../feedback/useAnnounceResults';
import { FilterChips } from '../grid/FilterBar';
import { LOOKING_UP } from '../grid/filterSentence';
import { GridCore, type GridCopyResult } from '../grid/GridCore';
import { useGridMetrics } from '../grid/metrics';
import { NeedsAnalysis } from '../grid/NeedsAnalysis';
import { UNAVAILABLE_BECAUSE, listOfNames } from '../grid/summaryWords';
import type { GridColumn } from '../grid/types';
import { usePagedRows } from '../grid/usePagedRows';
import { useUrlWords } from '../grid/useUrlWords';
import { useInspector } from '../routing/inspector';
import { MAX_COPY_ROWS } from '../grid/tsv';
import { urlColumnWords, urlEmptyWords, urlValueWords } from '../grid/urlFilterWords';
import { formatBytes, formatCount, formatMilliseconds, formatShownOf } from '../lib/format';
import { useLiveCrawl } from '../live/liveStore';
import { Banner } from '../ui/Banner';
import { Button } from '../ui/Button';
import { FilteredEmptyState } from '../ui/EmptyState';
import { LiveBadge } from '../ui/LiveBadge';
import { GridHeader, ResultCount } from '../ui/PageHeader';
import { SearchField } from '../ui/SearchField';
import { siteOriginOf } from '../ui/urlText';

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

/** "Inlinks", "Inlinks and Issues", "Title, Inlinks and Issues": the columns' names, in the grid's order. */
function namesOf(ids: readonly string[]): string {
  return listOfNames(COLUMNS.filter((column) => ids.includes(column.id)).map((column) => column.header));
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
  const inspector = useInspector();
  const inspecting = search.inspect !== undefined;

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
    rows.rowCount === null || rows.unfilteredTotal === null ? null : (
      <ResultCount shown={rows.rowCount} total={rows.unfilteredTotal} noun="URL" />
    );
  // A filter or a search the user applied is answered once, with the count it leaves. The count itself is no live
  // region: a running crawl moves it every second.
  useAnnounceResults(
    crawlId,
    useMemo(() => JSON.stringify({ filters, q: q ?? '' }), [filters, q]),
    rows.rowCount === null || rows.unfilteredTotal === null
      ? null
      : formatShownOf(rows.rowCount, rows.unfilteredTotal, 'URL'),
  );

  const summary = rows.firstPage?.summary;
  const unavailable = (rows.firstPage?.unavailable ?? []).filter((id) => COLUMNS.some((column) => column.id === id));
  const analyse = (label: string) =>
    canAnalyse ? (
      <Button size="sm" icon={RefreshCw} command="crawl.analyse" onClick={() => void analyseCrawl(crawlId)}>
        {label}
      </Button>
    ) : null;
  const clearView = () => void navigate({ search: {} });
  const chips = (
    <ActiveFilters
      crawlId={crawlId}
      siteOrigin={siteOriginOf(item?.startUrl)}
      filters={filters}
      onChange={(next) =>
        void navigate({ search: (previous) => ({ ...previous, filter: next.length > 0 ? next : undefined }) })
      }
    />
  );

  return (
    <div className="flex h-full min-h-0 flex-col">
      <GridHeader
        title="URLs"
        count={count}
        badge={crawling ? <LiveBadge /> : null}
        className="px-6 pt-2 pb-1"
        actions={
          <SearchField
            key={q ?? ''}
            label="Search URLs and titles"
            placeholder="Search URLs and titles"
            defaultValue={q ?? ''}
            onKeyDown={(event) => {
              if (event.key !== 'Enter') return;
              const value = event.currentTarget.value.trim();
              void navigate({ search: (previous) => ({ ...previous, q: value || undefined }) });
            }}
            onClear={() => void navigate({ search: (previous) => ({ ...previous, q: undefined }) })}
            className="w-(--search-width)"
          />
        }
      />
      {filters.length > 0 ? (
        <div role="group" aria-label="Filters" className="flex flex-wrap items-center gap-2 px-6 pb-3">
          {chips}
        </div>
      ) : null}
      {summary && !summary.available && unavailable.length > 0 ? (
        <Banner inset className="mx-6 mb-3">
          <span className="inline-flex flex-wrap items-center gap-3">
            {namesOf(unavailable)} {UNAVAILABLE_BECAUSE[summary.reason ?? 'notAnalyzed']}.
            {summary.reason === 'crawling'
              ? null
              : analyse(summary.reason === 'notAnalyzed' ? 'Analyse it' : 'Analyse again')}
          </span>
        </Banner>
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
            if (row) inspector.open(row.id);
          }}
          // While it's open, the inspector follows the cursor, in place.
          onActiveRowChange={(index) => {
            const row = inspecting && index !== null ? rows.getRow(index) : undefined;
            if (row) inspector.open(row.id);
          }}
          // Live refreshes update in place: the progress line is for a refresh the user asked for.
          busy={rows.isRefreshing && !crawling}
          emptyState={
            <FilteredEmptyState
              title="No URLs match these filters."
              level="none"
              filters={filters.length > 0 ? chips : undefined}
              onClear={clearView}
            />
          }
          className="mx-6 mb-4 flex-1 rounded-lg border border-border shadow-1"
        />
      )}
    </div>
  );
}

/**
 * The view's filters as chips that read as sentences ("Check is Internal links without anchor text", "Related URL is
 * /"), each removable: the grid's own chips (`grid/FilterBar.tsx`, which the Links grid edits its filters with), given
 * the URL grid's words. Editing them here, with the rest of the full filter bar, is the URLs slice's.
 */
function ActiveFilters({
  crawlId,
  siteOrigin,
  filters,
  onChange,
}: {
  crawlId: string;
  /** URLs on the crawl's origin read as their path, as the grid shows them. */
  siteOrigin: string | null;
  filters: readonly GridFilter[];
  onChange: (filters: GridFilter[]) => void;
}) {
  const checks = useQuery({ ...checksQuery(), enabled: filters.some((filter) => filter.column === 'check') });
  const relatedIds = useMemo(
    () =>
      filters.flatMap((filter) =>
        'group' in filter && filter.group.relatedUrlId !== null ? [filter.group.relatedUrlId] : [],
      ),
    [filters],
  );
  const relatedWords = useUrlWords(crawlId, relatedIds, siteOrigin);
  // Names, never ids: while they're read, a placeholder; when they can't be, plain words.
  const checkWords = (id: string | number) => {
    if (checks.data)
      return checks.data.checks.find((check) => check.id === id)?.title ?? 'a check this version doesn’t have';
    return checks.isError ? 'a check whose name couldn’t be read' : LOOKING_UP;
  };
  const words = {
    column: (id: string) =>
      id === 'check' ? 'Check' : (COLUMNS.find((column) => column.id === id)?.header ?? urlColumnWords(id) ?? id),
    value: (column: string, value: string | number) =>
      // A number this list has no words for reads with its thousands separators (the sentence's default).
      column === 'check' ? checkWords(value) : urlValueWords(column, value),
    empty: urlEmptyWords,
  };
  return <FilterChips filters={filters} onChange={onChange} words={words} relatedUrl={relatedWords} />;
}
