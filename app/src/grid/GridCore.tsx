// The grid primitive: a fixed-height windowed virtualiser with the ARIA grid pattern, full keyboard use, row
// selection and copy as tab-separated text. Rows can be grouped (`tree`): groups that open to show their rows, as the
// ARIA treegrid pattern has them, which the Issues screen uses to group a check's rows by detail. The full grid
// (column presets, chooser, filter bar, saved views) wraps this in the slices; every grid in the app is this component.
import clsx from 'clsx';
import { ArrowDown, ArrowLeft, ArrowRight, ArrowUp, ChevronsUpDown, Pin, PinOff, RotateCcw } from 'lucide-react';
import {
  memo,
  useEffect,
  useMemo,
  useEffectEvent,
  useId,
  useLayoutEffect,
  useRef,
  useState,
  type CSSProperties,
  type FocusEvent,
  type KeyboardEvent,
  type MouseEvent,
  type PointerEvent,
  type ReactNode,
} from 'react';
import { announce } from '../feedback/announce';
import { notifyError } from '../feedback/notify';
import { useDelayedLoading } from '../feedback/useDelayedLoading';
import { commands } from '../keyboard/registry';
import { copyText } from '../lib/clipboard';
import { formatCountOf } from '../lib/format';
import { isMac } from '../lib/platform';
import { INITIAL_NAV, gridKey, gridPointer, headerKey, type GridNavState, type GridPosition } from './gridNavigation';
import { DEFAULT_OVERSCAN, firstFullyVisible, rowsPerPage, scrollLeftToReveal } from './rowRange';
import { useRowWindow, type RowWindowModel } from './rowWindow';
import { windowFor } from './scrollWindow';
import {
  EMPTY_SELECTION,
  clampSelection,
  effectiveRanges,
  selectOnly,
  selectedCount,
  selectedIndices,
  type Range,
} from './selection';
import { MAX_COPY_ROWS, toTsv } from './tsv';
import type { GridColumn, GridRange, GridTree, RowFetcher } from './types';
import { VirtualScrollbar } from './VirtualScrollbar';
import { RefreshLine } from '../ui/ProgressBar';
import { Icon } from '../ui/Icon';
import { Kbd } from '../ui/Kbd';
import { MenuAt, MenuItem, MenuSeparator } from '../ui/Menu';
import { RESIZE_STEP, clampWidth, movedOrder } from './columnLayout';
import { footWords, type GridKeyHint } from './gridFoot';

export interface GridCopyResult {
  /** Rows copied, or 1 for a single cell. */
  rows: number;
  /** More rows were selected than MAX_COPY_ROWS. */
  truncated: boolean;
  /** Only the active cell was copied: nothing was selected. */
  cell: boolean;
}

/** What a row's menu acts on: the row it was opened on, or the selection that row is in. */
export interface GridMenuContext<Row> {
  /** The rows' indices when the menu opened, in order: the selection's (at most MAX_COPY_ROWS of it), or the row's. */
  indices: readonly number[];
  /**
   * The row the menu was opened on, as it was then: what its items inspect, open and name. Undefined only for a row
   * whose page wasn't loaded. Never read it again by its number: the rows can move while the menu is open.
   */
  row: Row | undefined;
  /** The selection had more rows than a menu acts on. */
  truncated: boolean;
  /** The rows themselves, as they were when the menu opened (read then where they weren't loaded); undefined for one that couldn't be. */
  rows: () => Promise<readonly (Row | undefined)[]>;
  /** Copies the rows as tab-separated text with a header row, as Ctrl+C copies a selection. */
  copy: () => Promise<void>;
  /** Gives the keyboard back to the grid, with the cursor where it was. */
  focus: () => void;
}

/**
 * What changes a grid's columns (`GridCore`'s `columnLayout`): the header's edges drag to resize, its cells drag to
 * move, and its menu (a right-click, or Shift+F10 on the header) pins, moves and resizes; on the header the keys do
 * the same (Shift+Left and Shift+Right resize, Ctrl+Shift+Left and Ctrl+Shift+Right move). `useColumnLayout` gives
 * these, keeping the layout with the preferences.
 */
export interface GridColumnLayoutProps {
  /** A column's width set, px, or put back to the grid's own (`null`). */
  onResize: (column: string, width: number | null) => void;
  /** A column moved to another place: `to` is its index among the grid's columns afterwards. */
  onMove?: ((column: string, to: number) => void) | undefined;
  /** A column pinned to the grid's left edge, or unpinned. */
  onPin?: ((column: string, pinned: boolean) => void) | undefined;
  /** Every column put back as the grid has them. */
  onReset?: (() => void) | undefined;
}

export interface GridCoreProps<Row> {
  /** The grid's accessible name: "URLs", "Links". */
  label: string;
  columns: readonly GridColumn<Row>[];
  /** Null until the first page says how many rows there are. */
  rowCount: number | null;
  /** The row at an index, or undefined when its page hasn't loaded (a placeholder row shows). */
  getRow: (index: number) => Row | undefined;
  rowHeight: number;
  headerHeight: number;
  overscan?: number;
  /**
   * Rows in view changed: fetch their pages, and prefetch ahead. `scrubbing` while the scrollbar's thumb is held:
   * rows fly past, so the data layer waits for it to settle.
   */
  onRangeChange?: (range: GridRange) => void;
  /** Every scroll's virtual offset, for prefetching by speed. */
  onScrollOffset?: (offset: number) => void;
  /**
   * Enter or double-click: open the inspector. `column` is the id of the column the cursor was in, or the cell that
   * was double-clicked, for a grid whose rows hold more than one thing to open (a link's two ends).
   */
  onActivate?: (index: number, row: Row | undefined, column: string | undefined) => void;
  /**
   * Enter or double-click, with the column the cursor (or the pointer) was on: for a grid whose cells lead to
   * different places (a sitemap's counts). Called in place of `onActivate` when given.
   */
  onActivateCell?: (index: number, row: Row | undefined, column: string) => void;
  /** The cursor moved to another row (the inspector follows it). */
  onActiveRowChange?: (index: number | null) => void;
  /** The cursor moved to another column, by its id: for an action on the column the cursor is in (copy it). */
  onActiveColumnChange?: (column: string | undefined) => void;
  /**
   * Right-click, Shift+F10 or the menu key. `menu` says which rows the menu is for (the selection, when the row is in
   * it, else the row), reads them, copies them as Ctrl+C does, and gives the keyboard back to the grid.
   */
  onContextMenu?: (index: number, column: number, anchor: { x: number; y: number }, menu: GridMenuContext<Row>) => void;
  /** Fetches rows that aren't loaded, for copying a selection that spans pages. */
  fetchRows?: RowFetcher<Row>;
  onCopy?: (result: GridCopyResult) => void;
  /** Shown instead of rows when there are none: say why, and what to do. */
  emptyState?: ReactNode;
  /** Data is refreshing behind what's shown: a thin progress line, nothing blanks. */
  busy?: boolean;
  /**
   * What the first load is doing, said over its skeleton when it's taking long enough for one to show ("Reading all
   * 5,053,909 links: a search looks at every one."). The view announces it; here it's for the eye.
   */
  loadingNote?: ReactNode;
  /**
   * The fewest rows the grid keeps room for, however little its container has: a screen's own grid asks for 5, so a
   * window too short for its toolbars and its grid (200% zoom of 1280x720) scrolls the screen rather than squeeze the
   * grid to no rows. With it the grid also keeps the room its empty state needs, so "nothing matches" and its Clear
   * are never cut off. Left out, the grid is as tall as it's given (a grid with a height of its own).
   */
  minRows?: number | undefined;
  className?: string;
  /** Tests and stories: the virtualiser's model, to drive scrolling. */
  modelRef?: (model: RowWindowModel) => void;
  /**
   * Grouped rows, as the ARIA treegrid pattern has them: the grid is a `treegrid`, each row says its level and whether
   * it's open, Right on a closed group's first cell opens it, Left closes it or moves from a row in a group to the
   * group's row, and Enter or a double-click opens and closes a group. An element with `data-tree-toggle` in a row
   * (its chevron) opens and closes it when pressed.
   */
  tree?: GridTree;
  /**
   * A row drawn as one cell across every column (a group's link to all its rows), or null for the row's own cells. Its
   * cell is the cursor's wherever the cursor's column is.
   */
  spanRow?: (row: Row, index: number) => ReactNode;
  /**
   * A sortable column's header was pressed, or Enter or Space with the cursor on it: sort by it, or change its
   * direction. `focused`: the grid had focus, so the grid the new sort brings (another `key`) should start with the
   * cursor on this header, focused (`focusHeader`). `extend`: Shift was held (Shift+click, Shift+Enter), to add the
   * column to the sort as a further key rather than sort by it alone.
   */
  onSort?: (column: string, focused: boolean, extend: boolean) => void;
  /**
   * Read when the grid mounts: the cursor starts on this column's header and the grid takes focus. For the grid that
   * replaced one sorted from its header, so the cursor stays where the user pressed.
   */
  focusHeader?: string | undefined;
  /**
   * Read when the grid mounts: the cursor starts on this row, which comes into view once the rows are there. For a
   * grid shown again where it was left (a URL's links, back along a walk).
   */
  startRow?: number | undefined;
  /**
   * A row's identity, for a list whose rows move under the cursor (a running crawl puts a new one above it): the
   * cursor stays on the row it was on, wherever that row is once the rows have changed, and Enter opens that row.
   * Without it the cursor is a row number, and the row under it is whichever is there now.
   */
  rowKey?: ((row: Row) => string | number) | undefined;
  /** With `startRow` and `rowKey`: the row the grid was left on, so it starts there even if the row has moved. */
  startKey?: string | number | undefined;
  /** The columns can be resized, moved and pinned: see `GridColumnLayoutProps`. Left out, they're fixed. */
  columnLayout?: GridColumnLayoutProps | undefined;
  /**
   * A footer under the rows with these key hints, and where the cursor is ("Row 12 of 264", "3 rows selected"): a
   * screen's own grid shows it (`GRID_KEY_HINTS`); a grid in a panel leaves it out.
   */
  keyHints?: readonly GridKeyHint[] | undefined;
  /** What a row is called in the footer's words ("URL", "link"): "row" when left out. */
  rowNoun?: [string, string] | undefined;
}

