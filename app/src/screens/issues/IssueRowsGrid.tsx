import { useQuery } from '@tanstack/react-query';
import { useCallback, useMemo } from 'react';
import { api } from '../../api/client';
import type { GridSort, IssueRow } from '../../api/grids';
import { queryKeys, urlColumnsQuery } from '../../api/queries';
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
import { urlGridColumns } from '../urls/urlColumns';
import { flatRowColumns } from './evidenceColumns';
import { IssuePageMenu } from './IssuePageMenu';
import { WhereFoundLine } from './WhereFoundLine';
import { referencesOf } from './whereFound';
import { evidenceFrameClass } from './evidenceFrame';
import { periodLabel, rowChangeCell, type ComparedPeriod } from '../compare/changeCell';

/** The fix loop: the rows compared with another crawl, each with how it stood then. */
export interface RowsCompare {
  compareWith: string;
  period: ComparedPeriod;
}

const CHANGE_WIDTH = 132;

/** An issue's identity among a check's rows: a URL can be flagged more than once, by detail or by related URL. */
function issueRowKey(row: IssueRow): string {
  return `${row.id}|${row.detail ?? ''}|${row.relatedUrlId ?? ''}`;
}

const DETAIL_WIDTH = 260;
/** The detail beside a check's evidence: the evidence says the rest, so the detail gives way to it. */
const DETAIL_WITH_EVIDENCE_WIDTH = 220;
/** "Found on 3 pages · /garden-furniture/ +2", for a check whose URLs other pages refer to. */
const WHERE_WIDTH = 300;
const MIN_URL_WIDTH = 240;
/** The least the URL and each column of the evidence's words get, where they share the room. */
const MIN_SHARED_URL_WIDTH = 200;
const MIN_WORDS_WIDTH = 160;
/** The same in a narrow panel (a 1280 px window zoomed to 150%): the words cut sooner, the figures stay whole. */
const TIGHT_DETAIL_WIDTH = 180;
const TIGHT_URL_WIDTH = 160;
const TIGHT_WORDS_WIDTH = 120;
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
  /** The fix loop: a column of how each row stood in the crawl compared with. Left out, none. */
  compare?: RowsCompare | null | undefined;
}

