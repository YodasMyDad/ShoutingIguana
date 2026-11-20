import { createMemoryHistory } from '@tanstack/react-router';
import { act, render, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import type { GridFilter } from '../api/grids';
import { setTransport } from '../api/transport';
import { App } from '../app/App';
import { createQueryClient } from '../app/queryClient';
import { createAppRouter } from '../app/router';
import { liveStore } from '../live/liveStore';
import { TEST_CRAWL_ID, fakeSidecar } from '../test/fakeSidecar';
import { overviewRoutes, type OverviewSidecarOptions } from '../test/overviewSidecar';

const OVERVIEW = `/crawls/${TEST_CRAWL_ID}/overview`;

async function open(options: OverviewSidecarOptions = {}) {
  setTransport(fakeSidecar(overviewRoutes(options)));
  const queryClient = createQueryClient();
  const router = createAppRouter(queryClient, createMemoryHistory({ initialEntries: [OVERVIEW] }));
  render(<App router={router} queryClient={queryClient} />);
  await act(() => router.load());
  await waitFor(() => expect(document.querySelector('[aria-label="Loading the overview"]')).toBeNull());
}

const main = () => within(document.getElementById('main') ?? document.body);
const filtersOf = (link: HTMLElement): GridFilter[] =>
  JSON.parse(new URL((link as HTMLAnchorElement).href).searchParams.get('filter') ?? '[]') as GridFilter[];

const SITE_PAGES: GridFilter[] = [
  { column: 'isInternal', op: 'is', value: true },
  { column: 'type', op: 'in', values: ['page'] },
  { column: 'source', op: 'notIn', values: ['siteVersion'] },
];
const ANSWERED: GridFilter[] = [
  ...SITE_PAGES,
  { column: 'statusClass', op: 'in', values: ['2xx'] },
  { column: 'wordCount', op: 'isNotEmpty' },
  { column: 'url', op: 'notContains', value: '/cdn-cgi/' },
];

afterEach(() => {
  liveStore.reset();
  setTransport(fakeSidecar([]));
});

// The audit of 2026-11-19, H-07 and H-19: the counts in the cards' headers and the crawl's line named lists and
// weren't links; and five figure cards pushed Fix first off the first screen of a 720 px window.
describe('Overview’s figures that name a list', () => {
  it('open that list from each card’s header', async () => {
    await open();
    const indexability = within(await main().findByRole('region', { name: 'Indexability' }));
    const pageUrls = await indexability.findByRole('link', { name: /page URLs? on the site$/ });
    expect(filtersOf(pageUrls)).toEqual([...SITE_PAGES, { column: 'fetched', op: 'is', value: true }]);
    const depth = within(main().getByRole('region', { name: 'Pages by depth' }));
    // The header's count is the card's first link; the columns under it have their own.
    expect(filtersOf((await depth.findAllByRole('link', { name: /^[\d,]+ pages?$/ }))[0]!)).toEqual(ANSWERED);
    const response = within(main().getByRole('region', { name: 'Response times' }));
    expect(filtersOf((await response.findAllByRole('link', { name: /^[\d,]+ pages?$/ }))[0]!)).toEqual(ANSWERED);
  });

  it('open the pages read from the crawl’s own line', async () => {
    await open();
    const heading = await main().findByRole('heading', { level: 2, name: 'The crawl' });
    const facts = within(heading.parentElement!.querySelector('p')!);
    expect(filtersOf(facts.getByRole('link', { name: '265 pages' }))).toEqual(ANSWERED);
  });
});

describe('Overview’s totals', () => {
  it('are the first line of “The crawl” once there is an answer, each still opening its URLs', async () => {
    await open();
    const crawl = main().getByRole('region', { name: 'The crawl' });
    const totals = within(await within(crawl).findByRole('region', { name: 'Totals' }));
    expect(filtersOf(totals.getByRole('link', { name: /^Crawled/ }))).toEqual([
      { column: 'fetched', op: 'is', value: true },
    ]);
    expect(totals.getAllByRole('link')).toHaveLength(5);
    // Below the answer: Fix first comes before them.
    const fix = main().getByRole('region', { name: 'Fix first' });
    expect(fix.compareDocumentPosition(crawl) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it('lead the screen while there is no answer to lead with', async () => {
    await open({ item: { analyzedAt: null, issues: null }, analysis: null });
    const totals = await main().findByRole('region', { name: 'Totals' });
    const crawl = main().getByRole('region', { name: 'The crawl' });
    expect(crawl.contains(totals)).toBe(false);
    expect(totals.compareDocumentPosition(crawl) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });
});
