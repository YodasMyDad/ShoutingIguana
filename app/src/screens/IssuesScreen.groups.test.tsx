// A check's groups on Advanced's Issues screen (fix-it pass, links lane, #9; the 2026-11-19 UX round, A-06): a grouped
// check opens as a list, and "Grouped by …" shows its groups; the first of a few groups opens by itself; a page is
// inspected from its address, a link; share bars show only where they compare something; and a group's row never
// repeats what one of its own pages says.
import { createMemoryHistory } from '@tanstack/react-router';
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { IssueGroup } from '../api/grids';
import { setTransport } from '../api/transport';
import { App } from '../app/App';
import { createQueryClient } from '../app/queryClient';
import { createAppRouter } from '../app/router';
import { installKeyboard } from '../keyboard/registry';
import { TEST_CRAWL_ID, fakeSidecar } from '../test/fakeSidecar';
import {
  ISSUE_GROUPS,
  ISSUES_ANALYSIS,
  ISSUES_CHECKS,
  issuesRoutes,
  type IssuesSidecarOptions,
} from '../test/issuesSidecar';
import { forgetCheckPlaces } from './issues/issuesPlace';

const ISSUES = `/crawls/${TEST_CRAWL_ID}/issues`;
const HOME_GROUP = encodeURIComponent('{"detail":null,"relatedUrlId":1}');

async function open(path: string, options: IssuesSidecarOptions = {}) {
  setTransport(fakeSidecar(issuesRoutes(options)));
  const queryClient = createQueryClient();
  const router = createAppRouter(queryClient, createMemoryHistory({ initialEntries: [path] }));
  render(<App router={router} queryClient={queryClient} />);
  await act(() => router.load());
  return router;
}

/** The check's groups, which its "Grouped by …" shows: it opens as a list. */
async function showGroups() {
  fireEvent.click(await screen.findByRole('radio', { name: /^Grouped by/ }));
  return screen.findByRole('treegrid', { name: /grouped by/ });
}

const cellOf = (row: HTMLElement) => row.querySelector<HTMLElement>('[role="gridcell"]') ?? row;

const headers = (grid: HTMLElement) =>
  within(grid)
    .getAllByRole('columnheader')
    .map((header) => header.textContent);

/** As many groups of one page each, each of another URL. */
const groupsOf = (count: number): IssueGroup[] =>
  Array.from({ length: count }, (_, index) => ({
    detail: null,
    relatedUrlId: 100 + index,
    relatedUrl: `https://example.com/target-${index}/`,
    pages: 1,
    share: 1 / count,
    likelyTemplate: false,
  }));

let uninstall: () => void = () => undefined;
beforeEach(() => {
  uninstall = installKeyboard();
});
afterEach(() => {
  cleanup();
  uninstall();
  // The view chosen for a check is kept for the session: each test starts with none chosen.
  forgetCheckPlaces();
  setTransport(fakeSidecar([]));
});

