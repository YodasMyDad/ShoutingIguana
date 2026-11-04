import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { useState } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { announce } from '../feedback/announce';
import { notifyError } from '../feedback/notify';
import { commands } from '../keyboard/registry';
import { GridCore } from './GridCore';
import type { RowWindowModel } from './rowWindow';
import type { GridColumn } from './types';

vi.mock('../feedback/notify', () => ({ notifyError: vi.fn(), notifyResult: vi.fn() }));
vi.mock('../feedback/announce', () => ({ announce: vi.fn() }));

afterEach(() => {
  vi.mocked(notifyError).mockClear();
  vi.mocked(announce).mockClear();
});

/** jsdom does no layout: give the grid's scroll element a size, and a scroll position that sticks. */
function layOut(grid: HTMLElement, size: { width: number; height: number }) {
  let top = 0;
  let left = 0;
  Object.defineProperty(grid, 'clientWidth', { configurable: true, get: () => size.width });
  Object.defineProperty(grid, 'clientHeight', { configurable: true, get: () => size.height });
  Object.defineProperty(grid, 'scrollTop', {
    configurable: true,
    get: () => top,
    set: (value: number) => (top = Math.max(0, value)),
  });
  Object.defineProperty(grid, 'scrollLeft', {
    configurable: true,
    get: () => left,
    set: (value: number) => (left = Math.max(0, value)),
  });
  act(() => {
    grid.dispatchEvent(new Event('scroll'));
  });
  return {
    scrollLeft: () => left,
    scrollTo: (value: number) =>
      act(() => {
        top = value;
        grid.dispatchEvent(new Event('scroll'));
      }),
  };
}

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

  it('leaves out rows that still can’t be read, and announces what it copied, not what was selected', async () => {
    const fetchRows = vi.fn((indices: readonly number[]) =>
      Promise.resolve(indices.map((index) => (index % 2 === 0 ? rows[index] : undefined))),
    );
    const { grid, writeText, onCopy } = setup({ getRow: (index) => (index < 2 ? rows[index] : undefined), fetchRows });
    act(() => grid.focus());
    fireEvent.keyDown(grid, { key: 'a', ctrlKey: true });
    fireEvent.keyDown(grid, { key: 'c', ctrlKey: true });
    await waitFor(() => expect(writeText).toHaveBeenCalledOnce());
    const lines = String(writeText.mock.calls[0]?.[0]).split('\r\n').slice(1);
    expect(lines).toHaveLength(51);
    expect(lines.filter((line) => line.replace(/\t/g, '') === '')).toEqual([]);
    expect(announce).toHaveBeenCalledWith('Copied 51 rows. 49 rows couldn’t be read.');
    expect(onCopy).toHaveBeenCalledWith({ rows: 51, truncated: false, cell: false });
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

  it('keeps the cursor in view sideways: the grid scrolls to its cell, just enough', () => {
    const wide: GridColumn<Row>[] = Array.from({ length: 6 }, (_, index) => ({
      id: `c${index}`,
      header: `Column ${index}`,
      width: 300,
      cell: (row) => row.url,
      text: (row) => row.url,
    }));
    const { grid, activeCell } = setup({ columns: wide });
    const view = layOut(grid, { width: 700, height: 32 * 11 });
    act(() => grid.focus());
    fireEvent.keyDown(grid, { key: 'End' });
    expect(activeCell()).toHaveAttribute('aria-colindex', '6');
    // Column 6 spans 1,500 to 1,800 px: its right edge meets the view's.
    expect(view.scrollLeft()).toBe(1_100);
    fireEvent.keyDown(grid, { key: 'ArrowLeft' });
    expect(view.scrollLeft()).toBe(1_100);
    fireEvent.keyDown(grid, { key: 'Home' });
    expect(view.scrollLeft()).toBe(0);
    fireEvent.keyDown(grid, { key: 'ArrowRight' });
    fireEvent.keyDown(grid, { key: 'ArrowRight' });
    fireEvent.keyDown(grid, { key: 'ArrowRight' });
    // Column 4 spans 900 to 1,200 px.
    expect(view.scrollLeft()).toBe(500);
  });

  it('puts the cursor on the first row wholly in view when focused, not one half under the header', () => {
    const { grid, activeCell } = setup();
    const view = layOut(grid, { width: 800, height: 32 * 11 });
    view.scrollTo(50); // row 1 is half under the sticky header
    act(() => grid.focus());
    expect(activeCell()).toHaveTextContent('https://example.com/2');
  });

  it('goes to the first and last cell with Ctrl+Home and Ctrl+End', () => {
    const { grid, activeCell } = setup();
    layOut(grid, { width: 800, height: 32 * 11 });
    act(() => grid.focus());
    fireEvent.keyDown(grid, { key: 'End', ctrlKey: true });
    expect(activeCell()).toHaveTextContent('200');
    expect(activeCell()?.closest('[role="row"]')).toHaveAttribute('aria-rowindex', '101');
    fireEvent.keyDown(grid, { key: 'Home', ctrlKey: true });
    expect(activeCell()).toHaveTextContent('https://example.com/0');
  });

  it('keeps the skeleton up for its minimum once shown, even when the rows come at once', () => {
    vi.useFakeTimers();
    try {
      const view = render(
        <GridCore<Row>
          label="URLs"
          columns={columns}
          rowCount={null}
          getRow={(index) => rows[index]}
          rowHeight={32}
          headerHeight={32}
        />,
      );
      const placeholders = () => document.querySelectorAll('[role="row"][aria-busy="true"]').length;
      act(() => {
        vi.advanceTimersByTime(160);
      });
      expect(placeholders()).toBe(12);
      view.rerender(
        <GridCore<Row>
          label="URLs"
          columns={columns}
          rowCount={rows.length}
          getRow={(index) => rows[index]}
          rowHeight={32}
          headerHeight={32}
        />,
      );
      act(() => {
        vi.advanceTimersByTime(10);
      });
      expect(placeholders()).toBe(12);
      expect(screen.getByRole('grid')).toHaveAttribute('aria-busy', 'true');
      act(() => {
        vi.advanceTimersByTime(300);
      });
      expect(placeholders()).toBe(0);
      expect(screen.getByRole('grid')).toHaveAttribute('aria-rowcount', '101');
    } finally {
      vi.useRealTimers();
    }
  });

  it('leaves Esc to the app while something around the grid takes it (the inspector closes first)', () => {
    const { grid } = setup();
    act(() => grid.focus());
    const firstCell = screen.getAllByRole('gridcell')[0];
    if (!firstCell) throw new Error('no cell');
    fireEvent.pointerDown(firstCell, { button: 0 });
    const selected = () => screen.getAllByRole('row').filter((row) => row.getAttribute('aria-selected') === 'true');
    expect(selected()).toHaveLength(1);
    const unregister = commands.register([
      { id: 'inspector.close', title: 'Close', group: 'View', keys: ['escape'], run: vi.fn() },
    ]);
    try {
      // Not taken by the grid: it goes on to the app's handler.
      expect(fireEvent.keyDown(grid, { key: 'Escape' })).toBe(true);
      expect(selected()).toHaveLength(1);
    } finally {
      unregister();
    }
    // Nothing else takes it: Esc clears the selection.
    expect(fireEvent.keyDown(grid, { key: 'Escape' })).toBe(false);
    expect(selected()).toHaveLength(0);
  });

  it('names a real cell while the cursor’s row waits for its page', () => {
    const { grid, activeCell } = setup({ getRow: (index) => (index === 1 ? undefined : rows[index]) });
    act(() => grid.focus());
    fireEvent.keyDown(grid, { key: 'ArrowDown' });
    expect(activeCell()).not.toBeNull();
    expect(activeCell()?.closest('[role="row"]')).toHaveAttribute('aria-busy', 'true');
    expect(activeCell()).toHaveAttribute('data-active');
  });

  it('keeps the cursor’s row in the DOM when it scrolls out of view, so the cursor always names a cell', () => {
    let model: RowWindowModel | undefined;
    const { grid, activeCell } = setup({ modelRef: (value) => (model = value) });
    layOut(grid, { width: 800, height: 32 * 6 });
    act(() => grid.focus());
    expect(activeCell()).toHaveTextContent('https://example.com/0');
    act(() => model?.scrollToOffset(80 * 32));
    const shown = screen.getAllByRole('row').filter((row) => !row.classList.contains('sr-only'));
    expect(shown.some((row) => row.getAttribute('aria-rowindex') === '2')).toBe(false);
    expect(activeCell()).toHaveTextContent('https://example.com/0');
  });

  it('copies one cell exactly as it is, and rows as text a spreadsheet won’t run', async () => {
    const formula: Row[] = [{ id: 0, url: '=HYPERLINK("x")', status: 200 }];
    const { grid, writeText } = setup({ rowCount: 1, getRow: (index) => formula[index] });
    act(() => grid.focus());
    fireEvent.keyDown(grid, { key: 'c', ctrlKey: true });
    await waitFor(() => expect(writeText).toHaveBeenCalledWith('=HYPERLINK("x")'));
    expect(announce).toHaveBeenCalledWith('Copied the cell.');
    fireEvent.keyDown(grid, { key: ' ' });
    fireEvent.keyDown(grid, { key: 'c', ctrlKey: true });
    await waitFor(() => expect(writeText).toHaveBeenLastCalledWith('URL\tStatus\r\n"\'=HYPERLINK(""x"")"\t200'));
    expect(announce).toHaveBeenLastCalledWith('Copied 1 row.');
  });

  it('says so when a copy fails because a page can’t be fetched', async () => {
    const fetchRows = vi.fn(() => Promise.reject(new Error('The page didn’t come.')));
    const { grid } = setup({ getRow: (index) => (index < 2 ? rows[index] : undefined), fetchRows });
    act(() => grid.focus());
    fireEvent.keyDown(grid, { key: 'a', ctrlKey: true });
    fireEvent.keyDown(grid, { key: 'c', ctrlKey: true });
    await waitFor(() =>
      expect(notifyError).toHaveBeenCalledWith(
        expect.any(Error),
        expect.objectContaining({ title: 'The rows couldn’t be copied.' }),
      ),
    );
    expect(announce).not.toHaveBeenCalled();
  });

  it('says so when the clipboard refuses', async () => {
    const { grid, writeText } = setup();
    writeText.mockRejectedValue(new Error('denied'));
    Object.defineProperty(document, 'execCommand', { configurable: true, value: () => false });
    act(() => grid.focus());
    fireEvent.keyDown(grid, { key: 'c', ctrlKey: true });
    await waitFor(() => expect(notifyError).toHaveBeenCalledOnce());
    expect(vi.mocked(notifyError).mock.calls[0]?.[1]).toMatchObject({
      title: 'The cell couldn’t be copied.',
      description: 'The clipboard didn’t take it. Try again.',
    });
    expect(announce).not.toHaveBeenCalled();
  });

  it('tells the data layer when the scrollbar’s thumb is held, and the range again when it’s let go', () => {
    let model: RowWindowModel | undefined;
    const onRangeChange = vi.fn();
    const { grid } = setup({ modelRef: (value) => (model = value), onRangeChange });
    layOut(grid, { width: 800, height: 32 * 11 });
    act(() => model?.setScrubbing(true));
    act(() => model?.scrollToOffset(40 * 32));
    expect(onRangeChange).toHaveBeenLastCalledWith(expect.objectContaining({ scrubbing: true }));
    act(() => model?.setScrubbing(false));
    expect(onRangeChange).toHaveBeenLastCalledWith(expect.objectContaining({ first: 34, scrubbing: false }));
  });
});

