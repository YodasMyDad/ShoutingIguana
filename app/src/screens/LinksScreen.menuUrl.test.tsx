// the Links grid asks the engine only for the columns it shows (linkQueryColumns: from.id, to.id and the
// shown ones), and the engine answers with only those (LinkGridReader.Shown). The row menu reads `${end}.url` from the
// row, so with the Target URL column hidden its URL items are dead. The fake sidecar here answers with the asked
// columns only, as the engine does.
import { createMemoryHistory } from '@tanstack/react-router';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { GridPage, GridQuery, LinkRow } from '../api/grids';
import { setTransport, type TransportRequest } from '../api/transport';
import { App } from '../app/App';
import { createQueryClient } from '../app/queryClient';
import { createAppRouter } from '../app/router';
import { TEST_CRAWL_ID, fakeSidecar, json } from '../test/fakeSidecar';
import type { FakeRoute } from '../test/fakeSidecar';
import { linksRoutes } from '../test/linksSidecar';

const LINKS = `/crawls/${TEST_CRAWL_ID}/links`;

function projecting(routes: FakeRoute[]): FakeRoute[] {
  return routes.map((route) => {
    const [method, pattern, answer] = route;
    if (method !== 'POST' || !pattern.source.endsWith('links$')) return route;
    return [
      method,
      pattern,
      async (request: TransportRequest, match: RegExpMatchArray) => {
        const response = await answer(request, match);
        if (!response.ok) return response;
        const page = (await response.json()) as GridPage<LinkRow>;
        const asked = new Set(['id', ...((request.body as GridQuery).columns ?? [])]);
        const rows = page.rows.map(
          (row) => Object.fromEntries(Object.entries(row).filter(([key]) => asked.has(key))) as LinkRow,
        );
        return json({ ...page, rows });
      },
    ] as FakeRoute;
  });
}

describe('the Links row menu with the Target URL column hidden', () => {
  async function menuOn(cols: string) {
    const { routes } = linksRoutes();
    setTransport(fakeSidecar(projecting(routes)));
    const queryClient = createQueryClient();
    const router = createAppRouter(queryClient, createMemoryHistory({ initialEntries: [`${LINKS}?cols=${cols}`] }));
    render(<App router={router} queryClient={queryClient} />);
    await act(() => router.load());
    const grid = await screen.findByRole('grid', { name: 'Links' });
    const cell = await waitFor(() => {
      const found = grid.querySelector('[data-row="0"][data-col="2"]');
      if (!found) throw new Error('no row yet');
      return found;
    });
    // Right-click on the anchor text: the menu is for the target, as Enter is.
    fireEvent.contextMenu(cell);
    return screen.findByRole('menu');
  }

  // The menu names each end (the UX round's D-16): the target's item is "Open the target in your browser".
  it('control: with the Target URL shown, Open the target in your browser is enabled', async () => {
    const menu = await menuOn('from.url,to.url,text');
    expect(within(menu).getByRole('menuitem', { name: /^Open the target in your browser/ })).not.toHaveAttribute(
      'aria-disabled',
      'true',
    );
  });

  it('with the Target URL hidden, Open the target in your browser is still enabled', async () => {
    const menu = await menuOn('from.url,kind,text');
    expect(within(menu).getByRole('menuitem', { name: /^Open the target in your browser/ })).not.toHaveAttribute(
      'aria-disabled',
      'true',
    );
    expect(within(menu).getByRole('menuitem', { name: 'Copy the target URL' })).not.toHaveAttribute(
      'aria-disabled',
      'true',
    );
  });
});
