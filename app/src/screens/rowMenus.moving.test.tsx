// Every grid's row menu acts on the row it was opened on: a running crawl's refresh that puts another row at that
// row's number while the menu is open changes neither its name nor what Copy the URL takes. One test per grid with a
// menu: URLs, Links, Issues' flat rows and groups, one sitemap's URLs, an origin's blocked URLs.
import { createMemoryHistory } from '@tanstack/react-router';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { GridQuery, IssueGroup, IssueRow, RobotsBlockedUrl, UrlRow } from '../api/grids';
import { answerGridQuery } from '../api/mock/gridQuery';
import { gridPagesOf } from '../api/queries';
import { setTransport } from '../api/transport';
import { App } from '../app/App';
import { createQueryClient } from '../app/queryClient';
import { createAppRouter } from '../app/router';
import { installKeyboard } from '../keyboard/registry';
import { updateSettings } from '../settings/settingsStore';
import { TEST_CRAWL_ID, fakeSidecar, json, type FakeRoute } from '../test/fakeSidecar';
import { ISSUE_GROUPS, ISSUE_ROWS, issuesRoutes } from '../test/issuesSidecar';
import { linksRoutes } from '../test/linksSidecar';
import { SITE, TEST_BLOCKED, sitemapsRobotsRoutes } from '../test/sitemapsRobotsSidecar';
import { urlsRoutes } from '../test/urlsSidecar';

const CRAWL = `/crawls/${TEST_CRAWL_ID}`;

// CodeMirror needs a browser's layout: robots.txt's file is a plain stand-in here (its tests cover it).
vi.mock('../inspector/SourceViewer', () => ({ default: () => <div data-testid="source-viewer" /> }));

let uninstall: () => void = () => undefined;
let writeText: ReturnType<typeof vi.fn>;
beforeEach(() => {
  uninstall = installKeyboard();
  updateSettings({ hiddenChecks: [], hiddenChecksByCrawl: {}, savedViews: [], showFullUrls: true });
});
afterEach(() => {
  uninstall();
  setTransport(fakeSidecar([]));
  updateSettings({ showFullUrls: false });
});

async function open(path: string, routes: FakeRoute[]) {
  setTransport(fakeSidecar(routes));
  const queryClient = createQueryClient();
  const router = createAppRouter(queryClient, createMemoryHistory({ initialEntries: [path] }));
  render(<App router={router} queryClient={queryClient} />);
  await act(() => router.load());
  const user = userEvent.setup();
  writeText = vi.fn().mockResolvedValue(undefined);
  Object.defineProperty(navigator, 'clipboard', { configurable: true, value: { writeText } });
  return { queryClient, user };
}

/**
 * Right-clicks the row at `index` of the grid, then lets `move` put another row at that number and the grid read its
 * rows again; the menu must still be of the row it was opened on, and copy its URL.
 */
async function expectMenuHoldsItsRow(options: {
  grid: HTMLElement;
  index: number;
  name: string | RegExp;
  url: string;
  move: () => void;
  moved: string;
  queryClient: ReturnType<typeof createQueryClient>;
  user: ReturnType<typeof userEvent.setup>;
  copy?: string;
}) {
  const { grid, index } = options;
  const cell = await waitFor(() => {
    const found = grid.querySelector<HTMLElement>(`[data-row="${index}"][data-col="0"]`);
    if (!found) throw new Error('no cell yet');
    return found;
  });
  fireEvent.contextMenu(cell);
  const menu = await screen.findByRole('menu', { name: options.name });
  options.move();
  await act(() => options.queryClient.invalidateQueries(gridPagesOf(TEST_CRAWL_ID)));
  await waitFor(() => expect(grid.textContent).toContain(options.moved));
  expect(screen.getByRole('menu')).toBe(menu);
  expect(menu).toHaveAccessibleName(options.name);
  await options.user.click(within(menu).getByRole('menuitem', { name: options.copy ?? 'Copy the URL' }));
  await waitFor(() => expect(writeText).toHaveBeenCalled());
  expect(writeText).toHaveBeenLastCalledWith(options.url);
}