/**
 * A check that doesn't group (its detail is a measurement, "612 px, over 580 (64 characters)", or the same on every
 * page): its URLs flat, server-paged and sortable, with the detail as a column (E3a, `/rows`), then the check's evidence
 * (the title and its width, the redirect chain, the robots.txt rule: docs/v2-evidence.md), then the URL's inlinks (or the
 * pages using it), which the engine's order goes by when no sort is chosen: the pages that matter most first.
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
  compare = null,
}: IssueRowsGridProps) {
  const compareWith = compare?.compareWith ?? null;
  const { rowHeight, headerHeight } = useGridMetrics();
  const [measure, width] = useElementWidth();
  // The URL, the detail, the check's evidence and what orders the rows: one key per set, so a check's own are cached.
  const asked = useMemo(() => flatRowColumns(check), [check]);
  const extra = useMemo(() => asked.filter((id) => id !== 'url' && id !== 'detail'), [asked]);
  const catalogue = useQuery(urlColumnsQuery()).data;
  const queryKey = useMemo(
    () => [...queryKeys.issueRowsGrid(crawlId, check.id, sort), asked, compareWith ?? 'none'] as const,
    [crawlId, check.id, sort, asked, compareWith],
  );
  const fetchPage = useCallback(
    (offset: number, limit: number, signal: AbortSignal) =>
      api.issueRows(
        crawlId,
        check.id,
        {
          columns: asked,
          filters: [],
          sort: [...sort],
          offset,
          limit,
          ...(compareWith ? { compareWith } : {}),
        },
        signal,
      ),
    [crawlId, check.id, sort, asked, compareWith],
  );
  const rows = usePagedRows<IssueRow>({ queryKey, fetchPage, rowHeight });
  const handoff = useHeaderHandoff(sort);
  const rowMenu = useRowMenu<IssueRow>();
  // The row as it was when the menu opened: rows can move under a running crawl while it is open.
  const menuRow = rowMenu.state?.menu.row;

  const references = referencesOf(check);
  const whereWidth = references === 'none' ? 0 : WHERE_WIDTH;
  // The evidence's columns, drawn as the URL grid draws them: a number right-aligned, a chain hop by hop.
  const drawn = useMemo<GridColumn<IssueRow>[]>(
    () => urlGridColumns(extra, catalogue, { siteOrigin, fullUrls: false, sort, crawling: false }),
    [extra, catalogue, siteOrigin, sort],
  );
  // The room: the detail, the figures and the where line keep their widths; the URL and the evidence's words (a
  // title, a chain) share the rest, so a narrow window keeps every figure in view rather than scrolling to it.
  const words = drawn.filter((column) => !column.numeric).length;
  const fixed = drawn.filter((column) => column.numeric).reduce((sum, column) => sum + column.width, 0);
  const available = (width ?? 720) - fixed - whereWidth - (compare ? CHANGE_WIDTH : 0) - FRAME_SLACK;
  // Where the usual widths don't fit, the words take less: a figure is never scrolled out of view for them.
  const usual = extra.length > 1 ? DETAIL_WITH_EVIDENCE_WIDTH : DETAIL_WIDTH;
  const tight = words > 0 && available < usual + MIN_SHARED_URL_WIDTH + MIN_WORDS_WIDTH * words;
  const detailWidth = tight ? TIGHT_DETAIL_WIDTH : usual;
  const room = available - detailWidth;
  const urlWidth =
    words > 0
      ? Math.max(tight ? TIGHT_URL_WIDTH : MIN_SHARED_URL_WIDTH, Math.round(room * 0.4))
      : Math.max(MIN_URL_WIDTH, room);
  const wordsWidth =
    words > 0 ? Math.max(tight ? TIGHT_WORDS_WIDTH : MIN_WORDS_WIDTH, Math.floor((room - urlWidth) / words)) : 0;
  const evidenceColumns = useMemo(
    () => drawn.map((column) => (column.numeric ? column : { ...column, width: wordsWidth })),
    [drawn, wordsWidth],
  );
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
        width: detailWidth,
        sortable: true,
        ...sorted('detail'),
        cell: (row) => <span title={row.detail ?? undefined}>{row.detail ?? '–'}</span>,
        text: (row) => row.detail ?? '',
      },
      ...evidenceColumns,
      // The fix loop: how the row stood in the crawl compared with (Still, New, New URL, Not compared).
      ...(compare
        ? [
            {
              id: 'change',
              header: periodLabel(compare.period),
              width: CHANGE_WIDTH,
              cell: (row: IssueRow) => {
                if (!row.change) return <span className="text-text-muted">–</span>;
                const cell = rowChangeCell(row.change, compare.period);
                return <span title={cell.title}>{cell.shown}</span>;
              },
              text: (row: IssueRow) => (row.change ? rowChangeCell(row.change, compare.period).shown : ''),
            },
          ]
        : []),
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
  }, [sort, urlWidth, detailWidth, evidenceColumns, siteOrigin, references, crawlId, check, compare]);
  // The columns as the user laid them out (resized, moved, pinned), kept for this grid with the preferences.
  const laid = useColumnLayout('issues.rows', columns);

  const shown = rows.rowCount ?? Math.min(Math.max(total, 1), 8);
  const height = Math.max(shown, 1) * rowHeight + headerHeight + 2;

  if (rows.error && rows.rowCount === null) return <ErrorState error={rows.error} onRetry={rows.refetch} compact />;

  return (
    <div ref={measure} className={evidenceFrameClass(shown)} style={{ height }}>
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