const ORDINALS = ['first', 'second', 'third'];

/**
 * A column's place in a sort of more than one key: its number beside the arrow, and in words for a screen reader,
 * which hears the first key from `aria-sort` and the others from this ("sorted second, descending").
 */
function SortOrder<Row>({ column }: { column: GridColumn<Row> }) {
  const order = column.sortOrder ?? 1;
  return (
    <>
      <span className="si-grid-sort__order" aria-hidden="true">
        {order}
      </span>
      {order > 1 ? (
        <span className="sr-only">{`, sorted ${ORDINALS[order - 1] ?? String(order)}, ${column.sort ?? ''}`}</span>
      ) : null}
    </>
  );
}

const SKELETON_ROWS = 12;
/** How many rows either side of where the cursor's row was it's looked for, once another row is in its place. */
const REANCHOR_REACH = 500;
/** What a grid's frame takes besides its header and rows: its border, and the scrollbar under columns too wide for it. */
const FRAME_ROOM = 12;

/** The width the custom vertical scrollbar covers at the right of a windowed grid (tokens.css `--scrollbar-size`). */
function coveredWidth(element: HTMLElement, windowed: boolean): number {
  if (!windowed) return 0;
  const size = Number.parseFloat(getComputedStyle(element).getPropertyValue('--scrollbar-size'));
  return Number.isFinite(size) ? size : 0;
}

/** The footer's height (tokens.css `--grid-foot-height`), for the room a grid with a least height keeps. */
const FOOT_HEIGHT_FALLBACK = 36;
function footHeight(): number {
  if (typeof document === 'undefined') return FOOT_HEIGHT_FALLBACK;
  const size = Number.parseFloat(getComputedStyle(document.documentElement).getPropertyValue('--grid-foot-height'));
  return Number.isFinite(size) && size > 0 ? size : FOOT_HEIGHT_FALLBACK;
}

/** How far a pointer goes with the button down on a header before it's moving the column, not pressing it, px. */
const DRAG_THRESHOLD = 5;

/**
 * Where each column starts from the grid's left edge, and for the leading run of pinned columns, where each sticks
 * (the same, as they come first).
 */
function columnOffsets<Row>(columns: readonly GridColumn<Row>[]) {
  const lefts: number[] = [];
  const pins: (number | undefined)[] = [];
  let left = 0;
  let pinning = true;
  for (const column of columns) {
    lefts.push(left);
    pinning = pinning && column.pinned === true;
    pins.push(pinning ? left : undefined);
    left += column.width;
  }
  const pinnedWidth = columns.reduce((sum, column, index) => sum + (pins[index] === undefined ? 0 : column.width), 0);
  const lastPinned = pins.reduce<number>((last, pin, index) => (pin === undefined ? last : index), -1);
  return { lefts, pins, pinnedWidth, lastPinned };
}

