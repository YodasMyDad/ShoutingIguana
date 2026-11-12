// A grid whose cells lead to different places (`onActivateCell`), and a tree whose groups open as rows do
// (`tree.activates`): what the Sitemaps list is built on.
import { act, fireEvent, render, screen } from '@testing-library/react';
import { useState } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { GridCore } from './GridCore';
import type { GridColumn, GridTree } from './types';

interface Row {
  id: number;
  name: string;
  count: number;
  parent: number | null;
}

/** An index (0) with two rows under it, then a row on its own. */
const ROWS: Row[] = [
  { id: 0, name: 'index', count: 5, parent: null },
  { id: 1, name: 'first', count: 3, parent: 0 },
  { id: 2, name: 'second', count: 2, parent: 0 },
  { id: 3, name: 'alone', count: 1, parent: null },
];

const columns: GridColumn<Row>[] = [
  { id: 'name', header: 'Name', width: 200, cell: (row) => row.name, text: (row) => row.name },
  {
    id: 'count',
    header: 'Count',
    width: 80,
    numeric: true,
    cell: (row) => row.count,
    text: (row) => String(row.count),
  },
];

function Tree({
  activates,
  onActivate,
  onActivateCell,
}: {
  activates?: boolean;
  onActivate?: (index: number, row: Row | undefined) => void;
  onActivateCell?: (index: number, row: Row | undefined, column: string) => void;
}) {
  const [open, setOpen] = useState(true);
  const rows = open ? ROWS : ROWS.filter((row) => row.parent === null);
  const tree: GridTree = {
    level: (index) => (rows[index]?.parent === null ? 1 : 2),
    expanded: (index) => (rows[index]?.id === 0 ? open : undefined),
    parent: (index) => (rows[index]?.parent === null ? null : 0),
    onToggle: (_index, next) => {
      setOpen(next);
      return undefined;
    },
    ...(activates === undefined ? {} : { activates }),
  };
  return (
    <GridCore<Row>
      label="Tree"
      columns={columns}
      rowCount={rows.length}
      getRow={(index) => rows[index]}
      rowHeight={32}
      headerHeight={32}
      tree={tree}
      {...(onActivate ? { onActivate } : {})}
      {...(onActivateCell ? { onActivateCell } : {})}
    />
  );
}

const cellOf = (text: string) => screen.getByText(text).closest<HTMLElement>('[data-row][data-col]')!;

describe('a grid whose cells lead to different places', () => {
  it('says which column the cursor is on when Enter opens a row', () => {
    const onActivateCell = vi.fn();
    render(
      <GridCore<Row>
        label="Rows"
        columns={columns}
        rowCount={ROWS.length}
        getRow={(index) => ROWS[index]}
        rowHeight={32}
        headerHeight={32}
        onActivateCell={onActivateCell}
      />,
    );
    const grid = screen.getByRole('grid', { name: 'Rows' });
    act(() => grid.focus());
    fireEvent.keyDown(grid, { key: 'Enter' });
    expect(onActivateCell).toHaveBeenLastCalledWith(0, ROWS[0], 'name');
    fireEvent.keyDown(grid, { key: 'ArrowDown' });
    fireEvent.keyDown(grid, { key: 'ArrowRight' });
    fireEvent.keyDown(grid, { key: 'Enter' });
    expect(onActivateCell).toHaveBeenLastCalledWith(1, ROWS[1], 'count');
  });

  it('says which column a double-click was on', () => {
    const onActivateCell = vi.fn();
    render(
      <GridCore<Row>
        label="Rows"
        columns={columns}
        rowCount={ROWS.length}
        getRow={(index) => ROWS[index]}
        rowHeight={32}
        headerHeight={32}
        onActivateCell={onActivateCell}
      />,
    );
    fireEvent.doubleClick(cellOf('2'));
    expect(onActivateCell).toHaveBeenCalledExactlyOnceWith(2, ROWS[2], 'count');
  });

  it('is called in place of onActivate, which a grid without it still gets as before', () => {
    const onActivate = vi.fn();
    const onActivateCell = vi.fn();
    const props = {
      label: 'Rows',
      columns,
      rowCount: ROWS.length,
      getRow: (index: number) => ROWS[index],
      rowHeight: 32,
      headerHeight: 32,
    };
    const view = render(<GridCore<Row> {...props} onActivate={onActivate} onActivateCell={onActivateCell} />);
    const grid = screen.getByRole('grid', { name: 'Rows' });
    act(() => grid.focus());
    fireEvent.keyDown(grid, { key: 'Enter' });
    expect(onActivateCell).toHaveBeenCalledOnce();
    expect(onActivate).not.toHaveBeenCalled();
    view.rerender(<GridCore<Row> {...props} onActivate={onActivate} />);
    fireEvent.keyDown(grid, { key: 'Enter' });
    // With the column the cursor is in, as every grid's onActivate is told (the Links slice's third argument).
    expect(onActivate).toHaveBeenCalledExactlyOnceWith(0, ROWS[0], 'name');
  });
});

