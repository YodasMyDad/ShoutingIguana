import { useCallback, useMemo } from 'react';
import { api } from '../../api/client';
import type { GridSort, IssueRow } from '../../api/grids';
import { queryKeys } from '../../api/queries';
import type { Check } from '../../api/types';
import { affectedWords } from '../../crawl/checkSubject';
import { ErrorState } from '../../feedback/ErrorState';
import { GridCore } from '../../grid/GridCore';
import { useColumnLayout } from '../../grid/useColumnLayout';
import { useRowMenu } from '../../grid/rowMenuState';
import { useGridMetrics } from '../../grid/metrics';
import { nextSort, useHeaderHandoff } from '../../grid/sort';
import type { GridColumn } from '../../grid/types';
import { useElementWidth } from '../../grid/useElementWidth';
import { usePagedRows } from '../../grid/usePagedRows';
import { Url } from '../../ui/Url';
import { linksOfUrl } from '../links/linkPlaces';
import { IssuePageMenu } from './IssuePageMenu';
import { WhereFoundLine } from './WhereFoundLine';
import { referencesOf } from './whereFound';

/** An issue's identity among a check's rows: a URL can be flagged more than once, by detail or by related URL. */
function issueRowKey(row: IssueRow): string {
  return `${row.id}|${row.detail ?? ''}|${row.relatedUrlId ?? ''}`;
}

const DETAIL_WIDTH = 260;
/** "Found on 3 pages · /garden-furniture/ +2", for a check whose URLs other pages refer to. */
const WHERE_WIDTH = 300;
const MIN_URL_WIDTH = 240;
const FRAME_SLACK = 24;

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
  /** While the inspector is open: the URL under the grid's cursor, which it follows. */
  onFollow?: ((urlId: number) => void) | undefined;
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
  onFollow,
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
  const handoff = useHeaderHandoff(sort);
  const rowMenu = useRowMenu<IssueRow>();
  // The row as it was when the menu opened: rows can move under a running crawl while it is open.
  const menuRow = rowMenu.state?.menu.row;

  const references = referencesOf(check);
  const whereWidth = references === 'none' ? 0 : WHERE_WIDTH;
  const urlWidth = Math.max(MIN_URL_WIDTH, (width ?? 720) - DETAIL_WIDTH - whereWidth - FRAME_SLACK);
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
        cell: (row) => <Url url={row.url ?? ''} siteOrigin={siteOrigin} copyable={false} tabbable={false} />,
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
      // Where each is found (or what each page names): read for the rows drawn, a page of them in one request.
      ...(references === 'none'
        ? []
        : [
            {
              id: 'where',
              header: references === 'sources' ? 'Found on' : 'Names',
              width: WHERE_WIDTH,
              cell: (row: IssueRow) => (
                <WhereFoundLine
                  crawlId={crawlId}
                  check={check}
                  urlId={row.id}
                  siteOrigin={siteOrigin}
                  className="block"
                />
              ),
              text: () => '',
            },
          ]),
    ];
  }, [sort, urlWidth, siteOrigin, references, crawlId, check]);
  // The columns as the user laid them out (resized, moved, pinned), kept for this grid with the preferences.
  const laid = useColumnLayout('issues.rows', columns);

  const shown = rows.rowCount ?? Math.min(Math.max(total, 1), 8);
  const height = Math.max(shown, 1) * rowHeight + headerHeight + 2;

  if (rows.error && rows.rowCount === null) return <ErrorState error={rows.error} onRetry={rows.refetch} compact />;

  return (
    <div ref={measure} className="flex max-h-(--evidence-max-height) min-h-0 flex-col" style={{ height }}>
      <GridCore<IssueRow>
        key={JSON.stringify(queryKey)}
        label={`${check.title}: ${affectedWords(check).named}`}
        columns={laid.columns}
        columnLayout={laid.columnLayout}
        rowCount={rows.rowCount}
        getRow={rows.getRow}
        rowHeight={rowHeight}
        headerHeight={headerHeight}
        onRangeChange={rows.onRangeChange}
        onScrollOffset={rows.onScrollOffset}
        fetchRows={rows.fetchRows}
        onSort={(column, focused) => {
          const next = nextSort(sort, column);
          handoff.sorted(column, focused, next);
          onSortChange(next);
        }}
        focusHeader={handoff.focusHeader}
        onActivate={(_index, row) => {
          if (row) onInspect(row.id);
        }}
        onActiveRowChange={(index) => {
          const row = onFollow && index !== null ? rows.getRow(index) : undefined;
          if (row) onFollow?.(row.id);
        }}
        // The cursor is on an issue, not at a row number: the rows read again (an analysis) keep it on its own.
        rowKey={issueRowKey}
        onContextMenu={rowMenu.onContextMenu}
        busy={rows.isRefreshing}
        className="flex-1 rounded-md border border-border"
      />
      <IssuePageMenu<IssueRow>
        crawlId={crawlId}
        state={rowMenu.state}
        onOpenChange={rowMenu.onOpenChange}
        siteOrigin={siteOrigin}
        page={menuRow?.url ? { id: menuRow.id, url: menuRow.url } : null}
        foundIn={menuRow && references === 'sources' ? linksOfUrl('inlinks', menuRow.id, 'all') : null}
        urlOf={(row) => row.url}
        onInspect={onInspect}
      />
    </div>
  );
}