function GridCoreInner<Row>({
  label,
  rowCount,
  getRow,
  rowHeight,
  headerHeight,
  overscan = DEFAULT_OVERSCAN,
  onRangeChange,
  onScrollOffset,
  onActivate,
  onActivateCell,
  onActiveRowChange,
  onActiveColumnChange,
  onContextMenu,
  fetchRows,
  onCopy,
  emptyState,
  busy = false,
  loadingNote,
  minRows,
  className,
  modelRef,
  tree,
  spanRow,
  onSort,
  focusHeader,
  startRow,
  rowKey,
  startKey,
  columnLayout,
  keyHints,
  rowNoun = ['row', 'rows'],
  columns: givenColumns,
}: GridCoreProps<Row>) {
  const id = useId();
  // A column's edge being dragged: its width as it goes, before the layout keeps it on letting go.
  const [dragWidth, setDragWidth] = useState<{ id: string; width: number } | null>(null);
  const columns = useMemo(
    () =>
      dragWidth === null
        ? givenColumns
        : givenColumns.map((column) => (column.id === dragWidth.id ? { ...column, width: dragWidth.width } : column)),
    [givenColumns, dragWidth],
  );
  const offsets = useMemo(() => columnOffsets(columns), [columns]);
  const scrollRef = useRef<HTMLDivElement>(null);
  /** How tall the empty state's own content is, for a grid that keeps room for it (`minRows`). */
  const [emptyHeight, setEmptyHeight] = useState(0);
  const [nav, setNav] = useState<GridNavState>(() =>
    startRow === undefined || startRow < 0 ? INITIAL_NAV : { ...INITIAL_NAV, active: { row: startRow, col: 0 } },
  );
  // The header row is a row the cursor reaches when the columns sort: the column it's on there, or null in the rows.
  const sortable = onSort !== undefined && columns.some((column) => column.sortable);
  // The header is a row the cursor reaches where it does something: sorts, or lays the columns out.
  const headerReach = sortable || columnLayout !== undefined;
  const [headerCursor, setHeaderCursor] = useState<number | null>(() => {
    const at = focusHeader === undefined ? -1 : columns.findIndex((column) => column.id === focusHeader);
    return at >= 0 ? at : null;
  });
  const header = headerReach && headerCursor !== null ? Math.min(headerCursor, columns.length - 1) : null;
  const takeFocus = useEffectEvent(() => {
    if (header !== null) scrollRef.current?.focus({ preventScroll: true });
  });
  useEffect(() => takeFocus(), []);
  // A skeleton shows only for a first load over 150 ms, and then for at least 300 ms, rows or no rows: a skeleton
  // that flashes away is what the rule exists to prevent.
  const skeleton = useDelayedLoading(rowCount === null);
  const loading = rowCount === null || skeleton;
  const count = loading ? 0 : rowCount;

  const { view, model } = useRowWindow(scrollRef, { count, rowHeight, headerHeight, overscan });

  const exposeModel = useEffectEvent(() => {
    modelRef?.(model);
  });
  useLayoutEffect(() => {
    exposeModel();
  }, [model]);

  // The data layer hears which rows are in view, and every scroll's offset, to fetch and prefetch pages.
  const reportRange = useEffectEvent((first: number, last: number) =>
    onRangeChange?.({ first, last, scrubbing: model.isScrubbing() }),
  );
  useEffect(() => {
    if (count > 0) reportRange(view.first, view.last);
  }, [view.first, view.last, count]);
  // Letting go of the thumb settles the range: its pages are fetched at once, and prefetching resumes.
  useEffect(
    () =>
      model.onScrubbing(() => {
        const current = model.getSnapshot();
        if (current.last >= current.first) reportRange(current.first, current.last);
      }),
    [model],
  );
  const reportOffset = useEffectEvent((offset: number) => onScrollOffset?.(offset));
  useEffect(() => model.onOffset((offset) => reportOffset(offset)), [model]);

  // The row the cursor started on (`startRow`) comes into view once, when the rows are first there.
  const started = useRef(startRow === undefined);
  const revealStart = useEffectEvent(() => {
    if (started.current || count === 0) return;
    started.current = true;
    if (nav.active) model.scrollToIndex(Math.min(nav.active.row, count - 1));
  });
  useEffect(() => revealStart(), [count]);

  // With `rowKey`, the cursor is on a row, not at a number. `anchor` is the row it was last seen on. When the rows
  // change and another row is at that number, the cursor goes to where its own row went (looked for among the loaded
  // rows either side). The row hasn't changed, so no move is told (`onActiveRowChange`). A row that isn't found (it's
  // gone, or far off) leaves the cursor at its number, as a grid without a key does.
  const anchor = useRef<{ key: string | number; row: number; start: boolean } | null>(
    startKey === undefined || startRow === undefined ? null : { key: startKey, row: startRow, start: true },
  );
  const reanchor = useEffectEvent(() => {
    if (!rowKey || !nav.active || count === 0) return;
    const at = Math.min(nav.active.row, count - 1);
    const here = getRow(at);
    if (here === undefined) return;
    const held = anchor.current;
    // The cursor hasn't been moved since its row was seen (it's at the same number, as it was given: a list that
    // shrank shows it on its last row), and that row isn't the one there now.
    if (held && (held.row === nav.active.row || held.start) && rowKey(here) !== held.key) {
      for (let step = 1; step <= REANCHOR_REACH; step++) {
        for (const row of [at + step, at - step]) {
          const candidate = row >= 0 && row < count ? getRow(row) : undefined;
          if (candidate === undefined || rowKey(candidate) !== held.key) continue;
          anchor.current = { key: held.key, row, start: false };
          setNav((current) =>
            current.active
              ? {
                  ...current,
                  active: { ...current.active, row },
                  selection: current.follow ? selectOnly(row) : current.selection,
                }
              : current,
          );
          // A grid that starts on a row it was left on shows that row, wherever it is now.
          if (held.start) model.scrollToIndex(row);
          return;
        }
      }
    }
    anchor.current = { key: rowKey(here), row: nav.active.row, start: false };
  });
  useLayoutEffect(() => reanchor(), [getRow, count, nav.active?.row]);

  const template = columns.map((column) => `${column.width}px`).join(' ');
  const width = columns.reduce((sum, column) => sum + column.width, 0);
  const bodyHeight = Math.min(count * rowHeight, windowFor(rowHeight));

  // The cursor and selection, kept within the rows that exist now.
  const active =
    nav.active && count > 0
      ? { row: Math.min(nav.active.row, count - 1), col: Math.min(nav.active.col, columns.length - 1) }
      : null;
  const selection = clampSelection(nav.selection, count);
  const ranges = effectiveRanges(selection);
  const cellId = (row: number, col: number) => `${id}-r${row}-c${col}`;
  /** Whether the row at an index is drawn as one cell: only a loaded row can be. */
  const spans = (index: number) => {
    const row = spanRow ? getRow(index) : undefined;
    return row !== undefined && spanRow?.(row, index) != null;
  };
  const headerCellId = (col: number) => `${id}-h${col}`;
  const activeCellId =
    header !== null
      ? headerCellId(header)
      : active
        ? cellId(active.row, spans(active.row) ? 0 : active.col)
        : undefined;
  /** The cursor's column in a row, or -1: none while the cursor is on the header. */
  const cursorCol = (index: number) => (header === null && active?.row === index ? active.col : -1);

  const indices: number[] = [];
  for (let index = view.first; index <= view.last; index++) indices.push(index);
  const activeOffscreen = active !== null && (active.row < view.first || active.row > view.last);

  /** Scrolls sideways, just enough, so the cursor's cell is in view: the URL grid is wider than most windows. */
  const revealColumn = (col: number) => {
    const element = scrollRef.current;
    const column = columns[col];
    // A pinned column is always in view.
    if (!element || !column || offsets.pins[col] !== undefined) return;
    // The pinned columns cover the left of what's in view: a column is revealed beside them.
    const left = (offsets.lefts[col] ?? 0) - offsets.pinnedWidth;
    const visible = element.clientWidth - coveredWidth(element, view.windowed) - offsets.pinnedWidth;
    const next = scrollLeftToReveal(left, column.width, element.scrollLeft, visible);
    if (next !== element.scrollLeft) element.scrollLeft = next;
  };

  const apply = (next: GridNavState, reveal: number | null) => {
    if (next.active?.row !== active?.row) onActiveRowChange?.(next.active?.row ?? null);
    if (next.active?.col !== active?.col) {
      onActiveColumnChange?.(next.active ? columns[next.active.col]?.id : undefined);
    }
    setNav(next);
    if (reveal !== null) {
      model.scrollToIndex(reveal);
      if (next.active) revealColumn(next.active.col);
    }
  };

  /**
   * Opens or closes a group, keeping the cursor on its row (which moves when a group above it closes) and the
   * selection on it only if it was a plain click's.
   */
  const toggle = (row: number, open: boolean) => {
    if (!tree) return;
    const moved = tree.onToggle(row, open);
    const at = typeof moved === 'number' ? moved : row;
    apply(
      {
        active: { row: at, col: active?.row === row ? active.col : 0 },
        selection: nav.follow ? selectOnly(at) : EMPTY_SELECTION,
        follow: nav.follow,
      },
      at,
    );
  };

  /** The tree's own keys: Right and Left on a row's first cell, and Enter on a group. Returns whether it took the key. */
  const treeKey = (event: KeyboardEvent<HTMLDivElement>): boolean => {
    if (!tree || !active || event.shiftKey || event.altKey || event.ctrlKey || event.metaKey) return false;
    const firstCell = active.col === 0 || spans(active.row);
    const expanded = tree.expanded(active.row);
    if (event.key === 'Enter' && expanded !== undefined && !tree.activates) {
      toggle(active.row, !expanded);
      return true;
    }
    if (!firstCell) return false;
    if (event.key === 'ArrowRight' && expanded === false) {
      toggle(active.row, true);
      return true;
    }
    if (event.key === 'ArrowLeft') {
      if (expanded === true) {
        toggle(active.row, false);
        return true;
      }
      const parent = tree.parent(active.row);
      if (parent !== null) {
        apply(
          {
            active: { row: parent, col: 0 },
            selection: nav.follow ? selectOnly(parent) : nav.selection,
            follow: nav.follow,
          },
          parent,
        );
        return true;
      }
    }
    return false;
  };

  /**
   * The rows as text: rows that aren't loaded are fetched first. A row that still isn't there, or that has no text
   * (a group's link to all its rows), is left out rather than copied as a blank line: `rows` is what was copied,
   * `unread` what couldn't be. Throws when a page can't be fetched.
   */
  const rowsAt = async (indices: readonly number[]): Promise<readonly (Row | undefined)[]> => {
    const missing = indices.filter((index) => getRow(index) === undefined);
    const fetched = missing.length && fetchRows ? await fetchRows(missing) : [];
    const byIndex = new Map(missing.map((index, i) => [index, fetched[i]]));
    return indices.map((index) => getRow(index) ?? byIndex.get(index));
  };

  /** Rows as text, with a header row; a row that has no text (a group's link to all its rows) is left out. */
  const textOf = (rows: readonly (Row | undefined)[]) => {
    const lines = rows
      .filter((row) => row !== undefined)
      .map((row) => columns.map((column) => column.text(row)))
      .filter((cells) => cells.some((cell) => cell !== ''));
    return {
      text: toTsv(
        columns.map((column) => column.header),
        lines,
      ),
      rows: lines.length,
      unread: rows.filter((row) => row === undefined).length,
    };
  };

  /**
   * Ctrl+C: the selected rows as tab-separated text for a spreadsheet, or with nothing selected the cell under the
   * cursor, exactly as it is. Screen readers hear what was copied; a copy that fails says so.
   */
  const copy = async () => {
    const pick = selectedIndices(selection, MAX_COPY_ROWS);
    if (pick.indices.length > 0) {
      await copyRows(() => rowsAt(pick.indices), pick.truncated);
      return;
    }
    try {
      if (!active) return;
      const column = columns[active.col];
      const row = getRow(active.row) ?? (await fetchRows?.([active.row]))?.[0];
      if (!column || row === undefined) return;
      if (!(await copyText(column.text(row)))) throw new ClipboardRefused();
      announce('Copied the cell.');
      onCopy?.({ rows: 1, truncated: false, cell: true });
    } catch (error) {
      notifyError(error, {
        title: 'The cell couldn’t be copied.',
        ...(error instanceof ClipboardRefused ? { description: 'The clipboard didn’t take it. Try again.' } : {}),
      });
    }
  };

  /** Rows as tab-separated text with a header row: a selection's (Ctrl+C), or a row menu's. */
  const copyRows = async (read: () => Promise<readonly (Row | undefined)[]>, truncated: boolean) => {
    try {
      const copied = textOf(await read());
      if (copied.rows === 0) throw new NothingToCopy();
      if (!(await copyText(copied.text))) throw new ClipboardRefused();
      // What was copied, never what was selected: rows that weren't there aren't counted.
      announce(
        copied.unread > 0
          ? `Copied ${formatCountOf(copied.rows, 'row')}. ${formatCountOf(copied.unread, 'row')} couldn’t be read.`
          : `Copied ${formatCountOf(copied.rows, 'row')}.`,
      );
      onCopy?.({ rows: copied.rows, truncated, cell: false });
    } catch (error) {
      notifyError(error, {
        title: 'The rows couldn’t be copied.',
        ...(error instanceof ClipboardRefused
          ? { description: 'The clipboard didn’t take it. Try again.' }
          : error instanceof NothingToCopy
            ? { description: 'None of the selected rows could be read.' }
            : {}),
      });
    }
  };

  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    // Text selected inside the grid by mouse: let the browser copy that instead.
    const textSelected = (window.getSelection()?.toString() ?? '') !== '';
    const mod = isMac ? event.metaKey : event.ctrlKey;
    if (mod && event.key.toLowerCase() === 'c' && textSelected) return;
    // Esc closes what's open around the grid (the inspector) before it clears the grid's selection.
    if (event.key === 'Escape' && commands.claims(event.nativeEvent)) return;
    // A control in the header that has focus (a click can give it focus in some browsers) keeps its own keys.
    if (event.target !== event.currentTarget && isHeaderControl(event.target)) return;
    const input = { key: event.key, shift: event.shiftKey, mod, alt: event.altKey };
    if (header !== null) {
      const move = headerKey(header, input, columns.length);
      if (move?.type === 'move') {
        event.preventDefault();
        setHeaderCursor(move.col);
        revealColumn(move.col);
        return;
      }
      if (move?.type === 'sort') {
        event.preventDefault();
        const column = columns[move.col];
        if (column?.sortable) onSort?.(column.id, true, move.add === true);
        // A header that can't sort, where others can, says why: a key that does nothing reads as broken.
        else if (column?.sortHint) announce(column.sortHint);
        return;
      }
      if (move?.type === 'rows') {
        event.preventDefault();
        setHeaderCursor(null);
        // The row under the sticky header: the first one wholly in view.
        const row = Math.min(firstFullyVisible(model.offset(), rowHeight, view), Math.max(count - 1, 0));
        if (count > 0) {
          apply(
            {
              active: { row, col: header },
              selection: nav.follow ? selectOnly(row) : nav.selection,
              follow: nav.follow,
            },
            row,
          );
        }
        return;
      }
      if (move?.type === 'resize' || move?.type === 'reorder' || move?.type === 'menu') {
        event.preventDefault();
        if (!columnLayout) return;
        if (move.type === 'resize') resizeBy(move.col, move.by);
        else if (move.type === 'reorder') moveBy(move.col, move.by);
        else openHeaderMenuFromKeys(move.col);
        return;
      }
      if (move?.type === 'pass') setHeaderCursor(null);
    } else if (
      headerReach &&
      event.key === 'ArrowUp' &&
      !event.shiftKey &&
      !mod &&
      !event.altKey &&
      (active === null || active.row === 0)
    ) {
      // Up from the first row reaches the header, where Enter or Space sorts.
      event.preventDefault();
      setHeaderCursor(active?.col ?? 0);
      model.scrollToIndex(0);
      return;
    }
    if (treeKey(event)) {
      event.preventDefault();
      return;
    }
    const result = gridKey({ active, selection, follow: nav.follow }, input, {
      rowCount: count,
      colCount: columns.length,
      pageRows: rowsPerPage(view.viewport, rowHeight),
    });
    if (!result.handled) return;
    event.preventDefault();
    apply(result.state, result.reveal);
    const effect = result.effect;
    if (effect?.type === 'activate') activate(effect.row, result.state.active?.col ?? active?.col ?? 0);
    else if (effect?.type === 'copy') void copy();
    else if (effect?.type === 'contextMenu') openMenuFromKeys(effect.row, effect.col);
  };

  /** Shift+Left or Shift+Right on a header: its right edge moves a step, and the width is said. */
  const resizeBy = (col: number, by: -1 | 1) => {
    const column = columns[col];
    if (!column || !columnLayout) return;
    const width = Math.max(column.minWidth ?? 0, clampWidth(column.width + by * RESIZE_STEP));
    if (width === column.width) {
      announce(`${column.header} is as ${by < 0 ? 'narrow' : 'wide'} as it goes.`);
      return;
    }
    columnLayout.onResize(column.id, width);
    announce(`${column.header}: ${width} pixels wide.`);
  };

  /** Ctrl+Shift+Left or Ctrl+Shift+Right on a header: the column moves a place, and the cursor with it. */
  const moveBy = (col: number, by: -1 | 1) => {
    const column = columns[col];
    if (!column || !columnLayout?.onMove) return;
    const ids = columns.map((other) => other.id);
    const pinned = columns.filter((other) => other.pinned).map((other) => other.id);
    const to = movedOrder(ids, column.id, col + by, pinned).indexOf(column.id);
    if (to === col) {
      announce(`${column.header} can’t move further ${by < 0 ? 'left' : 'right'}.`);
      return;
    }
    columnLayout.onMove(column.id, to);
    setHeaderCursor(to);
  };

  /**
   * A column pinned or unpinned from its menu takes another place: the header's cursor goes with it once the columns
   * are laid out again, as `moveBy`'s does (round 5 review R1-5).
   */
  const followColumn = useRef<string | null>(null);
  useLayoutEffect(() => {
    const id = followColumn.current;
    if (id === null) return;
    const to = columns.findIndex((column) => column.id === id);
    if (to < 0) return;
    followColumn.current = null;
    setHeaderCursor(to);
  }, [columns]);
  const pinColumn = (id: string, pinned: boolean) => {
    if (!columnLayout?.onPin) return;
    followColumn.current = id;
    columnLayout.onPin(id, pinned);
  };

  /** A column's menu, open at a point: pin, move, resize. */
  // Each opening is a menu of its own, placed afresh at its point (see `RowMenuState.opening`).
  const [headerMenu, setHeaderMenu] = useState<{
    col: number;
    at: { x: number; y: number };
    open: boolean;
    opening: number;
  } | null>(null);
  const openHeaderMenu = (col: number, at: { x: number; y: number }) =>
    setHeaderMenu((current) => ({ col, at, open: true, opening: (current?.opening ?? 0) + 1 }));
  const openHeaderMenuFromKeys = (col: number) => {
    const element = scrollRef.current;
    if (!element) return;
    const box = element.getBoundingClientRect();
    const pin = offsets.pins[col];
    const left = box.left + (pin ?? (offsets.lefts[col] ?? 0) - element.scrollLeft);
    openHeaderMenu(col, { x: Math.min(Math.max(left, box.left), box.right), y: box.top + headerHeight });
  };

  // A header being dragged to another place: which column, where the pointer started, and where it would go.
  const headerDrag = useRef<{ col: number; startX: number; pointer: number; moving: boolean } | null>(null);
  const [dropAt, setDropAt] = useState<{ to: number; x: number } | null>(null);
  /** A drag just ended on a header: the click that follows it isn't a press of the header (no sort). */
  const dragEnded = useRef(false);
  /** The place a column dragged to this x (the grid's content coordinates) would take, and where its line goes. */
  const dropFor = (col: number, x: number) => {
    const column = columns[col];
    if (!column) return null;
    let to = 0;
    for (let index = 0; index < columns.length; index++) {
      const middle = (offsets.lefts[index] ?? 0) + (columns[index]?.width ?? 0) / 2;
      if (x > middle) to = index + (index < col ? 1 : 0);
    }
    const ids = columns.map((other) => other.id);
    const pinned = columns.filter((other) => other.pinned).map((other) => other.id);
    const placed = movedOrder(ids, column.id, Math.min(to, columns.length - 1), pinned).indexOf(column.id);
    // The line is at the edge the column would be put against.
    const edge = placed > col ? placed + 1 : placed;
    const end = (offsets.lefts.at(-1) ?? 0) + (columns.at(-1)?.width ?? 0);
    return { to: placed, x: edge >= columns.length ? end : (offsets.lefts[edge] ?? end) };
  };
  /** A pointer's x in the grid's content (its scroll included). */
  const contentX = (clientX: number) => {
    const element = scrollRef.current;
    if (!element) return clientX;
    return clientX - element.getBoundingClientRect().left + element.scrollLeft;
  };
  const onHeaderPointerDown = (event: PointerEvent<HTMLDivElement>, col: number) => {
    if (event.button !== 0 || !columnLayout?.onMove) return;
    if (event.target instanceof Element && event.target.closest('.si-grid-resize')) return;
    headerDrag.current = { col, startX: event.clientX, pointer: event.pointerId, moving: false };
  };
  const onHeaderPointerMove = (event: PointerEvent<HTMLDivElement>) => {
    const drag = headerDrag.current;
    if (drag?.pointer !== event.pointerId) return;
    // No button held: the press was let go off the header, which never heard it (round 5 review R1-6).
    if (event.buttons === 0) {
      headerDrag.current = null;
      setDropAt(null);
      return;
    }
    if (!drag.moving) {
      if (Math.abs(event.clientX - drag.startX) < DRAG_THRESHOLD) return;
      drag.moving = true;
      capturePointer(event.currentTarget, event.pointerId);
    }
    setDropAt(dropFor(drag.col, contentX(event.clientX)));
  };
  const onHeaderPointerUp = (event: PointerEvent<HTMLDivElement>) => {
    const drag = headerDrag.current;
    headerDrag.current = null;
    setDropAt(null);
    if (!drag?.moving || drag.pointer !== event.pointerId) return;
    const drop = dropFor(drag.col, contentX(event.clientX));
    dragEnded.current = true;
    // The click (if any) comes straight after the pointer goes up: after it, a press is a press again.
    setTimeout(() => (dragEnded.current = false), 0);
    const column = columns[drag.col];
    if (drop && column && drop.to !== drag.col) {
      columnLayout?.onMove?.(column.id, drop.to);
      setHeaderCursor(drop.to);
    }
  };
  const onHeaderPointerCancel = () => {
    headerDrag.current = null;
    setDropAt(null);
  };

  /** A column's right edge dragged: its width follows the pointer, and is kept on letting go. */
  const edgeDrag = useRef<{ col: number; startX: number; startWidth: number; pointer: number } | null>(null);
  const edgeWidth = (clientX: number) => {
    const drag = edgeDrag.current;
    const column = drag ? givenColumns[drag.col] : undefined;
    if (!drag || !column) return null;
    return { column, width: Math.max(column.minWidth ?? 0, clampWidth(drag.startWidth + clientX - drag.startX)) };
  };
  const onEdgePointerDown = (event: PointerEvent<HTMLSpanElement>, col: number) => {
    const column = columns[col];
    if (event.button !== 0 || !column) return;
    event.preventDefault();
    event.stopPropagation();
    capturePointer(event.currentTarget, event.pointerId);
    edgeDrag.current = { col, startX: event.clientX, startWidth: column.width, pointer: event.pointerId };
  };
  const onEdgePointerMove = (event: PointerEvent<HTMLSpanElement>) => {
    if (edgeDrag.current?.pointer !== event.pointerId) return;
    const next = edgeWidth(event.clientX);
    if (next) setDragWidth({ id: next.column.id, width: next.width });
  };
  const onEdgePointerUp = (event: PointerEvent<HTMLSpanElement>) => {
    if (edgeDrag.current?.pointer !== event.pointerId) return;
    const next = edgeWidth(event.clientX);
    edgeDrag.current = null;
    setDragWidth(null);
    if (next && next.width !== next.column.width) columnLayout?.onResize(next.column.id, next.width);
  };
  const onEdgePointerCancel = () => {
    edgeDrag.current = null;
    setDragWidth(null);
  };

  /**
   * Shift+F10 or the menu key: the menu opens at the bottom-left corner of the cursor's cell (the row's one cell, for a
   * row drawn as one), worked out from where the row is in the grid's scroll space, not measured: the keys have just
   * scrolled the row into view where it wasn't, and what's drawn still has it where it was (or the copy of it kept
   * off screen for assistive technology).
   */
  const openMenuFromKeys = (row: number, col: number) => {
    const element = scrollRef.current;
    if (!element || !onContextMenu) return;
    const at = spans(row) ? 0 : col;
    const box = element.getBoundingClientRect();
    const pin = offsets.pins[at];
    const left = box.left + (pin ?? (offsets.lefts[at] ?? 0) - element.scrollLeft);
    const bottom = box.top + headerHeight + (row + 1) * rowHeight - model.offset();
    const anchor = {
      x: Math.min(Math.max(left, box.left), box.right),
      y: Math.min(Math.max(bottom, box.top + headerHeight), box.bottom),
    };
    onContextMenu(row, at, anchor, menuContext(row, selection));
  };

  /**
   * What a row's menu acts on: the selection, when the row is in it, else the row alone (a right-click outside the
   * selection has just made it the selection).
   */
  const menuContext = (row: number, within: typeof selection): GridMenuContext<Row> => {
    const inSelection = effectiveRanges(within).some(([first, last]) => row >= first && row <= last);
    const pick = inSelection ? selectedIndices(within, MAX_COPY_ROWS) : { indices: [row], truncated: false };
    // The rows themselves, held as they are now: a running crawl's refresh can put other rows at these numbers while
    // the menu is open, and the menu is of these. Rows not loaded are read at once, before anything can move.
    const held = pick.indices.map((index) => getRow(index));
    const reading = held.some((entry) => entry === undefined) ? rowsAt(pick.indices) : Promise.resolve(held);
    // A read that fails says so when an item asks for the rows, not here.
    reading.catch(() => undefined);
    return {
      indices: pick.indices,
      truncated: pick.truncated,
      row: getRow(row),
      rows: () => reading,
      copy: () => copyRows(() => reading, pick.truncated),
      focus: () => scrollRef.current?.focus({ preventScroll: true }),
    };
  };

  /**
   * Enter or a double-click on a row: the cell's own place when the grid has those (`onActivateCell`), else the
   * row's, with the column it was asked from.
   */
  const activate = (row: number, col: number) => {
    const column = columns[col];
    if (onActivateCell && column) onActivateCell(row, getRow(row), column.id);
    else onActivate?.(row, getRow(row), column?.id);
  };

  const positionOf = (target: EventTarget): GridPosition | null => {
    const cell = target instanceof Element ? target.closest<HTMLElement>('[data-row][data-col]') : null;
    if (!cell) return null;
    return { row: Number(cell.dataset.row), col: Number(cell.dataset.col) };
  };

  const onPointerDown = (event: PointerEvent<HTMLDivElement>) => {
    if (event.button !== 0) return;
    const position = positionOf(event.target);
    if (!position) return;
    setHeaderCursor(null);
    // A group's chevron opens or closes it on press, as a button would.
    const expanded = tree?.expanded(position.row);
    if (expanded !== undefined && event.target instanceof Element && event.target.closest('[data-tree-toggle]')) {
      toggle(position.row, !expanded);
      return;
    }
    apply(
      gridPointer({ active, selection, follow: nav.follow }, position, {
        shift: event.shiftKey,
        mod: isMac ? event.metaKey : event.ctrlKey,
      }),
      null,
    );
  };

  const onDoubleClick = (event: MouseEvent<HTMLDivElement>) => {
    const position = positionOf(event.target);
    if (!position) return;
    // The chevron's own press has opened or closed the group already.
    if (event.target instanceof Element && event.target.closest('[data-tree-toggle]')) return;
    const expanded = tree?.expanded(position.row);
    if (expanded !== undefined && !tree?.activates) toggle(position.row, !expanded);
    else activate(position.row, position.col);
  };

  const onContextMenuEvent = (event: MouseEvent<HTMLDivElement>) => {
    const position = positionOf(event.target);
    if (!position || !onContextMenu) return;
    event.preventDefault();
    // The cursor goes to the row the menu is for, from the header too, so the keys go on from there once it closes.
    setHeaderCursor(null);
    let within = selection;
    if (!ranges.some(([first, last]) => position.row >= first && position.row <= last)) {
      const next = gridPointer({ active, selection, follow: nav.follow }, position, { shift: false, mod: false });
      apply(next, null);
      within = next.selection;
    } else if (active?.row !== position.row || active.col !== position.col) {
      // Inside the selection: the selection stays (the menu is of it), and the cursor moves to the row clicked.
      apply({ active: position, selection, follow: false }, null);
    }
    onContextMenu(
      position.row,
      position.col,
      { x: event.clientX, y: event.clientY },
      menuContext(position.row, within),
    );
  };

  // Shift+F10 and the menu key open the row's menu from the keys (above); the browser's own menu, which the same keys
  // ask for on the focused grid, would open over it.
  const onGridContextMenu = (event: MouseEvent<HTMLDivElement>) => {
    if (onContextMenu && event.target === event.currentTarget) event.preventDefault();
  };

  // Rows arriving while the grid has focus and no cursor (it was focused before its first page came, or while the
  // skeleton showed): the cursor goes where focus arriving would have put it, so Enter works at once. Focus stays, and
  // nothing scrolls.
  const placeCursor = useEffectEvent(() => {
    if (count === 0 || nav.active !== null || document.activeElement !== scrollRef.current) return;
    const first = firstFullyVisible(model.offset(), rowHeight, view);
    setNav((current) => (current.active ? current : { ...current, active: { row: first, col: 0 } }));
  });
  useEffect(() => placeCursor(), [count]);

  const onFocus = (event: FocusEvent<HTMLDivElement>) => {
    // Focus arriving on the grid lands on a cell: the last one, or the first row wholly in view (the top one can be
    // half under the sticky header). A click that focused it has already placed the cursor, which the functional
    // update sees.
    if (event.target !== event.currentTarget || count === 0) return;
    const first = firstFullyVisible(model.offset(), rowHeight, view);
    setNav((current) => (current.active ? current : { ...current, active: { row: first, col: 0 } }));
  };

  const style = { '--grid-template': template } as CSSProperties;
  const showsEmpty = !loading && rowCount === 0 && emptyState !== undefined && emptyState !== null;
  const foot = keyHints !== undefined && keyHints.length > 0;
  const leastHeight =
    minRows === undefined
      ? undefined
      : headerHeight +
        Math.max(minRows * rowHeight, showsEmpty ? emptyHeight : 0) +
        FRAME_ROOM +
        (foot ? footHeight() : 0);
  const pinned = offsets.lastPinned >= 0;
  /** A pinned cell's own style: where it sticks. */
  const pinStyle = (col: number): CSSProperties | undefined => {
    const left = offsets.pins[col];
    return left === undefined ? undefined : { left };
  };
  const pinClass = (col: number) =>
    offsets.pins[col] === undefined
      ? undefined
      : clsx('si-grid-cell--pinned', col === offsets.lastPinned && 'si-grid-cell--pin-edge');
  const menuColumn = headerMenu ? columns[headerMenu.col] : undefined;
  return (
    <div
      className={clsx('si-grid-frame relative flex min-h-0 flex-col overflow-hidden', className)}
      // With a least height the frame's size is its container's to give and never its rows' (a scroller of millions
      // of pixels): `contain: size` says so, so a container that sizes to its content takes the least height from it.
      style={leastHeight === undefined ? undefined : { minHeight: leastHeight, contain: 'size' }}
    >
      {busy ? <RefreshLine /> : null}
      <div
        ref={scrollRef}
        role={tree ? 'treegrid' : 'grid'}
        aria-label={label}
        aria-rowcount={loading ? -1 : count + 1}
        aria-colcount={columns.length}
        aria-multiselectable="true"
        aria-busy={loading || busy}
        aria-activedescendant={activeCellId}
        tabIndex={0}
        onKeyDown={onKeyDown}
        onFocus={onFocus}
        onContextMenu={onGridContextMenu}
        className={clsx('si-grid min-h-0 flex-1 overflow-auto', view.windowed && 'si-grid--windowed')}
        style={style}
      >
        <div role="rowgroup" className="sticky top-0 z-2">
          <div
            role="row"
            aria-rowindex={1}
            className="si-grid-row si-grid-head"
            style={{ height: headerHeight, width }}
          >
            {columns.map((column, col) => (
              // The header's pointer handlers move the column (a drag) and open its menu (a right-click): the keys do
              // the same from the grid, which is the one Tab stop, so the cell itself needs no role of a control.
              <div
                key={column.id}
                id={headerCellId(col)}
                role="columnheader"
                aria-colindex={col + 1}
                // A sort of more than one key: the first is the grid's sort, the others say their place in words.
                aria-sort={column.sortOrder === undefined || column.sortOrder === 1 ? column.sort : undefined}
                data-active={header === col || undefined}
                data-pinned={column.pinned ? true : undefined}
                className={clsx('si-grid-cell', column.numeric && 'si-grid-cell--numeric', pinClass(col))}
                style={pinStyle(col)}
                title={sortable && !column.sortable ? column.sortHint : undefined}
                {...(columnLayout
                  ? {
                      onPointerDown: (event: PointerEvent<HTMLDivElement>) => onHeaderPointerDown(event, col),
                      onPointerMove: onHeaderPointerMove,
                      onPointerUp: onHeaderPointerUp,
                      onPointerCancel: onHeaderPointerCancel,
                      onContextMenu: (event: MouseEvent<HTMLDivElement>) => {
                        event.preventDefault();
                        setHeaderCursor(col);
                        scrollRef.current?.focus({ preventScroll: true });
                        openHeaderMenu(col, { x: event.clientX, y: event.clientY });
                      },
                    }
                  : {})}
              >
                {column.sortable && onSort ? (
                  // The grid is the one stop for Tab: the cursor reaches the header, and Enter or Space sorts. The
                  // button is for the pointer, and doesn't take focus from the grid.
                  <button
                    type="button"
                    tabIndex={-1}
                    className={clsx('si-grid-sort', column.sort && 'si-grid-sort--active')}
                    onMouseDown={(event) => {
                      event.preventDefault();
                      scrollRef.current?.focus({ preventScroll: true });
                      setHeaderCursor(col);
                    }}
                    onClick={(event) => {
                      // The end of a drag that moved the column isn't a press.
                      if (dragEnded.current) return;
                      onSort(column.id, document.activeElement === scrollRef.current, event.shiftKey);
                    }}
                  >
                    {column.header}
                    {column.headerDetail ? <span className="si-grid-head__detail">{column.headerDetail}</span> : null}
                    <Icon
                      icon={
                        column.sort === 'ascending'
                          ? ArrowUp
                          : column.sort === 'descending'
                            ? ArrowDown
                            : ChevronsUpDown
                      }
                      size="xs"
                    />
                    {column.sort && column.sortOrder !== undefined ? <SortOrder column={column} /> : null}
                  </button>
                ) : (
                  <>
                    {column.header}
                    {column.headerDetail ? <span className="si-grid-head__detail">{column.headerDetail}</span> : null}
                  </>
                )}
                {columnLayout ? (
                  // The column's right edge, for the pointer: a drag resizes it, a double-click puts its width back.
                  // The keys do the same from the header (Shift+Left, Shift+Right), and its menu.
                  <span
                    aria-hidden="true"
                    className="si-grid-resize"
                    onPointerDown={(event) => onEdgePointerDown(event, col)}
                    onPointerMove={onEdgePointerMove}
                    onPointerUp={onEdgePointerUp}
                    onPointerCancel={onEdgePointerCancel}
                    onDoubleClick={(event) => {
                      event.stopPropagation();
                      columnLayout.onResize(column.id, null);
                    }}
                    // The header's own press (a sort) and its drag never start here.
                    onClick={(event) => event.stopPropagation()}
                  />
                ) : null}
              </div>
            ))}
            {dropAt ? <span aria-hidden="true" className="si-grid-drop" style={{ left: dropAt.x }} /> : null}
          </div>
        </div>
        <div
          role="rowgroup"
          className="relative"
          style={{ height: skeleton ? SKELETON_ROWS * rowHeight : loading ? 0 : bodyHeight, width }}
          onPointerDown={onPointerDown}
          onDoubleClick={onDoubleClick}
          onContextMenu={onContextMenuEvent}
        >
          {skeleton
            ? Array.from({ length: SKELETON_ROWS }, (_, index) => (
                <PlaceholderRow
                  key={index}
                  index={index}
                  top={index * rowHeight}
                  height={rowHeight}
                  width={width}
                  columns={columns.length}
                  activeCol={-1}
                  cellIdPrefix={null}
                />
              ))
            : null}
          {/* The selection's accent rule: a gutter that stays at the left edge when the grid scrolls sideways. */}
          <div aria-hidden="true" className="si-grid-gutter">
            {loading
              ? null
              : indices
                  .filter((index) => inRanges(ranges, index))
                  .map((index) => (
                    <span
                      key={index}
                      className="si-grid-mark"
                      style={{ transform: `translateY(${index * rowHeight - view.base}px)`, height: rowHeight }}
                    />
                  ))}
          </div>
          {indices.map((index) => {
            const row = getRow(index);
            const top = index * rowHeight - view.base;
            return row === undefined ? (
              // Its page is on its way: the cursor's cell keeps its id, so aria-activedescendant names a real cell.
              <PlaceholderRow
                key={index}
                index={index}
                top={top}
                height={rowHeight}
                width={width}
                columns={columns.length}
                activeCol={cursorCol(index)}
                cellIdPrefix={id}
                level={tree?.level(index)}
                expanded={tree?.expanded(index)}
                pins={pinned ? offsets.pins : undefined}
                pinEdge={offsets.lastPinned}
              />
            ) : (
              <GridRow
                key={index}
                index={index}
                row={row}
                columns={columns}
                top={top}
                height={rowHeight}
                width={width}
                selected={inRanges(ranges, index)}
                activeCol={cursorCol(index)}
                cellIdPrefix={id}
                level={tree?.level(index)}
                expanded={tree?.expanded(index)}
                span={spanRow?.(row, index) ?? null}
                pins={pinned ? offsets.pins : undefined}
                pinEdge={offsets.lastPinned}
              />
            );
          })}
          {activeOffscreen ? (
            // The active row stays in the DOM while scrolled away, so aria-activedescendant always points somewhere.
            <OffscreenRow
              index={active.row}
              row={getRow(active.row)}
              columns={columns}
              activeCol={header === null ? active.col : -1}
              cellIdPrefix={id}
              selected={inRanges(ranges, active.row)}
              level={tree?.level(active.row)}
              expanded={tree?.expanded(active.row)}
              spans={spans(active.row)}
            />
          ) : null}
        </div>
      </div>
      {view.windowed ? (
        <VirtualScrollbar model={model} total={count * rowHeight} viewport={view.viewport} top={headerHeight} />
      ) : null}
      {skeleton && rowCount === null && loadingNote ? (
        <p className="si-grid-note" style={{ top: headerHeight }}>
          {loadingNote}
        </p>
      ) : null}
      {!loading && rowCount === 0 && emptyState ? (
        <EmptyRegion
          label={label}
          top={headerHeight}
          bottom={foot ? 'var(--grid-foot-height)' : undefined}
          onHeight={minRows === undefined ? undefined : setEmptyHeight}
        >
          {emptyState}
        </EmptyRegion>
      ) : null}
      {foot ? (
        <GridFoot
          hints={keyHints}
          where={footWords(active?.row ?? null, count, selectedCount(selection), loading, rowNoun)}
        />
      ) : null}
      {columnLayout ? (
        <MenuAt
          key={headerMenu?.opening ?? 0}
          at={headerMenu?.at ?? null}
          open={headerMenu?.open ?? false}
          onOpenChange={(open) => setHeaderMenu((current) => (current ? { ...current, open } : current))}
          label={`Menu for the ${menuColumn?.header ?? ''} column`}
          onCloseFocus={() => scrollRef.current?.focus({ preventScroll: true })}
        >
          {menuColumn && headerMenu ? (
            <ColumnMenuItems
              column={menuColumn}
              col={headerMenu.col}
              count={columns.length}
              layout={columnLayout}
              onResize={(by) => resizeBy(headerMenu.col, by)}
              onMove={(by) => moveBy(headerMenu.col, by)}
              onPin={(pinned) => pinColumn(menuColumn.id, pinned)}
            />
          ) : null}
        </MenuAt>
      ) : null}
    </div>
  );
}

