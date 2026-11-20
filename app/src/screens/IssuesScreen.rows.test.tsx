// A check's URLs as rows of their own: the row menu on its pages and groups, and the cursor staying on its row (by
// the row's identity, not its number) when the rows are read again with others among them. A grouped check opens as a
// list (the 2026-11-19 UX round, A-06): the tests of its groups choose "Grouped by …" first.
import { createMemoryHistory } from '@tanstack/react-router';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import type { IssueGroup, IssueRow } from '../api/grids';
import { gridPagesOf } from '../api/queries';
import { setTransport } from '../api/transport';
import { App } from '../app/App';
import { createQueryClient } from '../app/queryClient';
import { createAppRouter } from '../app/router';
import { installKeyboard } from '../keyboard/registry';
import { updateSettings } from '../settings/settingsStore';
import { TEST_CRAWL_ID, fakeSidecar } from '../test/fakeSidecar';
import { ISSUE_GROUPS, ISSUE_ROWS, issuesRoutes } from '../test/issuesSidecar';
import { forgetCheckPlaces } from './issues/issuesPlace';

const ISSUES = `/crawls/${TEST_CRAWL_ID}/issues`;

function sidecar() {
  // Copies the test can change: the routes read them on each request.
  const rows: Record<string, (IssueRow & { group?: number })[]> = Object.fromEntries(
    Object.entries(ISSUE_ROWS).map(([check, list]) => [check, [...list]]),
  );
  const groups: Record<string, IssueGroup[]> = Object.fromEntries(
    Object.entries(ISSUE_GROUPS).map(([check, list]) => [check, [...list]]),
  );
  setTransport(fakeSidecar(issuesRoutes({ rows, groups })));
  return { rows, groups };
}

async function open(path: string) {
  const queryClient = createQueryClient();
  const router = createAppRouter(queryClient, createMemoryHistory({ initialEntries: [path] }));
  render(<App router={router} queryClient={queryClient} />);
  await act(() => router.load());
  return { router, queryClient, user: userEvent.setup() };
}

/** The check's groups, which its "Grouped by …" shows: it opens as a list. */
async function showGroups(user: ReturnType<typeof userEvent.setup>) {
  await user.click(await screen.findByRole('radio', { name: /^Grouped by/ }));
  return screen.findByRole('treegrid', { name: /grouped by/ });
}

/** Puts the grid's cursor on a row, as a press on it does, and focus on the grid. */
function pressRow(grid: HTMLElement, row: HTMLElement) {
  fireEvent.pointerDown(row.querySelector<HTMLElement>('[role="gridcell"]') ?? row, { button: 0 });
  act(() => grid.focus());
}

/** Closes the group that opened by itself, so the groups after it are in the grid's window. */
async function closeOpenGroup(grid: HTMLElement) {
  const open = await within(grid).findByRole('row', { expanded: true });
  fireEvent.pointerDown(open.querySelector('[data-tree-toggle]') ?? open, { button: 0 });
  await waitFor(() => expect(within(grid).queryByRole('row', { expanded: true })).toBeNull());
}

/** A menu's items, each without the keys said after its words. */
const itemsOf = (menu: HTMLElement) =>
  within(menu)
    .getAllByRole('menuitem')
    .map((item) => item.querySelector('.si-menu-item__label')?.textContent);

const cursorRowText = (grid: HTMLElement) =>
  document.getElementById(grid.getAttribute('aria-activedescendant') ?? '')?.closest('[role="row"]')?.textContent ??
  null;

let uninstall: () => void = () => undefined;
beforeEach(() => {
  uninstall = installKeyboard();
  updateSettings({ hiddenChecks: [], hiddenChecksByCrawl: {} });
});
afterEach(() => {
  uninstall();
  // Where a check was left (its view, its cursor) is kept for the session: each test starts with none.
  forgetCheckPlaces();
  setTransport(fakeSidecar([]));
});

describe('a check’s flat rows', () => {
  it('keep the cursor on its issue when the rows are read again with one above it', async () => {
    const { rows } = sidecar();
    const { queryClient, user } = await open(`${ISSUES}/title-too-long`);
    const grid = await screen.findByRole('grid', { name: /Title too long/ });
    await within(grid).findByText('742 px, over 580 (78 characters)');
    act(() => grid.focus());
    await user.keyboard('{ArrowDown}');
    expect(cursorRowText(grid)).toMatch(/64 characters/);
    rows['title-too-long'] = [
      { id: 15, url: 'https://example.com/0/', detail: '930 px, over 580 (99 characters)' },
      ...rows['title-too-long']!,
    ];
    await act(() => queryClient.invalidateQueries(gridPagesOf(TEST_CRAWL_ID)));
    await within(grid).findByText('930 px, over 580 (99 characters)');
    expect(cursorRowText(grid)).toMatch(/64 characters/);
  });

  it('have a row menu that shows the page in URLs, with the inspector on it', async () => {
    sidecar();
    const { router, user } = await open(`${ISSUES}/title-too-long`);
    const grid = await screen.findByRole('grid', { name: /Title too long/ });
    await within(grid).findByText('742 px, over 580 (78 characters)');
    act(() => grid.focus());
    await user.keyboard('{ArrowDown}{Shift>}{F10}{/Shift}');
    const menu = await screen.findByRole('menu', { name: 'Menu for /b/' });
    await user.click(within(menu).getByRole('menuitem', { name: 'Show the page in URLs' }));
    await waitFor(() => expect(router.state.location.pathname).toBe(`/crawls/${TEST_CRAWL_ID}/urls`));
    expect(router.state.location.search).toMatchObject({
      filter: [{ column: 'url', op: 'eq', value: 'https://example.com/b/' }],
      inspect: 13,
    });
  });
});

