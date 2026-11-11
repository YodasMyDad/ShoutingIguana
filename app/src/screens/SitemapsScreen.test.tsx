import { createMemoryHistory } from '@tanstack/react-router';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { GridQuery, SitemapList } from '../api/grids';
import { gridPagesOf } from '../api/queries';
import { setTransport, type TransportRequest } from '../api/transport';
import { App } from '../app/App';
import { createQueryClient } from '../app/queryClient';
import { createAppRouter } from '../app/router';
import { onAnnounce } from '../feedback/announce';
import { commands, installKeyboard } from '../keyboard/registry';
import { TEST_CRAWL_ID, fakeSidecar, json, problem } from '../test/fakeSidecar';
import { createQueryClientWithoutBackoff } from '../test/queryClient';
import { wholeText } from '../test/text';
import {
  COVERAGE_PAGE,
  SITE,
  TEST_SITEMAPS,
  TEST_SITEMAP_LIST,
  sitemapsRobotsRoutes,
  testSitemap,
  type SitemapsRobotsOptions,
} from '../test/sitemapsRobotsSidecar';
import { resetPlaces } from './listPlace';
import { ANSWERED_SITE_PAGES } from './overview/links';

const SITEMAPS = `/crawls/${TEST_CRAWL_ID}/sitemaps`;

function sidecar(options: SitemapsRobotsOptions = {}) {
  const analyse = vi.fn(() => new Response(null, { status: 202 }));
  const transport = fakeSidecar([
    ['POST', /^\/api\/crawls\/[0-9a-f]{16}\/analyze$/, analyse],
    ...sitemapsRobotsRoutes(options),
  ]);
  setTransport(transport);
  return { transport, analyse };
}

async function open(path: string, queryClient = createQueryClient()) {
  const router = createAppRouter(queryClient, createMemoryHistory({ initialEntries: [path] }));
  render(<App router={router} queryClient={queryClient} />);
  await act(() => router.load());
  return { router, queryClient };
}

const searchOf = (router: Awaited<ReturnType<typeof open>>['router']) =>
  router.state.location.search as Record<string, unknown>;
const pathOf = (router: Awaited<ReturnType<typeof open>>['router']) => router.state.location.pathname;
const rowOf = (text: string) => screen.getByText(text).closest<HTMLElement>('[role="row"]')!;
const gridRequests = (transport: { requests: TransportRequest[] }) =>
  transport.requests
    .filter((request) => request.path.endsWith('/sitemaps/urls'))
    .map((request) => request.body as GridQuery);

afterEach(() => {
  setTransport(fakeSidecar([]));
  resetPlaces();
});

