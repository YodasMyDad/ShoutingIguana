// A grid's columns laid out by the user (gap 10 of the closing audit): resized, moved and pinned, from the header's
// keys, its menu and the pointer; and the footer's key hints (gap 21).
import { act, fireEvent, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { GridCore, type GridColumnLayoutProps } from './GridCore';
import { GRID_KEY_HINTS } from './gridFoot';
import type { GridColumn } from './types';

vi.mock('../feedback/notify', () => ({ notifyError: vi.fn(), notifyResult: vi.fn() }));
vi.mock('../feedback/announce', () => ({ announce: vi.fn() }));

interface Row {
  id: number;
}

const rows: Row[] = Array.from({ length: 10 }, (_, id) => ({ id }));
const make = (id: string, width: number, extra: Partial<GridColumn<Row>> = {}): GridColumn<Row> => ({
  id,
  header: id.toUpperCase(),
  width,
  cell: (row) => `${id}${row.id}`,
  text: (row) => `${id}${row.id}`,
  ...extra,
});

function setup({
  columns = [make('a', 200), make('b', 100), make('c', 100)],
  layout = true,
  onSort,
  keyHints,
}: {
  columns?: GridColumn<Row>[];
  layout?: boolean;
  onSort?: (column: string, focused: boolean, extend: boolean) => void;
  keyHints?: typeof GRID_KEY_HINTS;
} = {}) {
  const columnLayout: GridColumnLayoutProps = {
    onResize: vi.fn(),
    onMove: vi.fn(),
    onPin: vi.fn(),
    onReset: vi.fn(),
  };
  render(
    <GridCore<Row>
      label="Rows"
      columns={columns}
      rowCount={rows.length}
      getRow={(index) => rows[index]}
      rowHeight={32}
      headerHeight={32}
      {...(layout ? { columnLayout } : {})}
      {...(onSort ? { onSort } : {})}
      {...(keyHints ? { keyHints } : {})}
    />,
  );
  const grid = screen.getByRole('grid', { name: 'Rows' });
  act(() => grid.focus());
  return { grid, columnLayout };
}

const cursorCell = (grid: HTMLElement) => document.getElementById(grid.getAttribute('aria-activedescendant') ?? '');

describe('laying the columns out from the keys', () => {
  it('reaches the header of a grid that doesn’t sort, where Shift+arrows resize the column', () => {
    const { grid, columnLayout } = setup();
    fireEvent.keyDown(grid, { key: 'ArrowUp' });
    expect(cursorCell(grid)).toHaveAttribute('role', 'columnheader');
    fireEvent.keyDown(grid, { key: 'ArrowRight', shiftKey: true });
    expect(columnLayout.onResize).toHaveBeenLastCalledWith('a', 216);
    fireEvent.keyDown(grid, { key: 'ArrowLeft', shiftKey: true });
    expect(columnLayout.onResize).toHaveBeenLastCalledWith('a', 184);
  });

  it('moves a column with Ctrl+Shift+arrows, the cursor going with it', () => {
    const { grid, columnLayout } = setup();
    fireEvent.keyDown(grid, { key: 'ArrowUp' });
    fireEvent.keyDown(grid, { key: 'ArrowRight', shiftKey: true, ctrlKey: true });
    expect(columnLayout.onMove).toHaveBeenCalledWith('a', 1);
    expect(cursorCell(grid)).toHaveAttribute('aria-colindex', '2');
    // The first column can't go further left.
    fireEvent.keyDown(grid, { key: 'ArrowLeft' });
    fireEvent.keyDown(grid, { key: 'ArrowLeft' });
    fireEvent.keyDown(grid, { key: 'ArrowLeft', shiftKey: true, ctrlKey: true });
    expect(columnLayout.onMove).toHaveBeenCalledTimes(1);
  });

  it('opens the column’s menu with Shift+F10 on its header, which pins it', async () => {
    const user = userEvent.setup();
    const { grid, columnLayout } = setup();
    fireEvent.keyDown(grid, { key: 'ArrowUp' });
    fireEvent.keyDown(grid, { key: 'ArrowRight' });
    fireEvent.keyDown(grid, { key: 'F10', shiftKey: true });
    const menu = await screen.findByRole('menu', { name: 'Menu for the B column' });
    expect(within(menu).getByRole('menuitem', { name: /Move left/ })).toBeEnabled();
    await user.click(within(menu).getByRole('menuitem', { name: 'Pin the column to the left' }));
    expect(columnLayout.onPin).toHaveBeenCalledWith('b', true);
  });

  it('leaves the header keys alone in a grid whose columns are fixed', () => {
    const onSort = vi.fn();
    const { grid } = setup({ layout: false, columns: [make('a', 200, { sortable: true })], onSort });
    fireEvent.keyDown(grid, { key: 'ArrowUp' });
    fireEvent.keyDown(grid, { key: 'F10', shiftKey: true });
    expect(screen.queryByRole('menu')).toBeNull();
    fireEvent.keyDown(grid, { key: 'Enter' });
    expect(onSort).toHaveBeenCalledWith('a', true, false);
  });
});

describe('laying the columns out with the pointer', () => {
  it('resizes a column by its right edge, and a double-click puts its width back', () => {
    const { grid, columnLayout } = setup();
    const edge = grid.querySelectorAll('.si-grid-resize')[0]!;
    fireEvent.pointerDown(edge, { button: 0, clientX: 200, pointerId: 1 });
    fireEvent.pointerMove(edge, { clientX: 260, pointerId: 1 });
    // While it's dragged the column follows the pointer.
    expect(grid.style.getPropertyValue('--grid-template')).toBe('260px 100px 100px');
    fireEvent.pointerUp(edge, { clientX: 260, pointerId: 1 });
    expect(columnLayout.onResize).toHaveBeenCalledWith('a', 260);
    fireEvent.doubleClick(edge);
    expect(columnLayout.onResize).toHaveBeenLastCalledWith('a', null);
  });

  it('moves a column dragged by its header, and the drag isn’t a press that sorts', () => {
    const onSort = vi.fn();
    const { grid, columnLayout } = setup({
      columns: [make('a', 200, { sortable: true }), make('b', 100, { sortable: true }), make('c', 100)],
      onSort,
    });
    const header = grid.querySelectorAll('[role="columnheader"]')[0]!;
    fireEvent.pointerDown(header, { button: 0, clientX: 20, pointerId: 2 });
    fireEvent.pointerMove(header, { clientX: 270, pointerId: 2 });
    expect(grid.querySelector('.si-grid-drop')).not.toBeNull();
    fireEvent.pointerUp(header, { clientX: 270, pointerId: 2 });
    fireEvent.click(within(header as HTMLElement).getByRole('button'));
    expect(columnLayout.onMove).toHaveBeenCalledWith('a', 1);
    expect(onSort).not.toHaveBeenCalled();
    expect(grid.querySelector('.si-grid-drop')).toBeNull();
  });

  it('opens the column’s menu on a right-click on its header', async () => {
    const { grid } = setup();
    fireEvent.contextMenu(grid.querySelectorAll('[role="columnheader"]')[2]!, { clientX: 300, clientY: 20 });
    expect(await screen.findByRole('menu', { name: 'Menu for the C column' })).toBeInTheDocument();
    expect(screen.getByRole('menuitem', { name: /Move right/ })).toHaveAttribute('aria-disabled', 'true');
  });
});

describe('pinned columns', () => {
  it('stick to the left edge, the last drawing the edge', () => {
    const { grid } = setup({
      columns: [make('a', 200, { pinned: true }), make('b', 100, { pinned: true }), make('c', 100)],
    });
    const cells = [...grid.querySelectorAll<HTMLElement>('[data-row="0"]')];
    expect(cells.map((cell) => cell.style.left)).toEqual(['0px', '200px', '']);
    expect(cells[1]).toHaveClass('si-grid-cell--pin-edge');
    expect(cells[2]).not.toHaveClass('si-grid-cell--pinned');
  });
});

describe('the footer', () => {
  it('shows the key hints and where the cursor is', () => {
    const { grid } = setup({ keyHints: GRID_KEY_HINTS });
    const foot = grid.parentElement?.querySelector('.si-grid-foot');
    expect(foot).toHaveTextContent('Copy for Excel');
    expect(foot).toHaveTextContent('Row 1 of 10');
    fireEvent.keyDown(grid, { key: 'ArrowDown' });
    fireEvent.keyDown(grid, { key: 'ArrowDown', shiftKey: true });
    expect(foot).toHaveTextContent('2 rows selected');
  });

  it('isn’t there in a grid that doesn’t ask for it', () => {
    const { grid } = setup();
    expect(grid.parentElement?.querySelector('.si-grid-foot')).toBeNull();
  });
});
