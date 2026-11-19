// "Crawl again" is a control wherever the words recommend it (the audit of 2026-11-19, H-04): on a skipped check's
// page and in the notes banner, as in Overview's "Not checked" card (OverviewScreen.test.tsx).
import { createMemoryHistory } from '@tanstack/react-router';
import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it } from 'vitest';
import { setTransport } from '../../api/transport';
import type { Analysis } from '../../api/types';
import { App } from '../../app/App';
import { resetOverlaysForTests } from '../../app/overlays';
import { createQueryClient } from '../../app/queryClient';
import { createAppRouter } from '../../app/router';
import { TEST_CRAWL_ID, fakeSidecar } from '../../test/fakeSidecar';
import { ISSUES_ANALYSIS, issuesRoutes } from '../../test/issuesSidecar';
import { asksForACrawl } from './answer';

const CRAWL = `/crawls/${TEST_CRAWL_ID}`;
const NEEDS_A_CRAWL = 'This crawl doesn’t hold what this check needs. Crawl again to check it.';

afterEach(() => {
  resetOverlaysForTests();
  setTransport(fakeSidecar([]));
});

async function open(path: string, analysis: Analysis) {
  setTransport(fakeSidecar(issuesRoutes({ analysis })));
  const queryClient = createQueryClient();
  const router = createAppRouter(queryClient, createMemoryHistory({ initialEntries: [path] }));
  render(<App router={router} queryClient={queryClient} />);
  await act(() => router.load());
  return router;
}

const main = () => screen.getByRole('main');

describe('a reason that recommends a fresh crawl', () => {
  it('is told from one that doesn’t', () => {
    expect(asksForACrawl(NEEDS_A_CRAWL)).toBe(true);
    expect(asksForACrawl('No sitemap could be read.')).toBe(false);
    expect(asksForACrawl(null)).toBe(false);
  });

  it('carries "Crawl again…" on the skipped check’s page, to New crawl with this crawl’s settings', async () => {
    const router = await open(`${CRAWL}/issues/orphan-page`, {
      ...ISSUES_ANALYSIS,
      notes: [],
      skipped: [{ checkId: 'orphan-page', reason: { code: 'needs-crawl', text: NEEDS_A_CRAWL } }],
    });
    const panel = (await screen.findByText('This check was skipped on this crawl')).closest('.si-empty');
    if (!(panel instanceof HTMLElement)) throw new Error('no skipped panel');
    await userEvent.click(within(panel).getByRole('link', { name: 'Crawl again…' }));
    await waitFor(() => expect(router.state.location.pathname).toBe('/new'));
    expect(router.state.location.search).toEqual({ from: TEST_CRAWL_ID });
  });

  it('carries none where the reason is something a crawl wouldn’t change', async () => {
    await open(`${CRAWL}/issues/orphan-page`, { ...ISSUES_ANALYSIS, notes: [] });
    await screen.findByText('This check was skipped on this crawl');
    expect(within(main()).queryByRole('link', { name: 'Crawl again…' })).not.toBeInTheDocument();
  });

  it('carries it in the notes banner above Issues when a note says so, and not otherwise', async () => {
    await open(`${CRAWL}/issues`, {
      ...ISSUES_ANALYSIS,
      notes: ['254 pages were fetched by an earlier version. Crawl again to check them.'],
    });
    const banner = (await screen.findByText(/Some checks didn’t run on this crawl/)).closest('.si-banner');
    if (!(banner instanceof HTMLElement)) throw new Error('no notes banner');
    expect(within(banner).getByRole('link', { name: 'Crawl again…' })).toHaveAttribute(
      'href',
      `/new?from=${TEST_CRAWL_ID}`,
    );
  });

  it('and the banner of a note that doesn’t has no such control', async () => {
    await open(`${CRAWL}/issues`, ISSUES_ANALYSIS);
    await screen.findByText(/Some checks didn’t run on this crawl/);
    expect(within(main()).queryByRole('link', { name: 'Crawl again…' })).not.toBeInTheDocument();
  });
});
