import { useCallback, useMemo } from 'react';
import { api } from '../../api/client';
import type { GridSort, IssueRow } from '../../api/grids';
import { queryKeys } from '../../api/queries';
import type { Check } from '../../api/types';
import { ErrorState } from '../../feedback/ErrorState';
import { GridCore } from '../../grid/GridCore';
import { useGridMetrics } from '../../grid/metrics';
import { nextSort } from '../../grid/sort';
import type { GridColumn } from '../../grid/types';
import { useElementWidth } from '../../grid/useElementWidth';
import { usePagedRows } from '../../grid/usePagedRows';
import { Url } from '../../ui/Url';

const DETAIL_WIDTH = 260;
const MIN_URL_WIDTH = 240;
const FRAME_SLACK = 12;

export interface IssueRowsGridProps {
  crawlId: string;
  check: Check;
  /** The check's flagged URLs: how tall the grid is before its first page. */
  total: number;
  siteOrigin: string | null;
  sort: readonly GridSort[];
  /** A header was pressed: ascending, then descending, then the engine's order. */
  onSortChange: (sort: GridSort[]) => void;
  onInspect: (urlId: number) => void;
}

/**
 * A check that doesn't group (its detail is a measurement, "78 characters", or the same on every page): its URLs
 * flat, server-paged and sortable, with the detail as a column (E3a, `/rows`).
 */
export function IssueRowsGrid({
  crawlId,
  check,
  total,
  siteOrigin,
  sort,
  onSortChange,
  onInspect,
}: IssueRowsGridProps) {
  const { rowHeight, headerHeight } = useGridMetrics();
  const [measure, width] = useElementWidth();
  const queryKey = useMemo(() => queryKeys.issueRowsGrid(crawlId, check.id, sort), [crawlId, check.id, sort]);
  const fetchPage = useCallback(
    (offset: number, limit: number, signal: AbortSignal) =>
      api.issueRows(
        crawlId,
        check.id,
        { columns: ['url', 'detail'], filters: [], sort: [...sort], offset, limit },
        signal,
      ),
    [crawlId, check.id, sort],
  );
  const rows = usePagedRows<IssueRow>({ queryKey, fetchPage, rowHeight });

  const urlWidth = Math.max(MIN_URL_WIDTH, (width ?? 720) - DETAIL_WIDTH - FRAME_SLACK);
  const columns = useMemo<GridColumn<IssueRow>[]>(() => {
    const sorted = (id: string) => {
      const entry = sort.find((candidate) => candidate.column === id);
      return entry ? { sort: entry.desc ? ('descending' as const) : ('ascending' as const) } : {};
    };
    return [
      {
        id: 'url',
        header: 'URL',
        width: urlWidth,
        sortable: true,
        ...sorted('url'),
        cell: (row) => <Url url={row.url ?? ''} siteOrigin={siteOrigin} copyable={false} />,
        text: (row) => row.url ?? '',
      },
      {
        id: 'detail',
        header: 'Detail',
        width: DETAIL_WIDTH,
        sortable: true,
        ...sorted('detail'),
        cell: (row) => <span title={row.detail ?? undefined}>{row.detail ?? '–'}</span>,
        text: (row) => row.detail ?? '',
      },
    ];
  }, [sort, urlWidth, siteOrigin]);

  const shown = rows.rowCount ?? Math.min(Math.max(total, 1), 8);
  const height = Math.max(shown, 1) * rowHeight + headerHeight + 2;

  if (rows.error && rows.rowCount === null) return <ErrorState error={rows.error} onRetry={rows.refetch} compact />;

  return (
    <div ref={measure} className="flex max-h-(--evidence-max-height) min-h-0 flex-col" style={{ height }}>
      <GridCore<IssueRow>
        key={JSON.stringify(queryKey)}
        label={`${check.title}: affected pages`}
        columns={columns}
        rowCount={rows.rowCount}
        getRow={rows.getRow}
        rowHeight={rowHeight}
        headerHeight={headerHeight}
        onRangeChange={rows.onRangeChange}
        onScrollOffset={rows.onScrollOffset}
        fetchRows={rows.fetchRows}
        onSort={(column) => onSortChange(nextSort(sort, column))}
        onActivate={(_index, row) => {
          if (row) onInspect(row.id);
        }}
        busy={rows.isRefreshing}
        className="flex-1 rounded-md border border-border"
      />
    </div>
  );
}