describe('a check’s groups', () => {
  it('opens a grouped check as a list, naming no group, with the way to its groups beside it', async () => {
    const router = await open(`${ISSUES}/internal-client-error`);
    const list = await screen.findByRole('treegrid', { name: 'Internal URL returns 4xx: affected URLs' });
    expect(await within(list).findByRole('row', { name: /\/old\// })).toBeVisible();
    const views = screen.getByRole('radiogroup', { name: 'How the rows are shown' });
    expect(within(views).getByRole('radio', { name: 'List' })).toHaveAttribute('aria-checked', 'true');
    expect(within(views).getByRole('radio', { name: 'Grouped by cause' })).toHaveAttribute('aria-checked', 'false');
    await new Promise((resolve) => setTimeout(resolve, 50));
    expect(router.state.location.search).not.toHaveProperty('group');
    expect(screen.queryByRole('treegrid', { name: /grouped by/ })).toBeNull();
  });

  it('opens a check’s only group by itself once its groups are shown, in the address, and leaves it closed once closed', async () => {
    const router = await open(`${ISSUES}/internal-client-error`);
    const grid = await showGroups();
    await waitFor(() => expect(router.state.location.search).toMatchObject({ group: { detail: '404 Not Found' } }));
    expect(await within(grid).findByRole('row', { expanded: true })).toHaveAccessibleName(/404 Not Found/);
    expect(await within(grid).findByRole('row', { name: /\/old\// })).toBeVisible();

    const group = within(grid).getByRole('row', { expanded: true });
    fireEvent.pointerDown(group.querySelector('[data-tree-toggle]') ?? group, { button: 0 });
    await waitFor(() => expect(router.state.location.search).not.toHaveProperty('group'));
    await new Promise((resolve) => setTimeout(resolve, 50));
    expect(router.state.location.search).not.toHaveProperty('group');
    // Its groups stay in view, closed: the view chosen isn't the route's group.
    expect(screen.getByRole('radio', { name: 'Grouped by cause' })).toHaveAttribute('aria-checked', 'true');
    expect(within(grid).getByRole('row', { expanded: false })).toHaveAccessibleName(/404 Not Found/);
  });

  it('opens the first of a check’s few groups by itself, up to 20 of them', async () => {
    const two = await open(`${ISSUES}/empty-anchor-text`);
    const grid = await showGroups();
    // The largest, the home page's, of the fixture's two.
    await waitFor(() => expect(two.state.location.search).toMatchObject({ group: { detail: null, relatedUrlId: 1 } }));
    expect(await within(grid).findByRole('row', { name: /\/page-2\// })).toBeVisible();
    cleanup();
    forgetCheckPlaces();

    const twenty = await open(`${ISSUES}/empty-anchor-text`, {
      groups: { ...ISSUE_GROUPS, 'empty-anchor-text': groupsOf(20) },
    });
    await showGroups();
    await waitFor(() =>
      expect(twenty.state.location.search).toMatchObject({ group: { detail: null, relatedUrlId: 100 } }),
    );
  });

  it('leaves a check of more than 20 groups with none open', async () => {
    const router = await open(`${ISSUES}/empty-anchor-text`, {
      groups: { ...ISSUE_GROUPS, 'empty-anchor-text': groupsOf(21) },
    });
    const grid = await showGroups();
    await within(grid).findAllByRole('row', { expanded: false });
    await new Promise((resolve) => setTimeout(resolve, 50));
    expect(router.state.location.search).not.toHaveProperty('group');
    expect(within(grid).queryByRole('row', { expanded: true })).toBeNull();
  });

  it('shows the groups of a check the address names a group of, without asking', async () => {
    await open(`${ISSUES}/empty-anchor-text?group=${HOME_GROUP}`);
    expect(await screen.findByRole('treegrid', { name: /grouped by linked URL/ })).toBeVisible();
    expect(screen.getByRole('radio', { name: 'Grouped by linked URL' })).toHaveAttribute('aria-checked', 'true');
  });

  it('inspects a page from its address, a link, with Enter or with a double-click: no button at its row’s end', async () => {
    const router = await open(`${ISSUES}/internal-client-error`);
    const grid = await showGroups();
    const old = await within(grid).findByRole('row', { name: /\/old\// });
    // The chevron that inspected the row is gone: nothing is named "Inspect …".
    expect(within(grid).queryByRole('button', { name: /^Inspect / })).toBeNull();
    const address = within(old).getByRole('button', { name: 'https://example.com/old/' });
    expect(address).toHaveClass('si-url__go');
    fireEvent.click(address);
    await waitFor(() => expect(router.state.location.search).toMatchObject({ inspect: 10 }));

    const gone = within(grid).getByRole('row', { name: /\/gone\// });
    fireEvent.pointerDown(cellOf(gone), { button: 0 });
    act(() => grid.focus());
    fireEvent.keyDown(grid, { key: 'Enter' });
    await waitFor(() => expect(router.state.location.search).toMatchObject({ inspect: 11 }));

    fireEvent.doubleClick(cellOf(within(grid).getByRole('row', { name: /\/old\// })));
    await waitFor(() => expect(router.state.location.search).toMatchObject({ inspect: 10 }));
  });

  it('draws share bars only where groups differ: not for one group, nor for groups of one page each', async () => {
    // A panel wide enough for the bar beside the words (jsdom lays nothing out: every element is 0 px wide).
    vi.spyOn(HTMLElement.prototype, 'clientWidth', 'get').mockReturnValue(1200);
    await open(`${ISSUES}/internal-client-error`);
    const one = await showGroups();
    await within(one).findByRole('row', { expanded: true });
    expect(headers(one)).not.toContain('Share of issues');
    cleanup();
    forgetCheckPlaces();

    await open(`${ISSUES}/empty-anchor-text`);
    const several = await showGroups();
    await waitFor(() => expect(headers(several)).toContain('Share of issues'));
    cleanup();
    forgetCheckPlaces();

    const single = (ISSUE_GROUPS['empty-anchor-text'] ?? []).map((group) => ({ ...group, pages: 1, share: 0.5 }));
    await open(`${ISSUES}/empty-anchor-text`, { groups: { ...ISSUE_GROUPS, 'empty-anchor-text': single } });
    const ones = await showGroups();
    await within(ones).findByRole('row', { name: /\/page-2\// });
    expect(headers(ones)).toEqual(['Linked URL', 'Pages']);
  });

  it('leaves the share bars out of a narrow panel, where the words and the counts need the room', async () => {
    vi.spyOn(HTMLElement.prototype, 'clientWidth', 'get').mockReturnValue(700);
    await open(`${ISSUES}/empty-anchor-text`);
    const narrow = await showGroups();
    await within(narrow).findByRole('row', { name: /\/page-2\// });
    expect(headers(narrow)).toEqual(['Linked URL', 'Pages']);
    // And the columns it keeps are no wider than the panel.
    const widths = (narrow.style.getPropertyValue('--grid-template').match(/\d+(?=px)/g) ?? []).map(Number);
    expect(widths.reduce((sum, width) => sum + width, 0)).toBeLessThanOrEqual(700);
  });

  it('names a group by the text its pages share, not by one of its own pages', async () => {
    const shared: IssueGroup[] = [
      {
        detail: null,
        relatedUrlId: 2,
        relatedUrl: 'https://example.com/page-2/',
        sharedText: 'Garden chairs',
        pages: 2,
        share: 1,
        likelyTemplate: false,
      },
    ];
    await open(`${ISSUES}/empty-anchor-text`, { groups: { ...ISSUE_GROUPS, 'empty-anchor-text': shared } });
    const grid = await showGroups();
    await within(grid).findByText('“Garden chairs”');
    const group = grid.querySelector<HTMLElement>('[role="row"][aria-level="1"]');
    expect(group).toHaveTextContent('“Garden chairs”');
    expect(group).not.toHaveTextContent('/page-2/');
  });

  it('opens no page under a group of the URL it names when that’s the only URL it names', async () => {
    await open(`${ISSUES}/empty-anchor-text?group=${HOME_GROUP}`);
    const grid = await screen.findByRole('treegrid');
    const page = await within(grid).findByRole('row', { name: /\/page-2\// });
    // The page's line says its link to the group's URL; there's no other URL to open it to.
    await waitFor(() => expect(page).not.toHaveAttribute('aria-expanded'));
    expect(page.querySelector('[data-tree-toggle]')).toBeNull();
  });

  it('says what a page under a group of its URL names besides that URL, when it names others', async () => {
    const named = {
      urlId: 2,
      total: 2,
      targets: [
        {
          id: 1,
          url: 'https://example.com/',
          statusCode: 200,
          fetched: true,
          outcome: 'success' as const,
          kind: 'hyperlink' as const,
          text: null,
          position: 'header' as const,
        },
        {
          id: 30,
          url: 'https://example.com/contact/',
          statusCode: 200,
          fetched: true,
          outcome: 'success' as const,
          kind: 'hyperlink' as const,
          text: null,
          position: 'footer' as const,
        },
      ],
    };
    const router = await open(`${ISSUES}/empty-anchor-text?group=${HOME_GROUP}`, {
      targets: { 'empty-anchor-text': { 2: named } },
    });
    const grid = await screen.findByRole('treegrid');
    const page = await within(grid).findByRole('row', { name: /\/page-2\// });
    await waitFor(() => expect(page).toHaveAttribute('aria-expanded', 'false'));
    // One more URL than the group's own: said on the row, and a press on it opens the row.
    fireEvent.pointerDown(within(page).getByText('+1 more'), { button: 0 });
    await waitFor(() => expect(page).toHaveAttribute('aria-expanded', 'true'));
    expect(within(page).getByText('Hide 1')).toBeVisible();
    await waitFor(() => expect(grid.querySelectorAll('[role="row"][aria-level="3"]').length).toBeGreaterThan(0));
    const under = [...grid.querySelectorAll<HTMLElement>('[role="row"][aria-level="3"]')];
    // The other URL alone, never the group's again: led by how the page names it, then the URL (a link), its status,
    // what the link says and where on the page it is.
    expect(under.map((row) => row.textContent)).toEqual([
      'Links tohttps://example.com/contact//contact/200No textFooter',
    ]);
    expect(under[0]).not.toHaveTextContent('Names');
    fireEvent.click(within(under[0]!).getByRole('button', { name: 'https://example.com/contact/' }));
    await waitFor(() => expect(router.state.location.search).toMatchObject({ inspect: 30 }));
  });
});

describe('a check grouped by detail with no detail', () => {
  it('names the group of pages without an H1 “No H1”', async () => {
    const checks = {
      ...ISSUES_CHECKS,
      checks: [
        ...ISSUES_CHECKS.checks,
        {
          ...ISSUES_CHECKS.checks[0]!,
          id: 'h1-missing',
          title: 'Missing H1',
        },
      ],
    };
    await open(`${ISSUES}/h1-missing`, {
      checks,
      analysis: { ...ISSUES_ANALYSIS, counts: [...ISSUES_ANALYSIS.counts, { checkId: 'h1-missing', count: 3 }] },
      groups: {
        'h1-missing': [
          { detail: null, relatedUrlId: null, relatedUrl: null, pages: 2, share: 1, likelyTemplate: false },
          { detail: 'Empty', relatedUrlId: null, relatedUrl: null, pages: 1, share: 1, likelyTemplate: false },
        ],
      },
    });
    const grid = await showGroups();
    expect(await within(grid).findByText('No H1')).toBeVisible();
  });
});
