import { createMemoryHistory } from '@tanstack/react-router';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { GridQuery, IssueGroup } from '../api/grids';
import { setTransport } from '../api/transport';
import { App } from '../app/App';
import { createQueryClient } from '../app/queryClient';
import { createAppRouter } from '../app/router';
import { installKeyboard } from '../keyboard/registry';
import { updateSettings } from '../settings/settingsStore';
import { LOCATE_GROUP_LIMIT, LOCATE_GROUP_PAGE } from '../api/queries';
import { TEST_CRAWL_ID, fakeSidecar, type FakeRoute } from '../test/fakeSidecar';
import {
  ANCHOR_ROWS,
  ISSUES_ANALYSIS,
  ISSUE_GROUPS,
  issuesRoutes,
  type IssuesSidecarOptions,
} from '../test/issuesSidecar';

const ISSUES = `/crawls/${TEST_CRAWL_ID}/issues`;

function sidecar(options: Omit<IssuesSidecarOptions, 'onAnalyse'> = {}) {
  const analyse = vi.fn();
  const transport = fakeSidecar(issuesRoutes({ ...options, onAnalyse: analyse }));
  setTransport(transport);
  return { requests: transport.requests, analyse };
}

async function open(path: string) {
  const queryClient = createQueryClient();
  const history = createMemoryHistory({ initialEntries: [path] });
  const router = createAppRouter(queryClient, history);
  render(<App router={router} queryClient={queryClient} />);
  await act(() => router.load());
  return { router, history };
}

const path = (router: Awaited<ReturnType<typeof open>>['router']) => router.state.location.pathname;
const options = () => screen.getAllByRole('option');
const selectedOption = () =>
  screen.getAllByRole('option').find((option) => option.getAttribute('aria-selected') === 'true');

let uninstall: () => void = () => undefined;
beforeEach(() => {
  uninstall = installKeyboard();
});
afterEach(() => {
  uninstall();
  updateSettings({ hiddenChecks: [] });
  setTransport(fakeSidecar([]));
});

