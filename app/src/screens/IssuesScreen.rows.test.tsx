// A check's URLs as rows of their own: the row menu on its pages and groups, and the cursor staying on its row (by
// the row's identity, not its number) when the rows are read again with others among them.
import { createMemoryHistory } from '@tanstack/react-router';
import { act, render, screen, waitFor, within } from '@testing-library/react';
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
    const grid = await screen.findByRole('treegrid', { name: /grouped by/ });
    await within(grid).findAllByRole('row');
    act(() => grid.focus());
    await user.keyboard('{ArrowDown}');
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
    const grid = await screen.findByRole('treegrid', { name: /grouped by/ });
    await within(grid).findAllByRole('row');
    act(() => grid.focus());
    await user.keyboard('{ArrowDown}{Shift>}{F10}{/Shift}');
    const menu = await screen.findByRole('menu', { name: 'Menu for the group' });
    await user.click(within(menu).getByRole('menuitem', { name: /in URLs$/ }));
    await waitFor(() => expect(router.state.location.pathname).toBe(`/crawls/${TEST_CRAWL_ID}/urls`));
    expect(router.state.location.search).toMatchObject({
      filter: [{ column: 'check', op: 'in', values: ['empty-anchor-text'], group: { detail: null, relatedUrlId: 9 } }],
    });
  });
});
