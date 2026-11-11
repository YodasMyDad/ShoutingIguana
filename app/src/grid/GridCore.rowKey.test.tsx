// A grid whose rows move under the cursor (`rowKey`): the cursor stays on its row, by the row's identity, when the
// rows change around it. What the list of robots.txt origins and the tree of sitemaps rest on while a crawl runs.
import { act, fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { GridCore, type GridCoreProps } from './GridCore';
import type { GridColumn } from './types';

interface Row {
  id: string;
}

const columns: GridColumn<Row>[] = [
  { id: 'id', header: 'Name', width: 200, cell: (row) => row.id, text: (row) => row.id },
];

const rowsOf = (...ids: string[]): Row[] => ids.map((id) => ({ id }));

function grid(rows: Row[], props: Partial<GridCoreProps<Row>> = {}) {
  return (
    <GridCore<Row>
      label="Names"
      columns={columns}
      rowCount={rows.length}
      getRow={(index) => rows[index]}
      rowHeight={32}
      headerHeight={32}
      rowKey={(row) => row.id}
      {...props}
    />
  );
}

/** The row the cursor is on, as assistive technology is told it: by the grid's active descendant. */
const cursorRow = () => {
  const element = screen.getByRole('grid', { name: 'Names' });
  return document.getElementById(element.getAttribute('aria-activedescendant') ?? '')?.closest('[role="row"]') ?? null;
};
const cursorText = () => cursorRow()?.textContent ?? null;

function withCursorOn(text: string, rows: Row[], props: Partial<GridCoreProps<Row>> = {}) {
  const view = render(grid(rows, props));
  const element = screen.getByRole('grid', { name: 'Names' });
  act(() => element.focus());
  for (let moves = 0; cursorText() !== text && moves < rows.length; moves++) {
    fireEvent.keyDown(element, { key: 'ArrowDown' });
  }
  expect(cursorText()).toBe(text);
  return { ...view, element };
}

describe('a grid whose rows have an identity', () => {
  it('keeps the cursor on its row when a row arrives above it, and Enter opens that row', () => {
    const onActivate = vi.fn();
    const onActiveRowChange = vi.fn();
    const { rerender, element } = withCursorOn('zebra', rowsOf('site', 'zebra'), { onActivate, onActiveRowChange });
    onActiveRowChange.mockClear();
    // The crawl asks another origin, which sorts ahead of the cursor's.
    const grown = rowsOf('site', 'apple', 'zebra');
    rerender(grid(grown, { onActivate, onActiveRowChange }));
    expect(cursorText()).toBe('zebra');
    expect(cursorRow()).toHaveAttribute('aria-rowindex', '4');
    // The cursor's row is the same row: no move is told.
    expect(onActiveRowChange).not.toHaveBeenCalled();
    fireEvent.keyDown(element, { key: 'Enter' });
    expect(onActivate).toHaveBeenCalledWith(2, { id: 'zebra' });
    // And it moves on from where its row is now.
    fireEvent.keyDown(element, { key: 'ArrowUp' });
    expect(cursorText()).toBe('apple');
  });

  it('follows its row up as well as down, however many rows come and go', () => {
    const { rerender } = withCursorOn('m', rowsOf('a', 'b', 'c', 'm', 'x'));
    rerender(grid(rowsOf('m', 'x')));
    expect(cursorText()).toBe('m');
    rerender(grid(rowsOf('0', '1', '2', '3', '4', '5', 'a', 'b', 'm', 'x', 'y')));
    expect(cursorText()).toBe('m');
    expect(cursorRow()).toHaveAttribute('aria-rowindex', '10');
  });

  it('takes a selection that follows the cursor along with it', () => {
    const { rerender } = render(grid(rowsOf('site', 'zebra')));
    // A plain click selects the row it lands on.
    fireEvent.pointerDown(screen.getByText('zebra'), { button: 0 });
    expect(screen.getByText('zebra').closest('[role="row"]')).toHaveAttribute('aria-selected', 'true');
    rerender(grid(rowsOf('site', 'apple', 'zebra')));
    expect(screen.getByText('zebra').closest('[role="row"]')).toHaveAttribute('aria-selected', 'true');
    expect(screen.getByText('apple').closest('[role="row"]')).toHaveAttribute('aria-selected', 'false');
  });

  it('leaves the cursor at its number when its row is gone', () => {
    const { rerender } = withCursorOn('b', rowsOf('a', 'b', 'c'));
    rerender(grid(rowsOf('a', 'c', 'd')));
    expect(cursorText()).toBe('c');
    // From then on it's on that row.
    rerender(grid(rowsOf('z', 'a', 'c', 'd')));
    expect(cursorText()).toBe('c');
  });

  it('is a row number without a key: the row under the cursor is whichever is there now', () => {
    const { rerender } = withCursorOn('zebra', rowsOf('site', 'zebra'), { rowKey: undefined });
    rerender(grid(rowsOf('site', 'apple', 'zebra'), { rowKey: undefined }));
    expect(cursorText()).toBe('apple');
  });

  it('starts on the row it was left on by its key, wherever that row is by then', () => {
    // Left on "zebra" when it was the second row; a row has come in above it since.
    render(grid(rowsOf('site', 'apple', 'zebra'), { startRow: 1, startKey: 'zebra' }));
    const element = screen.getByRole('grid', { name: 'Names' });
    act(() => element.focus());
    expect(cursorText()).toBe('zebra');
  });

  it('starts on the row number it was left on when the key isn’t among the rows', () => {
    render(grid(rowsOf('site', 'apple', 'zebra'), { startRow: 1, startKey: 'gone' }));
    act(() => screen.getByRole('grid', { name: 'Names' }).focus());
    expect(cursorText()).toBe('apple');
  });
});