/** A tree of groups: group g has `size` rows, and one group at most is open (an accordion, as Issues has it). */
function TreeGrid({ onActivate, sort }: { onActivate?: (index: number) => void; sort?: (column: string) => void }) {
  const groups = [
    { name: 'Group A', size: 2 },
    { name: 'Group B', size: 3 },
    { name: 'Group C', size: 1 },
  ];
  const [open, setOpen] = useState<number | null>(null);
  type TreeRow = { kind: 'group'; group: number } | { kind: 'row'; group: number; row: number } | { kind: 'more' };
  const flat: TreeRow[] = groups.flatMap((group, index): TreeRow[] => [
    { kind: 'group', group: index },
    ...(open === index
      ? [
          ...Array.from({ length: group.size }, (_, row): TreeRow => ({ kind: 'row', group: index, row })),
          { kind: 'more' } as const,
        ]
      : []),
  ]);
  const treeColumns: GridColumn<TreeRow>[] = [
    {
      id: 'name',
      header: 'Name',
      width: 200,
      sortable: true,
      sort: 'ascending',
      cell: (row) =>
        row.kind === 'group' ? (
          <>
            <span data-tree-toggle>›</span>
            {groups[row.group]?.name}
          </>
        ) : null,
      text: (row) => (row.kind === 'group' ? (groups[row.group]?.name ?? '') : ''),
    },
    {
      id: 'size',
      header: 'Size',
      width: 80,
      sortable: true,
      cell: (row) => (row.kind === 'group' ? groups[row.group]?.size : null),
      text: () => '',
    },
  ];
  return (
    <GridCore<TreeRow>
      label="Groups"
      columns={treeColumns}
      rowCount={flat.length}
      getRow={(index) => flat[index]}
      rowHeight={32}
      headerHeight={32}
      onActivate={(index) => onActivate?.(index)}
      {...(sort ? { onSort: sort } : {})}
      spanRow={(row) =>
        row.kind === 'row'
          ? `${groups[row.group]?.name ?? ''} row ${row.row + 1}`
          : row.kind === 'more'
            ? 'Show all'
            : null
      }
      tree={{
        level: (index) => (flat[index]?.kind === 'group' ? 1 : 2),
        expanded: (index) => {
          const row = flat[index];
          return row?.kind === 'group' ? row.group === open : undefined;
        },
        parent: (index) => {
          const row = flat[index];
          if (row?.kind === 'group' || row === undefined) return null;
          return flat.findIndex((candidate) => candidate.kind === 'group' && candidate.group === open);
        },
        onToggle: (index, opening) => {
          const row = flat[index];
          if (row?.kind !== 'group') return undefined;
          setOpen(opening ? row.group : null);
          return row.group;
        },
      }}
    />
  );
}

