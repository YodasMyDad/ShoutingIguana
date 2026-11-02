// The grid primitive: a fixed-height windowed virtualiser with the ARIA grid pattern, full keyboard use, row
// selection and copy as tab-separated text. The full grid (column presets, chooser, filter bar, saved views,
// grouping) wraps this in the slices; every grid in the app is this component.
import clsx from 'clsx';
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
import { INITIAL_NAV, gridKey, gridPointer, type GridNavState, type GridPosition } from './gridNavigation';
import { DEFAULT_OVERSCAN, firstFullyVisible, rowsPerPage, scrollLeftToReveal } from './rowRange';
import { useRowWindow, type RowWindowModel } from './rowWindow';
import { windowFor } from './scrollWindow';
import { clampSelection, effectiveRanges, selectedIndices, type Range } from './selection';
import { MAX_COPY_ROWS, toTsv } from './tsv';
import type { GridColumn, GridRange, RowFetcher } from './types';
import { VirtualScrollbar } from './VirtualScrollbar';

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
  /** Enter or double-click: open the inspector. */
  onActivate?: (index: number, row: Row | undefined) => void;
  /** The cursor moved to another row (the inspector follows it). */
  onActiveRowChange?: (index: number | null) => void;
  /** Right-click, Shift+F10 or the menu key. */
  onContextMenu?: (index: number, column: number, anchor: { x: number; y: number }) => void;
  /** Fetches rows that aren't loaded, for copying a selection that spans pages. */
  fetchRows?: RowFetcher<Row>;
  onCopy?: (result: GridCopyResult) => void;
  /** Shown instead of rows when there are none: say why, and what to do. */
  emptyState?: ReactNode;
  /** Data is refreshing behind what's shown: a thin progress line, nothing blanks. */
  busy?: boolean;
  className?: string;
  /** Tests and stories: the virtualiser's model, to drive scrolling. */
  modelRef?: (model: RowWindowModel) => void;
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
  onContextMenu,
  fetchRows,
  onCopy,
  emptyState,
  busy = false,
  className,
  modelRef,
}: GridCoreProps<Row>) {
  const id = useId();
  const scrollRef = useRef<HTMLDivElement>(null);
  const [nav, setNav] = useState<GridNavState>(INITIAL_NAV);
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
    setNav(next);
    if (reveal !== null) {
      model.scrollToIndex(reveal);
      if (next.active) revealColumn(next.active.col);
    }
  };

  /** The rows as text: rows that aren't loaded are fetched first. Throws when a page can't be. */
  const selectionText = async (indices: readonly number[]) => {
    const missing = indices.filter((index) => getRow(index) === undefined);
    const fetched = missing.length && fetchRows ? await fetchRows(missing) : [];
    const byIndex = new Map(missing.map((index, i) => [index, fetched[i]]));
    const rows = indices.map((index) => getRow(index) ?? byIndex.get(index));
    return toTsv(
      columns.map((column) => column.header),
      rows.map((row) => columns.map((column) => (row === undefined ? '' : column.text(row)))),
    );
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
      if (!(await copyText(await selectionText(pick.indices)))) throw new ClipboardRefused();
      announce(`Copied ${formatCountOf(pick.indices.length, 'row')}.`);
      onCopy?.({ rows: pick.indices.length, truncated: pick.truncated, cell: false });
    } catch (error) {
      notifyError(error, {
        title: pick.indices.length > 0 ? 'The rows couldn’t be copied.' : 'The cell couldn’t be copied.',
        ...(error instanceof ClipboardRefused ? { description: 'The clipboard didn’t take it. Try again.' } : {}),
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
    const result = gridKey(
      { active, selection, follow: nav.follow },
      { key: event.key, shift: event.shiftKey, mod, alt: event.altKey },
      { rowCount: count, colCount: columns.length, pageRows: rowsPerPage(view.viewport, rowHeight) },
    );
    if (!result.handled) return;
    event.preventDefault();
    apply(result.state, result.reveal);
    const effect = result.effect;
    if (effect?.type === 'activate') onActivate?.(effect.row, getRow(effect.row));
    else if (effect?.type === 'copy') void copy();
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
    if (position) onActivate?.(position.row, getRow(position.row));
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
      {busy ? <div role="progressbar" aria-label="Refreshing" className="si-progress-line" /> : null}
      <div
        ref={scrollRef}
        role="grid"
        aria-label={label}
        aria-rowcount={loading ? -1 : count + 1}
        aria-colcount={columns.length}
        aria-multiselectable="true"
        aria-busy={loading || busy}
        aria-activedescendant={active ? cellId(active.row, active.col) : undefined}
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
                role="columnheader"
                aria-colindex={col + 1}
                aria-sort={column.sort}
                className={clsx('si-grid-cell', column.numeric && 'si-grid-cell--numeric')}
              >
                {column.header}
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
                activeCol={active?.row === index ? active.col : -1}
                cellIdPrefix={id}
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
                activeCol={active?.row === index ? active.col : -1}
                cellIdPrefix={id}
              />
            );
          })}
          {activeOffscreen ? (
            // The active row stays in the DOM while scrolled away, so aria-activedescendant always points somewhere.
            <OffscreenRow
              index={active.row}
              row={getRow(active.row)}
              columns={columns}
              activeCol={active.col}
              cellIdPrefix={id}
              selected={inRanges(ranges, active.row)}
            />
          ) : null}
        </div>
      </div>
      {view.windowed ? (
        <VirtualScrollbar model={model} total={count * rowHeight} viewport={view.viewport} top={headerHeight} />
      ) : null}
      {!loading && rowCount === 0 && emptyState ? (
        <div className="absolute inset-x-0 bottom-0 overflow-auto" style={{ top: headerHeight }}>
          {emptyState}
        </div>
      ) : null}
    </div>
  );
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
}: GridRowProps<Row>) {
  return (
    <div
      role="row"
      aria-rowindex={index + 2}
      aria-selected={selected}
      data-selected={selected || undefined}
      className="si-grid-row absolute left-0"
      style={{ transform: `translateY(${top}px)`, height, width }}
    >
      {columns.map((column, col) => (
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
}: {
  index: number;
  row: Row | undefined;
  columns: readonly GridColumn<Row>[];
  activeCol: number;
  cellIdPrefix: string;
  selected: boolean;
}) {
  return (
    <div role="row" aria-rowindex={index + 2} aria-selected={selected} className="sr-only">
      {columns.map((column, col) => (
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
}) {
  return (
    <div
      role="row"
      aria-rowindex={index + 2}
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

/** The grid primitive. Give it a new `key` when the rows change meaning (another sort or filter). */
export const GridCore = GridCoreInner as <Row>(props: GridCoreProps<Row>) => ReactNode;