describe('a check’s groups', () => {
  it('keep the cursor on its group when the groups are read again with one above it', async () => {
    const { groups } = sidecar();
    const { queryClient, user } = await open(`${ISSUES}/empty-anchor-text`);
    const grid = await showGroups(user);
    // The first of its two groups opened by itself: closed, the cursor goes on the other.
    await closeOpenGroup(grid);
    pressRow(grid, await within(grid).findByRole('row', { name: /\/garden\//, expanded: false }));
    expect(cursorRowText(grid)).toMatch(/garden/);
    groups['empty-anchor-text'] = [
      {
        detail: null,
        relatedUrlId: 20,
        relatedUrl: 'https://example.com/new/',
        pages: 6,
        share: 1,
        likelyTemplate: false,
      },
      ...groups['empty-anchor-text']!,
    ];
    await act(() => queryClient.invalidateQueries(gridPagesOf(TEST_CRAWL_ID)));
    await waitFor(() =>
      expect(
        within(grid)
          .getAllByRole('row')
          .some((row) => row.textContent.includes('/new/')),
      ).toBe(true),
    );
    expect(cursorRowText(grid)).toMatch(/garden/);
  });

  it('give a group’s row a menu with its pages in URLs', async () => {
    sidecar();
    const { router, user } = await open(`${ISSUES}/empty-anchor-text`);
    const grid = await showGroups(user);
    await closeOpenGroup(grid);
    pressRow(grid, await within(grid).findByRole('row', { name: /\/garden\//, expanded: false }));
    await user.keyboard('{Shift>}{F10}{/Shift}');
    const menu = await screen.findByRole('menu', { name: 'Menu for the group' });
    // The group is a URL (the one its page links to): the menu acts on it as on any URL, then on the group's pages.
    expect(itemsOf(menu)).toEqual([
      'Inspect',
      'Open in the browser',
      'Show the links to it in Links',
      'Copy its URL',
      'Copy the row',
      'Show the page in URLs',
    ]);
    await user.click(within(menu).getByRole('menuitem', { name: /in URLs$/ }));
    await waitFor(() => expect(router.state.location.pathname).toBe(`/crawls/${TEST_CRAWL_ID}/urls`));
    expect(router.state.location.search).toMatchObject({
      filter: [{ column: 'check', op: 'in', values: ['empty-anchor-text'], group: { detail: null, relatedUrlId: 9 } }],
    });
  });

  it('inspect the URL a group is of from its menu, show the links to it in Links, and count the URLs a copy takes', async () => {
    sidecar();
    const { router, user } = await open(`${ISSUES}/empty-anchor-text`);
    const grid = await showGroups(user);
    // The home page's group, of five pages: the first of the two, opened by itself.
    pressRow(grid, await within(grid).findByRole('row', { expanded: true }));
    await user.keyboard('{Shift>}{F10}{/Shift}');
    let menu = await screen.findByRole('menu', { name: 'Menu for the group' });
    expect(within(menu).getByRole('menuitem', { name: 'Copy its 5 URLs' })).toBeVisible();
    expect(within(menu).getByRole('menuitem', { name: 'Show all 5 pages in URLs' })).toBeVisible();
    await user.click(within(menu).getByRole('menuitem', { name: /^Inspect/ }));
    await waitFor(() => expect(router.state.location.search).toMatchObject({ inspect: 1 }));

    pressRow(grid, within(grid).getByRole('row', { expanded: true }));
    await user.keyboard('{Shift>}{F10}{/Shift}');
    menu = await screen.findByRole('menu', { name: 'Menu for the group' });
    await user.click(within(menu).getByRole('menuitem', { name: 'Show the links to it in Links' }));
    await waitFor(() => expect(router.state.location.pathname).toBe(`/crawls/${TEST_CRAWL_ID}/links`));
    expect(router.state.location.search).toMatchObject({
      filter: [
        { column: 'to.id', op: 'eq', value: 1 },
        { column: 'selfLink', op: 'is', value: false },
      ],
    });
  });

  it('give a group that is no URL only the group’s own items', async () => {
    sidecar();
    const { user } = await open(`${ISSUES}/internal-client-error`);
    const grid = await showGroups(user);
    pressRow(grid, await within(grid).findByRole('row', { name: /404 Not Found/ }));
    await user.keyboard('{Shift>}{F10}{/Shift}');
    const menu = await screen.findByRole('menu', { name: 'Menu for the group' });
    // The check can flag any URL: its group's URLs aren't called pages.
    expect(itemsOf(menu)).toEqual(['Copy its 2 URLs', 'Copy the row', 'Show all 2 in URLs']);
  });

  it('call a URL “it”, not “the page”, in the menu of a check that can flag any URL', async () => {
    sidecar();
    const { user } = await open(`${ISSUES}/internal-client-error`);
    const grid = await showGroups(user);
    pressRow(grid, await within(grid).findByRole('row', { name: /\/old\// }));
    await user.keyboard('{Shift>}{F10}{/Shift}');
    const menu = await screen.findByRole('menu', { name: 'Menu for /old/' });
    expect(within(menu).getByRole('menuitem', { name: 'Show it in URLs' })).toBeVisible();
    expect(within(menu).queryByRole('menuitem', { name: 'Show the page in URLs' })).toBeNull();
  });
});