/** A column's menu: pin it, move it, change its width, or put every column back. */
function ColumnMenuItems<Row>({
  column,
  col,
  count,
  layout,
  onResize,
  onMove,
  onPin,
}: {
  column: GridColumn<Row>;
  col: number;
  count: number;
  layout: GridColumnLayoutProps;
  onResize: (by: -1 | 1) => void;
  onMove: (by: -1 | 1) => void;
  onPin: (pinned: boolean) => void;
}) {
  const pinned = column.pinned === true;
  return (
    <>
      {layout.onPin ? (
        <MenuItem icon={pinned ? PinOff : Pin} onSelect={() => onPin(!pinned)}>
          {pinned ? 'Unpin the column' : 'Pin the column to the left'}
        </MenuItem>
      ) : null}
      {layout.onMove ? (
        <>
          <MenuItem icon={ArrowLeft} shortcut="mod+shift+arrowleft" disabled={col === 0} onSelect={() => onMove(-1)}>
            Move left
          </MenuItem>
          <MenuItem
            icon={ArrowRight}
            shortcut="mod+shift+arrowright"
            disabled={col >= count - 1}
            onSelect={() => onMove(1)}
          >
            Move right
          </MenuItem>
        </>
      ) : null}
      <MenuSeparator />
      <MenuItem shortcut="shift+arrowright" onSelect={() => onResize(1)}>
        Wider
      </MenuItem>
      <MenuItem shortcut="shift+arrowleft" onSelect={() => onResize(-1)}>
        Narrower
      </MenuItem>
      <MenuItem onSelect={() => layout.onResize(column.id, null)}>Put its width back</MenuItem>
      {layout.onReset ? (
        <>
          <MenuSeparator />
          <MenuItem icon={RotateCcw} onSelect={layout.onReset}>
            Put every column back
          </MenuItem>
        </>
      ) : null}
    </>
  );
}

