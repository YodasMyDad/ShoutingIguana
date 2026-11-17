// Step 3b: a page an image check flagged opens its images in Links, as the Images preset shows them; a page another
// check flagged has no such item.
import { createMemoryHistory } from '@tanstack/react-router';
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import type { GridFilter } from '../api/grids';
import { setTransport } from '../api/transport';
import { App } from '../app/App';
import { createQueryClient } from '../app/queryClient';
import { createAppRouter } from '../app/router';
import { installKeyboard } from '../keyboard/registry';
import { evidenceRoutes } from '../test/evidenceSidecar';
import { TEST_CRAWL_ID, fakeSidecar } from '../test/fakeSidecar';
import { IMAGE_COLUMNS, IMAGE_SORT } from './links/presets';

const ISSUES = `/crawls/${TEST_CRAWL_ID}/issues`;
const groupOf = (relatedUrlId: number | null, detail: string | null = null) =>
  `?group=${encodeURIComponent(JSON.stringify({ detail, relatedUrlId }))}`;

async function open(path: string) {
  setTransport(fakeSidecar(evidenceRoutes()));
  const queryClient = createQueryClient();
  const router = createAppRouter(queryClient, createMemoryHistory({ initialEntries: [path] }));
  render(<App router={router} queryClient={queryClient} />);
  await act(() => router.load());
  return router;
}

const cellOf = (row: HTMLElement) => row.querySelector<HTMLElement>('[role="gridcell"]') ?? row;
const pages = (grid: HTMLElement) => [...grid.querySelectorAll<HTMLElement>('[role="row"][aria-level="2"]')];

let uninstall: () => void = () => undefined;
beforeEach(() => {
  uninstall = installKeyboard();
});
afterEach(() => {
  cleanup();
  uninstall();
  setTransport(fakeSidecar([]));
});

describe('a page an image check flagged', () => {
  it('opens its images in Links, the largest first, with the image’s own columns', async () => {
    const router = await open(`${ISSUES}/image-too-large${groupOf(812)}`);
    const grid = await screen.findByRole('treegrid', { name: /Images over 200 KB/ });
    await waitFor(() => expect(pages(grid)[0]).toHaveTextContent('/'));
    fireEvent.contextMenu(cellOf(pages(grid)[0]!));
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Show its images in Links' }));
    await waitFor(() => expect(router.state.location.pathname).toBe(`/crawls/${TEST_CRAWL_ID}/links`));
    const search = router.state.location.search as { filter?: GridFilter[]; cols?: string[]; sort?: unknown };
    expect(search.filter?.[0]).toMatchObject({ column: 'from.id', op: 'eq' });
    expect(search.filter?.slice(1)).toEqual([
      { column: 'selfLink', op: 'is', value: false },
      { column: 'kind', op: 'in', values: ['image'] },
    ]);
    expect(search.cols).toEqual([...IMAGE_COLUMNS]);
    expect(search.sort).toEqual([...IMAGE_SORT]);
  });

  it('names the image a group is about by its file, read and copied in full', async () => {
    await open(`${ISSUES}/image-too-large${groupOf(812)}`);
    const grid = await screen.findByRole('treegrid', { name: /Images over 200 KB/ });
    const hero = await within(grid).findByRole('row', { name: /hero\.jpg/ });
    expect(hero.querySelector('.si-url__file')).toHaveTextContent('hero.jpg');
    expect(hero.querySelector('.si-url__rest')).toHaveTextContent('/img/');
  });

  it('is the only kind of page with the item', async () => {
    await open(`${ISSUES}/links-to-redirects${groupOf(90)}`);
    const grid = await screen.findByRole('treegrid', { name: /Internal links to redirects/ });
    await waitFor(() => expect(pages(grid)[0]).toHaveTextContent('/'));
    fireEvent.contextMenu(cellOf(pages(grid)[0]!));
    const menu = await screen.findByRole('menu');
    expect(within(menu).getByRole('menuitem', { name: 'Show the page in URLs' })).toBeVisible();
    expect(within(menu).queryByRole('menuitem', { name: 'Show its images in Links' })).toBeNull();
  });
});