describe('Sitemaps: the list', () => {
  it('answers first, then lists every sitemap as a tree with how it was found and what became of it', async () => {
    sidecar();
    await open(SITEMAPS);
    expect(await screen.findByText('The crawl found 6 sitemaps: 5 were read and 1 couldn’t be read.')).toBeVisible();
    const tree = await screen.findByRole('treegrid', { name: 'Sitemaps' });
    const rows = within(tree).getAllByRole('row').slice(1);
    expect(rows).toHaveLength(6);
    // The index, open, with its three sitemaps a level under it.
    expect(rows[0]).toHaveAttribute('aria-expanded', 'true');
    expect(rows[0]).toHaveAttribute('aria-level', '1');
    expect(rows.slice(1, 4).map((row) => row.getAttribute('aria-level'))).toEqual(['2', '2', '2']);
    expect(within(rows[0]!).getByText('Index of 3')).toBeVisible();
    expect(within(rows[0]!).getByText('robots.txt')).toBeVisible();
    expect(within(rowOf(`${SITE}/sitemaps/pages.xml`)).getByText('Sitemap index')).toBeVisible();
    expect(within(rowOf(`${SITE}/news-sitemap.xml`)).getByText('Crawl settings')).toBeVisible();
    expect(within(rowOf(`${SITE}/sitemaps/missing.xml`)).getByText('Couldn’t be read: HTTP 404')).toBeVisible();
  });

  it('says plainly when the crawl can’t say how it found a sitemap', async () => {
    sidecar();
    await open(SITEMAPS);
    const cell = within(await waitFor(() => rowOf(`${SITE}/old-sitemap.xml`))).getByText('Not known');
    expect(cell).toHaveAttribute('title', expect.stringContaining('can’t say how it found this sitemap'));
  });

  it('shows a sitemap that couldn’t be read with dashes for its counts, never 0', async () => {
    sidecar();
    await open(SITEMAPS);
    const failed = await waitFor(() => rowOf(`${SITE}/sitemaps/missing.xml`));
    expect(within(failed).getAllByText('–')).toHaveLength(4);
    expect(within(failed).queryByRole('link')).toBeNull();
  });

  it('makes every count a link to that sitemap’s URLs under the count’s own filter, and a 0 plain', async () => {
    sidecar();
    await open(SITEMAPS);
    const products = await waitFor(() => rowOf(`${SITE}/sitemaps/products.xml`));
    const links = within(products).getAllByRole('link');
    expect(links.map((link) => link.textContent)).toEqual(['3', '2', '1', '1']);
    expect(links.map((link) => link.getAttribute('aria-label'))).toEqual([
      '3 URLs listed',
      '2 URLs crawled',
      '1 URL not crawled',
      '1 URL non-indexable',
    ]);
    const hrefs = links.map((link) => decodeURIComponent(link.getAttribute('href') ?? ''));
    expect(hrefs[0]).toBe(`${SITEMAPS}/3`);
    expect(hrefs[2]).toContain(`${SITEMAPS}/3?filter=`);
    expect(hrefs[2]).toContain('"column":"fetched","op":"is","value":false');
    expect(hrefs[3]).toContain('"column":"indexable","op":"is","value":false');
    // The news sitemap's URLs were all crawled: nothing to open behind "not crawled".
    const news = rowOf(`${SITE}/news-sitemap.xml`);
    expect(
      within(news)
        .getAllByRole('link')
        .map((link) => link.textContent),
    ).toEqual(['2', '2']);
    expect(within(news).getAllByText('0')).toHaveLength(2);
  });

  it('lands each count on exactly its URLs: the grid it opens counts what the list said', async () => {
    sidecar();
    const { router } = await open(SITEMAPS);
    const index = await waitFor(() => rowOf(`${SITE}/sitemap.xml`));
    fireEvent.click(within(index).getByRole('link', { name: '2 URLs non-indexable' }));
    await waitFor(() => expect(pathOf(router)).toBe(`${SITEMAPS}/1`));
    expect(await screen.findByText('of 6 URLs')).toBeVisible();
    const grid = await screen.findByRole('grid');
    await waitFor(() => expect(grid).toHaveAttribute('aria-rowcount', '3'));
    expect(searchOf(router).filter).toEqual([{ column: 'indexable', op: 'is', value: false }]);
  });

  it('is one Tab stop: its links are reached with the arrows, and Enter does what a click on one does', async () => {
    sidecar();
    const { router } = await open(SITEMAPS);
    const tree = await screen.findByRole('treegrid', { name: 'Sitemaps' });
    await waitFor(() => expect(within(tree).getAllByRole('link').length).toBeGreaterThan(0));
    expect(tree).toHaveAttribute('tabindex', '0');
    for (const link of within(tree).getAllByRole('link')) expect(link).toHaveAttribute('tabindex', '-1');
    act(() => tree.focus());
    // Down to pages.xml, along to its "Non-indexable", and in.
    fireEvent.keyDown(tree, { key: 'ArrowDown' });
    fireEvent.keyDown(tree, { key: 'End' });
    fireEvent.keyDown(tree, { key: 'Enter' });
    await waitFor(() => expect(pathOf(router)).toBe(`${SITEMAPS}/2`));
    expect(searchOf(router).filter).toEqual([{ column: 'indexable', op: 'is', value: false }]);
  });

  it('opens an index with Enter, as any sitemap, and closes and opens it with Left and Right', async () => {
    sidecar();
    const { router } = await open(SITEMAPS);
    const tree = await screen.findByRole('treegrid', { name: 'Sitemaps' });
    await screen.findByText(`${SITE}/sitemaps/pages.xml`);
    act(() => tree.focus());
    fireEvent.keyDown(tree, { key: 'ArrowLeft' });
    expect(screen.queryByText(`${SITE}/sitemaps/pages.xml`)).toBeNull();
    expect(within(tree).getAllByRole('row')[1]).toHaveAttribute('aria-expanded', 'false');
    fireEvent.keyDown(tree, { key: 'ArrowRight' });
    expect(screen.getByText(`${SITE}/sitemaps/pages.xml`)).toBeVisible();
    fireEvent.keyDown(tree, { key: 'Enter' });
    await waitFor(() => expect(pathOf(router)).toBe(`${SITEMAPS}/1`));
    expect(searchOf(router).filter).toBeUndefined();
  });

  it('starts, coming back from a sitemap, on the row it was opened from', async () => {
    sidecar();
    const { router } = await open(SITEMAPS);
    const news = await waitFor(() => rowOf(`${SITE}/news-sitemap.xml`));
    fireEvent.click(within(news).getByRole('link', { name: '2 URLs listed' }));
    await waitFor(() => expect(pathOf(router)).toBe(`${SITEMAPS}/5`));
    act(() => router.history.back());
    const tree = await screen.findByRole('treegrid', { name: 'Sitemaps' });
    await screen.findByText(`${SITE}/news-sitemap.xml`);
    // The cursor is where the list was left: focus arriving finds it there, not on the first row.
    act(() => tree.focus());
    const cursor = document.getElementById(tree.getAttribute('aria-activedescendant') ?? '');
    expect(cursor?.closest('[role="row"]')).toHaveTextContent('/news-sitemap.xml');
  });

  it('opens the sitemap itself from a count that’s nothing or unknown', async () => {
    sidecar();
    const { router } = await open(SITEMAPS);
    const tree = await screen.findByRole('treegrid', { name: 'Sitemaps' });
    await screen.findByText(`${SITE}/sitemaps/missing.xml`);
    fireEvent.doubleClick(within(rowOf(`${SITE}/sitemaps/missing.xml`)).getAllByText('–')[0]!);
    await waitFor(() => expect(pathOf(router)).toBe(`${SITEMAPS}/4`));
    expect(searchOf(router).filter).toBeUndefined();
    expect(tree).not.toBeInTheDocument();
  });

  it('says how many of the site’s pages no sitemap lists, as a link to exactly those pages', async () => {
    sidecar();
    await open(SITEMAPS);
    const line = await screen.findByText(wholeText('2 of the site’s 9 pages aren’t in any sitemap.'));
    // The pages it's "of" are a link too: the site's pages, as Overview counts them.
    const pages = decodeURIComponent(within(line).getByRole('link', { name: '9 pages' }).getAttribute('href') ?? '');
    expect(JSON.parse(pages.slice(pages.indexOf('filter=') + 'filter='.length))).toEqual(ANSWERED_SITE_PAGES);
    const link = within(line).getByRole('link', { name: '2' });
    const href = decodeURIComponent(link.getAttribute('href') ?? '');
    expect(href.startsWith(`/crawls/${TEST_CRAWL_ID}/urls?filter=`)).toBe(true);
    const filter = JSON.parse(href.slice(href.indexOf('filter=') + 'filter='.length)) as unknown;
    expect(filter).toEqual([...ANSWERED_SITE_PAGES, { column: 'inSitemap', op: 'is', value: false }]);
  });

  it('says the analysis didn’t count them, and why, when the engine can’t place the site’s pages: never “in a sitemap”', async () => {
    // A crawl stopped before its sitemaps were read, then analysed: the check that compares pages with sitemaps was
    // skipped, so the engine answers null for every page, never false.
    sidecar({
      sitemaps: {
        ...TEST_SITEMAP_LIST,
        discovery: 'unfinished',
        sitemaps: [
          testSitemap({ id: 1, isIndex: true, sitemaps: 1 }),
          testSitemap({ id: 2, parentId: 1, foundVia: 'index', state: 'pending', statusCode: null }),
        ],
      },
      coverage: {
        ...COVERAGE_PAGE,
        matching: 1,
        facets: { inSitemap: [{ value: null, count: 1, filter: { column: 'inSitemap', op: 'isEmpty' } }] },
      },
      analysis: {
        skipped: [
          { checkId: 'not-in-sitemap', reason: { code: 'crawl-unfinished', text: 'The crawl didn’t finish.' } },
        ],
      },
      item: { status: 'stopped' },
    });
    await open(SITEMAPS);
    expect(
      await screen.findByText('The analysis didn’t count the site’s pages no sitemap lists. The crawl didn’t finish.'),
    ).toBeVisible();
    expect(screen.queryByText(/in a sitemap\./)).toBeNull();
    expect(screen.queryByText(/in any sitemap/)).toBeNull();
  });

  it('still says the analysis didn’t count them when its reason can’t be read', async () => {
    sidecar({
      coverage: {
        ...COVERAGE_PAGE,
        facets: { inSitemap: [{ value: null, count: 9, filter: { column: 'inSitemap', op: 'isEmpty' } }] },
      },
    });
    await open(SITEMAPS, createQueryClientWithoutBackoff());
    expect(
      await screen.findByText(/The analysis didn’t count the site’s pages no sitemap lists: it couldn’t compare/),
    ).toBeVisible();
    expect(screen.queryByText(/in a sitemap\./)).toBeNull();
  });

  it('keeps the list when the pages no sitemap lists can’t be counted, and says so with Try again', async () => {
    sidecar({ coverage: problem({ type: 'internal', title: 'Something went wrong in the engine.', status: 500 }) });
    await open(SITEMAPS, createQueryClientWithoutBackoff());
    expect(await screen.findByText('The site’s pages no sitemap lists couldn’t be counted.')).toBeVisible();
    expect(screen.getByRole('treegrid', { name: 'Sitemaps' })).toBeVisible();
    expect(screen.getByRole('button', { name: 'Try again' })).toBeVisible();
  });
});

