// Click, Shift+click, Ctrl+C copies the rows (the UX audit of 2026-11-19, D-06): the browser extended a text
// selection over the rows on the Shift+click, and Ctrl+C then copied that text, mid-word and without tabs.
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { GridCore } from './GridCore';
import type { GridColumn } from './types';

vi.mock('../feedback/notify', () => ({ notifyError: vi.fn(), notifyResult: vi.fn() }));
vi.mock('../feedback/announce', () => ({ announce: vi.fn() }));

interface Row {
  url: string;
}
const rows: Row[] = Array.from({ length: 20 }, (_, id) => ({ url: `https://example.com/${id}` }));
const columns: GridColumn<Row>[] = [
  { id: 'url', header: 'URL', width: 300, cell: (row) => row.url, text: (row) => row.url },
];

function setup() {
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
    />,
  );
  const grid = screen.getByRole('grid', { name: 'URLs' });
  const cell = (row: number) => grid.querySelector<HTMLElement>(`[data-row="${row}"][data-col="0"]`)!;
  return { grid, cell, writeText };
}

/** Text selected on the page, as the browser leaves it after a Shift+click across rows. */
function selectTextOf(element: HTMLElement) {
  const range = document.createRange();
  range.selectNodeContents(element);
  const selection = window.getSelection()!;
  selection.removeAllRanges();
  selection.addRange(range);
}

afterEach(() => window.getSelection()?.removeAllRanges());

describe('copying rows chosen with the mouse', () => {
  it('stops the browser selecting text on a Shift+click or a Ctrl+click, and clears what it had', () => {
    const { grid, cell } = setup();
    fireEvent.pointerDown(cell(2), { button: 0 });
    selectTextOf(cell(2));
    const shift = fireEvent.mouseDown(cell(4), { button: 0, shiftKey: true });
    // Prevented: the browser doesn't extend a text selection to the row clicked.
    expect(shift).toBe(false);
    expect(window.getSelection()?.toString()).toBe('');
    // The grid still has the keyboard, which the prevented press would have given it.
    expect(document.activeElement).toBe(grid);
    expect(fireEvent.mouseDown(cell(3), { button: 0, ctrlKey: true })).toBe(false);
    // A plain press is left alone: text in a cell can still be selected by dragging.
    expect(fireEvent.mouseDown(cell(1), { button: 0 })).toBe(true);
  });

  it('copies the rows on Ctrl+C when several are selected, whatever text the page has selected', async () => {
    const { grid, cell, writeText } = setup();
    act(() => grid.focus());
    fireEvent.pointerDown(cell(2), { button: 0 });
    fireEvent.pointerDown(cell(4), { button: 0, shiftKey: true });
    selectTextOf(cell(3));
    fireEvent.keyDown(grid, { key: 'c', ctrlKey: true });
    await waitFor(() => expect(writeText).toHaveBeenCalledOnce());
    expect(writeText).toHaveBeenCalledWith(
      'URL\r\nhttps://example.com/2\r\nhttps://example.com/3\r\nhttps://example.com/4',
    );
  });

  it('leaves Ctrl+C to the browser for text dragged over in a row, with one row or none selected', () => {
    const { grid, cell, writeText } = setup();
    act(() => grid.focus());
    fireEvent.pointerDown(cell(2), { button: 0 });
    selectTextOf(cell(2));
    fireEvent.keyDown(grid, { key: 'c', ctrlKey: true });
    expect(writeText).not.toHaveBeenCalled();
  });
});
