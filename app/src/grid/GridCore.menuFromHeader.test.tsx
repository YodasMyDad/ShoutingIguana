// with the cursor on the header row (a sortable grid), a right-click on a row selects that row and opens
// its menu, but leaves the cursor on the header (onPointerDown, which clears the header cursor, returns for button 2;
// onContextMenuEvent doesn't clear it). When the menu closes and the keyboard is given back, Enter sorts instead of
// acting on the row the menu was for.
import { act, fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { GridCore } from './GridCore';
import type { GridColumn } from './types';

vi.mock('../feedback/notify', () => ({ notifyError: vi.fn(), notifyResult: vi.fn() }));
vi.mock('../feedback/announce', () => ({ announce: vi.fn() }));

interface Row {
  id: number;
}

const rows: Row[] = Array.from({ length: 10 }, (_, id) => ({ id }));
const columns: GridColumn<Row>[] = [
  { id: 'a', header: 'A', width: 200, sortable: true, cell: (row) => `a${row.id}`, text: (row) => `a${row.id}` },
];

describe('a right-click on a row while the cursor is on the header', () => {
  it('moves the cursor to that row', () => {
    const onSort = vi.fn();
    const onActivate = vi.fn();
    const onContextMenu = vi.fn();
    render(
      <GridCore<Row>
        label="Rows"
        columns={columns}
        rowCount={rows.length}
        getRow={(index) => rows[index]}
        rowHeight={32}
        headerHeight={32}
        onSort={onSort}
        onActivate={onActivate}
        onContextMenu={onContextMenu}
      />,
    );
    const grid = screen.getByRole('grid', { name: 'Rows' });
    act(() => grid.focus());
    fireEvent.keyDown(grid, { key: 'ArrowUp' });
    expect(document.getElementById(grid.getAttribute('aria-activedescendant')!)).toHaveAttribute(
      'role',
      'columnheader',
    );
    // Right-click row 3: on Windows the button-2 pointerdown comes first, then contextmenu.
    const cell = grid.querySelector('[data-row="3"][data-col="0"]')!;
    fireEvent.pointerDown(cell, { button: 2 });
    fireEvent.contextMenu(cell);
    expect(onContextMenu.mock.lastCall?.[0]).toBe(3);
    // The menu closes and gives the keyboard back (menu.focus()); Enter should act on row 3.
    act(() => (onContextMenu.mock.lastCall?.[3] as { focus: () => void }).focus());
    fireEvent.keyDown(grid, { key: 'Enter' });
    expect(onSort).not.toHaveBeenCalled();
    expect(onActivate).toHaveBeenCalledWith(3, rows[3], 'a');
  });
});
