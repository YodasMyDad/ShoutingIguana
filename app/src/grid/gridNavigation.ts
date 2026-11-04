// Keyboard and pointer behaviour of the grid as a pure state machine, following the ARIA grid pattern and the IA's
// keyboard map: arrows move, Home/End go to the row's first and last cell, Ctrl+Home/End to the grid's first and last
// cell (the APG's rule), Ctrl+Up/Down to the first and last row in the column (Cmd+Up/Down on macOS, whose laptops
// have no Home or End), Page Up/Down move by a screenful, Enter inspects, Space selects, Shift extends, Ctrl+A selects
// all, Ctrl+C copies, Shift+F10 or the menu key opens the row's menu. Cmd replaces Ctrl on macOS. In a grid whose
// columns sort, Up from the first row reaches the header row, where Enter or Space sorts (`headerKey`): the grid stays
// one stop for Tab.
//
// One rule keeps selection predictable: a row selected by a plain click moves with the cursor, as in a list; a
// selection built with Space, Shift, Ctrl+click or Ctrl+A stays put while the cursor moves.
import {
  EMPTY_SELECTION,
  effectiveRanges,
  extendTo,
  selectAll,
  selectOnly,
  selectedCount,
  toggleRow,
  type Selection,
} from './selection';

export interface GridPosition {
  row: number;
  col: number;
}

export interface GridNavState {
  active: GridPosition | null;
  selection: Selection;
  /** The selection is one row picked by a plain click, so it follows the cursor. */
  follow: boolean;
}

export const INITIAL_NAV: GridNavState = { active: null, selection: EMPTY_SELECTION, follow: false };

export interface GridDims {
  rowCount: number;
  colCount: number;
  /** Rows per screenful, for Page Up and Page Down. */
  pageRows: number;
}

export interface KeyInput {
  key: string;
  shift: boolean;
  /** Ctrl, or Cmd on macOS. */
  mod: boolean;
  alt: boolean;
}

export type GridEffect =
  { type: 'activate'; row: number } | { type: 'copy' } | { type: 'contextMenu'; row: number; col: number } | null;

export interface GridKeyResult {
  state: GridNavState;
  effect: GridEffect;
  /** Whether the grid used the key (and the event's default should be prevented). */
  handled: boolean;
  /** A row to scroll into view. */
  reveal: number | null;
}

const unhandled = (state: GridNavState): GridKeyResult => ({ state, effect: null, handled: false, reveal: null });

function clamp(value: number, max: number): number {
  return Math.min(Math.max(0, value), max);
}

/** Whether a plain move takes the selection with it: a clicked row, still exactly the row under the cursor. */
function selectionFollows(state: GridNavState): boolean {
  if (!state.follow || !state.active) return false;
  const ranges = effectiveRanges(state.selection);
  return ranges.length === 1 && ranges[0]?.[0] === state.active.row && ranges[0][1] === state.active.row;
}

function moveTo(state: GridNavState, target: GridPosition, extend: boolean): GridKeyResult {
  const from = state.active?.row ?? target.row;
  const follows = !extend && selectionFollows(state);
  let selection: Selection;
  if (extend) selection = extendTo(state.selection, from, target.row);
  else if (follows) selection = selectOnly(target.row);
  else selection = { ranges: effectiveRanges(state.selection), anchor: null, extent: null };
  return { state: { active: target, selection, follow: follows }, effect: null, handled: true, reveal: target.row };
}