describe('Sitemaps: what isn’t there', () => {
  const none = (discovery: SitemapList['discovery']): SitemapList => ({
    ...TEST_SITEMAP_LIST,
    discovery,
    sitemaps: [],
  });

  it('says a site has no sitemap, and where the crawl looked', async () => {
    sidecar({ sitemaps: none('done') });
    await open(SITEMAPS);
    expect(await screen.findByRole('heading', { name: 'The crawl found no sitemap.' })).toBeVisible();
    expect(
      screen.getByText(/looks in the start URL’s robots.txt and at the sitemaps given in its settings/),
    ).toBeVisible();
    expect(screen.queryByRole('treegrid')).toBeNull();
  });

  it('says a crawl told not to read sitemaps was, and leads to its settings', async () => {
    sidecar({ sitemaps: none('off') });
    await open(SITEMAPS);
    expect(await screen.findByRole('heading', { name: 'This crawl was told not to read sitemaps.' })).toBeVisible();
    expect(screen.getByRole('link', { name: 'Open the crawl’s settings' })).toHaveAttribute(
      'href',
      `/crawls/${TEST_CRAWL_ID}/settings`,
    );
  });

  it('says where the crawl looked when the only sitemap it found couldn’t be read', async () => {
    const only = testSitemap({
      id: 1,
      foundVia: 'defaultLocation',
      state: 'failed',
      statusCode: 404,
      error: 'HTTP 404',
    });
    sidecar({ sitemaps: { ...TEST_SITEMAP_LIST, sitemaps: [only] } });
    await open(SITEMAPS);
    // The crawl's own try of the default location: no sitemap was found, so none is said to have failed.
    expect(await screen.findByText('Nothing declared a sitemap, and there’s none at /sitemap.xml.')).toBeVisible();
    expect(screen.queryByText(/found 1 sitemap/)).toBeNull();
    expect(await screen.findByText('No sitemap was read')).toBeVisible();
    expect(within(rowOf(only.url)).getByText('Default location')).toBeVisible();
    expect(within(rowOf(only.url)).getByText('None there: HTTP 404')).toBeVisible();
    // No sitemap was read, so there's no count of the pages none lists to make.
    expect(screen.queryByText(/in any sitemap/)).toBeNull();
  });

  it('says the list isn’t all there is when the crawl stopped before reading every sitemap, with Continue', async () => {
    sidecar({ sitemaps: { ...TEST_SITEMAP_LIST, discovery: 'unfinished' }, item: { status: 'stopped' } });
    await open(SITEMAPS);
    expect(
      await screen.findByText(/stopped before it had read every sitemap, so this list isn’t all there is/),
    ).toBeVisible();
    // The view's own Continue, beside the top bar's.
    expect(within(screen.getByRole('main')).getByRole('button', { name: 'Continue' })).toBeVisible();
  });

  it('says which counts are the crawl’s own judgement without an analysis, and offers to analyse', async () => {
    const { analyse } = sidecar({
      sitemaps: { ...TEST_SITEMAP_LIST, summary: { available: false, reason: 'notAnalyzed' } },
      coverage: {
        ...COVERAGE_PAGE,
        summary: { available: false, reason: 'notAnalyzed' },
        unavailable: ['inSitemap'],
        facets: {},
      },
      item: { analyzedAt: null },
    });
    await open(SITEMAPS);
    const note = await screen.findByText(
      /Indexable and non-indexable are the crawl’s own judgement: this crawl hasn’t been analysed yet/,
    );
    expect(note).toHaveTextContent('The other counts need no analysis.');
    // The counts still show: they need no analysis.
    expect(
      within(await waitFor(() => rowOf(`${SITE}/sitemap.xml`))).getByRole('link', { name: '6 URLs listed' }),
    ).toBeVisible();
    expect(await screen.findByText(/counted by the analysis: this crawl hasn’t been analysed yet/)).toBeVisible();
    fireEvent.click(screen.getByRole('button', { name: 'Analyse it' }));
    await waitFor(() => expect(analyse).toHaveBeenCalledOnce());
  });

  it('says the list couldn’t be read, with Try again, and reads it then', async () => {
    const failing = problem({ type: 'internal', title: 'Something went wrong in the engine.', status: 500 });
    const { transport } = sidecar({ sitemaps: failing });
    await open(SITEMAPS, createQueryClientWithoutBackoff());
    expect(await screen.findByText('The sitemaps couldn’t be read.')).toBeVisible();
    setTransport(fakeSidecar(sitemapsRobotsRoutes()));
    fireEvent.click(screen.getByRole('button', { name: 'Try again' }));
    expect(await screen.findByRole('treegrid', { name: 'Sitemaps' })).toBeVisible();
    expect(transport.requests.filter((request) => request.path.endsWith('/sitemaps')).length).toBeGreaterThanOrEqual(3);
  });
});