/**
 * The grid's footer: its key hints, then where the cursor is. The hints are for the eye (the keys are the grid's and
 * listed in the `?` sheet); the place is no live region, as it moves with every key.
 */
function GridFoot({ hints, where }: { hints: readonly GridKeyHint[]; where: string }) {
  return (
    <div className="si-grid-foot">
      {hints.map((hint) => (
        <span key={hint.chord} className="si-grid-foot__hint">
          {hint.chord.split(' ').map((chord) => (
            <Kbd key={chord} chord={chord} size="sm" aria-hidden />
          ))}
          <span className="sr-only">{hint.chord.split(' ').join(' or ')}</span>
          {hint.words}
        </span>
      ))}
      <span className="si-grid-foot__end">{where}</span>
    </div>
  );
}

/**
 * Why there are no rows, under the header. In a short grid (a small window, the inspector docked at the bottom) it
 * can be taller than its room and scroll: then, and only then, it's a Tab stop with a name, so the keyboard can
 * scroll it (WCAG 2.1.1). With room for it, the grid stays the one stop. `onHeight` hears how tall what it says is
 * (its own height, whatever room it has), for a grid that keeps room for it (`minRows`).
 */
function EmptyRegion({
  label,
  top,
  bottom,
  onHeight,
  children,
}: {
  label: string;
  top: number;
  /** What's under it in the frame (the footer), as a length. */
  bottom?: string | undefined;
  onHeight?: ((height: number) => void) | undefined;
  children: ReactNode;
}) {
  const ref = useRef<HTMLDivElement>(null);
  const [scrolls, setScrolls] = useState(false);
  useLayoutEffect(() => {
    const element = ref.current;
    if (!element) return undefined;
    const measure = () => {
      setScrolls(element.scrollHeight > element.clientHeight + 1);
      onHeight?.([...element.children].reduce((sum, child) => sum + (child as HTMLElement).offsetHeight, 0));
    };
    measure();
    const observer = typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(measure);
    observer?.observe(element);
    if (element.firstElementChild) observer?.observe(element.firstElementChild);
    return () => observer?.disconnect();
  }, [onHeight]);
  return (
    <div
      ref={ref}
      className="absolute inset-x-0 bottom-0 overflow-auto"
      style={bottom === undefined ? { top } : { top, bottom }}
      {...(scrolls ? { tabIndex: 0, role: 'group', 'aria-label': `${label}: no rows` } : {})}
    >
      {children}
    </div>
  );
}

