// The grid primitive: a fixed-height windowed virtualiser with the ARIA grid pattern, full keyboard use, row
// selection and copy as tab-separated text. Rows can be grouped (`tree`): groups that open to show their rows, as the
// ARIA treegrid pattern has them, which the Issues screen uses to group a check's rows by detail. The full grid
// (column presets, chooser, filter bar, saved views) wraps this in the slices; every grid in the app is this component.
import clsx from 'clsx';
import { ArrowDown, ArrowUp, ChevronsUpDown } from 'lucide-react';
import {
  memo,
  useEffect,
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
import { EMPTY_SELECTION, clampSelection, effectiveRanges, selectOnly, selectedIndices, type Range } from './selection';
import { MAX_COPY_ROWS, toTsv } from './tsv';
import type { GridColumn, GridRange, GridTree, RowFetcher } from './types';
import { VirtualScrollbar } from './VirtualScrollbar';
import { RefreshLine } from '../ui/ProgressBar';
import { Icon } from '../ui/Icon';

export interface GridCopyResult {
  /** Rows copied, or 1 for a single cell. */
  rows: number;
  /** More rows were selected than MAX_COPY_ROWS. */
  truncated: boolean;
  /** Only the active cell was copied: nothing was selected. */
  cell: boolean;
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
  /** The cursor moved to another row (the inspector follows it). */
  onActiveRowChange?: (index: number | null) => void;
  /** The cursor moved to another column, by its id: for an action on the column the cursor is in (copy it). */
  onActiveColumnChange?: (column: string | undefined) => void;
  /** Right-click, Shift+F10 or the menu key. */
  onContextMenu?: (index: number, column: number, anchor: { x: number; y: number }) => void;
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
   * cursor on this header, focused (`focusHeader`).
   */
  onSort?: (column: string, focused: boolean) => void;
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
}

const SKELETON_ROWS = 12;

/** The width the custom vertical scrollbar covers at the right of a windowed grid (tokens.css `--scrollbar-size`). */
function coveredWidth(element: HTMLElement, windowed: boolean): number {
  if (!windowed) return 0;
  const size = Number.parseFloat(getComputedStyle(element).getPropertyValue('--scrollbar-size'));
  return Number.isFinite(size) ? size : 0;
}

function GridCoreInner<Row>({
  label,
  columns,
  rowCount,
  getRow,
  rowHeight,
  headerHeight,
  overscan = DEFAULT_OVERSCAN,
  onRangeChange,
  onScrollOffset,
  onActivate,
  onActiveRowChange,
  onActiveColumnChange,
  onContextMenu,
  fetchRows,
  onCopy,
  emptyState,
  busy = false,
  loadingNote,
  className,
  modelRef,
  tree,
  spanRow,
  onSort,
  focusHeader,
  startRow,
}: GridCoreProps<Row>) {
  const id = useId();
  const scrollRef = useRef<HTMLDivElement>(null);
  const [nav, setNav] = useState<GridNavState>(() =>
    startRow === undefined || startRow < 0 ? INITIAL_NAV : { ...INITIAL_NAV, active: { row: startRow, col: 0 } },
  );
  // The header row is a row the cursor reaches when the columns sort: the column it's on there, or null in the rows.
  const sortable = onSort !== undefined && columns.some((column) => column.sortable);
  const [headerCursor, setHeaderCursor] = useState<number | null>(() => {
    const at = focusHeader === undefined ? -1 : columns.findIndex((column) => column.id === focusHeader);
    return at >= 0 ? at : null;
  });
  const header = sortable && headerCursor !== null ? Math.min(headerCursor, columns.length - 1) : null;
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
    if (!element || !column) return;
    const left = columns.slice(0, col).reduce((sum, other) => sum + other.width, 0);
    const visible = element.clientWidth - coveredWidth(element, view.windowed);
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
    if (event.key === 'Enter' && expanded !== undefined) {
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
  const selectionText = async (indices: readonly number[]) => {
    const missing = indices.filter((index) => getRow(index) === undefined);
    const fetched = missing.length && fetchRows ? await fetchRows(missing) : [];
    const byIndex = new Map(missing.map((index, i) => [index, fetched[i]]));
    const rows = indices.map((index) => getRow(index) ?? byIndex.get(index));
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
    try {
      if (pick.indices.length === 0) {
        if (!active) return;
        const column = columns[active.col];
        const row = getRow(active.row) ?? (await fetchRows?.([active.row]))?.[0];
        if (!column || row === undefined) return;
        if (!(await copyText(column.text(row)))) throw new ClipboardRefused();
        announce('Copied the cell.');
        onCopy?.({ rows: 1, truncated: false, cell: true });
        return;
      }
      const copied = await selectionText(pick.indices);
      if (copied.rows === 0) throw new NothingToCopy();
      if (!(await copyText(copied.text))) throw new ClipboardRefused();
      // What was copied, never what was selected: rows that weren't there aren't counted.
      announce(
        copied.unread > 0
          ? `Copied ${formatCountOf(copied.rows, 'row')}. ${formatCountOf(copied.unread, 'row')} couldn’t be read.`
          : `Copied ${formatCountOf(copied.rows, 'row')}.`,
      );
      onCopy?.({ rows: copied.rows, truncated: pick.truncated, cell: false });
    } catch (error) {
      notifyError(error, {
        title: pick.indices.length > 0 ? 'The rows couldn’t be copied.' : 'The cell couldn’t be copied.',
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
        if (column?.sortable) onSort?.(column.id, true);
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
      if (move?.type === 'pass') setHeaderCursor(null);
    } else if (
      sortable &&
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
    if (effect?.type === 'activate') {
      onActivate?.(effect.row, getRow(effect.row), columns[result.state.active?.col ?? active?.col ?? 0]?.id);
    } else if (effect?.type === 'copy') void copy();
    else if (effect?.type === 'contextMenu') {
      const cell = document.getElementById(cellId(effect.row, effect.col))?.getBoundingClientRect();
      onContextMenu?.(effect.row, effect.col, cell ? { x: cell.left, y: cell.bottom } : { x: 0, y: 0 });
    }
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
    if (expanded !== undefined) toggle(position.row, !expanded);
    else onActivate?.(position.row, getRow(position.row), columns[position.col]?.id);
  };

  const onContextMenuEvent = (event: MouseEvent<HTMLDivElement>) => {
    const position = positionOf(event.target);
    if (!position || !onContextMenu) return;
    event.preventDefault();
    if (!ranges.some(([first, last]) => position.row >= first && position.row <= last)) {
      apply(gridPointer({ active, selection, follow: nav.follow }, position, { shift: false, mod: false }), null);
    }
    onContextMenu(position.row, position.col, { x: event.clientX, y: event.clientY });
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
  return (
    <div className={clsx('si-grid-frame relative flex min-h-0 flex-col overflow-hidden', className)}>
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
              <div
                key={column.id}
                id={headerCellId(col)}
                role="columnheader"
                aria-colindex={col + 1}
                aria-sort={column.sort}
                data-active={header === col || undefined}
                className={clsx('si-grid-cell', column.numeric && 'si-grid-cell--numeric')}
                title={sortable && !column.sortable ? column.sortHint : undefined}
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
                    onClick={() => onSort(column.id, document.activeElement === scrollRef.current)}
                  >
                    {column.header}
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
                  </button>
                ) : (
                  column.header
                )}
              </div>
            ))}
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
        <EmptyRegion label={label} top={headerHeight}>
          {emptyState}
        </EmptyRegion>
      ) : null}
    </div>
  );
}

