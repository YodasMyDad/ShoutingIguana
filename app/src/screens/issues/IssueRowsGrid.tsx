import { useQuery } from '@tanstack/react-query';
import { useNavigate } from '@tanstack/react-router';
import { useCallback, useMemo, useState } from 'react';
import { api } from '../../api/client';
import type { GridSort, IssueRow } from '../../api/grids';
import { queryKeys, urlColumnsQuery } from '../../api/queries';
import type { Check, CheckFile } from '../../api/types';
import { robotsOriginOf } from '../../inspector/placeLinks';
import { formatCount } from '../../lib/format';
import { useFontsLoaded } from '../../lib/useFontsLoaded';
import { TextLink } from '../../ui/TextLink';
import { affectedWords } from '../../crawl/checkSubject';
import { ErrorState } from '../../feedback/ErrorState';
import { GridCore } from '../../grid/GridCore';
import { useColumnLayout } from '../../grid/useColumnLayout';
import { useRowMenu } from '../../grid/rowMenuState';
import { useGridMetrics } from '../../grid/metrics';
import { nextSort, useHeaderHandoff } from '../../grid/sort';
import type { GridColumn, GridRange, GridTree, RowFetcher } from '../../grid/types';
import { useElementWidth } from '../../grid/useElementWidth';
import { usePagedRows } from '../../grid/usePagedRows';
import { linkTextMeasure } from '../links/linkColumns';
import { linksOfUrl } from '../links/linkPlaces';
import { imagesOfPage } from '../links/presets';
import { urlGridColumns } from '../urls/urlColumns';
import { flatRowColumns, wrappedLines, wrapsText } from './evidenceColumns';
import { fitColumns, type ColumnNeed } from './fitColumns';
import { IssuePageMenu } from './IssuePageMenu';
import { GRID_FOOT_HEIGHT, ISSUE_KEY_HINTS, leastFrameHeight } from './issueGrid';
import { DetailWords } from './DetailWords';
import { IssueUrl } from './IssueUrl';
import { URL_OPEN_ROOM, shownUrlWidth } from './issueUrlWidth';
import { checkPlace, keepCheckPlace } from './issuesPlace';
import { useKeptScroll } from './useKeptScroll';
import {
  MoreToggle,
  ReferenceOther,
  ReferencePlace,
  ReferenceRest,
  ReferenceText,
  type ReferenceView,
} from './ReferenceCells';
import { openAtLink, referencePlace, referenceRest, referenceText } from './referenceWords';
import { OPEN_ALL_ROWS, rowsLayout, type OpenRow } from './rowsTree';
import { useRowReferences, type RowReferences } from './useRowReferences';
import { detailSaysMore, referenceColumns, referencesOf, whereRowText, type WhereRow } from './whereFound';
import { evidenceFrameClass } from './evidenceFrame';
import { periodLabel, rowChangeCell, type ComparedPeriod } from '../compare/changeCell';
import { recheckable } from '../rechecks/rechecks';

/** The fix loop: the rows compared with another crawl, each with how it stood then. */
export interface RowsCompare {
  compareWith: string;
  period: ComparedPeriod;
}

/**
 * A row of a check's list: one of its URLs, with its references once they're read (the first is drawn in its own row),
 * or, under a URL that's open, another of its references: another page linking to it, another URL its page names.
 */
export type ListRow =
  | { kind: 'issue'; row: IssueRow; refs: RowReferences | undefined; open: boolean }
  | { kind: 'ref'; of: IssueRow; where: WhereRow; at: number };

/** Wrappers kept per row object, so a row the grid has drawn keeps its identity (and its memoised cells). */
const issueRows = new WeakMap<IssueRow, ListRow & { kind: 'issue' }>();
const refRows = new WeakMap<WhereRow, ListRow>();

function issueRowOf(row: IssueRow, refs: RowReferences | undefined, open: boolean): ListRow {
  const kept = issueRows.get(row);
  if (kept && kept.refs === refs && kept.open === open) return kept;
  const made = { kind: 'issue' as const, row, refs, open };
  issueRows.set(row, made);
  return made;
}

function refRowOf(of: IssueRow, where: WhereRow, at: number): ListRow {
  let row = refRows.get(where);
  if (!row) {
    row = { kind: 'ref', of, where, at };
    refRows.set(where, row);
  }
  return row;
}

/** The URL a row stands for: its own, or for a row under it the page or URL that row names. */
function urlOfRow(row: ListRow): { id: number; url: string } | null {
  if (row.kind === 'issue') return row.row.url ? { id: row.row.id, url: row.row.url } : null;
  const { where } = row;
  if (where.kind === 'source') return { id: where.link.id, url: where.link.url };
  if (where.kind === 'target') return { id: where.target.id, url: where.target.url };
  return row.of.url ? { id: row.of.id, url: row.of.url } : null;
}

const CHANGE_WIDTH = 132;
/** An issue's identity among a check's rows: a URL can be flagged more than once, by detail or by related URL. */
function issueRowKey(row: IssueRow): string {
  return `${row.id}|${row.detail ?? ''}|${row.relatedUrlId ?? ''}`;
}

