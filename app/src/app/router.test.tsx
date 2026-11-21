import { createMemoryHistory } from '@tanstack/react-router';
import { act, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { setTransport } from '../api/transport';
import { updateSettings } from '../settings/settingsStore';
import { TEST_CRAWL_ID, fakeSidecar, standardRoutes } from '../test/fakeSidecar';
import { App } from './App';
import { createQueryClient } from './queryClient';
import { createAppRouter } from './router';

const CRAWL = `/crawls/${TEST_CRAWL_ID}`;

/** The params a hand-edited link, an old bookmark or an updated app's lastLocation can bring. */
const GARBAGE =
  '?q=true&url=%7B%22a%22%3A1%7D&sort=%25%25&filter=%22x%22&cols=%5B1%2C2%5D&view=Not%20A%20View!' +
  '&severity=fatal&group=%5B%5D&inspect=abc&bogus=1';

async function open(path: string) {
  setTransport(fakeSidecar(standardRoutes()));
  const router = createAppRouter(createQueryClient(), createMemoryHistory({ initialEntries: [path] }));
  render(<App router={router} queryClient={createQueryClient()} />);
  await act(() => router.load());
  return router;
}

/** The current view's search, as its screen and the inspector read it: only values its validator checked. */
function viewSearch(router: Awaited<ReturnType<typeof open>>) {
  const leaf = router.state.matches[router.state.matches.length - 1];
  return Object.fromEntries(Object.entries(leaf?.search ?? {}).filter(([, value]) => value !== undefined));
}

afterEach(() => setTransport(fakeSidecar([])));

describe('every IA route, opened with unusable search params', () => {
  const routes: [path: string, heading: RegExp][] = [
    ['/', /Crawl a site/],
    ['/new', /New crawl/],
    [`${CRAWL}/overview`, /Overview/],
    [`${CRAWL}/issues`, /Issues/],
    [`${CRAWL}/issues/title-too-long`, /Issues/],
    [`${CRAWL}/urls`, /URLs/],
    [`${CRAWL}/links`, /Links/],
    [`${CRAWL}/sitemaps`, /Sitemaps/],
    [`${CRAWL}/robots`, /robots\.txt/],
    [`${CRAWL}/settings`, /settings/i],
    [`${CRAWL}/extraction`, /Custom extraction/],
    ['/settings', /General/],
    ['/settings/shortcuts', /Shortcuts/],
  ];

  it.each(routes)('%s shows its screen, and its search holds nothing unchecked', async (path, heading) => {
    const router = await open(path + GARBAGE);
    expect(await screen.findByRole('heading', { level: 1, name: heading })).toBeVisible();
    expect(screen.queryByText('This view couldn’t be shown.')).toBeNull();
    expect(viewSearch(router)).toEqual({});
    // `?inspect=abc` is no URL: the inspector stays closed.
    expect(screen.queryByRole('complementary', { name: 'Inspector' })).toBeNull();
  });

  it('opens a crawl at its last view, dropping the junk on the way', async () => {
    // Overview the first time.
    updateSettings({ lastViewByCrawl: {} });
    const router = await open(CRAWL + GARBAGE);
    expect(await screen.findByRole('heading', { level: 1, name: /Overview/ })).toBeVisible();
    expect(router.state.location.pathname).toBe(`${CRAWL}/overview`);
    expect(viewSearch(router)).toEqual({});
  });
});

describe('the reviewer’s cases', () => {
  it.each(['/?q=true', '/?q=%7B%22a%22%3A1%7D'])('Home reads %s as no search, and lists the library', async (path) => {
    const router = await open(path);
    expect(await screen.findByRole('link', { name: 'example.com' })).toBeVisible();
    expect(screen.getByRole('searchbox', { name: 'Search the library' })).toHaveValue('');
    expect(viewSearch(router)).toEqual({});
  });

  it('a sort of %% falls back to crawl order, and the grid shows', async () => {
    const router = await open(`${CRAWL}/urls?sort=%25%25`);
    expect(await screen.findByRole('grid', { name: 'URLs' })).toBeVisible();
    expect(viewSearch(router)).toEqual({});
  });

  it('a filter that is only a string is dropped, never sent', async () => {
    const sidecar = fakeSidecar(standardRoutes());
    setTransport(sidecar);
    const router = createAppRouter(
      createQueryClient(),
      createMemoryHistory({ initialEntries: [`${CRAWL}/urls?filter=%22x%22`] }),
    );
    render(<App router={router} queryClient={createQueryClient()} />);
    await act(() => router.load());
    expect(await screen.findByRole('grid', { name: 'URLs' })).toBeVisible();
    const query = sidecar.requests.find((request) => request.path.endsWith('/urls'))?.body as { filters: unknown };
    expect(query.filters).toEqual([]);
  });

  it('opens the inspector for a real URL id only, and only on a view that has one', async () => {
    await open(`${CRAWL}/urls?inspect=2`);
    expect(await screen.findByRole('complementary', { name: 'Inspector' })).toBeVisible();
  });

  it('keeps the inspector closed on Home, whatever the URL says', async () => {
    const router = await open('/?inspect=2');
    expect(await screen.findByRole('heading', { level: 1, name: /Crawl a site/ })).toBeVisible();
    expect(screen.queryByRole('complementary', { name: 'Inspector' })).toBeNull();
    expect(viewSearch(router)).toEqual({});
    // Back and forward bring URLs as they were, unchecked: the view still reads only its own params.
    await act(async () => {
      router.history.push('/?inspect=3&q=garden');
      await router.load();
    });
    expect(router.state.location.search).toMatchObject({ inspect: 3 });
    expect(screen.queryByRole('complementary', { name: 'Inspector' })).toBeNull();
    expect(viewSearch(router)).toEqual({ q: 'garden' });
  });
});

describe('the search a valid link brings', () => {
  it('reaches the URL grid’s query checked and within the contract’s limits', async () => {
    const sidecar = fakeSidecar(standardRoutes());
    setTransport(sidecar);
    const sort = encodeURIComponent('-issues,url,statusCode,depth,title');
    const filters = encodeURIComponent(
      JSON.stringify(Array.from({ length: 25 }, (_, index) => ({ column: `c${index}`, op: 'isEmpty' }))),
    );
    const router = createAppRouter(
      createQueryClient(),
      createMemoryHistory({ initialEntries: [`${CRAWL}/urls?sort=${sort}&filter=${filters}&q=garden&inspect=3`] }),
    );
    render(<App router={router} queryClient={createQueryClient()} />);
    await act(() => router.load());
    expect(await screen.findByRole('grid', { name: 'URLs' })).toBeVisible();
    const query = sidecar.requests.find((request) => request.path.endsWith('/urls'))?.body as {
      sort: unknown[];
      filters: unknown[];
      search: string;
    };
    expect(query.sort).toEqual([
      { column: 'issues', desc: true },
      { column: 'url', desc: false },
      { column: 'statusCode', desc: false },
    ]);
    expect(query.filters).toHaveLength(20);
    expect(query.search).toBe('garden');
    expect(viewSearch(router)).toMatchObject({ q: 'garden', inspect: 3 });
  });
});
