// a row menu holds the row's NUMBER (state.index, menu.indices), not the row. A running crawl's refresh
// that moves rows while the menu is open makes the menu act on whatever row is now at that number. This harness wires
// GridCore + useRowMenu + RowMenu + UrlRowItems as the screens do, with the row the menu holds (menu.row).
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useCallback } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { GridCore } from './GridCore';
import { RowMenu, UrlRowItems } from './RowMenu';
import { copyRowUrls, rowMenuLabel, useRowMenu } from './rowMenuState';
import type { GridColumn } from './types';

vi.mock('../feedback/notify', () => ({ notifyError: vi.fn(), notifyResult: vi.fn() }));
vi.mock('../feedback/announce', () => ({ announce: vi.fn() }));

interface Row {
  id: number;
  url: string;
}

const columns: GridColumn<Row>[] = [
  { id: 'url', header: 'URL', width: 300, cell: (row) => row.url, text: (row) => row.url },
];

function Harness({ list }: { list: Row[] }) {
  const rowMenu = useRowMenu<Row>();
  // As usePagedRows: getRow reads the rows there are now, with a new identity when a page lands.
  const getRow = useCallback((index: number) => list[index], [list]);
  // The row as it was when the menu opened: rows can move under a running crawl while it is open.
  const menuRow = rowMenu.state?.menu.row;
  const count = rowMenu.state?.menu.indices.length ?? 1;
  return (
    <>
      <GridCore<Row>
        label="URLs"
        columns={columns}
        rowCount={list.length}
        getRow={getRow}
        rowHeight={32}
        headerHeight={32}
        rowKey={(row) => row.id}
        onContextMenu={rowMenu.onContextMenu}
      />
      <RowMenu
        state={rowMenu.state}
        onOpenChange={rowMenu.onOpenChange}
        label={rowMenuLabel(count, menuRow?.url, ['URL', 'URLs'])}
      >
        <UrlRowItems
          count={count}
          url={menuRow?.url}
          onCopyUrls={() => {
            if (rowMenu.state) void copyRowUrls(rowMenu.state.menu, (row) => row.url);
          }}
          onCopyRows={() => void rowMenu.state?.menu.copy()}
        />
      </RowMenu>
    </>
  );
}

const row = (id: number): Row => ({ id, url: `https://example.com/${id}` });

describe('a row menu open while the rows move', () => {
  it('acts on the row it was opened on, not on the row now at its number', async () => {
    const user = userEvent.setup();
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, 'clipboard', { configurable: true, value: { writeText } });
    const { rerender } = render(<Harness list={[row(1), row(2), row(3)]} />);
    const grid = screen.getByRole('grid', { name: 'URLs' });
    fireEvent.contextMenu(grid.querySelector('[data-row="1"][data-col="0"]')!);
    await screen.findByRole('menu', { name: 'Menu for https://example.com/2' });
    // A crawl's refresh: a new URL sorts in above. The grid's cursor follows its row (rowKey); the menu doesn't.
    rerender(<Harness list={[row(9), row(1), row(2), row(3)]} />);
    const menu = await screen.findByRole('menu');
    // The menu now names another row than the one it was opened on.
    expect.soft(menu.getAttribute('aria-label')).toBe('Menu for https://example.com/2');
    await user.click(within(menu).getByRole('menuitem', { name: 'Copy the URL' }));
    await waitFor(() => expect(writeText).toHaveBeenCalled());
    // Opened on /2; copies /1.
    expect(writeText).toHaveBeenLastCalledWith('https://example.com/2');
    act(() => undefined);
  });
});