describe('GridCore as a tree', () => {
  const activeCell = (grid: HTMLElement) => {
    const id = grid.getAttribute('aria-activedescendant');
    return id ? document.getElementById(id) : null;
  };
  const rowOf = (grid: HTMLElement) => activeCell(grid)?.closest('[role="row"]');

  it('is a treegrid whose group rows say their level and whether they are open', () => {
    render(<TreeGrid />);
    const grid = screen.getByRole('treegrid', { name: 'Groups' });
    const rows = screen.getAllByRole('row').slice(1);
    expect(rows).toHaveLength(3);
    for (const row of rows) {
      expect(row).toHaveAttribute('aria-level', '1');
      expect(row).toHaveAttribute('aria-expanded', 'false');
    }
    expect(grid).toHaveAttribute('aria-rowcount', '4');
  });

  it('opens a group with Right and closes it with Left, on its first cell', () => {
    render(<TreeGrid />);
    const grid = screen.getByRole('treegrid');
    act(() => grid.focus());
    fireEvent.keyDown(grid, { key: 'ArrowDown' });
    fireEvent.keyDown(grid, { key: 'ArrowRight' });
    expect(rowOf(grid)).toHaveAttribute('aria-expanded', 'true');
    expect(screen.getAllByRole('row').filter((row) => row.getAttribute('aria-level') === '2')).toHaveLength(4);
    // Into the group, then Left goes back up to its row, then closes it.
    fireEvent.keyDown(grid, { key: 'ArrowDown' });
    expect(activeCell(grid)).toHaveTextContent('Group B row 1');
    expect(activeCell(grid)).toHaveAttribute('aria-colspan', '2');
    fireEvent.keyDown(grid, { key: 'ArrowLeft' });
    expect(activeCell(grid)).toHaveTextContent('Group B');
    fireEvent.keyDown(grid, { key: 'ArrowLeft' });
    expect(rowOf(grid)).toHaveAttribute('aria-expanded', 'false');
  });

  it('keeps the cursor on a group whose row moved when the open group above it closed', () => {
    render(<TreeGrid />);
    const grid = screen.getByRole('treegrid');
    act(() => grid.focus());
    fireEvent.keyDown(grid, { key: 'Enter' }); // opens A: B is now row 4
    fireEvent.keyDown(grid, { key: 'End', ctrlKey: true });
    fireEvent.keyDown(grid, { key: 'ArrowUp' });
    fireEvent.keyDown(grid, { key: 'Home' });
    expect(activeCell(grid)).toHaveTextContent('Group B');
    fireEvent.keyDown(grid, { key: 'Enter' }); // opens B, closing A above it
    expect(activeCell(grid)).toHaveTextContent('Group B');
    expect(rowOf(grid)).toHaveAttribute('aria-expanded', 'true');
  });

  it('activates a row in a group on Enter, and opens a group by its chevron or a double-click', () => {
    const onActivate = vi.fn();
    render(<TreeGrid onActivate={onActivate} />);
    const grid = screen.getByRole('treegrid');
    const chevron = document.querySelectorAll('[data-tree-toggle]')[2];
    if (!chevron) throw new Error('No chevron for the third group');
    fireEvent.pointerDown(chevron, { button: 0 });
    expect(screen.getAllByRole('row').filter((row) => row.getAttribute('aria-level') === '2')).toHaveLength(2);
    expect(onActivate).not.toHaveBeenCalled();
    fireEvent.keyDown(grid, { key: 'ArrowDown' });
    fireEvent.keyDown(grid, { key: 'Enter' });
    expect(onActivate).toHaveBeenCalledWith(3);
    fireEvent.doubleClick(screen.getByText('Group A'));
    expect(screen.getByText('Group A').closest('[role="row"]')).toHaveAttribute('aria-expanded', 'true');
  });

  it('sorts from a sortable column’s header, which says its direction', () => {
    const sort = vi.fn();
    render(<TreeGrid sort={sort} />);
    const header = screen.getByRole('columnheader', { name: 'Name' });
    expect(header).toHaveAttribute('aria-sort', 'ascending');
    fireEvent.click(screen.getByRole('button', { name: 'Size' }));
    expect(sort).toHaveBeenCalledWith('size', false);
  });
});