/** Keeps a pointer's events on an element while it drags (where the browser can: jsdom can't). */
function capturePointer(element: Element, pointer: number) {
  (element as Partial<Pick<Element, 'setPointerCapture'>>).setPointerCapture?.(pointer);
}

/** A control inside the header row: a sort button that took focus keeps its keys to itself. */
function isHeaderControl(target: EventTarget): boolean {
  return target instanceof Element && target.closest('[role="columnheader"]') !== null;
}

function inRanges(ranges: readonly Range[], index: number): boolean {
  return ranges.some(([first, last]) => index >= first && index <= last);
}

interface GridRowProps<Row> {
  index: number;
  row: Row;
  columns: readonly GridColumn<Row>[];
  top: number;
  height: number;
  width: number;
  selected: boolean;
  /** The active cell's column in this row, or -1. */
  activeCol: number;
  cellIdPrefix: string;
  /** In a tree: the row's level (1 for a group), and whether it's open (undefined for a row that doesn't open). */
  level?: number | undefined;
  expanded?: boolean | undefined;
  /** The row's one cell across every column, or null for its own cells. */
  span?: ReactNode;
  /** Where each pinned column sticks, px from the left (undefined for one that isn't); left out with none pinned. */
  pins?: readonly (number | undefined)[] | undefined;
  /** The last pinned column, which draws the pinned columns' edge. */
  pinEdge?: number | undefined;
}

