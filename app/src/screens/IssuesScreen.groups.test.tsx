// A check's groups on Advanced's Issues screen (fix-it pass, links lane, #9): a check's only group opens by itself, a
// page's chevron inspects it, share bars show only where they compare something, and a group's row never repeats what
// one of its own pages says.
import { createMemoryHistory } from '@tanstack/react-router';
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
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

const headers = (grid: HTMLElement) =>
  within(grid)
    .getAllByRole('columnheader')
    .map((header) => header.textContent);

let uninstall: () => void = () => undefined;
beforeEach(() => {
  uninstall = installKeyboard();
});
afterEach(() => {
  cleanup();
  uninstall();
  setTransport(fakeSidecar([]));
});

describe('a check’s groups', () => {
  it('opens a check’s only group by itself, in the address, and leaves it closed once closed', async () => {
    const router = await open(`${ISSUES}/internal-client-error`);
    const grid = await screen.findByRole('treegrid');
    await waitFor(() => expect(router.state.location.search).toMatchObject({ group: { detail: '404 Not Found' } }));
    expect(await within(grid).findByRole('row', { expanded: true })).toHaveAccessibleName(/404 Not Found/);
    expect(await within(grid).findByRole('row', { name: /\/old\// })).toBeVisible();

    const group = within(grid).getByRole('row', { expanded: true });
    fireEvent.pointerDown(group.querySelector('[data-tree-toggle]') ?? group, { button: 0 });
    await waitFor(() => expect(router.state.location.search).not.toHaveProperty('group'));
    await new Promise((resolve) => setTimeout(resolve, 50));
    expect(router.state.location.search).not.toHaveProperty('group');
  });

  it('leaves a check of several groups with none open', async () => {
    const router = await open(`${ISSUES}/empty-anchor-text`);
    const grid = await screen.findByRole('treegrid');
    await within(grid).findAllByRole('row', { expanded: false });
    await new Promise((resolve) => setTimeout(resolve, 50));
    expect(router.state.location.search).not.toHaveProperty('group');
  });

  it('inspects a page from the chevron at its row’s end', async () => {
    const router = await open(`${ISSUES}/internal-client-error`);
    const grid = await screen.findByRole('treegrid');
    fireEvent.click(await within(grid).findByRole('button', { name: 'Inspect https://example.com/old/' }));
    await waitFor(() => expect(router.state.location.search).toMatchObject({ inspect: 10 }));
  });

  it('draws share bars only where groups differ: not for one group, nor for groups of one page each', async () => {
    await open(`${ISSUES}/internal-client-error`);
    const one = await screen.findByRole('treegrid');
    await within(one).findByRole('row', { expanded: true });
    expect(headers(one)).not.toContain('Share of issues');
    cleanup();

    await open(`${ISSUES}/empty-anchor-text`);
    const several = await screen.findByRole('treegrid');
    await waitFor(() => expect(headers(several)).toContain('Share of issues'));
    cleanup();

    const single = (ISSUE_GROUPS['empty-anchor-text'] ?? []).map((group) => ({ ...group, pages: 1, share: 0.5 }));
    await open(`${ISSUES}/empty-anchor-text`, { groups: { ...ISSUE_GROUPS, 'empty-anchor-text': single } });
    const ones = await screen.findByRole('treegrid');
    await within(ones).findAllByRole('row', { expanded: false });
    expect(headers(ones)).not.toContain('Share of issues');
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
    const grid = await screen.findByRole('treegrid');
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
    await open(`${ISSUES}/empty-anchor-text?group=${HOME_GROUP}`, {
      targets: { 'empty-anchor-text': { 2: named } },
    });
    const grid = await screen.findByRole('treegrid');
    const page = await within(grid).findByRole('row', { name: /\/page-2\// });
    await waitFor(() => expect(page).toHaveAttribute('aria-expanded', 'false'));
    fireEvent.pointerDown(page.querySelector('[data-tree-toggle]') ?? page, { button: 0 });
    await waitFor(() => expect(page).toHaveAttribute('aria-expanded', 'true'));
    await waitFor(() => expect(grid.querySelectorAll('[role="row"][aria-level="3"]').length).toBeGreaterThan(0));
    const under = [...grid.querySelectorAll<HTMLElement>('[role="row"][aria-level="3"]')];
    expect(under.map((row) => row.textContent).join(' ')).toContain('/contact/');
    expect(under.some((row) => /Names\s*\/\s*200/.test(row.textContent))).toBe(false);
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
    const grid = await screen.findByRole('treegrid');
    expect(await within(grid).findByText('No H1')).toBeVisible();
  });
});