describe('GridCore with columns that sort', () => {
  const sortable: GridColumn<Row>[] = columns.map((column) => ({ ...column, sortable: true }));

  it('stays one stop for Tab: the cursor reaches the header with Up, and Enter or Space there sorts', () => {
    const onSort = vi.fn();
    const { grid, activeCell, onActivate } = setup({ columns: sortable, onSort });
    // The header's buttons are for the pointer: Tab goes to the grid alone.
    expect(screen.getAllByRole('button').map((button) => button.tabIndex)).toEqual([-1, -1]);
    act(() => grid.focus());
    fireEvent.keyDown(grid, { key: 'ArrowUp' });
    expect(activeCell()).toHaveAttribute('role', 'columnheader');
    expect(activeCell()).toHaveTextContent('URL');
    fireEvent.keyDown(grid, { key: 'Enter' });
    expect(onSort).toHaveBeenLastCalledWith('url', true);
    expect(onActivate).not.toHaveBeenCalled();
    fireEvent.keyDown(grid, { key: 'ArrowRight' });
    expect(activeCell()).toHaveTextContent('Status');
    fireEvent.keyDown(grid, { key: ' ' });
    expect(onSort).toHaveBeenLastCalledWith('status', true);
    // Space sorted: it selected no row.
    expect(grid.querySelectorAll('[aria-selected="true"]')).toHaveLength(0);
    fireEvent.keyDown(grid, { key: 'ArrowDown' });
    expect(activeCell()).toHaveAttribute('role', 'gridcell');
    expect(activeCell()).toHaveTextContent('200');
    expect(activeCell()).toHaveAttribute('aria-colindex', '2');
  });

  it('leaves the keys of a header control that has focus to it', () => {
    const onSort = vi.fn();
    const { grid, onActivate } = setup({ columns: sortable, onSort });
    act(() => grid.focus());
    const button = screen.getByRole('button', { name: 'URL' });
    act(() => button.focus());
    fireEvent.keyDown(button, { key: 'Enter' });
    fireEvent.keyDown(button, { key: ' ' });
    fireEvent.keyDown(button, { key: 'ArrowDown' });
    expect(onActivate).not.toHaveBeenCalled();
    expect(grid.querySelectorAll('[aria-selected="true"]')).toHaveLength(0);
  });

  it('keeps focus on the grid when a header is clicked, with the cursor on it', () => {
    const onSort = vi.fn();
    const { grid, activeCell } = setup({ columns: sortable, onSort });
    const button = screen.getByRole('button', { name: 'Status' });
    fireEvent.mouseDown(button);
    fireEvent.click(button);
    expect(grid).toHaveFocus();
    expect(onSort).toHaveBeenCalledWith('status', true);
    expect(activeCell()).toHaveTextContent('Status');
  });

  it('starts on the header it was handed, focused: the grid a sort from that header brought', () => {
    const { grid, activeCell } = setup({ columns: sortable, onSort: vi.fn(), focusHeader: 'status' });
    expect(grid).toHaveFocus();
    expect(activeCell()).toHaveAttribute('role', 'columnheader');
    expect(activeCell()).toHaveTextContent('Status');
  });

  it('keeps Up on the first row for a grid whose columns don’t sort', () => {
    const { grid, activeCell } = setup();
    act(() => grid.focus());
    fireEvent.keyDown(grid, { key: 'ArrowUp' });
    expect(activeCell()).toHaveTextContent('https://example.com/0');
  });
});