/** A pinned cell's class and style in a row, or nothing for a cell that isn't. */
function pinOf(pins: readonly (number | undefined)[] | undefined, col: number, edge: number | undefined) {
  const left = pins?.[col];
  return left === undefined
    ? { className: undefined, style: undefined }
    : { className: clsx('si-grid-cell--pinned', col === edge && 'si-grid-cell--pin-edge'), style: { left } };
}

/** A row's place in a tree, for its `role="row"`. */
function treeAttributes(level: number | undefined, expanded: boolean | undefined) {
  return level === undefined
    ? {}
    : { 'aria-level': level, ...(expanded === undefined ? {} : { 'aria-expanded': expanded }) };
}

const GridRow = memo(function GridRow<Row>({
  index,
  row,
  columns,
  top,
  height,
  width,
  selected,
  activeCol,
  cellIdPrefix,
  level,
  expanded,
  span = null,
  pins,
  pinEdge,
}: GridRowProps<Row>) {
  return (
    <div
      role="row"
      aria-rowindex={index + 2}
      aria-selected={selected}
      {...treeAttributes(level, expanded)}
      data-selected={selected || undefined}
      data-level={level}
      className="si-grid-row absolute left-0"
      style={{ transform: `translateY(${top}px)`, height, width }}
    >
      {span !== null ? (
        <div
          id={`${cellIdPrefix}-r${index}-c0`}
          role="gridcell"
          aria-colindex={1}
          aria-colspan={columns.length}
          data-row={index}
          data-col={0}
          data-active={activeCol >= 0 || undefined}
          className="si-grid-cell si-grid-cell--span"
        >
          {span}
        </div>
      ) : null}
      {span !== null
        ? null
        : columns.map((column, col) => {
            const pin = pinOf(pins, col, pinEdge);
            return (
              <div
                key={column.id}
                id={`${cellIdPrefix}-r${index}-c${col}`}
                role="gridcell"
                aria-colindex={col + 1}
                data-row={index}
                data-col={col}
                data-active={col === activeCol || undefined}
                className={clsx(
                  'si-grid-cell',
                  column.numeric && 'si-grid-cell--numeric',
                  column.mono && 'font-mono',
                  pin.className,
                )}
                style={pin.style}
              >
                {column.cell(row)}
              </div>
            );
          })}
    </div>
  );
}) as <Row>(props: GridRowProps<Row>) => ReactNode;