export function gridKey(state: GridNavState, input: KeyInput, dims: GridDims): GridKeyResult {
  const { rowCount, colCount, pageRows } = dims;
  if (rowCount <= 0 || colCount <= 0 || input.alt) return unhandled(state);
  const lastRow = rowCount - 1;
  const lastCol = colCount - 1;
  const active = state.active ? { row: clamp(state.active.row, lastRow), col: clamp(state.active.col, lastCol) } : null;
  const current = { ...state, active };
  const here = active ?? { row: 0, col: 0 };

  switch (input.key) {
    case 'ArrowDown':
      if (input.mod) return moveTo(current, { row: lastRow, col: here.col }, input.shift);
      return moveTo(current, { row: active ? clamp(here.row + 1, lastRow) : 0, col: here.col }, input.shift);
    case 'ArrowUp':
      if (input.mod) return moveTo(current, { row: 0, col: here.col }, input.shift);
      return moveTo(current, { row: active ? clamp(here.row - 1, lastRow) : 0, col: here.col }, input.shift);
    case 'ArrowRight':
      if (input.mod || input.shift) return unhandled(state);
      return moveTo(current, { row: here.row, col: active ? clamp(here.col + 1, lastCol) : 0 }, false);
    case 'ArrowLeft':
      if (input.mod || input.shift) return unhandled(state);
      return moveTo(current, { row: here.row, col: active ? clamp(here.col - 1, lastCol) : 0 }, false);
    case 'Home':
      return input.mod
        ? moveTo(current, { row: 0, col: 0 }, input.shift)
        : moveTo(current, { row: here.row, col: 0 }, false);
    case 'End':
      return input.mod
        ? moveTo(current, { row: lastRow, col: lastCol }, input.shift)
        : moveTo(current, { row: here.row, col: lastCol }, false);
    case 'PageDown':
      return moveTo(current, { row: clamp(here.row + pageRows, lastRow), col: here.col }, input.shift);
    case 'PageUp':
      return moveTo(current, { row: clamp(here.row - pageRows, lastRow), col: here.col }, input.shift);
    case 'Enter':
      if (!active || input.mod || input.shift) return unhandled(state);
      return { state: current, effect: { type: 'activate', row: active.row }, handled: true, reveal: active.row };
    case ' ': {
      if (input.mod) return unhandled(state);
      // Shift+Space selects from the anchor to here; Space alone flips this row.
      const anchor = current.selection.anchor;
      const selection =
        input.shift && anchor !== null
          ? { ranges: effectiveRanges(extendTo(current.selection, here.row, here.row)), anchor, extent: null }
          : toggleRow(current.selection, here.row);
      return { state: { active: here, selection, follow: false }, effect: null, handled: true, reveal: here.row };
    }
    case 'a':
    case 'A':
      if (!input.mod || input.shift) return unhandled(state);
      return {
        state: { ...current, selection: selectAll(rowCount), follow: false },
        effect: null,
        handled: true,
        reveal: null,
      };
    case 'c':
    case 'C':
      if (!input.mod || input.shift) return unhandled(state);
      return { state: current, effect: { type: 'copy' }, handled: true, reveal: null };
    case 'Escape':
      if (selectedCount(current.selection) === 0) return unhandled(state);
      return {
        state: { ...current, selection: EMPTY_SELECTION, follow: false },
        effect: null,
        handled: true,
        reveal: null,
      };
    case 'F10':
      if (!input.shift || !active) return unhandled(state);
      return {
        state: current,
        effect: { type: 'contextMenu', row: active.row, col: active.col },
        handled: true,
        reveal: active.row,
      };
    case 'ContextMenu':
      if (!active) return unhandled(state);
      return {
        state: current,
        effect: { type: 'contextMenu', row: active.row, col: active.col },
        handled: true,
        reveal: active.row,
      };
    default:
      return unhandled(state);
  }
}

/**
 * What a key does with the cursor on the header row, in a grid whose columns sort (the APG's grid: the header row is a
 * row the cursor reaches, from the first row with Up). Left, Right, Home and End move along it; Enter or Space sorts by
 * the column; Down goes back to the rows; a key that moves through the rows (Page Down, Ctrl+End) takes the cursor
 * back to them and acts there. Null for a key the header doesn't use.
 */
export type HeaderKeyResult =
  { type: 'move'; col: number } | { type: 'sort'; col: number } | { type: 'rows' } | { type: 'pass' } | null;

export function headerKey(col: number, input: KeyInput, colCount: number): HeaderKeyResult {
  if (input.alt || colCount <= 0) return null;
  const last = colCount - 1;
  const here = clamp(col, last);
  const plain = !input.mod && !input.shift;
  switch (input.key) {
    case 'ArrowLeft':
      return plain ? { type: 'move', col: clamp(here - 1, last) } : null;
    case 'ArrowRight':
      return plain ? { type: 'move', col: clamp(here + 1, last) } : null;
    case 'Home':
      return plain ? { type: 'move', col: 0 } : { type: 'pass' };
    case 'End':
      return plain ? { type: 'move', col: last } : { type: 'pass' };
    case 'ArrowUp':
      return plain ? { type: 'move', col: here } : { type: 'pass' };
    case 'ArrowDown':
      return plain ? { type: 'rows' } : { type: 'pass' };
    case 'PageUp':
    case 'PageDown':
      return { type: 'pass' };
    case 'Enter':
      return plain ? { type: 'sort', col: here } : null;
    case ' ':
      // Shift+Space selects rows: on the header there's no row to select.
      return input.mod ? null : input.shift ? { type: 'move', col: here } : { type: 'sort', col: here };
    case 'ContextMenu':
      return { type: 'move', col: here };
    case 'F10':
      return input.shift ? { type: 'move', col: here } : null;
    default:
      return null;
  }
}

/** A pointer press on a cell: a click selects the row, Ctrl/Cmd+click toggles it, Shift+click extends. */
export function gridPointer(
  state: GridNavState,
  position: GridPosition,
  modifiers: { shift: boolean; mod: boolean },
): GridNavState {
  if (modifiers.shift) {
    return {
      active: position,
      selection: extendTo(state.selection, state.active?.row ?? position.row, position.row),
      follow: false,
    };
  }
  if (modifiers.mod) return { active: position, selection: toggleRow(state.selection, position.row), follow: false };
  return { active: position, selection: selectOnly(position.row), follow: true };
}
