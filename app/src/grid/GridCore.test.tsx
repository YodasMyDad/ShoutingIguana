import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { GridCore } from './GridCore';
import type { GridColumn } from './types';

interface Row {
  id: number;
  url: string;
  status: number;
}

const rows: Row[] = Array.from({ length: 100 }, (_, id) => ({
  id,
  url: `https://example.com/${id}`,
  status: id % 10 === 0 ? 404 : 200,
}));

const columns: GridColumn<Row>[] = [
  { id: 'url', header: 'URL', width: 300, mono: true, cell: (row) => row.url, text: (row) => row.url },
  {
    id: 'status',
    header: 'Status',
    width: 80,
    numeric: true,
    cell: (row) => row.status,
    text: (row) => String(row.status),
  },
];

function setup(props: Partial<Parameters<typeof GridCore<Row>>[0]> = {}) {
  const onActivate = vi.fn();
  const onCopy = vi.fn();
  const writeText = vi.fn().mockResolvedValue(undefined);
  Object.defineProperty(navigator, 'clipboard', { configurable: true, value: { writeText } });
  render(
    <GridCore<Row>
      label="URLs"
      columns={columns}
      rowCount={rows.length}
      getRow={(index) => rows[index]}
      rowHeight={32}
      headerHeight={32}
      onActivate={onActivate}
      onCopy={onCopy}
      {...props}
    />,
  );
  const grid = screen.getByRole('grid', { name: 'URLs' });
  const activeCell = () => {
    const id = grid.getAttribute('aria-activedescendant');
    return id ? document.getElementById(id) : null;
  };
  return { grid, activeCell, onActivate, onCopy, writeText };
}

describe('GridCore', () => {
  it('follows the ARIA grid pattern: counts include the header row', () => {
    const { grid } = setup();
    expect(grid).toHaveAttribute('aria-rowcount', '101');
    expect(grid).toHaveAttribute('aria-colcount', '2');
    expect(grid).toHaveAttribute('aria-multiselectable', 'true');
    expect(screen.getAllByRole('columnheader').map((header) => header.textContent)).toEqual(['URL', 'Status']);
    expect(screen.getAllByRole('row')[1]).toHaveAttribute('aria-rowindex', '2');
  });

  it('puts the cursor on the first cell when focused, and moves it with the keys', () => {
    const { grid, activeCell } = setup();
    act(() => grid.focus());
    expect(activeCell()).toHaveTextContent('https://example.com/0');
    fireEvent.keyDown(grid, { key: 'ArrowDown' });
    fireEvent.keyDown(grid, { key: 'ArrowRight' });
    expect(activeCell()).toHaveTextContent('200');
    expect(activeCell()).toHaveAttribute('aria-colindex', '2');
  });

  it('inspects the row under the cursor on Enter', () => {
    const { grid, onActivate } = setup();
    act(() => grid.focus());
    fireEvent.keyDown(grid, { key: 'ArrowDown' });
    fireEvent.keyDown(grid, { key: 'Enter' });
    expect(onActivate).toHaveBeenCalledWith(1, rows[1]);
  });

  it('selects rows with Space and Shift, and marks them selected', () => {
    const { grid } = setup();
    act(() => grid.focus());
    fireEvent.keyDown(grid, { key: ' ' });
    fireEvent.keyDown(grid, { key: 'ArrowDown', shiftKey: true });
    const selected = screen.getAllByRole('row').filter((row) => row.getAttribute('aria-selected') === 'true');
    expect(selected.map((row) => row.getAttribute('aria-rowindex'))).toEqual(['2', '3']);
  });

  it('copies the selection as tab-separated text with its header', async () => {
    const { grid, writeText, onCopy } = setup();
    act(() => grid.focus());
    fireEvent.keyDown(grid, { key: ' ' });
    fireEvent.keyDown(grid, { key: 'ArrowDown', shiftKey: true });
    fireEvent.keyDown(grid, { key: 'c', ctrlKey: true });
    await waitFor(() => expect(writeText).toHaveBeenCalledOnce());
    expect(writeText).toHaveBeenCalledWith('URL\tStatus\r\nhttps://example.com/0\t404\r\nhttps://example.com/1\t200');
    expect(onCopy).toHaveBeenCalledWith({ rows: 2, truncated: false, cell: false });
  });

  it('copies just the cell under the cursor when nothing is selected', async () => {
    const { grid, writeText } = setup();
    act(() => grid.focus());
    fireEvent.keyDown(grid, { key: 'ArrowRight' });
    fireEvent.keyDown(grid, { key: 'c', ctrlKey: true });
    await waitFor(() => expect(writeText).toHaveBeenCalledWith('404'));
  });

  it('fetches rows that are not loaded before copying them', async () => {
    const fetchRows = vi.fn((indices: readonly number[]) => Promise.resolve(indices.map((index) => rows[index])));
    const { grid, writeText } = setup({ getRow: (index) => (index < 2 ? rows[index] : undefined), fetchRows });
    act(() => grid.focus());
    fireEvent.keyDown(grid, { key: 'a', ctrlKey: true });
    fireEvent.keyDown(grid, { key: 'c', ctrlKey: true });
    await waitFor(() => expect(writeText).toHaveBeenCalledOnce());
    expect(fetchRows).toHaveBeenCalledWith(Array.from({ length: 98 }, (_, index) => index + 2));
    expect(String(writeText.mock.calls[0]?.[0]).split('\r\n')).toHaveLength(101);
  });

  it('shows placeholder rows for rows whose page has not arrived', () => {
    setup({ getRow: () => undefined });
    const busyRows = screen.getAllByRole('row').filter((row) => row.getAttribute('aria-busy') === 'true');
    expect(busyRows.length).toBeGreaterThan(0);
  });

  it('says why when there are no rows', () => {
    setup({ rowCount: 0, emptyState: <p>No URLs match these filters.</p> });
    expect(screen.getByText('No URLs match these filters.')).toBeInTheDocument();
  });

  it('is busy, with no row count, until the first page arrives', () => {
    const { grid } = setup({ rowCount: null });
    expect(grid).toHaveAttribute('aria-busy', 'true');
    expect(grid).toHaveAttribute('aria-rowcount', '-1');
  });
});
