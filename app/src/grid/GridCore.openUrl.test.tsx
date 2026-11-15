// Shift+Enter opens the URL of the row under the cursor in the browser, as the row's open-in-browser button does: the
// row's own `url`, or the one `openUrlOf` gives for the column the cursor is in (a link's two ends). Enter still
// activates the row, and a row without a web URL opens nothing.
import { act, fireEvent, render, screen } from '@testing-library/react';
import type * as Os from '../shell/os';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { GridCore } from './GridCore';
import type { GridColumn } from './types';

const opened = vi.hoisted(() => [] as string[]);
vi.mock('../shell/os', async (original) => ({
  ...(await original<typeof Os>()),
  openInBrowser: (url: string) => {
    opened.push(url);
    return Promise.resolve(true);
  },
}));
vi.mock('../feedback/announce', () => ({ announce: vi.fn() }));

afterEach(() => opened.splice(0));

interface Row {
  id: number;
  url: string;
  target: string;
}

const rows: Row[] = [
  { id: 0, url: 'https://example.com/a/', target: 'https://example.com/old/' },
  { id: 1, url: 'mailto:someone@example.com', target: 'https://example.com/x/' },
];
const columns: GridColumn<Row>[] = [
  { id: 'from.url', header: 'Source', width: 200, cell: (row) => row.url, text: (row) => row.url },
  { id: 'to.url', header: 'Target', width: 200, cell: (row) => row.target, text: (row) => row.target },
];

function renderGrid(openUrlOf?: (row: Row, column: string | undefined) => string | null) {
  const onActivate = vi.fn();
  render(
    <GridCore<Row>
      label="Rows"
      columns={columns}
      rowCount={rows.length}
      getRow={(index) => rows[index]}
      rowHeight={32}
      headerHeight={32}
      onActivate={onActivate}
      openUrlOf={openUrlOf}
    />,
  );
  const grid = screen.getByRole('grid', { name: 'Rows' });
  act(() => grid.focus());
  return { grid, onActivate };
}

describe('Shift+Enter in a grid', () => {
  it('opens the full URL of the row under the cursor, and Enter still activates it', () => {
    // Focus puts the cursor on the first row.
    const { grid, onActivate } = renderGrid();
    fireEvent.keyDown(grid, { key: 'Enter', shiftKey: true });
    expect(opened).toEqual(['https://example.com/a/']);
    expect(onActivate).not.toHaveBeenCalled();
    fireEvent.keyDown(grid, { key: 'Enter' });
    expect(onActivate).toHaveBeenCalledWith(0, rows[0], 'from.url');
  });

  it('opens the end the cursor is on, and nothing for a URL a browser can’t open', () => {
    const { grid } = renderGrid((row, column) => (column === 'to.url' ? row.target : row.url));
    fireEvent.keyDown(grid, { key: 'ArrowRight' });
    fireEvent.keyDown(grid, { key: 'Enter', shiftKey: true });
    fireEvent.keyDown(grid, { key: 'ArrowDown' });
    fireEvent.keyDown(grid, { key: 'ArrowLeft' });
    fireEvent.keyDown(grid, { key: 'Enter', shiftKey: true });
    expect(opened).toEqual(['https://example.com/old/']);
  });
});
