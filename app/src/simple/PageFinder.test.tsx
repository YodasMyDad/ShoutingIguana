// "What's wrong with this page?" on What to fix (the UX audit of 2026-11-19, S-06): a page found by part of its
// address or title, then everything the check found on it, each a link to that problem.
import { createMemoryHistory } from '@tanstack/react-router';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { setTransport } from '../api/transport';
import { App } from '../app/App';
import { createAppRouter } from '../app/router';
import { liveStore } from '../live/liveStore';
import { replaceSettings } from '../settings/settingsStore';
import { TEST_CRAWL_ID, fakeSidecar, json, type FakeRoute } from '../test/fakeSidecar';
import { createQueryClientWithoutBackoff } from '../test/queryClient';
import { GARDEN_ANALYSIS, gardenItem, problemRoutes, simpleRoutes } from '../test/simpleSidecar';
import { TABLE_STUBBED } from './tableStubbed';

const FIX = `/crawls/${TEST_CRAWL_ID}/issues`;
const SITE = 'https://www.gardenfurnituresets.co.uk';

const hit = (id: number, path: string, title: string | null, isInternal = true) => ({
  id,
  url: isInternal ? `${SITE}${path}` : path,
  title,
  isInternal,
  statusCode: 200,
  outcome: 'success',
  rank: 'contains',
  matchedIn: 'url',
});

/** The search finds two of the site's pages and another site's address; the first page has three issues, two of one check. */
const ROUTES: FakeRoute[] = [
  [
    'POST',
    /\/search$/,
    (request) => {
      const { text } = request.body as { text: string };
      const results =
        text === 'rattan'
          ? [
              hit(107, '/blog/best-rattan-patio-sets/', 'Best Rattan Patio Sets - Hand Picked by Experts'),
              hit(15, '/rattan-furniture/', null),
              hit(900, 'https://cdn.example.net/rattan.jpg', null, false),
            ]
          : [];
      return json({ results, matching: results.length, total: 4_362 });
    },
  ],
  [
    'POST',
    /\/urls\/inspect$/,
    (request) => {
      const { urlId } = request.body as { urlId: number };
      return json({
        id: urlId,
        url: `${SITE}/blog/best-rattan-patio-sets/`,
        issues:
          urlId === 107
            ? [
                { checkId: 'broken-internal-links', severity: 'error', detail: '5 broken URLs', related: null },
                {
                  checkId: 'title-too-long',
                  severity: 'notice',
                  detail: '742 px, over 580 (80 characters)',
                  related: null,
                },
                { checkId: 'broken-internal-links', severity: 'error', detail: '5 broken URLs', related: null },
              ]
            : [],
      });
    },
  ],
];

async function open() {
  replaceSettings({ mode: 'simple', hiddenChecks: [] });
  const sidecar = fakeSidecar([...ROUTES, ...problemRoutes(), ...simpleRoutes(gardenItem(), GARDEN_ANALYSIS)]);
  setTransport(sidecar);
  const queryClient = createQueryClientWithoutBackoff();
  const router = createAppRouter(queryClient, createMemoryHistory({ initialEntries: [FIX] }));
  render(<App router={router} queryClient={queryClient} />);
  await act(() => router.load());
  const finder = await screen.findByRole('region', { name: 'What’s wrong with one page?' }, { timeout: 10_000 });
  return { router, sidecar, finder: within(finder) };
}

const type = (finder: Awaited<ReturnType<typeof open>>['finder'], text: string) =>
  fireEvent.change(finder.getByRole('searchbox', { name: 'Find a page by its address or title' }), {
    target: { value: text },
  });

afterEach(() => {
  setTransport(fakeSidecar([]));
  liveStore.reset();
});

describe.skipIf(TABLE_STUBBED)('finding a page on What to fix', () => {
  it('finds the site’s pages by part of an address or title, by title then address', async () => {
    const { finder } = await open();
    type(finder, 'rattan');
    const pages = within(await finder.findByRole('list', { name: 'Pages found' })).getAllByRole('button');
    // The site's own pages: another site's address has no problems of its own.
    expect(pages.map((page) => page.textContent)).toEqual([
      'Best Rattan Patio Sets - Hand Picked by Experts/blog/best-rattan-patio-sets/',
      '/rattan-furniture/',
    ]);
  });

  it('lists everything found on the page picked, each once and a link to its problem', async () => {
    const { finder, router } = await open();
    type(finder, 'rattan');
    fireEvent.click(await finder.findByRole('button', { name: /Best Rattan Patio Sets/ }));
    expect(await finder.findByText('2 things were found on this page:')).toBeVisible();
    const found = within(
      finder.getByRole('list', { name: 'Found on Best Rattan Patio Sets - Hand Picked by Experts' }),
    ).getAllByRole('link');
    expect(found.map((link) => link.getAttribute('href'))).toEqual([
      `${FIX}/broken-internal-links`,
      `${FIX}/title-too-long`,
    ]);
    expect(found[0]).toHaveTextContent('Serious');
    expect(found[0]).toHaveTextContent('Links that lead to broken pages');
    // The page opens in the browser from its name and from Open page.
    expect(finder.getByRole('link', { name: /Best Rattan Patio Sets/ })).toHaveAttribute(
      'href',
      `${SITE}/blog/best-rattan-patio-sets/`,
    );
    expect(
      finder.getByRole('button', { name: 'Open page: Best Rattan Patio Sets - Hand Picked by Experts' }),
    ).toBeVisible();
    fireEvent.click(found[0]!);
    await waitFor(() => expect(router.state.location.pathname).toBe(`${FIX}/broken-internal-links`));
  });

  it('says so when nothing was found on the page, and when no page matches', async () => {
    const { finder } = await open();
    type(finder, 'rattan');
    fireEvent.click(await finder.findByRole('button', { name: '/rattan-furniture/' }));
    expect(await finder.findByText('Nothing wrong was found on this page.')).toBeVisible();
    fireEvent.click(finder.getByRole('button', { name: 'Back to the pages found' }));
    expect(await finder.findByRole('list', { name: 'Pages found' })).toBeVisible();
    type(finder, 'zzzz');
    expect(await finder.findByText('No page’s address or title has “zzzz” in it.')).toBeVisible();
  });

  it('asks nothing of the engine for a single letter', async () => {
    const { finder, sidecar } = await open();
    type(finder, 'r');
    await new Promise((resolve) => setTimeout(resolve, 350));
    expect(sidecar.requests.filter((request) => request.path.endsWith('/search'))).toHaveLength(0);
  });
});
