// What merged lanes' contracts show on Advanced's Issues screen: a check of the site's own files names the files at
// fault, not the home page its issue is on (index X4); a check whose fix the platform owns says so quietly, with its
// reason as given (Shopify).
import { createMemoryHistory } from '@tanstack/react-router';
import { act, cleanup, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import type { Analysis, Check, CheckFile, Checks } from '../api/types';
import { setTransport } from '../api/transport';
import { App } from '../app/App';
import { createQueryClient } from '../app/queryClient';
import { createAppRouter } from '../app/router';
import { installKeyboard } from '../keyboard/registry';
import { TEST_CRAWL_ID, fakeSidecar } from '../test/fakeSidecar';
import { ISSUE_ROWS, ISSUES_ANALYSIS, ISSUES_CHECKS, issuesRoutes } from '../test/issuesSidecar';

const ISSUES = `/crawls/${TEST_CRAWL_ID}/issues`;
const SITE = 'https://example.com';

const fileCheck = (id: string, about: 'robotsTxt' | 'sitemap'): Check => ({
  id,
  category: 'links',
  severity: 'warning',
  title: id === 'sitemap-error' ? 'Sitemap can’t be read' : 'robots.txt names no sitemap',
  description: '',
  why: '',
  fix: '',
  learnMore: null,
  grouping: 'none',
  subject: 'url',
  references: 'none',
  reportedOnce: true,
  about,
  evidence: [],
});

const CHECKS: Checks = {
  ...ISSUES_CHECKS,
  checks: [
    ...ISSUES_CHECKS.checks,
    fileCheck('robots-txt-no-sitemap', 'robotsTxt'),
    fileCheck('sitemap-error', 'sitemap'),
  ],
};

function analysisWith(files: Record<string, CheckFile[] | null>, extra: Partial<Analysis> = {}): Analysis {
  return {
    ...ISSUES_ANALYSIS,
    counts: [
      ...ISSUES_ANALYSIS.counts,
      ...Object.entries(files).map(([checkId, named]) => ({ checkId, count: 1, files: named })),
    ],
    ...extra,
  };
}

const HOME_ROW = [{ id: 1, url: `${SITE}/`, detail: 'Read, but it names no sitemap' }];

async function open(path: string, analysis: Analysis, checks: Checks = CHECKS) {
  setTransport(
    fakeSidecar(
      issuesRoutes({
        checks,
        analysis,
        rows: { ...ISSUE_ROWS, 'robots-txt-no-sitemap': HOME_ROW, 'sitemap-error': HOME_ROW },
      }),
    ),
  );
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

describe('a check of the site’s own files', () => {
  it('names the robots.txt at fault in place of the home page, with the way to it, and no count of links', async () => {
    await open(
      `${ISSUES}/robots-txt-no-sitemap`,
      analysisWith({ 'robots-txt-no-sitemap': [{ url: `${SITE}/robots.txt`, sitemapId: null }] }),
    );
    const grid = await screen.findByRole('grid', { name: /robots\.txt names no sitemap/ });
    const row = await within(grid).findByRole('row', { name: /\/robots\.txt/ });
    expect(row).toHaveTextContent('Read, but it names no sitemap');
    expect(within(row).getByRole('link', { name: 'Open robots.txt' })).toHaveAttribute(
      'href',
      `/crawls/${TEST_CRAWL_ID}/robots/${encodeURIComponent(SITE)}`,
    );
    expect(headers(grid).some((header) => /Used on|Inlinks/.test(header))).toBe(false);
    expect(screen.queryByText(/most used first/)).toBeNull();
  });

  it('names the first sitemap and how many more, with the way to it in Sitemaps', async () => {
    await open(
      `${ISSUES}/sitemap-error`,
      analysisWith({
        'sitemap-error': [
          { url: `${SITE}/sitemap-2.xml`, sitemapId: 7 },
          { url: `${SITE}/sitemap-3.xml`, sitemapId: 8 },
          { url: `${SITE}/sitemap-4.xml`, sitemapId: 9 },
        ],
      }),
    );
    const grid = await screen.findByRole('grid', { name: /Sitemap can’t be read/ });
    const row = await within(grid).findByRole('row', { name: /sitemap-2\.xml/ });
    expect(row).toHaveTextContent('and 2 more');
    expect(within(row).getByRole('link', { name: 'Open in Sitemaps' })).toHaveAttribute(
      'href',
      `/crawls/${TEST_CRAWL_ID}/sitemaps/7`,
    );
  });

  it('shows the home page its issue is on where the analysis names no files', async () => {
    await open(`${ISSUES}/robots-txt-no-sitemap`, analysisWith({ 'robots-txt-no-sitemap': null }));
    const grid = await screen.findByRole('grid', { name: /robots\.txt names no sitemap/ });
    await within(grid).findByRole('row', { name: /example\.com\/?/ });
    expect(within(grid).queryByRole('link', { name: 'Open robots.txt' })).toBeNull();
    await waitFor(() => expect(headers(grid)).toContain('URL'));
  });
});

describe('a check whose fix the platform owns', () => {
  it('says “Set by Shopify” on its row and at the top of the check, with the reason as given', async () => {
    const reason = 'Shopify sets these cookies; a store can’t change them.';
    const analysis: Analysis = {
      ...ISSUES_ANALYSIS,
      counts: ISSUES_ANALYSIS.counts.map((count) =>
        count.checkId === 'internal-client-error' ? { ...count, platform: { name: 'Shopify', reason } } : count,
      ),
    };
    await open(`${ISSUES}/internal-client-error`, analysis, ISSUES_CHECKS);
    const option = await screen.findByRole('option', { name: /Internal URL returns 4xx/ });
    expect(option).toHaveTextContent('Set by Shopify');
    const article = screen.getByRole('article', { name: 'Internal URL returns 4xx' });
    expect(within(article).getByText('Set by Shopify')).toBeVisible();
    expect(within(article).getByText(reason)).toBeVisible();
  });
});
