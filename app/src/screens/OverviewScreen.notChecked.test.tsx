import { createMemoryHistory } from '@tanstack/react-router';
import { act, render, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { setTransport } from '../api/transport';
import type { Analysis } from '../api/types';
import { App } from '../app/App';
import { createQueryClient } from '../app/queryClient';
import { createAppRouter } from '../app/router';
import { liveStore } from '../live/liveStore';
import { TEST_CRAWL_ID, fakeSidecar } from '../test/fakeSidecar';
import { ISSUES_ANALYSIS } from '../test/issuesSidecar';
import { overviewRoutes } from '../test/overviewSidecar';
import { skippedReasonWords } from './overview/answer';

const OVERVIEW = `/crawls/${TEST_CRAWL_ID}/overview`;

async function open(analysis: Analysis) {
  setTransport(fakeSidecar(overviewRoutes({ analysis })));
  const queryClient = createQueryClient();
  const router = createAppRouter(queryClient, createMemoryHistory({ initialEntries: [OVERVIEW] }));
  render(<App router={router} queryClient={queryClient} />);
  await act(() => router.load());
  await waitFor(() => expect(document.querySelector('[aria-label="Loading the overview"]')).toBeNull());
  return router;
}

const main = () => within(document.getElementById('main') ?? document.body);
const hrefOf = (link: HTMLElement) => new URL((link as HTMLAnchorElement).href);

afterEach(() => {
  liveStore.reset();
  setTransport(fakeSidecar([]));
});

const OLD_VERSION =
  '2 pages in this crawl were fetched by an earlier version, which didn’t record what this check needs. Crawl again to check it.';

/** Two checks skipped for the same reason, whose fix is a fresh crawl. */
const NEEDS_A_CRAWL: Analysis = {
  ...ISSUES_ANALYSIS,
  notes: ['2 checks weren’t run: 2 pages in this crawl were fetched by an earlier version. Crawl again to run them.'],
  counts: ISSUES_ANALYSIS.counts.filter((count) => count.checkId !== 'title-too-long'),
  skipped: [
    { checkId: 'orphan-page', reason: { code: 'old-crawl', text: OLD_VERSION } },
    { checkId: 'title-too-long', reason: { code: 'old-crawl', text: OLD_VERSION } },
  ],
};

// The audit of 2026-11-19, H-04 and H-11: "Crawl again to check it." was plain text in three places, the control two
// screens away; the card was one sentence of 22 names, then the reason in the singular, and a banner said it all again.
describe('Overview’s “Not checked on this crawl”', () => {
  it('leads each group with how many checks and why, in the plural, and says whose earlier version', async () => {
    await open(NEEDS_A_CRAWL);
    const card = within(await main().findByRole('region', { name: 'Not checked on this crawl' }));
    expect(
      card.getByText(
        '2 pages in this crawl were fetched by an earlier version of Shouting Iguana, which didn’t record what these checks need. Crawl again to check them.',
      ),
    ).toBeVisible();
    expect(card.getByText('2 checks')).toBeVisible();
    // The names are a list of links, each to its check.
    const names = within(card.getByRole('list', { name: 'The 2 checks' })).getAllByRole('link');
    expect(names.map((link) => link.textContent)).toEqual(['Orphan page in a sitemap', 'Title too long']);
    expect(hrefOf(names[0]!).pathname).toBe(`/crawls/${TEST_CRAWL_ID}/issues/orphan-page`);
  });

  it('carries “Crawl again…” as a control, with this crawl’s settings, when that is what the reason asks for', async () => {
    await open(NEEDS_A_CRAWL);
    const card = within(await main().findByRole('region', { name: 'Not checked on this crawl' }));
    const again = card.getByRole('link', { name: 'Crawl again…' });
    expect(hrefOf(again).pathname).toBe('/new');
    expect(hrefOf(again).searchParams.get('from')).toBe(TEST_CRAWL_ID);
  });

  it('offers no crawl where a fresh crawl wouldn’t help', async () => {
    await open(ISSUES_ANALYSIS);
    const card = within(await main().findByRole('region', { name: 'Not checked on this crawl' }));
    expect(card.getByText('No sitemap could be read.')).toBeVisible();
    expect(card.queryByRole('link', { name: 'Crawl again…' })).toBeNull();
  });

  it('counts the skipped checks in its header, as a link to the first of them', async () => {
    await open(NEEDS_A_CRAWL);
    const card = within(await main().findByRole('region', { name: 'Not checked on this crawl' }));
    const count = card.getByRole('link', { name: '2 checks skipped' });
    expect(hrefOf(count).pathname).toBe(`/crawls/${TEST_CRAWL_ID}/issues/orphan-page`);
  });

  it('is the one place that says it: no banner repeats the notes the card already explains', async () => {
    await open(NEEDS_A_CRAWL);
    await main().findByRole('region', { name: 'Not checked on this crawl' });
    expect(
      main().queryByText('Some checks didn’t run on this crawl, so it may have more issues than these.'),
    ).toBeNull();
  });

  it('keeps the banner when the analysis left more notes than the card has reasons', async () => {
    await open({ ...NEEDS_A_CRAWL, notes: [...NEEDS_A_CRAWL.notes, 'robots.txt couldn’t be read for 1 host.'] });
    await main().findByRole('region', { name: 'Not checked on this crawl' });
    expect(
      main().getByText('Some checks didn’t run on this crawl, so it may have more issues than these.'),
    ).toBeVisible();
  });
});

describe('a skipped group’s reason', () => {
  it('reads in the plural for several checks, and as the engine wrote it for one', () => {
    expect(skippedReasonWords(OLD_VERSION, 1)).toBe(
      '2 pages in this crawl were fetched by an earlier version of Shouting Iguana, which didn’t record what this check needs. Crawl again to check it.',
    );
    expect(skippedReasonWords('No sitemap could be read.', 3)).toBe('No sitemap could be read.');
    expect(skippedReasonWords('An earlier version of Shouting Iguana made it.', 2)).toBe(
      'An earlier version of Shouting Iguana made it.',
    );
  });
});