function OffscreenRow<Row>({
  index,
  row,
  columns,
  activeCol,
  cellIdPrefix,
  selected,
  level,
  expanded,
  spans,
}: {
  index: number;
  row: Row | undefined;
  columns: readonly GridColumn<Row>[];
  activeCol: number;
  cellIdPrefix: string;
  selected: boolean;
  level: number | undefined;
  expanded: boolean | undefined;
  /** Drawn as one cell: its first cell stands for it. */
  spans: boolean;
}) {
  return (
    <div
      role="row"
      aria-rowindex={index + 2}
      aria-selected={selected}
      {...treeAttributes(level, expanded)}
      className="sr-only"
    >
      {(spans ? columns.slice(0, 1) : columns).map((column, col) => (
        <div
          key={column.id}
          id={`${cellIdPrefix}-r${index}-c${col}`}
          role="gridcell"
          aria-colindex={col + 1}
          data-active={col === activeCol || undefined}
        >
          {row === undefined ? '' : column.text(row)}
        </div>
      ))}
    </div>
  );
}

const PlaceholderRow = memo(function PlaceholderRow({
  index,
  top,
  height,
  width,
  columns,
  activeCol,
  cellIdPrefix,
  level,
  expanded,
  pins,
  pinEdge,
}: {
  index: number;
  top: number;
  height: number;
  width: number;
  columns: number;
  pins?: readonly (number | undefined)[] | undefined;
  pinEdge?: number | undefined;
  /** The active cell's column in this row, or -1. */
  activeCol: number;
  /** Null for the first load's skeleton, whose rows aren't real yet. */
  cellIdPrefix: string | null;
  level?: number | undefined;
  expanded?: boolean | undefined;
}) {
  return (
    <div
      role="row"
      aria-rowindex={index + 2}
      {...treeAttributes(level, expanded)}
      aria-busy="true"
      className="si-grid-row absolute left-0"
      style={{ transform: `translateY(${top}px)`, height, width }}
    >
      {Array.from({ length: columns }, (_, col) => {
        const pin = pinOf(pins, col, pinEdge);
        return (
          <div
            key={col}
            {...(cellIdPrefix === null
              ? {}
              : { id: `${cellIdPrefix}-r${index}-c${col}`, 'data-row': index, 'data-col': col })}
            role="gridcell"
            aria-colindex={col + 1}
            data-active={col === activeCol || undefined}
            className={clsx('si-grid-cell', pin.className)}
            style={pin.style}
          >
            <span aria-hidden="true" className="skeleton-static block h-(--skeleton-text) w-3/4" />
          </div>
        );
      })}
    </div>
  );
});

/** The clipboard said no: the browser refused, and so did the fallback. */
class ClipboardRefused extends Error {
  constructor() {
    super('The clipboard refused the text.');
  }
}

/** None of the selected rows could be read: nothing to put on the clipboard. */
class NothingToCopy extends Error {
  constructor() {
    super('None of the selected rows could be read.');
  }
}

/** The grid primitive. Give it a new `key` when the rows change meaning (another sort or filter). */
export const GridCore = GridCoreInner as <Row>(props: GridCoreProps<Row>) => ReactNode;