/**
 * Why there are no rows, under the header. In a short grid (a small window, the inspector docked at the bottom) it
 * can be taller than its room and scroll: then, and only then, it's a Tab stop with a name, so the keyboard can
 * scroll it (WCAG 2.1.1). With room for it, the grid stays the one stop.
 */
function EmptyRegion({ label, top, children }: { label: string; top: number; children: ReactNode }) {
  const ref = useRef<HTMLDivElement>(null);
  const [scrolls, setScrolls] = useState(false);
  useLayoutEffect(() => {
    const element = ref.current;
    if (!element) return undefined;
    const measure = () => setScrolls(element.scrollHeight > element.clientHeight + 1);
    measure();
    const observer = typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(measure);
    observer?.observe(element);
    if (element.firstElementChild) observer?.observe(element.firstElementChild);
    return () => observer?.disconnect();
  }, []);
  return (
    <div
      ref={ref}
      className="absolute inset-x-0 bottom-0 overflow-auto"
      style={{ top }}
      {...(scrolls ? { tabIndex: 0, role: 'group', 'aria-label': `${label}: no rows` } : {})}
    >
      {children}
    </div>
  );
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
        : columns.map((column, col) => (
            <div
              key={column.id}
              id={`${cellIdPrefix}-r${index}-c${col}`}
              role="gridcell"
              aria-colindex={col + 1}
              data-row={index}
              data-col={col}
              data-active={col === activeCol || undefined}
              className={clsx('si-grid-cell', column.numeric && 'si-grid-cell--numeric', column.mono && 'font-mono')}
            >
              {column.cell(row)}
            </div>
          ))}
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
}: {
  index: number;
  top: number;
  height: number;
  width: number;
  columns: number;
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
      {Array.from({ length: columns }, (_, col) => (
        <div
          key={col}
          {...(cellIdPrefix === null
            ? {}
            : { id: `${cellIdPrefix}-r${index}-c${col}`, 'data-row': index, 'data-col': col })}
          role="gridcell"
          aria-colindex={col + 1}
          data-active={col === activeCol || undefined}
          className="si-grid-cell"
        >
          <span aria-hidden="true" className="skeleton-static block h-(--skeleton-text) w-3/4" />
        </div>
      ))}
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
