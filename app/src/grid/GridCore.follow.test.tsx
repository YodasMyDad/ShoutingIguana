// The grid's highlight follows what the panel beside it shows (the UX audit of 2026-11-19, D-08): after a hop inside
// the inspector the grid no longer marks the URL that was left.
import { act, fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { GridCore } from './GridCore';
import type { GridColumn } from './types';

vi.mock('../feedback/notify', () => ({ notifyError: vi.fn(), notifyResult: vi.fn() }));
vi.mock('../feedback/announce', () => ({ announce: vi.fn() }));

interface Row {
  id: number;
}
const rows: Row[] = Array.from({ length: 8 }, (_, index) => ({ id: 100 + index }));
const columns: GridColumn<Row>[] = [
  { id: 'id', header: 'Id', width: 100, cell: (row) => row.id, text: (row) => String(row.id) },
];

function Grid({ followKey, onMove }: { followKey?: number | undefined; onMove: (row: number | null) => void }) {
  return (
    <GridCore<Row>
      label="URLs"
      columns={columns}
      rowCount={rows.length}
      getRow={(index) => rows[index]}
      rowHeight={32}
      headerHeight={32}
      rowKey={(row) => row.id}
      followKey={followKey}
      onActiveRowChange={onMove}
    />
  );
}

const selectedRows = () =>
  [...document.querySelectorAll('[role="row"][data-selected]')].map(
    (row) => Number(row.getAttribute('aria-rowindex')) - 2,
  );
const frame = () => screen.getByRole('grid', { name: 'URLs' }).parentElement!;

describe('a grid beside a panel that shows one of its rows', () => {
  it('moves its highlight to the row the panel walks to, without telling a move of the cursor', () => {
    const onMove = vi.fn();
    const { rerender } = render(<Grid onMove={onMove} />);
    const grid = screen.getByRole('grid', { name: 'URLs' });
    fireEvent.pointerDown(grid.querySelector('[data-row="1"][data-col="0"]')!, { button: 0 });
    expect(selectedRows()).toEqual([1]);
    onMove.mockClear();
    // The panel opens on that row, then walks to the row with id 105.
    rerender(<Grid onMove={onMove} followKey={101} />);
    expect(selectedRows()).toEqual([1]);
    rerender(<Grid onMove={onMove} followKey={105} />);
    expect(selectedRows()).toEqual([5]);
    expect(frame()).not.toHaveAttribute('data-cursor-elsewhere');
    // The walk moved the highlight; the cursor's own move is the user's to make, and the panel isn't told of one.
    expect(onMove).not.toHaveBeenCalled();
  });

  it('takes the tint off the row left when the panel shows a URL that isn’t among the rows', () => {
    const { rerender } = render(<Grid onMove={vi.fn()} followKey={101} />);
    const grid = screen.getByRole('grid', { name: 'URLs' });
    fireEvent.pointerDown(grid.querySelector('[data-row="1"][data-col="0"]')!, { button: 0 });
    expect(frame()).not.toHaveAttribute('data-cursor-elsewhere');
    rerender(<Grid onMove={vi.fn()} followKey={999} />);
    expect(selectedRows()).toEqual([1]);
    expect(frame()).toHaveAttribute('data-cursor-elsewhere');
    // The user moves the cursor: the panel will follow it, and the mark is the cursor's again once it has.
    act(() => grid.focus());
    fireEvent.keyDown(grid, { key: 'ArrowDown' });
    rerender(<Grid onMove={vi.fn()} followKey={102} />);
    expect(selectedRows()).toEqual([2]);
    expect(frame()).not.toHaveAttribute('data-cursor-elsewhere');
  });
});