describe('Sitemaps: live and refreshing', () => {
  it('marks a running crawl’s list live, and words what it hasn’t read as still to come', async () => {
    const pending = testSitemap({ id: 9, url: `${SITE}/late.xml`, state: 'pending' });
    sidecar({
      sitemaps: {
        discovery: 'running',
        summary: { available: false, reason: 'crawling' },
        sitemaps: [...TEST_SITEMAPS, pending],
      },
      item: { status: 'running', finishedAt: null },
    });
    await open(SITEMAPS);
    expect(await screen.findByText(/1 is still to be read\. It’s still looking for more\./)).toBeVisible();
    expect(within(await waitFor(() => rowOf(pending.url))).getByText('Not read yet')).toBeVisible();
    expect(screen.getByText('Live')).toBeVisible();
    expect(screen.getByText(/the crawl’s own judgement while it runs/)).toBeVisible();
    // Nothing offers an analysis while the crawl runs.
    expect(screen.queryByRole('button', { name: /Analyse/ })).toBeNull();
  });

  it('keeps the list on screen while it’s read again, and never puts the loading frame over it', async () => {
    let hold: (() => void) | null = null;
    let calls = 0;
    const routes = sitemapsRobotsRoutes();
    setTransport(
      fakeSidecar([
        [
          'GET',
          /\/sitemaps$/,
          async () => {
            calls++;
            if (calls > 1) await new Promise<void>((resolve) => (hold = resolve));
            return json(calls > 1 ? { ...TEST_SITEMAP_LIST, sitemaps: TEST_SITEMAPS.slice(0, 5) } : TEST_SITEMAP_LIST);
          },
        ],
        ...routes,
      ]),
    );
    const { queryClient } = await open(SITEMAPS);
    await screen.findByText(`${SITE}/old-sitemap.xml`);
    act(() => void queryClient.invalidateQueries(gridPagesOf(TEST_CRAWL_ID)));
    await waitFor(() => expect(hold).not.toBeNull());
    expect(screen.getByText(`${SITE}/old-sitemap.xml`)).toBeVisible();
    expect(screen.queryByLabelText('Loading the sitemaps')).toBeNull();
    await act(async () => {
      hold?.();
      await Promise.resolve();
    });
    await waitFor(() => expect(screen.queryByText(`${SITE}/old-sitemap.xml`)).toBeNull());
    expect(screen.getByText('The crawl found 5 sitemaps: 4 were read and 1 couldn’t be read.')).toBeVisible();
  });
});