describe('a tree whose groups are rows of their own', () => {
  it('opens a group with Enter as it opens any row, and leaves it as open or closed as it was', () => {
    const onActivateCell = vi.fn();
    render(<Tree activates onActivateCell={onActivateCell} />);
    const grid = screen.getByRole('treegrid', { name: 'Tree' });
    act(() => grid.focus());
    fireEvent.keyDown(grid, { key: 'Enter' });
    expect(onActivateCell).toHaveBeenCalledExactlyOnceWith(0, ROWS[0], 'name');
    expect(screen.getByText('first')).toBeInTheDocument();
    expect(screen.getAllByRole('row')[1]).toHaveAttribute('aria-expanded', 'true');
  });

  it('still closes and opens the group with Left and Right on its first cell', () => {
    render(<Tree activates onActivateCell={vi.fn()} />);
    const grid = screen.getByRole('treegrid', { name: 'Tree' });
    act(() => grid.focus());
    fireEvent.keyDown(grid, { key: 'ArrowLeft' });
    expect(screen.queryByText('first')).toBeNull();
    expect(screen.getAllByRole('row')[1]).toHaveAttribute('aria-expanded', 'false');
    fireEvent.keyDown(grid, { key: 'ArrowRight' });
    expect(screen.getByText('first')).toBeInTheDocument();
  });

  it('opens a group on a double-click, by the cell it was on', () => {
    const onActivateCell = vi.fn();
    render(<Tree activates onActivateCell={onActivateCell} />);
    fireEvent.doubleClick(cellOf('5'));
    expect(onActivateCell).toHaveBeenCalledExactlyOnceWith(0, ROWS[0], 'count');
    expect(screen.getByText('first')).toBeInTheDocument();
  });

  it('goes up from a row to its group with Left, as every tree does', () => {
    render(<Tree activates onActivateCell={vi.fn()} />);
    const grid = screen.getByRole('treegrid', { name: 'Tree' });
    act(() => grid.focus());
    fireEvent.keyDown(grid, { key: 'ArrowDown' });
    fireEvent.keyDown(grid, { key: 'ArrowLeft' });
    const active = document.getElementById(grid.getAttribute('aria-activedescendant') ?? '');
    expect(active).toHaveTextContent('index');
  });

  it('without `activates`, Enter and a double-click on a group open and close it, as before', () => {
    const onActivate = vi.fn();
    render(<Tree onActivate={onActivate} />);
    const grid = screen.getByRole('treegrid', { name: 'Tree' });
    act(() => grid.focus());
    fireEvent.keyDown(grid, { key: 'Enter' });
    expect(screen.queryByText('first')).toBeNull();
    expect(onActivate).not.toHaveBeenCalled();
    fireEvent.doubleClick(cellOf('index'));
    expect(screen.getByText('first')).toBeInTheDocument();
    expect(onActivate).not.toHaveBeenCalled();
  });
});