describe('a row menu open while a running crawl moves the rows', () => {
  it('on URLs', async () => {
    const { routes, state } = urlsRoutes({ rows: [1, 2, 3].map(urlRow) });
    const { queryClient, user } = await open(`${CRAWL}/urls?sort=url`, routes);
    const grid = await screen.findByRole('grid', { name: 'URLs' });
    await expectMenuHoldsItsRow({
      grid,
      index: 1,
      name: 'Menu for https://example.com/p2',
      url: 'https://example.com/p2',
      move: () => (state.rows = [urlRow(0), ...state.rows]),
      moved: 'https://example.com/p0',
      queryClient,
      user,
    });
  });

  it('on Links', async () => {
    const { routes, state } = linksRoutes();
    const { queryClient, user } = await open(`${CRAWL}/links?cols=id,from.url,to.url&sort=-id`, routes);
    const grid = await screen.findByRole('grid', { name: 'Links' });
    await waitFor(() => expect(grid.querySelector('[data-row="1"][data-col="0"]')).not.toBeNull());
    const sorted = [...state.links].sort((a, b) => b.id - a.id);
    const second = sorted[1]!;
    await expectMenuHoldsItsRow({
      grid,
      index: 1,
      name: /^Menu for the link to /,
      url: String(second['to.url']),
      // Links' menu names the end in every item (the UX round's D-16): the menu was opened on the target's side.
      copy: 'Copy the target URL',
      move: () => (state.links = [...state.links, { ...sorted[0]!, id: 999, 'to.url': 'https://example.com/new' }]),
      moved: '999',
      queryClient,
      user,
    });
  });

  it('on a check’s flat rows', async () => {
    const rows: Record<string, (IssueRow & { group?: number })[]> = { ...ISSUE_ROWS };
    rows['title-too-long'] = [...(ISSUE_ROWS['title-too-long'] ?? [])];
    const { queryClient, user } = await open(`${CRAWL}/issues/title-too-long`, issuesRoutes({ rows }));
    const grid = await screen.findByRole('grid', { name: /Title too long/ });
    await expectMenuHoldsItsRow({
      grid,
      index: 1,
      name: 'Menu for /b/',
      url: 'https://example.com/b/',
      move: () =>
        (rows['title-too-long'] = [
          { id: 15, url: 'https://example.com/0/', detail: '99 characters' },
          ...(rows['title-too-long'] ?? []),
        ]),
      moved: '99 characters',
      queryClient,
      user,
    });
  });

  it('on a check’s groups', async () => {
    const groups: Record<string, IssueGroup[]> = { ...ISSUE_GROUPS };
    groups['empty-anchor-text'] = [...(ISSUE_GROUPS['empty-anchor-text'] ?? [])];
    const { queryClient, user } = await open(`${CRAWL}/issues/empty-anchor-text`, issuesRoutes({ groups }));
    // The check opens as a list (the UX round of 2026-11-19): its groups are a choice away.
    await user.click(await screen.findByRole('radio', { name: /^Grouped by/ }));
    const grid = await screen.findByRole('treegrid', { name: /grouped by/ });
    // Its first group opens by itself: closed again, the garden group is the second row, as it was.
    const toggle = await waitFor(() => {
      const found = grid.querySelector<HTMLElement>('[role="row"][aria-expanded="true"] [data-tree-toggle]');
      if (!found) throw new Error('the first group hasn’t opened yet');
      return found;
    });
    await user.click(toggle);
    await waitFor(() => expect(grid.querySelector('[data-row="1"][data-col="0"]')?.textContent).toContain('/garden/'));
    await expectMenuHoldsItsRow({
      grid,
      index: 1,
      name: 'Menu for the group',
      // The garden group, opened on: its one page's URL. The group now at its number has five.
      url: 'https://example.com/page-7/',
      copy: 'Copy its URL',
      move: () =>
        (groups['empty-anchor-text'] = [
          {
            detail: null,
            relatedUrlId: 20,
            relatedUrl: 'https://example.com/new/',
            pages: 6,
            share: 1,
            likelyTemplate: false,
          },
          ...(groups['empty-anchor-text'] ?? []),
        ]),
      moved: '/new/',
      queryClient,
      user,
    });
  });

  it('on one sitemap’s URLs', async () => {
    let rows: UrlRow[] = [1, 2, 3].map(urlRow);
    const own: FakeRoute = [
      'POST',
      /^\/api\/crawls\/[0-9a-f]{16}\/sitemaps\/urls$/,
      (request) => {
        const answer = answerGridQuery(rows, request.body as GridQuery, { available: true, reason: null });
        return answer.ok ? json(answer.page) : json({});
      },
    ];
    const { queryClient, user } = await open(`${CRAWL}/sitemaps/3?sort=url`, [own, ...sitemapsRobotsRoutes()]);
    const grid = await screen.findByRole('grid', { name: `URLs listed by ${SITE}/sitemaps/products.xml` });
    await expectMenuHoldsItsRow({
      grid,
      index: 1,
      name: /^Menu for .*\/p2$/,
      url: 'https://example.com/p2',
      move: () => (rows = [urlRow(0), ...rows]),
      moved: 'https://example.com/p0',
      queryClient,
      user,
    });
  });

  it('on an origin’s blocked URLs', async () => {
    const blocked: Record<string, RobotsBlockedUrl[]> = { [SITE]: [...TEST_BLOCKED] };
    const { queryClient, user } = await open(
      `${CRAWL}/robots/${encodeURIComponent(SITE)}`,
      sitemapsRobotsRoutes({ blocked }),
    );
    const grid = await screen.findByRole('grid', { name: 'Blocked URLs' });
    const second = TEST_BLOCKED[1]!;
    await expectMenuHoldsItsRow({
      grid,
      index: 1,
      name: /^Menu for /,
      url: second.url,
      move: () =>
        (blocked[SITE] = [
          { id: 20, url: `${SITE}/admin/aaa`, reason: 'rule', line: 6, rule: 'Disallow: /admin/', error: null },
          ...(blocked[SITE] ?? []),
        ]),
      moved: '/admin/aaa',
      queryClient,
      user,
    });
  });
});

function urlRow(n: number): UrlRow {
  return { id: 100 + n, url: `https://example.com/p${n}`, statusCode: 200, statusClass: '2xx', fetched: true };
}