describe('Sitemaps: one sitemap', () => {
  it('shows its facts and its URLs, with what the crawl did with each', async () => {
    sidecar();
    await open(`${SITEMAPS}/3`);
    expect(await screen.findByRole('heading', { level: 1, name: `${SITE}/sitemaps/products.xml` })).toBeVisible();
    expect(screen.getByText('a sitemap index,')).toBeVisible();
    expect(screen.getByRole('link', { name: `${SITE}/sitemap.xml` })).toHaveAttribute('href', `${SITEMAPS}/1`);
    expect(screen.getByText('3 URL entries')).toBeVisible();
    const grid = await screen.findByRole('grid', { name: `URLs listed by ${SITE}/sitemaps/products.xml` });
    await waitFor(() => expect(grid).toHaveAttribute('aria-rowcount', '4'));
    expect(within(rowOf(`${SITE}/p/unreached`)).getByText('Not crawled')).toBeVisible();
    const outcome = (url: string) => rowOf(url).querySelector('[aria-colindex="2"]');
    expect(outcome(`${SITE}/p/gone`)).toHaveTextContent('Client error');
    expect(outcome(`${SITE}/contact`)).toHaveTextContent('Crawled');
  });

  it('narrows to what people come for with a quick filter, says the count once, and keeps it in the route', async () => {
    const said: string[] = [];
    const stop = onAnnounce((message) => said.push(message));
    const { transport } = sidecar();
    const { router } = await open(`${SITEMAPS}/1`);
    const show = await screen.findByRole('radiogroup', { name: 'Show' });
    await screen.findByText(`${SITE}/about`);
    // The counts on the filters: all it lists, and the two the list of sitemaps counted.
    expect(within(show).getByRole('radio', { name: /All/ })).toHaveTextContent('6');
    expect(within(show).getByRole('radio', { name: /Not crawled/ })).toHaveTextContent('1');
    expect(within(show).getByRole('radio', { name: /Non-indexable/ })).toHaveTextContent('2');
    fireEvent.click(within(show).getByRole('radio', { name: /Redirecting or broken/ }));
    await waitFor(() =>
      expect(searchOf(router).filter).toEqual([
        { column: 'outcome', op: 'in', values: ['redirect', 'clientError', 'serverError', 'timeout', 'networkError'] },
      ]),
    );
    expect(await screen.findByText('of 6 URLs')).toBeVisible();
    expect(screen.queryByText(`${SITE}/about`)).toBeNull();
    expect(screen.getByText(`${SITE}/old`)).toBeVisible();
    expect(screen.getByText(`${SITE}/p/gone`)).toBeVisible();
    await waitFor(() => expect(said).toEqual(['2 of 6 URLs']));
    expect(gridRequests(transport).at(-1)).toMatchObject({ sitemapId: 1, filters: searchOf(router).filter });
    stop();
  });

  it('shows a count’s filter that isn’t a quick filter as a chip, and none of the quick filters chosen', async () => {
    sidecar();
    const { router } = await open(
      `${SITEMAPS}/1?filter=${encodeURIComponent(JSON.stringify([{ column: 'fetched', op: 'is', value: true }]))}`,
    );
    const filters = await screen.findByRole('group', { name: 'Filters' });
    expect(filters).toHaveTextContent('Crawled is yes');
    expect(await screen.findByText('of 6 URLs')).toBeVisible();
    // One segment is always chosen, so focus arriving on the control chooses nothing for the user: these filters
    // are a segment of their own while they last, with the count the grid shows.
    const chosen = screen.getAllByRole('radio').filter((option) => option.getAttribute('aria-checked') === 'true');
    expect(chosen.map((option) => option.textContent)).toEqual(['Other filters 5']);
    act(() => chosen[0]?.focus());
    expect(searchOf(router).filter).toEqual([{ column: 'fetched', op: 'is', value: true }]);
    // Choosing a quick filter replaces them, and their segment goes.
    fireEvent.click(screen.getByRole('radio', { name: /All/ }));
    await waitFor(() => expect(searchOf(router).filter).toBeUndefined());
    await waitFor(() => expect(screen.queryByRole('radio', { name: /Other filters/ })).toBeNull());
  });

  it('sorts from a column’s header, by the columns the engine says it sorts', async () => {
    const { transport } = sidecar();
    const { router } = await open(`${SITEMAPS}/1`);
    await screen.findByText(`${SITE}/about`);
    const header = await screen.findByRole('columnheader', { name: /Status/ });
    await waitFor(() => expect(within(header).getByRole('button')).toBeInTheDocument());
    // Title is text: the engine doesn't sort by it, so its header is no button.
    expect(within(screen.getByRole('columnheader', { name: 'Title' })).queryByRole('button')).toBeNull();
    fireEvent.click(within(header).getByRole('button'));
    await waitFor(() => expect(searchOf(router).sort).toEqual([{ column: 'statusCode', desc: false }]));
    await waitFor(() => expect(gridRequests(transport).at(-1)?.sort).toEqual([{ column: 'statusCode', desc: false }]));
    await waitFor(() =>
      expect(screen.getByRole('columnheader', { name: /Status/ })).toHaveAttribute('aria-sort', 'ascending'),
    );
  });

  it('searches its URLs from the field, and Ctrl+F goes to it', async () => {
    const { transport } = sidecar();
    const { router } = await open(`${SITEMAPS}/1`);
    const field = await screen.findByRole('searchbox', { name: 'Search this sitemap’s URLs and titles' });
    fireEvent.change(field, { target: { value: 'contact' } });
    fireEvent.keyDown(field, { key: 'Enter' });
    await waitFor(() => expect(searchOf(router).q).toBe('contact'));
    await waitFor(() => expect(gridRequests(transport).at(-1)?.search).toBe('contact'));
    expect(await screen.findByText('of 6 URLs')).toBeVisible();
  });

  it('opens the inspector on a URL with Enter, in place', async () => {
    sidecar();
    const { router } = await open(`${SITEMAPS}/5`);
    const grid = await screen.findByRole('grid');
    await screen.findByText(`${SITE}/news/a`);
    act(() => grid.focus());
    fireEvent.keyDown(grid, { key: 'Enter' });
    await waitFor(() => expect(searchOf(router).inspect).toBe(7));
    expect(pathOf(router)).toBe(`${SITEMAPS}/5`);
  });

  it('says a sitemap that couldn’t be read has no URLs to show, and why', async () => {
    sidecar();
    await open(`${SITEMAPS}/4`);
    expect(await screen.findByText('No URLs to show for this sitemap.')).toBeVisible();
    expect(screen.getByText('The crawl couldn’t read it: HTTP 404.')).toBeVisible();
    expect(screen.getByText('Not known')).toBeVisible();
  });

  it('leads back to the list from its own link, the sidebar’s and Ctrl+5: never to the sitemap it’s on', async () => {
    const uninstall = installKeyboard();
    sidecar();
    const { router } = await open(`${SITEMAPS}/3`);
    const back = await screen.findByRole('link', { name: 'All sitemaps' });
    expect(back).toHaveAttribute('href', SITEMAPS);
    const entry = within(screen.getByRole('navigation', { name: 'Main' })).getByRole('link', { name: /^Sitemaps/ });
    expect(entry).toHaveAttribute('href', SITEMAPS);
    // It's still the place the sidebar marks as current.
    expect(entry).toHaveAttribute('aria-current', 'page');
    fireEvent.keyDown(document.body, { key: '5', code: 'Digit5', ctrlKey: true });
    await waitFor(() => expect(pathOf(router)).toBe(SITEMAPS));
    expect(await screen.findByRole('treegrid', { name: 'Sitemaps' })).toBeVisible();
    uninstall();
  });

  it('says a stale id names no sitemap of this crawl, and offers the list', async () => {
    sidecar();
    const { router } = await open(`${SITEMAPS}/999`);
    expect(await screen.findByRole('heading', { name: 'This sitemap isn’t in this crawl' })).toBeVisible();
    fireEvent.click(screen.getByRole('button', { name: 'Show every sitemap' }));
    await waitFor(() => expect(pathOf(router)).toBe(SITEMAPS));
    expect(await screen.findByRole('treegrid', { name: 'Sitemaps' })).toBeVisible();
  });

  it('says “not so far” of a sitemap a crawl that hasn’t ended doesn’t have, and fills in once it finds it', async () => {
    const running = { status: 'running', finishedAt: null } as const;
    const list = (sitemaps: typeof TEST_SITEMAPS): SitemapList => ({
      discovery: 'running',
      summary: { available: false, reason: 'crawling' },
      sitemaps,
    });
    sidecar({ sitemaps: list(TEST_SITEMAPS.slice(0, 1)), item: running });
    const { queryClient } = await open(`${SITEMAPS}/2`);
    expect(await screen.findByRole('heading', { name: 'The crawl hasn’t found this sitemap so far' })).toBeVisible();
    expect(screen.getByText(/If it finds it before it ends, it shows here\./)).toBeVisible();
    // Never "from before the site was crawled again": the crawl is still at it.
    expect(screen.queryByText(/crawled again/)).toBeNull();
    expect(screen.queryByRole('heading', { name: 'This sitemap isn’t in this crawl' })).toBeNull();
    // The crawl finds it: the list is read again with the crawl, and the page is the sitemap's.
    sidecar({ sitemaps: list(TEST_SITEMAPS), item: running });
    act(() => void queryClient.invalidateQueries(gridPagesOf(TEST_CRAWL_ID)));
    expect(await screen.findByText(`${SITE}/about`)).toBeVisible();
    expect(screen.queryByRole('heading', { name: 'The crawl hasn’t found this sitemap so far' })).toBeNull();
  });

  it('still says a sitemap isn’t in a running crawl when what the path names can’t be a sitemap’s id', async () => {
    sidecar({ item: { status: 'running', finishedAt: null } });
    await open(`${SITEMAPS}/not-a-number`);
    expect(await screen.findByRole('heading', { name: 'This sitemap isn’t in this crawl' })).toBeVisible();
  });

  it('offers, on a sitemap the crawl doesn’t have, only the command that can run: the way back to the list', async () => {
    sidecar();
    const { router } = await open(`${SITEMAPS}/999`);
    await screen.findByRole('heading', { name: 'This sitemap isn’t in this crawl' });
    const mine = commands.list().filter((command) => command.group === 'Sitemaps');
    expect(mine.length).toBeGreaterThan(4);
    // Nothing to search, filter, copy or open: each is greyed in the palette, and its key does nothing.
    expect(mine.filter((command) => commands.isEnabled(command)).map((command) => command.id)).toEqual([
      'sitemaps.list',
    ]);
    expect(commands.run('sitemaps.search')).toBe(false);
    expect(commands.run('sitemaps.show.notCrawled')).toBe(false);
    expect(searchOf(router).filter).toBeUndefined();
    // On a sitemap the crawl has, they run.
    act(() => {
      router.history.push(`${SITEMAPS}/2`);
    });
    await screen.findByText(`${SITE}/about`);
    expect(commands.isEnabled(commands.get('sitemaps.search')!)).toBe(true);
    expect(commands.isEnabled(commands.get('sitemaps.show.notCrawled')!)).toBe(true);
  });

  it('says the same of something in the path that can’t be a sitemap’s id, and asks the engine nothing about it', async () => {
    const { transport } = sidecar();
    await open(`${SITEMAPS}/not-a-number`);
    expect(await screen.findByRole('heading', { name: 'This sitemap isn’t in this crawl' })).toBeVisible();
    expect(gridRequests(transport)).toEqual([]);
  });

  it('still shows the URLs when the sitemap’s facts can’t be read, and says so', async () => {
    const failing = problem({ type: 'internal', title: 'Something went wrong in the engine.', status: 500 });
    sidecar({ sitemaps: failing });
    await open(`${SITEMAPS}/2`, createQueryClientWithoutBackoff());
    expect(await screen.findByText('This sitemap’s facts couldn’t be read.')).toBeVisible();
    expect(await screen.findByText(`${SITE}/about`)).toBeVisible();
  });

  it('lands on the sitemap a link names by its address, in place of the list', async () => {
    sidecar();
    const { router } = await open(`${SITEMAPS}?url=${encodeURIComponent(`${SITE}/sitemaps/missing.xml`)}`);
    await waitFor(() => expect(pathOf(router)).toBe(`${SITEMAPS}/4`));
    expect(searchOf(router).url).toBeUndefined();
    expect(await screen.findByText('The crawl couldn’t read it: HTTP 404.')).toBeVisible();
  });

  it('stays on the list, without the name, when no sitemap has that address', async () => {
    sidecar();
    const { router } = await open(`${SITEMAPS}?url=${encodeURIComponent(`${SITE}/nope.xml`)}`);
    expect(await screen.findByRole('treegrid', { name: 'Sitemaps' })).toBeVisible();
    await waitFor(() => expect(searchOf(router).url).toBeUndefined());
    expect(pathOf(router)).toBe(SITEMAPS);
  });
});