function listRowKey(row: ListRow): string {
  return row.kind === 'issue' ? issueRowKey(row.row) : `${issueRowKey(row.of)}|ref|${row.at}`;
}

/** A cell's padding on both sides (`--grid-cell-x`). */
const CELL_PADDING = 24;
/** What a URL's column is cut to at most where the columns don't all fit, as a share of the frame. */
const URL_SHARE = 0.42;
const MIN_URL_WIDTH = 180;
const MIN_WORDS_WIDTH = 120;
/** A wrapped text's column: wide enough to read, never a 40-character sliver, never the whole pane. */
const MIN_WRAPPED_WIDTH = 200;
const MAX_WRAPPED_WIDTH = 520;
const MAX_WORDS_WIDTH = 480;
/** A status after a URL ("404", "No response"), and a row's "+12 more". */
const STATUS_ROOM = 64;
const MORE_ROOM = 84;
/** A detail said on every row at this length or more is said once, above the list, not in a column of its own. */
const LONG_DETAIL = 28;
const FRAME_SLACK = 24;
/** "Open in Sitemaps", "Open robots.txt": a file's way to its own screen. */
const OPEN_FILE_WIDTH = 160;
/** A figure's column is at least as wide as its header's words ("Used on pages"), never cut to "sed on pages". */
const HEADER_CHAR_WIDTH = 7.5;
const HEADER_SLACK = 40;
/** A wrapped line's height, and the row's room above and below its lines. */
const LINE_HEIGHT = 18;
const LINE_HEIGHT_COMFORTABLE = 20;
const ROW_PADDING = 12;
/** The lines a wrapped text takes at most, as classes that compile (styles/classes.test.ts). */
const CLAMP: Readonly<Record<number, string>> = { 2: 'line-clamp-2', 3: 'line-clamp-3', 4: 'line-clamp-4' };

/** What heads the column of the other URL a row has, for a check that refers to nothing: the page it's the same as. */
function relatedHeader(check: Pick<Check, 'id'>): string {
  return check.id.endsWith('-duplicate') || check.id.includes('duplicate') ? 'Same as' : 'Related URL';
}

export interface IssueRowsGridProps {
  crawlId: string;
  check: Check;
  /**
   * For a check of the site's own files (`about`): the robots.txt or sitemaps it found at fault, named in place of the
   * home page its issue is reported on, with the way to them. Null or left out: the row's own URL.
   */
  files?: readonly CheckFile[] | null | undefined;
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
  /** The keys said under the rows; false in a window too short to spare a row for them. */
  legend?: boolean | undefined;
}

/**
 * A check's URLs as a list (A-06: the default for every check whose rows are one fault each): one row a URL,
 * server-paged and sortable, with its detail, the check's evidence (the title and its width, the redirect chain:
 * docs/v2-evidence.md) and, for a check whose URLs are referred to, where each is found as columns: the page that
 * links to it (a link, opened in the browser at the link's words), what the link says and where on the page it is. A
 * URL with more than one reference says "+2 more", which opens the rest under it in place; a list of up to
 * `OPEN_ALL_ROWS` rows opens them all, so a page with five broken links is five lines that read alike.
 */
