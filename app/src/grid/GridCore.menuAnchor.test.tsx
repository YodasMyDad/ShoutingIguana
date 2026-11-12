// Shift+F10 anchors the row menu at the cursor cell's rect, found by `cellId(row, col)`. A row drawn as
// one cell (`spanRow`: the Issues tree's page and "more" rows) has only its c0 cell, while the cursor keeps the column it
// had (activeCellId maps it to 0, the menu's anchor doesn't), so the lookup misses and the menu opens at (0, 0): the
// window's top-left corner.
import { act, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { GridCore } from './GridCore';
import type { GridColumn } from './types';

vi.mock('../feedback/notify', () => ({ notifyError: vi.fn(), notifyResult: vi.fn() }));
vi.mock('../feedback/announce', () => ({ announce: vi.fn() }));

interface Row {
  id: number;
  spanned: boolean;
}

const rows: Row[] = Array.from({ length: 10 }, (_, id) => ({ id, spanned: id % 2 === 1 }));
const columns: GridColumn<Row>[] = [
  { id: 'a', header: 'A', width: 200, cell: (row) => `a${row.id}`, text: (row) => `a${row.id}` },
  { id: 'b', header: 'B', width: 100, cell: (row) => `b${row.id}`, text: (row) => `b${row.id}` },
];

afterEach(() => {
  vi.restoreAllMocks();
});

describe('Shift+F10 on a row drawn as one cell', () => {
  it('opens the menu at the row, not at the window’s corner', () => {
    // jsdom does no layout: every element sits at (40, 300) here, so (0, 0) can only mean "no cell found".
    vi.spyOn(Element.prototype, 'getBoundingClientRect').mockImplementation(() => ({
      x: 40,
      y: 280,
      left: 40,
      top: 280,
      right: 240,
      bottom: 300,
      width: 200,
      height: 20,
      toJSON: () => ({}),
    }));
    const onContextMenu = vi.fn();
    render(
      <GridCore<Row>
        label="Rows"
        columns={columns}
        rowCount={rows.length}
        getRow={(index) => rows[index]}
        rowHeight={32}
        headerHeight={32}
        spanRow={(row) => (row.spanned ? `span ${row.id}` : null)}
        onContextMenu={onContextMenu}
      />,
    );
    const grid = screen.getByRole('grid', { name: 'Rows' });
    act(() => grid.focus());
    // Cursor on row 0, column B; then down onto row 1, which is drawn as one cell.
    fireEvent.keyDown(grid, { key: 'ArrowRight' });
    fireEvent.keyDown(grid, { key: 'ArrowDown' });
    fireEvent.keyDown(grid, { key: 'F10', shiftKey: true });
    expect(onContextMenu).toHaveBeenCalled();
    expect(onContextMenu.mock.lastCall?.[0]).toBe(1);
    // The menu is of the row's one cell: its column is the first, as the cursor's cell is drawn.
    expect(onContextMenu.mock.lastCall?.[1]).toBe(0);
    expect(onContextMenu.mock.lastCall?.[2]).toEqual({ x: 40, y: 300 });
  });
});
