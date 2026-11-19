import { createMemoryHistory } from '@tanstack/react-router';
import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it } from 'vitest';
import { setTransport } from '../api/transport';
import type { CrawlListItem } from '../api/types';
import { App } from '../app/App';
import { createQueryClient } from '../app/queryClient';
import { createAppRouter } from '../app/router';
import { liveStore } from '../live/liveStore';
import { TEST_CRAWL_ID, fakeSidecar, json, standardRoutes, testCrawlItem } from '../test/fakeSidecar';
import { librarySort, sortSites } from './home/library';
import { siteGroups } from '../simple/home/sites';

async function open(items: CrawlListItem[]) {
  setTransport(fakeSidecar([['GET', /^\/api\/crawls$/, () => json(items)], ...standardRoutes()]));
  const router = createAppRouter(createQueryClient(), createMemoryHistory({ initialEntries: ['/'] }));
  render(<App router={router} queryClient={createQueryClient()} />);
  await act(() => router.load());
  return router;
}

const rowOf = async (name: string) => (await screen.findByRole('link', { name })).closest('tr')!;
const hrefOf = (link: HTMLElement) => new URL((link as HTMLAnchorElement).href);

const GARDEN = testCrawlItem({
  id: 'aaaaaaaaaaaaaaaa',
  site: 'garden.example',
  startUrl: 'https://garden.example/',
  fileName: 'garden-b.sicrawl',
  startedAt: '2026-11-17T06:49:00Z',
  fetchedUrlCount: 4_362,
  crawledUrlCount: 4_362,
  pageCount: 260,
  issues: { error: 3, warning: 4, notice: 22 },
  score: { value: 85, band: 'good', partial: false },
});
const GARDEN_OLDER = testCrawlItem({
  id: 'bbbbbbbbbbbbbbbb',
  site: 'garden.example',
  startUrl: 'https://garden.example/',
  fileName: 'garden-a.sicrawl',
  startedAt: '2026-11-10T06:49:00Z',
});
const SHOP = testCrawlItem({
  id: 'cccccccccccccccc',
  site: 'shop.example',
  startUrl: 'https://shop.example/',
  fileName: 'shop.sicrawl',
  startedAt: '2026-11-18T09:00:00Z',
  fetchedUrlCount: 253,
  crawledUrlCount: 253,
  pageCount: 80,
  issues: { error: 0, warning: 1, notice: 9 },
  score: { value: 97, band: 'veryGood', partial: false },
});

afterEach(() => {
  liveStore.reset();
  setTransport(fakeSidecar([]));
});