describe('the Issues screen', () => {
  it('answers first, and selects the first check to fix in place of the address it came to', async () => {
    sidecar();
    const { router, history } = await open(ISSUES);
    expect(
      await screen.findByText('3 checks found 11 issues on this crawl. 1 passed and 1 was skipped.'),
    ).toBeVisible();
    await waitFor(() => expect(path(router)).toBe(`${ISSUES}/internal-client-error`));
    // Replaced, so back doesn't come here to be sent on again.
    expect(history.length).toBe(1);
    expect(await screen.findByRole('heading', { name: 'Internal URL returns 4xx', level: 2 })).toBeVisible();
    expect(screen.getByText('What’s wrong with Internal URL returns 4xx.')).toBeVisible();
    expect(screen.getByText('Why Internal URL returns 4xx matters.')).toBeVisible();
  });

  it('lists checks by category, a skipped one with its reason, and the passed ones only when asked', async () => {
    sidecar();
    await open(`${ISSUES}/internal-client-error`);
    const list = await screen.findByRole('listbox', { name: 'Checks' });
    expect(
      within(list)
        .getAllByRole('group')
        .map((group) => group.getAttribute('aria-labelledby') !== null),
    ).toEqual([true, true, true]);
    expect(within(list).getByRole('option', { name: 'Orphan page Skipped No sitemap could be read.' })).toBeVisible();
    expect(within(list).queryByRole('option', { name: /Internal URL returns 5xx/ })).toBeNull();
    fireEvent.click(screen.getByRole('switch', { name: 'Show the check that passed' }));
    expect(within(list).getByRole('option', { name: 'Internal URL returns 5xx Passed' })).toBeVisible();
  });

  it('moves the selection with the arrow keys, replacing the address, and a click is a new place', async () => {
    sidecar();
    const { router, history } = await open(`${ISSUES}/internal-client-error`);
    await screen.findByRole('listbox');
    const first = selectedOption();
    expect(first).toHaveAttribute('tabindex', '0');
    act(() => first?.focus());
    fireEvent.keyDown(first!, { key: 'ArrowDown' });
    await waitFor(() => expect(path(router)).toBe(`${ISSUES}/empty-anchor-text`));
    expect(document.activeElement).toHaveAccessibleName(/Internal links without anchor text/);
    expect(history.length).toBe(1);
    fireEvent.keyDown(document.activeElement as HTMLElement, { key: 'End' });
    await waitFor(() => expect(path(router)).toBe(`${ISSUES}/title-too-long`));
    fireEvent.click(screen.getByRole('option', { name: /Orphan page/ }));
    await waitFor(() => expect(path(router)).toBe(`${ISSUES}/orphan-page`));
    expect(history.length).toBe(2);
  });

  it('filters by text and severity in the route, and clears them in one click when nothing matches', async () => {
    sidecar();
    const { router } = await open(`${ISSUES}/internal-client-error`);
    const filter = await screen.findByRole('searchbox', { name: 'Filter checks' });
    fireEvent.change(filter, { target: { value: 'anchor' } });
    expect(options().map((option) => option.textContent)).toEqual([expect.stringContaining('anchor text')]);
    await waitFor(() => expect(router.state.location.search).toMatchObject({ q: 'anchor' }));
    fireEvent.click(screen.getByRole('radio', { name: /Errors/ }));
    expect(await screen.findByText('No checks match these filters.')).toBeVisible();
    fireEvent.click(screen.getByRole('button', { name: 'Clear the filters' }));
    await waitFor(() => expect(router.state.location.search).toEqual({}));
    expect(options().length).toBeGreaterThan(3);
  });

  it('focuses the check filter with Ctrl+F', async () => {
    sidecar();
    await open(`${ISSUES}/internal-client-error`);
    const filter = await screen.findByRole('searchbox', { name: 'Filter checks' });
    fireEvent.keyDown(document.body, { key: 'f', ctrlKey: true });
    expect(filter).toHaveFocus();
  });

  it('shows a skipped check’s reason, never 0, and a passed check plainly', async () => {
    sidecar();
    await open(`${ISSUES}/orphan-page`);
    expect(await screen.findByText('This check was skipped on this crawl')).toBeVisible();
    expect(screen.getAllByText('No sitemap could be read.').length).toBeGreaterThan(0);
    expect(screen.queryByText('Pages affected')).toBeNull();
    expect(screen.queryByRole('button', { name: 'Copy URLs' })).toBeNull();
  });

  it('opens a group to show its first pages and its link to all of them, then closes it', async () => {
    sidecar();
    const { router } = await open(`${ISSUES}/empty-anchor-text`);
    const grid = await screen.findByRole('treegrid', { name: /grouped by related URL/ });
    expect(await screen.findByText('Likely in the site’s template')).toBeVisible();
    await within(grid).findByText('5');
    const rows = () => within(grid).getAllByRole('row').slice(1);
    expect(rows().map((row) => row.getAttribute('aria-expanded'))).toEqual(['false', 'false']);
    fireEvent.pointerDown(grid.querySelector('[data-tree-toggle]')!, { button: 0 });
    await waitFor(() =>
      expect(router.state.location.search).toMatchObject({ group: { detail: null, relatedUrlId: 1 } }),
    );
    expect(await within(grid).findByText('/page-2/')).toBeVisible();
    const link = within(grid).getByRole('link', { name: 'Show all 5 pages in URLs' });
    expect(decodeURIComponent(link.getAttribute('href') ?? '')).toContain('"group":{"detail":null,"relatedUrlId":1}');
    expect(rows()[0]).toHaveAttribute('aria-expanded', 'true');
    expect(rows().filter((row) => row.getAttribute('aria-level') === '2')).toHaveLength(6);
    // One stop for Tab: nothing in its rows takes focus of its own (the cursor reaches them).
    expect(within(grid).queryAllByRole('button')).toEqual([]);
    // Enter on a page opens the inspector; Left on its group closes it.
    act(() => grid.focus());
    fireEvent.keyDown(grid, { key: 'ArrowDown' });
    fireEvent.keyDown(grid, { key: 'Enter' });
    await waitFor(() => expect(router.state.location.search).toMatchObject({ inspect: 2 }));
    fireEvent.keyDown(grid, { key: 'ArrowLeft' });
    fireEvent.keyDown(grid, { key: 'ArrowLeft' });
    await waitFor(() => expect(router.state.location.search).not.toHaveProperty('group'));
  });

  it('reopens the group the address names', async () => {
    sidecar();
    await open(`${ISSUES}/empty-anchor-text?group=${encodeURIComponent('{"detail":null,"relatedUrlId":9}')}`);
    const grid = await screen.findByRole('treegrid');
    expect(await within(grid).findByText('/page-7/')).toBeVisible();
    expect(within(grid).getByRole('link', { name: 'Show the page in URLs' })).toBeVisible();
  });

  it('lists a check that doesn’t group flat, with its detail, sorting from the header', async () => {
    const { requests } = sidecar();
    const { router } = await open(`${ISSUES}/title-too-long`);
    const grid = await screen.findByRole('grid', { name: /Title too long: affected pages/ });
    expect(await within(grid).findByText('78 characters')).toBeVisible();
    fireEvent.click(within(grid).getByRole('button', { name: 'Detail' }));
    await waitFor(() =>
      expect(router.state.location.search).toMatchObject({ sort: [{ column: 'detail', desc: false }] }),
    );
    await waitFor(() =>
      expect(
        requests.some(
          (request) =>
            request.path.endsWith('/title-too-long/rows') && (request.body as GridQuery).sort[0]?.column === 'detail',
        ),
      ).toBe(true),
    );
  });

  it('sorts a flat check from the keyboard, keeping focus and the cursor on the header it sorted by', async () => {
    sidecar();
    const { router } = await open(`${ISSUES}/title-too-long`);
    const grid = await screen.findByRole('grid', { name: /Title too long: affected pages/ });
    await within(grid).findByText('78 characters');
    act(() => grid.focus());
    fireEvent.keyDown(grid, { key: 'ArrowUp' });
    fireEvent.keyDown(grid, { key: 'ArrowRight' });
    fireEvent.keyDown(grid, { key: 'Enter' });
    await waitFor(() =>
      expect(router.state.location.search).toMatchObject({ sort: [{ column: 'detail', desc: false }] }),
    );
    expect(router.state.location.search).not.toHaveProperty('inspect');
    // The sort brings a new grid: it has focus, and its cursor is where the user pressed.
    await waitFor(() => expect(screen.getByRole('grid', { name: /Title too long/ })).not.toBe(grid));
    const sorted = screen.getByRole('grid', { name: /Title too long/ });
    expect(sorted).toHaveFocus();
    expect(document.getElementById(sorted.getAttribute('aria-activedescendant') ?? '')).toHaveTextContent('Detail');
  });

  it('copies the check’s URLs, one per line, and the open group’s when one is open', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, 'clipboard', { configurable: true, value: { writeText } });
    sidecar();
    await open(`${ISSUES}/empty-anchor-text`);
    fireEvent.click(await screen.findByRole('button', { name: 'Copy URLs' }));
    await waitFor(() => expect(writeText).toHaveBeenCalledOnce());
    expect(writeText.mock.calls[0]?.[0]).toBe(ANCHOR_ROWS.map((row) => row.url).join('\n'));
    expect(await screen.findByText('Copied 6 URLs')).toBeVisible();
  });

  it('copies the open group’s URLs with Ctrl+Shift+C', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, 'clipboard', { configurable: true, value: { writeText } });
    sidecar();
    await open(`${ISSUES}/empty-anchor-text?group=${encodeURIComponent('{"detail":null,"relatedUrlId":9}')}`);
    // Once the group is shown open: what's copied is what's shown.
    expect(await within(await screen.findByRole('treegrid')).findByText('/page-7/')).toBeVisible();
    fireEvent.keyDown(document.body, { key: 'C', ctrlKey: true, shiftKey: true });
    await waitFor(() => expect(writeText).toHaveBeenCalledOnce());
    expect(writeText.mock.calls[0]?.[0]).toBe('https://example.com/page-7/');
  });

  it('copies the check’s URLs, not a group the check no longer has, and drops that group from the address', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, 'clipboard', { configurable: true, value: { writeText } });
    sidecar();
    const { router } = await open(
      `${ISSUES}/empty-anchor-text?group=${encodeURIComponent('{"detail":null,"relatedUrlId":999}')}`,
    );
    expect(await screen.findByText('That group isn’t in this check now')).toBeVisible();
    await waitFor(() => expect(router.state.location.search).not.toHaveProperty('group'));
    const grid = screen.getByRole('treegrid');
    expect(within(grid).queryAllByRole('row', { expanded: true })).toHaveLength(0);
    fireEvent.click(screen.getByRole('button', { name: 'Copy URLs' }));
    await waitFor(() => expect(writeText).toHaveBeenCalledOnce());
    expect(String(writeText.mock.calls[0]?.[0]).split('\n')).toHaveLength(6);
  });

  it('says when the group the address names is past the groups it looks through, and drops it', async () => {
    const many: IssueGroup[] = Array.from({ length: LOCATE_GROUP_LIMIT + 1 }, (_, index) => ({
      detail: null,
      relatedUrlId: 1_000 + index,
      relatedUrl: `https://example.com/target-${index}/`,
      pages: 1,
      share: 1 / (LOCATE_GROUP_LIMIT + 1),
      likelyTemplate: false,
    }));
    sidecar({ groups: { ...ISSUE_GROUPS, 'empty-anchor-text': many } });
    const last = `{"detail":null,"relatedUrlId":${1_000 + LOCATE_GROUP_LIMIT}}`;
    const { router } = await open(`${ISSUES}/empty-anchor-text?group=${encodeURIComponent(last)}`);
    expect(await screen.findByText('That group couldn’t be found')).toBeVisible();
    expect(screen.getByText('It isn’t among the check’s 50,000 largest groups.')).toBeVisible();
    await waitFor(() => expect(router.state.location.search).not.toHaveProperty('group'));
  });

  it('copies nothing while the group the address names is still being looked for', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, 'clipboard', { configurable: true, value: { writeText } });
    const routes = issuesRoutes();
    const groups = routes.find(([method, pattern]) => method === 'POST' && pattern.source.includes('groups'));
    if (!groups) throw new Error('No groups route');
    // The grid's pages come; the search for the route's group doesn't.
    const hold: FakeRoute = [
      'POST',
      groups[1],
      (request, match) =>
        (request.body as { limit: number }).limit === LOCATE_GROUP_PAGE
          ? new Promise<Response>(() => undefined)
          : groups[2](request, match),
    ];
    setTransport(fakeSidecar([hold, ...routes]));
    await open(`${ISSUES}/empty-anchor-text?group=${encodeURIComponent('{"detail":null,"relatedUrlId":9}')}`);
    await within(await screen.findByRole('treegrid')).findByText('5');
    const copy = screen.getByRole('button', { name: 'Copy URLs' });
    expect(copy).toHaveAttribute('aria-busy', 'true');
    fireEvent.click(copy);
    fireEvent.keyDown(document.body, { key: 'C', ctrlKey: true, shiftKey: true });
    await new Promise((resolve) => setTimeout(resolve, 50));
    expect(writeText).not.toHaveBeenCalled();
  });

  it('hides a check, remembered, moving on to the next, with Undo', async () => {
    sidecar();
    const { router } = await open(`${ISSUES}/internal-client-error`);
    fireEvent.click(await screen.findByRole('button', { name: 'Hide this check' }));
    await waitFor(() => expect(path(router)).toBe(`${ISSUES}/empty-anchor-text`));
    expect(screen.queryByRole('option', { name: /Internal URL returns 4xx/ })).toBeNull();
    expect(screen.getByRole('button', { name: 'Show 1 hidden check' })).toBeVisible();
    fireEvent.click(await screen.findByRole('button', { name: 'Undo' }));
    expect(await screen.findByRole('option', { name: /Internal URL returns 4xx/ })).toBeVisible();
  });

  it('offers to analyse a crawl that never was, and says a running one is analysed when it finishes', async () => {
    const { analyse } = sidecar({ analysis: null, item: { analyzedAt: null, issues: null, status: 'stopped' } });
    await open(ISSUES);
    expect(await screen.findByText('This crawl hasn’t been analysed yet')).toBeVisible();
    fireEvent.click(screen.getByRole('button', { name: 'Analyse it' }));
    await waitFor(() => expect(analyse).toHaveBeenCalledOnce());

    sidecar({ analysis: null, item: { analyzedAt: null, issues: null, status: 'running' } });
    await open(ISSUES);
    expect(await screen.findByText('Issues come when the crawl finishes')).toBeVisible();
  });

  it('says when the issues are out of date, and offers to analyse again', async () => {
    const { analyse } = sidecar({
      analysis: { ...ISSUES_ANALYSIS, outOfDate: true, outOfDateReasons: ['crawlChanged'] },
      item: { outOfDate: true },
    });
    await open(`${ISSUES}/internal-client-error`);
    const banner = (await screen.findByText(/These issues are out of date/)).closest<HTMLElement>('[role="status"]');
    if (!banner) throw new Error('No banner');
    expect(within(banner).getByText('The crawl was continued after it was analysed.')).toBeVisible();
    fireEvent.click(within(banner).getByRole('button', { name: 'Analyse again' }));
    await waitFor(() => expect(analyse).toHaveBeenCalledOnce());
  });

  it('counts the checks with issues by severity under Issues in the sidebar, each opening the list filtered', async () => {
    sidecar({ item: { issues: { error: 1, warning: 0, notice: 2 } } });
    const { router } = await open(`${ISSUES}/internal-client-error`);
    const bySeverity = await screen.findByRole('list', { name: 'Issues by severity' });
    const notices = within(bySeverity).getByRole('link', { name: /Notices/ });
    expect(notices).toHaveTextContent('2');
    fireEvent.click(notices);
    await waitFor(() => expect(router.state.location.search).toMatchObject({ severity: ['notice'] }));
    expect(await screen.findByRole('radio', { name: /Notices/ })).toHaveAttribute('aria-checked', 'true');
  });
});
