import { describe, expect, it } from 'vitest';
import { INITIAL_NAV, gridKey, gridPointer, type GridNavState, type KeyInput } from './gridNavigation';
import { effectiveRanges, selectOnly } from './selection';

const dims = { rowCount: 1_000, colCount: 5, pageRows: 20 };
const key = (name: string, modifiers: Partial<KeyInput> = {}): KeyInput => ({
  key: name,
  shift: false,
  mod: false,
  alt: false,
  ...modifiers,
});
const at = (row: number, col = 0): GridNavState => ({ ...INITIAL_NAV, active: { row, col } });

describe('grid keys', () => {
  it('lands on the first cell when nothing is active yet', () => {
    const result = gridKey(INITIAL_NAV, key('ArrowDown'), dims);
    expect(result.state.active).toEqual({ row: 0, col: 0 });
    expect(result.handled).toBe(true);
  });

  it('moves with the arrows, clamped at the edges, and asks to reveal the row', () => {
    expect(gridKey(at(5, 2), key('ArrowDown'), dims)).toMatchObject({
      state: { active: { row: 6, col: 2 } },
      reveal: 6,
    });
    expect(gridKey(at(0, 2), key('ArrowUp'), dims).state.active).toEqual({ row: 0, col: 2 });
    expect(gridKey(at(5, 4), key('ArrowRight'), dims).state.active).toEqual({ row: 5, col: 4 });
    expect(gridKey(at(5, 0), key('ArrowLeft'), dims).state.active).toEqual({ row: 5, col: 0 });
  });

  it('goes to the row ends with Home and End, and the grid’s first and last cell with Ctrl, as the APG says', () => {
    expect(gridKey(at(5, 2), key('Home'), dims).state.active).toEqual({ row: 5, col: 0 });
    expect(gridKey(at(5, 2), key('End'), dims).state.active).toEqual({ row: 5, col: 4 });
    expect(gridKey(at(5, 2), key('Home', { mod: true }), dims).state.active).toEqual({ row: 0, col: 0 });
    expect(gridKey(at(5, 2), key('End', { mod: true }), dims).state.active).toEqual({ row: 999, col: 4 });
  });

  it('goes to the first and last row in the column with Ctrl+Up and Ctrl+Down (Cmd on macOS)', () => {
    expect(gridKey(at(5, 2), key('ArrowUp', { mod: true }), dims).state.active).toEqual({ row: 0, col: 2 });
    const down = gridKey(at(5, 2), key('ArrowDown', { mod: true }), dims);
    expect(down.state.active).toEqual({ row: 999, col: 2 });
    expect(down.reveal).toBe(999);
    const extended = gridKey(at(5, 2), key('ArrowDown', { mod: true, shift: true }), dims);
    expect(effectiveRanges(extended.state.selection)).toEqual([[5, 999]]);
  });

  it('moves a screenful with Page Up and Page Down', () => {
    expect(gridKey(at(5), key('PageDown'), dims).state.active?.row).toBe(25);
    expect(gridKey(at(5), key('PageUp'), dims).state.active?.row).toBe(0);
  });

  it('inspects the active row on Enter', () => {
    expect(gridKey(at(7), key('Enter'), dims).effect).toEqual({ type: 'activate', row: 7 });
  });

  it('selects with Space and extends with Shift', () => {
    let state = gridKey(at(3), key(' '), dims).state;
    expect(effectiveRanges(state.selection)).toEqual([[3, 3]]);
    state = gridKey(state, key('ArrowDown', { shift: true }), dims).state;
    state = gridKey(state, key('ArrowDown', { shift: true }), dims).state;
    expect(effectiveRanges(state.selection)).toEqual([[3, 5]]);
    state = gridKey(state, key('End', { mod: true, shift: true }), dims).state;
    expect(effectiveRanges(state.selection)).toEqual([[3, 999]]);
  });

  it('moves a click selection with the cursor, but leaves a built selection where it is', () => {
    const clicked = gridPointer(INITIAL_NAV, { row: 4, col: 0 }, { shift: false, mod: false });
    expect(effectiveRanges(gridKey(clicked, key('ArrowDown'), dims).state.selection)).toEqual([[5, 5]]);

    let built = gridKey(at(4), key(' '), dims).state;
    built = gridKey(built, key('ArrowDown'), dims).state;
    built = gridKey(built, key(' '), dims).state;
    built = gridKey(built, key('ArrowDown'), dims).state;
    expect(effectiveRanges(built.selection)).toEqual([[4, 5]]);
    expect(built.active?.row).toBe(6);
  });

  it('selects everything with Ctrl+A and copies with Ctrl+C', () => {
    const all = gridKey(at(0), key('a', { mod: true }), dims);
    expect(effectiveRanges(all.state.selection)).toEqual([[0, 999]]);
    expect(gridKey(all.state, key('c', { mod: true }), dims).effect).toEqual({ type: 'copy' });
  });

  it('clears a selection with Esc, and lets Esc through when there is none', () => {
    const selected = { ...at(2), selection: selectOnly(2), follow: true };
    expect(gridKey(selected, key('Escape'), dims)).toMatchObject({ handled: true });
    expect(gridKey(at(2), key('Escape'), dims).handled).toBe(false);
  });

  it('opens the row menu with Shift+F10 or the menu key', () => {
    expect(gridKey(at(3, 1), key('F10', { shift: true }), dims).effect).toEqual({
      type: 'contextMenu',
      row: 3,
      col: 1,
    });
    expect(gridKey(at(3, 1), key('ContextMenu'), dims).effect).toEqual({ type: 'contextMenu', row: 3, col: 1 });
  });

  it('leaves keys it does not use, and Alt combinations (back and forward), to the app', () => {
    expect(gridKey(at(3), key('x'), dims).handled).toBe(false);
    expect(gridKey(at(3), key('ArrowLeft', { alt: true }), dims).handled).toBe(false);
    expect(gridKey(INITIAL_NAV, key('ArrowDown'), { ...dims, rowCount: 0 }).handled).toBe(false);
  });
});

describe('grid pointer', () => {
  it('selects the clicked row, toggles with Ctrl, extends with Shift', () => {
    let state = gridPointer(INITIAL_NAV, { row: 2, col: 1 }, { shift: false, mod: false });
    expect(effectiveRanges(state.selection)).toEqual([[2, 2]]);
    state = gridPointer(state, { row: 6, col: 1 }, { shift: false, mod: true });
    expect(effectiveRanges(state.selection)).toEqual([
      [2, 2],
      [6, 6],
    ]);
    state = gridPointer(state, { row: 9, col: 0 }, { shift: true, mod: false });
    expect(effectiveRanges(state.selection)).toEqual([
      [2, 2],
      [6, 9],
    ]);
    expect(state.active).toEqual({ row: 9, col: 0 });
  });
});