// The audit of 2026-11-19, H-05: only the site's name (95 to 177 px of an 1,800 px row) was a link; the counts named
// three lists you couldn't open from Home; the columns didn't sort; and an older crawl's second line was its file name.
describe('the library’s rows', () => {
  it('open their crawl from a click anywhere on the row', async () => {
    const router = await open([testCrawlItem()]);
    const row = await rowOf('example.com');
    // The Started cell: nothing of its own to press.
    await userEvent.click(within(row).getAllByRole('cell')[6]!);
    await waitFor(() => expect(router.state.location.pathname).toMatch(new RegExp(`^/crawls/${TEST_CRAWL_ID}/`)));
  });

  it('leave a press on a control inside the row to that control', async () => {
    const router = await open([testCrawlItem()]);
    const row = await rowOf('example.com');
    await userEvent.click(within(row).getByRole('button', { name: /^Actions for/ }));
    expect(await screen.findByRole('menu')).toBeVisible();
    expect(router.state.location.pathname).toBe('/');
  });

  it('link each severity’s count to Issues at that severity, and leave a count of none as text', async () => {
    await open([SHOP]);
    const row = within(await rowOf('shop.example'));
    const warnings = row.getByRole('link', { name: /^1 warning$/ });
    expect(hrefOf(warnings).pathname).toBe(`/crawls/${SHOP.id}/issues`);
    expect(hrefOf(warnings).searchParams.get('severity')).toBe('["warning"]');
    expect(hrefOf(row.getByRole('link', { name: /^9 notices$/ })).searchParams.get('severity')).toBe('["notice"]');
    expect(row.queryByRole('link', { name: /errors?$/ })).toBeNull();
  });

  it('link the URLs to the URL grid, the pages to the pages read, and the score to how it’s worked out', async () => {
    await open([GARDEN]);
    const row = within(await rowOf('garden.example'));
    const urls = row.getByRole('link', { name: '4,362 URLs' });
    expect(hrefOf(urls).pathname).toBe(`/crawls/${GARDEN.id}/urls`);
    expect(hrefOf(urls).searchParams.get('filter')).toContain('"fetched"');
    const pages = row.getByRole('link', { name: '260 pages' });
    expect(hrefOf(pages).searchParams.get('filter')).toContain('"wordCount"');
    const score = row.getByRole('link', { name: /^85/ });
    expect(hrefOf(score).pathname).toBe(`/crawls/${GARDEN.id}/overview`);
    expect(hrefOf(score).searchParams.get('score')).toBe('1');
  });

  it('name an older crawl by where it started, not by its file', async () => {
    await open([GARDEN, GARDEN_OLDER]);
    const row = await rowOf('garden.example');
    await userEvent.click(within(row).getByRole('button', { name: /1 older crawl/ }));
    const older = (await screen.findByRole('link', { name: /^garden\.example, 10 Nov 2026/ })).closest('tr')!;
    expect(older).toHaveTextContent('https://garden.example/');
    expect(older).not.toHaveTextContent('garden-a.sicrawl');
  });
});

describe('the library’s columns', () => {
  it('sort by Site, URLs, Score and Started from their headers, newest first to begin with', async () => {
    await open([GARDEN, SHOP]);
    const library = within(await screen.findByRole('region', { name: 'Library' }));
    const names = () =>
      library
        .getAllByRole('row')
        .slice(1)
        .map((row) => within(row).getAllByRole('link')[0]!.textContent);
    await screen.findByRole('link', { name: 'shop.example' });
    expect(names()).toEqual(['shop.example', 'garden.example']);
    const started = library.getByRole('columnheader', { name: /Started/ });
    expect(started).toHaveAttribute('aria-sort', 'descending');

    await userEvent.click(library.getByRole('button', { name: /^Site/ }));
    expect(names()).toEqual(['garden.example', 'shop.example']);
    expect(library.getByRole('columnheader', { name: /Site/ })).toHaveAttribute('aria-sort', 'ascending');
    expect(started).not.toHaveAttribute('aria-sort', 'descending');

    // A number's first press is largest first; a second turns it round.
    await userEvent.click(library.getByRole('button', { name: /^URLs/ }));
    expect(names()).toEqual(['garden.example', 'shop.example']);
    await userEvent.click(library.getByRole('button', { name: /^URLs/ }));
    expect(names()).toEqual(['shop.example', 'garden.example']);

    await userEvent.click(library.getByRole('button', { name: /^Score/ }));
    expect(names()).toEqual(['shop.example', 'garden.example']);
  });

  it('sort the sites by their latest crawl, a crawl with no score or count last either way', () => {
    const sites = siteGroups([GARDEN, SHOP, testCrawlItem({ score: null })], []).sites;
    const by = (key: Parameters<typeof librarySort>[1]) => {
      const sort = librarySort({ key: 'started', descending: true }, key);
      return sortSites(sites, sort).map((group) => group.latest.site);
    };
    expect(by('score')).toEqual(['shop.example', 'garden.example', 'example.com']);
    expect(sortSites(sites, { key: 'score', descending: false }).map((group) => group.latest.site)).toEqual([
      'garden.example',
      'shop.example',
      'example.com',
    ]);
    expect(by('site')).toEqual(['example.com', 'garden.example', 'shop.example']);
  });
});