export function IssueRowsGrid({
  crawlId,
  check,
  files = null,
  total,
  siteOrigin,
  sort,
  onSortChange,
  onInspect,
  onFollow,
  compare = null,
  legend = true,
}: IssueRowsGridProps) {
  const compareWith = compare?.compareWith ?? null;
  const metrics = useGridMetrics();
  const { headerHeight } = metrics;
  const [measure, width] = useElementWidth();
  const references = referencesOf(check);
  const refColumns = referenceColumns(check);
  // The URL, the detail, the check's evidence and what orders the rows: one key per set, so a check's own are cached.
  // A check of the site's files names them: the home page's own figures (its links) say nothing of them. A check
  // that groups by another URL and refers to none (a duplicate's twin) shows that URL in a column.
  const asked = useMemo(() => {
    const own = files
      ? flatRowColumns(check).filter((id) => id !== 'inlinks' && id !== 'usedOn')
      : flatRowColumns(check);
    return check.grouping === 'relatedUrl' && references === 'none' ? [...own, 'relatedUrl', 'relatedUrlId'] : own;
  }, [check, files, references]);
  const extra = useMemo(
    () => asked.filter((id) => id !== 'url' && id !== 'detail' && id !== 'relatedUrl' && id !== 'relatedUrlId'),
    [asked],
  );
  // The text an owner has to change (a title, a description) wraps over two or three lines rather than being cut, in
  // rows as tall as that takes.
  const lines = wrappedLines(extra);
  const rowHeight =
    lines > 1
      ? Math.max(
          metrics.rowHeight,
          lines * (metrics.rowHeight >= 40 ? LINE_HEIGHT_COMFORTABLE : LINE_HEIGHT) + ROW_PADDING,
        )
      : metrics.rowHeight;
  const catalogue = useQuery(urlColumnsQuery()).data;
  const queryKey = useMemo(
    () => [...queryKeys.issueRowsGrid(crawlId, check.id, sort), asked, compareWith ?? 'none'] as const,
    [crawlId, check.id, sort, asked, compareWith],
  );
  const gridKey = JSON.stringify(queryKey);
  // The files a check of the site's files is about, by address: its rows' details say them by name.
  const fileUrls = useMemo(() => (files ?? []).map((file) => file.url), [files]);
  // Where its rows were scrolled to when the check was left (A-07), with the frame's width measured by the same ref.
  const keepScroll = useKeptScroll(crawlId, check.id, gridKey);
  const measureFrame = useCallback(
    (element: HTMLDivElement | null) => {
      measure(element);
      keepScroll(element);
    },
    [measure, keepScroll],
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
  const rowCount = rows.rowCount;
  const getIssueRow = rows.getRow;
  const handoff = useHeaderHandoff(sort);
  const rowMenu = useRowMenu<ListRow>();
  // The row as it was when the menu opened: rows can move under a running crawl while it is open.
  const menuRow = rowMenu.state?.menu.row;

  // The rows opened or closed by hand, by their place in this order of the list; a list short enough to read whole
  // opens every row that has more. Kept with the check for the session, so Back finds them as they were (A-07).
  const [toggledState, setToggled] = useState<{ of: string; rows: ReadonlyMap<number, boolean> }>(() => {
    const place = checkPlace(crawlId, check.id);
    return { of: place.toggledOf ?? gridKey, rows: place.toggled ?? new Map() };
  });
  const toggled = toggledState.of === gridKey ? toggledState.rows : NONE_TOGGLED;
  const opensAll = references !== 'none' && rowCount !== null && rowCount <= OPEN_ALL_ROWS;
  const isOpen = useCallback(
    (row: number, refs: RowReferences | undefined) =>
      toggled.get(row) ??
      (opensAll && refs?.rows.slice(1).some((where) => where.kind === 'source' || where.kind === 'target') === true),
    [toggled, opensAll],
  );

  // The rows in view, and the open ones wherever they are: their references are read (a page of them in one request).
  const [inView, setInView] = useState({ first: 0, last: OPEN_ALL_ROWS });
  const wanted = useMemo(() => {
    if (references === 'none' || rowCount === null) return [];
    const indices = new Set<number>();
    for (let row = Math.max(0, inView.first); row <= Math.min(rowCount - 1, inView.last); row += 1) indices.add(row);
    if (opensAll) for (let row = 0; row < rowCount; row += 1) indices.add(row);
    for (const [row, open] of toggled) if (open && row < rowCount) indices.add(row);
    return [...indices].sort((a, b) => a - b);
    // eslint-disable-next-line react-hooks/exhaustive-deps -- the rows read since change which of these have ids
  }, [references, rowCount, inView.first, inView.last, opensAll, toggled, rows.firstPage, getIssueRow]);
  const wantedIds = wanted.flatMap((row) => {
    const issue = getIssueRow(row);
    return issue ? [issue.id] : [];
  });
  const refsById = useRowReferences(crawlId, check, [...new Set(wantedIds)], siteOrigin);

  // Where every row is: the check's rows, and under each open one the rest of its references.
  const open: OpenRow[] = [];
  if (references !== 'none' && rowCount !== null) {
    for (const row of wanted) {
      const issue = getIssueRow(row);
      const refs = issue ? refsById.get(issue.id) : undefined;
      if (refs && refs.rows.length > 1 && isOpen(row, refs)) open.push({ row, children: refs.rows.length - 1 });
    }
  }
  const openKey = open.map((entry) => `${entry.row}:${entry.children}`).join(',');
  // eslint-disable-next-line react-hooks/exhaustive-deps -- `open` is built each time; its key says when it changed
  const layout = useMemo(() => rowsLayout(rowCount ?? 0, open), [rowCount, openKey]);

  const getRow = useCallback(
    (index: number): ListRow | undefined => {
      const place = layout.place(index);
      const issue = getIssueRow(place.row);
      if (!issue) return undefined;
      const refs = references === 'none' ? undefined : refsById.get(issue.id);
      if (place.kind === 'row') return issueRowOf(issue, refs, isOpen(place.row, refs));
      const where = refs?.rows[place.child + 1];
      return where ? refRowOf(issue, where, place.child + 1) : undefined;
    },
    [layout, getIssueRow, references, refsById, isOpen],
  );

  const toRows = (range: GridRange): GridRange => ({
    first: layout.place(range.first).row,
    last: layout.place(range.last).row,
    scrubbing: range.scrubbing,
  });
  const fetchIssueRows = rows.fetchRows;
  const fetchRows = useCallback<RowFetcher<ListRow>>(
    async (indices) => {
      const places = indices.map((index) => layout.place(index));
      const wantedRows = places.flatMap((place) => (place.kind === 'row' ? [place.row] : []));
      const fetched = await fetchIssueRows(wantedRows);
      const byRow = new Map(wantedRows.map((row, at) => [row, fetched[at]]));
      return indices.map((index, at) => {
        const place = places[at];
        if (place?.kind !== 'row') return getRow(index);
        const issue = getIssueRow(place.row) ?? byRow.get(place.row);
        const refs = issue ? refsById.get(issue.id) : undefined;
        return issue ? issueRowOf(issue, refs, isOpen(place.row, refs)) : undefined;
      });
    },
    [layout, fetchIssueRows, getIssueRow, getRow, refsById, isOpen],
  );

  const tree = useMemo<GridTree | undefined>(
    () =>
      references === 'none'
        ? undefined
        : {
            level: (index) => (layout.place(index).kind === 'row' ? 1 : 2),
            expanded: (index) => {
              const place = layout.place(index);
              if (place.kind !== 'row') return undefined;
              const issue = getIssueRow(place.row);
              const refs = issue ? refsById.get(issue.id) : undefined;
              return refs && refs.rows.length > 1 ? isOpen(place.row, refs) : undefined;
            },
            parent: (index) => {
              const place = layout.place(index);
              return place.kind === 'child' ? layout.indexOf(place.row) : null;
            },
            // Enter inspects the row's URL; its references open with Right, Left and "+2 more".
            activates: true,
            onToggle: (index, opening) => {
              const place = layout.place(index);
              const next = new Map(toggled).set(place.row, opening);
              setToggled({ of: gridKey, rows: next });
              keepCheckPlace(crawlId, check.id, { toggled: next, toggledOf: gridKey });
              return layout.indexOf(place.row);
            },
          },
    [references, layout, getIssueRow, refsById, isOpen, toggled, gridKey, crawlId, check.id],
  );

  // A check whose rows say nothing in their detail (the URL is the fault) has no Detail column of dashes; nor one
  // whose detail only counts what its row lists ("5 broken URLs"), or says the same long sentence on every row, which
  // is said once above the list (A-14).
  const firstRows = rows.firstPage?.rows;
  const allLoaded = firstRows !== undefined && rowCount !== null && firstRows.length >= rowCount;
  const firstDetail = firstRows?.[0]?.detail ?? null;
  const sameDetail =
    allLoaded && firstRows.length > 1 && firstDetail !== null && firstRows.every((row) => row.detail === firstDetail);
  const saidOnce = sameDetail && firstDetail.length >= LONG_DETAIL ? firstDetail : null;
  const noDetail =
    firstRows?.every((row) => !row.detail) === true ||
    saidOnce !== null ||
    (references === 'targets' && firstRows?.every((row) => !row.detail || !detailSaysMore(row.detail)) === true);
  const showRelated = asked.includes('relatedUrl') && firstRows?.some((row) => row.relatedUrl) === true;
  // What the check says of each URL a page names beyond the columns' own: a column only where it says something.
  const view = useMemo<ReferenceView>(
    () => ({ crawlId, check, siteOrigin, onInspect }),
    [crawlId, check, siteOrigin, onInspect],
  );
  const firstRefs = (firstRows ?? []).flatMap((row) => refsById.get(row.id)?.rows ?? []);
  const showRest = references === 'targets' && firstRefs.some((where) => referenceRest(where, view) !== '');
  const refsRead = firstRefs.length;

  // The evidence's columns, drawn as the URL grid draws them: a number right-aligned, a chain hop by hop.
  const sameType =
    allLoaded &&
    firstRows.length > 1 &&
    firstRows.every((row) => row.type === firstRows[0]?.type) &&
    extra.includes('type');
  const shownExtra = useMemo(() => (sameType ? extra.filter((id) => id !== 'type') : extra), [extra, sameType]);
  const drawn = useMemo<GridColumn<IssueRow>[]>(
    () =>
      urlGridColumns(shownExtra, catalogue, { siteOrigin, fullUrls: false, sort, crawling: false }).map((column) => {
        if (column.numeric) {
          const room = Math.ceil(column.header.length * HEADER_CHAR_WIDTH + HEADER_SLACK);
          return { ...column, width: Math.max(column.width, room) };
        }
        if (!wrapsText(column.id)) return column;
        return {
          ...column,
          cell: (row: IssueRow) => {
            const text = column.text(row);
            return text ? (
              <span className={`whitespace-normal ${CLAMP[lines] ?? 'line-clamp-2'}`} title={text}>
                {text}
              </span>
            ) : (
              <span className="text-text-muted">–</span>
            );
          },
        };
      }),
    [shownExtra, catalogue, siteOrigin, sort, lines],
  );

  // Each column as wide as what the rows read so far need (rule 8): the URL to its longest path, the words to their
  // longest value, a figure to its header. Where they don't all fit the words give way before a figure is scrolled
  // out of view, and the URL takes no more than its share. Measured again as the fonts and the references arrive.
  const fontsLoaded = useFontsLoaded();
  const frame = Math.max(0, (width ?? 720) - FRAME_SLACK);
  const widths = useMemo(() => {
    const measureText = linkTextMeasure();
    const sample = firstRows ?? [];
    const widest = (texts: readonly string[], face: 'url' | 'text') =>
      texts.reduce((most, text) => Math.max(most, text ? measureText(text, face) : 0), 0);
    const header = (words: string) => Math.ceil(words.length * HEADER_CHAR_WIDTH + HEADER_SLACK);
    const needs: ColumnNeed[] = [];
    const numericIds = new Set(drawn.filter((column) => column.numeric).map((column) => column.id));
    const urlNeed =
      sample.reduce(
        (most, row) => Math.max(most, row.url ? shownUrlWidth(row.url, siteOrigin, 'address', measureText) : 0),
        0,
      ) +
      URL_OPEN_ROOM +
      CELL_PADDING +
      (files && files.length > 1 ? 80 : 0);
    const urlCap = Math.max(MIN_URL_WIDTH, Math.round(frame * URL_SHARE));
    needs.push({
      id: 'url',
      need: Math.min(Math.max(MIN_URL_WIDTH, Math.ceil(urlNeed)), Math.max(urlCap, MIN_URL_WIDTH)),
      min: MIN_URL_WIDTH - 20,
    });
    if (!noDetail) {
      const need = widest(
        sample.map((row) => row.detail ?? ''),
        'text',
      );
      needs.push({
        id: 'detail',
        need: Math.min(MAX_WORDS_WIDTH, Math.max(header('Detail'), Math.ceil(need) + CELL_PADDING)),
        min: MIN_WORDS_WIDTH,
      });
    }
    if (showRelated) {
      const need = sample.reduce(
        (most, row) =>
          Math.max(most, row.relatedUrl ? shownUrlWidth(row.relatedUrl, siteOrigin, 'address', measureText) : 0),
        0,
      );
      needs.push({
        id: 'related',
        need: Math.min(urlCap, Math.max(MIN_URL_WIDTH, Math.ceil(need) + URL_OPEN_ROOM + CELL_PADDING)),
        min: MIN_URL_WIDTH - 20,
      });
    }
    for (const column of drawn) {
      if (column.numeric) {
        needs.push({ id: column.id, need: column.width, min: column.width });
        continue;
      }
      const text = widest(
        sample.map((row) => column.text(row)),
        'text',
      );
      const wraps = wrapsText(column.id);
      const need = wraps
        ? Math.min(
            MAX_WRAPPED_WIDTH,
            Math.max(MIN_WRAPPED_WIDTH, Math.ceil(text / Math.max(1, lines - 1)) + CELL_PADDING),
          )
        : Math.min(MAX_WORDS_WIDTH, Math.max(header(column.header), Math.ceil(text) + CELL_PADDING));
      needs.push({ id: column.id, need, min: wraps ? MIN_WRAPPED_WIDTH - 40 : Math.min(need, MIN_WORDS_WIDTH) });
    }
    if (refColumns) {
      const refs = sample.flatMap((row) => refsById.get(row.id)?.rows ?? []);
      const other = refs.reduce((most, where) => {
        if (where.kind === 'source')
          return Math.max(most, shownUrlWidth(where.link.url, siteOrigin, 'address', measureText));
        if (where.kind === 'target') {
          return Math.max(
            most,
            shownUrlWidth(
              where.target.url,
              siteOrigin,
              where.target.kind === 'image' ? 'file' : 'address',
              measureText,
            ) + STATUS_ROOM,
          );
        }
        return Math.max(most, measureText(whereRowText(where), 'text'));
      }, 0);
      const anyMore = sample.some((row) => (refsById.get(row.id)?.more ?? 0) > 0);
      needs.push({
        id: 'other',
        need: Math.min(
          urlCap + STATUS_ROOM,
          Math.max(MIN_URL_WIDTH, Math.ceil(other) + URL_OPEN_ROOM + CELL_PADDING + (anyMore ? MORE_ROOM : 0)),
        ),
        min: MIN_URL_WIDTH - 20,
      });
      if (refColumns.text) {
        const text = widest(
          refs.map((where) => {
            const words = referenceText(where);
            return words ? `“${words}”` : 'No alt text';
          }),
          'text',
        );
        needs.push({
          id: 'text',
          need: Math.min(360, Math.max(header(refColumns.text), Math.ceil(text) + CELL_PADDING)),
          min: 90,
        });
      }
      if (refColumns.place) {
        const place = widest(
          refs.map((where) => referencePlace(where)),
          'text',
        );
        needs.push({
          id: 'place',
          need: Math.min(260, Math.max(header('Where on the page'), Math.ceil(place) + CELL_PADDING)),
          min: 80,
        });
      }
      if (showRest) {
        const rest = widest(
          refs.map((where) => referenceRest(where, view)),
          'text',
        );
        needs.push({
          id: 'rest',
          need: Math.min(MAX_WORDS_WIDTH, Math.max(header('Details'), Math.ceil(rest) + CELL_PADDING)),
          min: MIN_WORDS_WIDTH,
        });
      }
    }
    if (files?.[0]) needs.push({ id: 'open', need: OPEN_FILE_WIDTH, min: OPEN_FILE_WIDTH });
    if (compare) needs.push({ id: 'change', need: CHANGE_WIDTH, min: CHANGE_WIDTH });
    const fitted = fitColumns(needs, frame);
    const last = needs.at(-1);
    if (last && fitted.spare > 0 && !numericIds.has(last.id)) {
      fitted.widths[last.id] = (fitted.widths[last.id] ?? last.need) + fitted.spare;
    }
    return fitted.widths;
    // eslint-disable-next-line react-hooks/exhaustive-deps -- the fonts and the references arriving change what's measured
  }, [
    firstRows,
    refsRead,
    frame,
    noDetail,
    showRelated,
    showRest,
    drawn,
    refColumns,
    files,
    compare,
    siteOrigin,
    lines,
    view,
    fontsLoaded,
  ]);

  const columns = useMemo<GridColumn<ListRow>[]>(() => {
    const sorted = (id: string) => {
      const entry = sort.find((candidate) => candidate.column === id);
      return entry ? { sort: entry.desc ? ('descending' as const) : ('ascending' as const) } : {};
    };
    const widthOf = (id: string, fallback: number) => widths[id] ?? fallback;
    const issue = (row: ListRow) => (row.kind === 'issue' ? row.row : null);
    /** The reference a row draws in its columns: a URL's first, or the one a row under it is. */
    const whereOf = (row: ListRow): WhereRow | null => (row.kind === 'issue' ? (row.refs?.rows[0] ?? null) : row.where);
    const subjectOf = (row: ListRow) => (row.kind === 'issue' ? row.row : row.of);
    return [
      files?.[0]
        ? {
            id: 'url',
            header: files[0].sitemapId === null ? 'robots.txt' : 'Sitemap',
            width: widthOf('url', 320),
            cell: () => <FileCell crawlId={crawlId} files={files} siteOrigin={siteOrigin} />,
            text: () => files.map((file) => file.url).join(' '),
          }
        : {
            id: 'url',
            header: references === 'targets' ? 'Page' : 'URL',
            width: widthOf('url', 320),
            sortable: true,
            ...sorted('url'),
            cell: (row: ListRow) => {
              const subject = subjectOf(row);
              // A URL other pages refer to is named once: the rows under it are the other pages linking to it.
              if (row.kind === 'ref' && references !== 'targets') return null;
              const where = whereOf(row);
              // A page holding the bad links: its open button lands on the row's own link.
              const at =
                references === 'targets' && where?.kind === 'target' && subject.url
                  ? openAtLink(subject.url, where.target.text)
                  : {};
              return (
                <IssueUrl
                  url={subject.url ?? ''}
                  siteOrigin={siteOrigin}
                  onInspect={() => onInspect(subject.id)}
                  {...at}
                />
              );
            },
            text: (row: ListRow) =>
              row.kind === 'issue' || references === 'targets' ? (subjectOf(row).url ?? '') : '',
          },
      ...(noDetail
        ? []
        : [
            {
              id: 'detail',
              header: check.category === 'responseCodes' ? 'Status' : 'Detail',
              width: widthOf('detail', 220),
              sortable: true,
              ...sorted('detail'),
              cell: (row: ListRow) => {
                const own = issue(row);
                if (!own) return null;
                // Its addresses are links (A-18); the file its row is about is named by its name alone.
                return own.detail ? (
                  <DetailWords crawlId={crawlId} detail={own.detail} files={fileUrls} siteOrigin={siteOrigin} />
                ) : (
                  <span>–</span>
                );
              },
              text: (row: ListRow) => issue(row)?.detail ?? '',
            },
          ]),
      ...(showRelated
        ? [
            {
              id: 'related',
              header: relatedHeader(check),
              width: widthOf('related', 280),
              cell: (row: ListRow) => {
                const own = issue(row);
                if (!own?.relatedUrl) return null;
                const relatedId = own.relatedUrlId;
                return (
                  <IssueUrl
                    url={own.relatedUrl}
                    siteOrigin={siteOrigin}
                    onInspect={typeof relatedId === 'number' ? () => onInspect(relatedId) : undefined}
                  />
                );
              },
              text: (row: ListRow) => issue(row)?.relatedUrl ?? '',
            },
          ]
        : []),
      // Where each is found, or what each page names, as columns: the other URL, what the link says, where it is.
      ...(refColumns
        ? [
            {
              id: 'other',
              header: refColumns.other,
              width: widthOf('other', 320),
              cell: (row: ListRow) => {
                const where = whereOf(row);
                if (!where) {
                  return (
                    <span className="text-text-muted" data-where-found="pending">
                      {references === 'sources' ? 'Finding where it’s linked…' : 'Finding what it names…'}
                    </span>
                  );
                }
                return (
                  <ReferenceOther
                    where={where}
                    subject={subjectOf(row)}
                    view={view}
                    more={
                      row.kind === 'issue' && row.refs && row.refs.rows.length > 1 ? (
                        <MoreToggle count={row.refs.more} open={row.open} />
                      ) : null
                    }
                  />
                );
              },
              text: (row: ListRow) => {
                const where = whereOf(row);
                if (!where) return '';
                if (where.kind === 'source') return where.link.url;
                if (where.kind === 'target') {
                  return `${where.target.url}${where.target.statusCode === null ? '' : ` (${where.target.statusCode})`}`;
                }
                return whereRowText(where);
              },
            },
            ...(refColumns.text
              ? [
                  {
                    id: 'text',
                    header: refColumns.text,
                    width: widthOf('text', 200),
                    cell: (row: ListRow) => {
                      const where = whereOf(row);
                      return where ? <ReferenceText where={where} /> : null;
                    },
                    text: (row: ListRow) => {
                      const where = whereOf(row);
                      return (where ? referenceText(where) : null) ?? '';
                    },
                  },
                ]
              : []),
            ...(refColumns.place
              ? [
                  {
                    id: 'place',
                    header: 'Where on the page',
                    width: widthOf('place', 160),
                    cell: (row: ListRow) => {
                      const where = whereOf(row);
                      return where ? <ReferencePlace where={where} /> : null;
                    },
                    text: (row: ListRow) => {
                      const where = whereOf(row);
                      return where ? referencePlace(where) : '';
                    },
                  },
                ]
              : []),
            ...(showRest
              ? [
                  {
                    id: 'rest',
                    header: 'Details',
                    width: widthOf('rest', 240),
                    cell: (row: ListRow) => {
                      const where = whereOf(row);
                      return where ? <ReferenceRest where={where} view={view} /> : null;
                    },
                    text: (row: ListRow) => {
                      const where = whereOf(row);
                      return where ? referenceRest(where, view) : '';
                    },
                  },
                ]
              : []),
          ]
        : []),
      ...drawn.map((column): GridColumn<ListRow> => ({
        ...column,
        width: widthOf(column.id, column.width),
        cell: (row: ListRow) => {
          const own = issue(row);
          return own ? column.cell(own) : null;
        },
        text: (row: ListRow) => {
          const own = issue(row);
          return own ? column.text(own) : '';
        },
      })),
      ...(files?.[0] ? [openFileColumn(crawlId, files[0])] : []),
      // The fix loop: how the row stood in the crawl compared with (Still, New, New URL, Not compared).
      ...(compare
        ? [
            {
              id: 'change',
              header: periodLabel(compare.period),
              width: CHANGE_WIDTH,
              cell: (row: ListRow) => {
                const own = issue(row);
                if (!own) return null;
                if (!own.change) return <span className="text-text-muted">–</span>;
                const cell = rowChangeCell(own.change, compare.period);
                return <span title={cell.title}>{cell.shown}</span>;
              },
              text: (row: ListRow) => {
                const own = issue(row);
                return own?.change ? rowChangeCell(own.change, compare.period).shown : '';
              },
            },
          ]
        : []),
    ];
  }, [
    sort,
    widths,
    fileUrls,
    noDetail,
    showRelated,
    showRest,
    drawn,
    siteOrigin,
    references,
    refColumns,
    crawlId,
    check,
    compare,
    files,
    view,
    onInspect,
  ]);
  // The columns as the user laid them out (resized, moved, pinned), kept for this grid with the preferences.
  const laid = useColumnLayout('issues.rows', columns);

  const shown = rowCount === null ? Math.min(Math.max(total, 1), 8) : layout.rowCount;
  const height = Math.max(shown, 1) * rowHeight + headerHeight + (legend ? GRID_FOOT_HEIGHT : 0) + 2;
  // Where the cursor was when the check was left, for this order of its rows.
  const start = useMemo(() => {
    const cursor = checkPlace(crawlId, check.id).cursor;
    return cursor?.grid === gridKey ? cursor : null;
  }, [crawlId, check.id, gridKey]);

  if (rows.error && rowCount === null) return <ErrorState error={rows.error} onRetry={rows.refetch} compact />;

  return (
    <div className="flex min-h-0 flex-col gap-2">
      {saidOnce !== null && rowCount !== null ? (
        <p className="text-13 text-text-secondary" data-said-once>
          <span className="flex min-w-0 flex-wrap items-center gap-x-1">
            <span>{rowCount === 2 ? 'Both' : `All ${formatCount(rowCount)}`}:</span>
            <DetailWords crawlId={crawlId} detail={saidOnce} files={fileUrls} siteOrigin={siteOrigin} wrap />
          </span>
        </p>
      ) : null}
      <div
        ref={measureFrame}
        className={evidenceFrameClass(shown)}
        style={{ height, minHeight: Math.min(height, leastFrameHeight(rowHeight, headerHeight, legend)) }}
      >
        <GridCore<ListRow>
          key={gridKey}
          label={`${check.title}: ${affectedWords(check).named}`}
          columns={laid.columns}
          columnLayout={laid.columnLayout}
          rowCount={rowCount === null ? null : layout.rowCount}
          getRow={getRow}
          rowHeight={rowHeight}
          headerHeight={headerHeight}
          onRangeChange={(range) => {
            const inRows = toRows(range);
            rows.onRangeChange(inRows);
            if (inRows.first !== inView.first || inRows.last !== inView.last) {
              setInView({ first: inRows.first, last: inRows.last });
            }
          }}
          onScrollOffset={rows.onScrollOffset}
          fetchRows={fetchRows}
          {...(tree ? { tree } : {})}
          onSort={(column, focused) => {
            const next = nextSort(sort, column);
            handoff.sorted(column, focused, next);
            onSortChange(next);
          }}
          focusHeader={handoff.focusHeader}
          onActivate={(_index, row) => {
            const url = row ? urlOfRow(row) : null;
            if (url) onInspect(url.id);
          }}
          onActiveRowChange={(index) => {
            const row = index !== null ? getRow(index) : undefined;
            if (index !== null) {
              keepCheckPlace(crawlId, check.id, {
                cursor: { grid: gridKey, row: index, key: row ? listRowKey(row) : undefined },
              });
            }
            const followed = row && onFollow ? urlOfRow(row) : null;
            if (followed) onFollow?.(followed.id);
          }}
          // The cursor is on an issue, not at a row number: the rows read again (an analysis) keep it on its own.
          rowKey={listRowKey}
          {...(start ? { startRow: start.row, ...(start.key === undefined ? {} : { startKey: start.key }) } : {})}
          openUrlOf={(row) => urlOfRow(row)?.url ?? null}
          onContextMenu={rowMenu.onContextMenu}
          busy={rows.isRefreshing}
          {...(legend
            ? { keyHints: tree ? ISSUE_KEY_HINTS : ISSUE_KEY_HINTS.filter((hint) => hint.chord !== 'arrowright') }
            : {})}
          rowNoun={tree ? ['row', 'rows'] : ['URL', 'URLs']}
          className="flex-1 rounded-md border border-border"
        />
        <IssuePageMenu<ListRow>
          crawlId={crawlId}
          state={rowMenu.state}
          onOpenChange={rowMenu.onOpenChange}
          siteOrigin={siteOrigin}
          page={menuRow ? urlOfRow(menuRow) : null}
          foundIn={
            menuRow?.kind === 'issue' && references === 'sources' ? linksOfUrl('inlinks', menuRow.row.id, 'all') : null
          }
          linksOf={
            menuRow?.kind === 'issue' && references === 'targets' && check.category !== 'images'
              ? linksOfUrl('outlinks', menuRow.row.id, 'all')
              : null
          }
          imagesIn={menuRow?.kind === 'issue' && check.category === 'images' ? imagesOfPage(menuRow.row.id) : null}
          linkingPage={menuRow ? linkingPageOf(menuRow) : null}
          isPage={check.subject !== 'url'}
          urlOf={(row) => urlOfRow(row)?.url ?? null}
          onInspect={onInspect}
          recheck={{
            checkId: check.id,
            idOf: (row) => (row.kind === 'issue' ? row.row.id : null),
            fullCheck: !recheckable(check),
          }}
        />
      </div>
    </div>
  );
}

const NONE_TOGGLED: ReadonlyMap<number, boolean> = new Map();

/**
 * The one page that links to a row's URL, where there is exactly one (A-17): the row menu inspects it and opens it at
 * the link. Null for a row with none, or several (they're in the rows under it).
 */
function linkingPageOf(row: ListRow): { id: number; url: string; text: string | null } | null {
  if (row.kind !== 'issue' || (row.refs?.more ?? 1) > 0) return null;
  if (!row.refs) return null;
  const [first] = row.refs.rows;
  return first?.kind === 'source' ? { id: first.link.id, url: first.link.url, text: first.link.text } : null;
}

/**
 * The first file a check of the site's files found at fault, and how many more: "/sitemap.xml and 2 more". Its address
 * is a link to its own screen: the sitemap in Sitemaps, the robots.txt of its origin.
 */
function FileCell({
  crawlId,
  files,
  siteOrigin,
}: {
  crawlId: string;
  files: readonly CheckFile[];
  siteOrigin: string | null;
}) {
  const navigate = useNavigate();
  const [first] = files;
  if (!first) return null;
  const origin = first.sitemapId === null ? robotsOriginOf(first.url) : null;
  const go =
    first.sitemapId !== null
      ? () =>
          void navigate({
            to: '/crawls/$crawl/sitemaps/{-$sitemap}',
            params: { crawl: crawlId, sitemap: String(first.sitemapId) },
          })
      : origin
        ? () =>
            void navigate({ to: '/crawls/$crawl/robots/{-$origin}', params: { crawl: crawlId, origin }, search: {} })
        : undefined;
  return (
    <span className="flex min-w-0 items-center gap-2">
      <IssueUrl
        url={first.url}
        siteOrigin={siteOrigin}
        onInspect={go}
        goHint={first.sitemapId !== null ? 'Open in Sitemaps' : 'Open robots.txt'}
      />
      {files.length > 1 ? (
        <span className="shrink-0 text-text-muted">and {formatCount(files.length - 1)} more</span>
      ) : null}
    </span>
  );
}

/** The way to a file's own screen: the sitemap in Sitemaps, the robots.txt of its origin. */
function openFileColumn(crawlId: string, file: CheckFile): GridColumn<ListRow> {
  const origin = file.sitemapId === null ? robotsOriginOf(file.url) : null;
  return {
    id: 'open',
    header: 'Open',
    width: OPEN_FILE_WIDTH,
    cell: () =>
      file.sitemapId !== null ? (
        <TextLink
          to="/crawls/$crawl/sitemaps/{-$sitemap}"
          params={{ crawl: crawlId, sitemap: String(file.sitemapId) }}
          tabIndex={-1}
        >
          Open in Sitemaps
        </TextLink>
      ) : origin ? (
        <TextLink to="/crawls/$crawl/robots/{-$origin}" params={{ crawl: crawlId, origin }} search={{}} tabIndex={-1}>
          Open robots.txt
        </TextLink>
      ) : null,
    text: () => '',
  };
}
