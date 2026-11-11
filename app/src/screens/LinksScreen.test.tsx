import { createMemoryHistory } from '@tanstack/react-router';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { GridFilter, GridQuery } from '../api/grids';
import { answerLinkQuery } from '../api/mock/linkGrid';
import { liveGridPagesOf } from '../api/queries';
import { setTransport, type TransportRequest } from '../api/transport';
import { App } from '../app/App';
import { createQueryClient } from '../app/queryClient';
import { createAppRouter } from '../app/router';
import { onAnnounce } from '../feedback/announce';
import { commands } from '../keyboard/registry';
import { TEST_CRAWL_ID, fakeSidecar, json, problem } from '../test/fakeSidecar';
import { inspectorRoutes, testLinkRow, testUrlDetail } from '../test/inspectorSidecar';
import { ISSUES_CHECKS } from '../test/issuesSidecar';
import {
  LINKS_COUNTS,
  LINKS_FACTS,
  LINKS_URLS,
  linksRoutes,
  testLinkRows,
  type LinksSidecarOptions,
} from '../test/linksSidecar';
import { createQueryClientWithoutBackoff } from '../test/queryClient';
import { wholeText } from '../test/text';
import { LINK_PRESETS } from './links/presets';

const LINKS = `/crawls/${TEST_CRAWL_ID}/links`;

function sidecar(options: LinksSidecarOptions = {}, first: Parameters<typeof fakeSidecar>[0] = []) {
  const { routes, state } = linksRoutes(options);
  const analyse = vi.fn(() => new Response(null, { status: 202 }));
  const transport = fakeSidecar([...first, ['POST', /^\/api\/crawls\/[0-9a-f]{16}\/analyze$/, analyse], ...routes]);
  setTransport(transport);
  return { transport, state, analyse };
}

async function open(path: string, queryClient = createQueryClient()) {
  const history = createMemoryHistory({ initialEntries: [path] });
  const router = createAppRouter(queryClient, history);
  render(<App router={router} queryClient={queryClient} />);
  await act(() => router.load());
  return { router, history, queryClient, user: userEvent.setup() };
}

const header = async () => (await screen.findByRole('heading', { level: 1, name: 'Links' })).parentElement!;
const counted = async (words: string) =>
  expect(await within(await header()).findByText(wholeText(words))).toBeVisible();
const grid = () => screen.findByRole('grid', { name: 'Links' });
const filtersOf = (router: { state: { location: { search: unknown } } }) =>
  (router.state.location.search as { filter?: GridFilter[] }).filter;
const linkRequests = (transport: { requests: TransportRequest[] }) =>
  transport.requests.filter((request) => request.path.endsWith('/links')).map((request) => request.body as GridQuery);

let said: string[] = [];
let stopListening: () => void = () => undefined;
beforeEach(() => {
  said = [];
  stopListening = onAnnounce((message) => said.push(message));
});
afterEach(() => {
  stopListening();
  setTransport(fakeSidecar([]));
});

describe('the Links grid', () => {
  it('lists every link in crawl order, each end as its path, with what the link is and how its target answered', async () => {
    const { transport } = sidecar();
    await open(LINKS);
    await counted('13 links');
    const table = await grid();
    const second = (await within(table).findByText('About us')).closest<HTMLElement>('[role="row"]')!;
    expect(within(second).getByText('/')).toBeVisible();
    expect(within(second).getByText('/about')).toBeVisible();
    expect(within(second).getByText('Hyperlink')).toBeVisible();
    expect(within(second).getByText('Navigation')).toBeVisible();
    expect(within(second).getByText('Followed')).toBeVisible();
    expect(within(second).getByText('200')).toBeVisible();
    expect(
      within(table)
        .getAllByRole('columnheader')
        .map((cell) => cell.textContent),
    ).toEqual(['Source URL', 'Target URL', 'Anchor or alt text', 'Kind', 'Position', 'Follow', 'Target status']);
    // Crawl order is the engine's default: nothing is asked of it but the columns.
    expect(linkRequests(transport)[0]).toMatchObject({ sort: [], filters: [], offset: 0 });
  });

  it('says what happened to a target with no status, by how the crawl left it', async () => {
    sidecar();
    await open(LINKS);
    const table = await grid();
    const refused = (await within(table).findByText('Partner')).closest<HTMLElement>('[role="row"]')!;
    expect(within(refused).getByText('Blocked by robots.txt')).toBeVisible();
    expect(within(refused).getByText('nofollow, sponsored')).toBeVisible();
    // The never-fetched target of a link with no text.
    const hidden = within(table).getByText('/hidden').closest<HTMLElement>('[role="row"]')!;
    expect(within(hidden).getByText('Not fetched')).toBeVisible();
    expect(within(hidden).getByText('None')).toBeVisible();
    expect(within(hidden).getByText('nofollow')).toBeVisible();
    // A kind that isn't followed or not has no word for it, and no status of 0.
    const canonical = within(table).getByText('Canonical').closest<HTMLElement>('[role="row"]')!;
    expect(within(canonical).getAllByText('–')).toHaveLength(1);
  });

  it('lands each preset on its count, as chips that say what it filters by', async () => {
    sidecar();
    const { router, user } = await open(LINKS);
    await counted('13 links');
    const views = screen.getByRole('group', { name: 'Views' });
    expect(within(views).getByRole('button', { name: 'All links' })).toHaveAttribute('aria-pressed', 'true');
    const expected: Record<string, number> = {
      broken: LINKS_COUNTS.broken,
      redirects: LINKS_COUNTS.redirects,
      nofollow: LINKS_COUNTS.nofollow,
      'no-text': LINKS_COUNTS.noText,
      external: LINKS_COUNTS.external,
    };
    for (const preset of LINK_PRESETS.filter((candidate) => candidate.id !== 'all')) {
      await user.click(within(views).getByRole('button', { name: preset.title }));
      const landed = expected[preset.id]!;
      await counted(`${landed} of 13 links`);
      expect(filtersOf(router)).toEqual(preset.filters);
      expect(within(views).getByRole('button', { name: preset.title })).toHaveAttribute('aria-pressed', 'true');
      expect(within(views).getByRole('button', { name: 'All links' })).toHaveAttribute('aria-pressed', 'false');
    }
    const bar = screen.getByRole('group', { name: 'Filters' });
    expect(within(bar).getByText('another site')).toBeVisible();
    await user.click(within(views).getByRole('button', { name: 'All links' }));
    await counted('13 links');
    expect(filtersOf(router)).toBeUndefined();
  });

  it('counts the links in view by value, and narrows to exactly a value’s count', async () => {
    sidecar();
    const { router, user } = await open(LINKS);
    await counted('13 links');
    await user.click(screen.getByRole('button', { name: 'Filter by kind' }));
    const menu = await screen.findByRole('menu');
    expect(within(menu).getByText('Kind, of 13 links')).toBeVisible();
    expect(
      within(menu)
        .getAllByRole('menuitem')
        .map((item) => item.textContent),
    ).toEqual(['Hyperlink9, 9 links', 'Image2, 2 links', 'Canonical1, 1 link', 'Redirect1, 1 link']);
    await user.click(within(menu).getByRole('menuitem', { name: /^Image/ }));
    await counted('2 of 13 links');
    expect(filtersOf(router)).toEqual([{ column: 'kind', op: 'in', values: ['image'] }]);
    // The next facet's counts are of the links now in view, and add up to them.
    await user.click(screen.getByRole('button', { name: 'Filter by position' }));
    const positions = await screen.findByRole('menu');
    expect(within(positions).getByText('Position, of 2 links')).toBeVisible();
    await user.click(within(positions).getByRole('menuitem', { name: /^Header/ }));
    await counted('1 of 13 links');
    expect(filtersOf(router)).toEqual([
      { column: 'kind', op: 'in', values: ['image'] },
      { column: 'position', op: 'in', values: ['header'] },
    ]);
    // The keyboard is still on the facet's button: the menu gave it back.
    await waitFor(() => expect(screen.getByRole('button', { name: 'Filter by position' })).toHaveFocus());
  });

  it('says so when the counts can’t be read, and the grid works without them', async () => {
    sidecar({ facetsFail: true });
    const { user } = await open(LINKS, createQueryClientWithoutBackoff());
    await counted('13 links');
    await user.click(screen.getByRole('button', { name: 'Filter by follow' }));
    const menu = await screen.findByRole('menu');
    expect(await within(menu).findByText('The counts couldn’t be read.')).toBeVisible();
    expect(within(menu).getByRole('menuitem', { name: 'Try again' })).toBeVisible();
  });
});

describe('the Links filter bar', () => {
  it('reads a link’s filters as sentences, and gives focus somewhere to go when one is removed', async () => {
    sidecar();
    const filter = JSON.stringify([
      { column: 'to.statusClass', op: 'in', values: ['4xx', '5xx'] },
      { column: 'to.id', op: 'eq', value: 4 },
    ]);
    const { router, user } = await open(`${LINKS}?filter=${encodeURIComponent(filter)}`);
    await counted('2 of 13 links');
    const bar = screen.getByRole('group', { name: 'Filters' });
    expect(within(bar).getByRole('button', { name: 'Target status class is 4xx or 5xx' })).toBeVisible();
    // A URL named by its id reads as the URL, as the grid shows it. Its chip is read and removed, not edited: no
    // one types a URL's id.
    expect(await within(bar).findByText('/gone')).toBeVisible();
    expect(within(bar).queryByRole('button', { name: 'Target URL is /gone' })).toBeNull();
    await user.click(within(bar).getByRole('button', { name: 'Remove the filter Target URL is /gone' }));
    await counted('3 of 13 links');
    // The chip went: the keyboard is on the one that's left, where Delete removes that too.
    expect(within(bar).getByRole('button', { name: 'Target status class is 4xx or 5xx' })).toHaveFocus();
    await user.keyboard('{Delete}');
    await counted('13 links');
    expect(filtersOf(router)).toBeUndefined();
    expect(within(bar).getByRole('button', { name: 'Add filter' })).toHaveFocus();
  });

  it('reads a filter by an end’s check as the check’s title, never its id', async () => {
    // The sidecar's own filter: the links whose target a check flagged. The tests' mock has no issues by link end, so
    // this sidecar answers it as "every link": what's under test is the chip's words.
    const answered = (request: TransportRequest) => {
      const query = request.body as GridQuery;
      const filters = query.filters.filter((filter) => !filter.column.endsWith('.check'));
      const answer = answerLinkQuery(testLinkRows(), { ...query, filters }, { available: true, reason: null });
      return answer.ok ? json(answer.page) : problem(answer.problem);
    };
    const { transport } = sidecar({}, [
      ['POST', /^\/api\/crawls\/[0-9a-f]{16}\/links$/, answered],
      ['GET', /^\/api\/checks$/, () => json(ISSUES_CHECKS)],
    ]);
    const filter = JSON.stringify([{ column: 'to.check', op: 'in', values: ['internal-client-error'] }]);
    await open(`${LINKS}?filter=${encodeURIComponent(filter)}`);
    await counted('13 links');
    const bar = screen.getByRole('group', { name: 'Filters' });
    // The catalog's title, as the URL grid's chip for a check reads.
    expect(
      await within(bar).findByRole('button', { name: 'Remove the filter Target check is Internal URL returns 4xx' }),
    ).toBeVisible();
    expect(within(bar).queryByText('internal-client-error')).toBeNull();
    expect(screen.queryByText(/internal-client-error/)).toBeNull();
    // The catalog was read for it, once; a view that names no check never reads it.
    expect(transport.requests.filter((request) => request.path === '/api/checks')).toHaveLength(1);
  });

  it('adds a filter on either end by the column’s type, and edits one from its chip', async () => {
    sidecar();
    const { router, user } = await open(LINKS);
    await counted('13 links');
    const bar = screen.getByRole('group', { name: 'Filters' });
    await user.click(within(bar).getByRole('button', { name: 'Add filter' }));
    const editor = await screen.findByRole('dialog', { name: 'Add a filter' });
    // Its target's status code, at least 400.
    await user.click(within(editor).getByRole('button', { name: /^About:/ }));
    await user.click(await screen.findByRole('menuitemradio', { name: 'Its target' }));
    await user.click(within(editor).getByRole('button', { name: /^Column:/ }));
    await user.click(await screen.findByRole('menuitemradio', { name: 'Status' }));
    await user.click(within(editor).getByRole('button', { name: /^Condition:/ }));
    await user.click(await screen.findByRole('menuitemradio', { name: 'is at least' }));
    // What's wrong is said under the field, and nothing is applied.
    await user.click(within(editor).getByRole('button', { name: 'Add filter' }));
    expect(within(editor).getByText('Give a number.')).toBeVisible();
    expect(filtersOf(router)).toBeUndefined();
    await user.type(within(editor).getByRole('textbox', { name: 'Number' }), '400{Enter}');
    await counted('3 of 13 links');
    expect(filtersOf(router)).toEqual([{ column: 'to.statusCode', op: 'gte', value: 400 }]);
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());

    // Its chip opens the same editor on it.
    await user.click(within(bar).getByRole('button', { name: 'Target status is at least 400' }));
    const editing = await screen.findByRole('dialog', { name: 'Change the filter' });
    const number = within(editing).getByRole('textbox', { name: 'Number' });
    expect(number).toHaveValue('400');
    await user.clear(number);
    await user.type(number, '500');
    await user.click(within(editing).getByRole('button', { name: 'Apply' }));
    await counted('1 of 13 links');
    expect(filtersOf(router)).toEqual([{ column: 'to.statusCode', op: 'gte', value: 500 }]);
    // The editor gave the keyboard back to the chip it came from.
    await waitFor(() =>
      expect(within(bar).getByRole('button', { name: 'Target status is at least 500' })).toHaveFocus(),
    );
  });

  it('filters by some of an enum’s values, and clears every filter at once', async () => {
    sidecar();
    const { router, user } = await open(LINKS);
    await counted('13 links');
    const bar = screen.getByRole('group', { name: 'Filters' });
    await user.click(within(bar).getByRole('button', { name: 'Add filter' }));
    const editor = await screen.findByRole('dialog', { name: 'Add a filter' });
    await user.click(within(editor).getByRole('checkbox', { name: 'Image' }));
    await user.click(within(editor).getByRole('checkbox', { name: 'Canonical' }));
    await user.click(within(editor).getByRole('button', { name: 'Add filter' }));
    await counted('3 of 13 links');
    expect(filtersOf(router)).toEqual([{ column: 'kind', op: 'in', values: ['image', 'canonical'] }]);
    expect(within(bar).getByRole('button', { name: 'Kind is Image or Canonical' })).toBeVisible();
    await user.click(within(bar).getByRole('button', { name: 'Clear filters' }));
    await counted('13 links');
    expect(filtersOf(router)).toBeUndefined();
    expect(within(bar).getByRole('button', { name: 'Add filter' })).toHaveFocus();
  });

  it('lists a column the analysis fills, and won’t take it, while there’s no analysis', async () => {
    sidecar({ item: { analyzedAt: null }, summary: { available: false, reason: 'notAnalyzed' } });
    const { user } = await open(LINKS);
    await counted('13 links');
    await user.click(screen.getByRole('button', { name: 'Add filter' }));
    const editor = await screen.findByRole('dialog', { name: 'Add a filter' });
    await user.click(within(editor).getByRole('button', { name: /^About:/ }));
    await user.click(await screen.findByRole('menuitemradio', { name: 'Its target' }));
    await user.click(within(editor).getByRole('button', { name: /^Column:/ }));
    expect(await screen.findByRole('menuitemradio', { name: 'Inlinks (needs an analysis)' })).toHaveAttribute(
      'aria-disabled',
      'true',
    );
  });
});

describe('searching the links', () => {
  it('searches on Enter, says the count once, and clears', async () => {
    const { transport } = sidecar();
    const { router, user } = await open(LINKS);
    await counted('13 links');
    const field = screen.getByRole('searchbox', { name: 'Search anchor text and URLs' });
    await user.type(field, 'gone{Enter}');
    await counted('2 of 13 links');
    expect(router.state.location.search).toMatchObject({ q: 'gone' });
    expect(linkRequests(transport).at(-1)).toMatchObject({ search: 'gone' });
    // The field kept the keyboard: nothing was taken away under it.
    expect(field).toHaveFocus();
    await waitFor(() => expect(said).toContain('2 of 13 links'));
    expect(said.filter((message) => message === '2 of 13 links')).toHaveLength(1);
    await user.click(screen.getByRole('button', { name: 'Clear the search' }));
    await counted('13 links');
    expect((router.state.location.search as { q?: string }).q).toBeUndefined();
  });

  it('focuses the search on Ctrl+F, from the registry', async () => {
    sidecar();
    await open(LINKS);
    await counted('13 links');
    expect(commands.get('links.search')?.keys).toEqual(['mod+f']);
    act(() => void commands.run('links.search'));
    expect(screen.getByRole('searchbox', { name: 'Search anchor text and URLs' })).toHaveFocus();
  });
});

describe('sorting the links', () => {
  it('sorts by a column of the target from its header, and says why another column doesn’t sort', async () => {
    const { transport } = sidecar();
    const { router, user } = await open(LINKS);
    await counted('13 links');
    const table = await grid();
    const status = within(table).getByRole('columnheader', { name: /Target status/ });
    await user.click(within(status).getByRole('button'));
    await waitFor(() =>
      expect(router.state.location.search).toMatchObject({ sort: [{ column: 'to.statusCode', desc: false }] }),
    );
    await waitFor(() =>
      expect(linkRequests(transport).at(-1)?.sort).toEqual([{ column: 'to.statusCode', desc: false }]),
    );
    const sorted = await grid();
    await waitFor(() =>
      expect(within(sorted).getByRole('columnheader', { name: /Target status/ })).toHaveAttribute(
        'aria-sort',
        'ascending',
      ),
    );
    await user.click(within(within(sorted).getByRole('columnheader', { name: /Target status/ })).getByRole('button'));
    await waitFor(() =>
      expect(router.state.location.search).toMatchObject({ sort: [{ column: 'to.statusCode', desc: true }] }),
    );
    // A link's own columns have no button, and say why.
    const text = within(await grid()).getByRole('columnheader', { name: 'Anchor or alt text' });
    expect(within(text).queryByRole('button')).toBeNull();
    expect(text).toHaveAttribute(
      'title',
      'Links can’t be sorted by anchor or alt text: they sort by crawl order, or by a column of the target.',
    );
  });

  it('drops the sort with its column when the column is hidden: no order is left that no header shows', async () => {
    const { transport } = sidecar();
    const { router, user } = await open(LINKS);
    await counted('13 links');
    const status = within(await grid()).getByRole('columnheader', { name: /Target status/ });
    await user.click(within(status).getByRole('button'));
    await waitFor(() =>
      expect(linkRequests(transport).at(-1)?.sort).toEqual([{ column: 'to.statusCode', desc: false }]),
    );
    // Hiding another column leaves the sort alone.
    await user.click(screen.getByRole('button', { name: 'Columns' }));
    const chooser = await screen.findByRole('dialog', { name: 'Columns' });
    await user.click(within(chooser).getByRole('checkbox', { name: 'Kind' }));
    await waitFor(() => expect((router.state.location.search as { cols?: string[] }).cols).not.toContain('kind'));
    expect(router.state.location.search).toMatchObject({ sort: [{ column: 'to.statusCode', desc: false }] });
    // Hiding the sorted column takes the sort with it, in the same step: the links are in crawl order again.
    await user.click(
      within(within(chooser).getByRole('group', { name: 'Its target' })).getByRole('checkbox', { name: 'Status' }),
    );
    await waitFor(() =>
      expect(router.state.location.search).toEqual({ cols: ['from.url', 'to.url', 'text', 'position', 'follow'] }),
    );
    await waitFor(() => expect(linkRequests(transport).at(-1)?.sort).toEqual([]));
    expect(
      within(await grid())
        .getAllByRole('columnheader')
        .filter((header) => header.hasAttribute('aria-sort') && header.getAttribute('aria-sort') !== 'none'),
    ).toEqual([]);
    // Shown again, the column comes back unsorted: the sort was dropped, not kept out of sight.
    await user.click(
      within(within(chooser).getByRole('group', { name: 'Its target' })).getByRole('checkbox', { name: 'Status' }),
    );
    await waitFor(() => expect((router.state.location.search as { cols?: string[] }).cols).toContain('to.statusCode'));
    expect((router.state.location.search as { sort?: unknown }).sort).toBeUndefined();
  });

  it('drops a sort the view arrives with by a column it doesn’t show, in place, and never reads by it', async () => {
    const { transport } = sidecar();
    const cols = encodeURIComponent(JSON.stringify(['from.url', 'to.url', 'text']));
    const { router, history } = await open(`${LINKS}?sort=-to.statusCode&cols=${cols}`);
    await counted('13 links');
    await waitFor(() => expect(router.state.location.search).toEqual({ cols: ['from.url', 'to.url', 'text'] }));
    // In place: back doesn't return to the sorted address.
    expect(history.length).toBe(1);
    expect(linkRequests(transport).every((request) => request.sort.length === 0)).toBe(true);
  });

  it('says why on Enter, for the keyboard, on a header that doesn’t sort', async () => {
    sidecar();
    await open(LINKS);
    await counted('13 links');
    const table = await grid();
    await within(table).findByText('About us');
    act(() => table.focus());
    fireEvent.keyDown(table, { key: 'ArrowUp' });
    fireEvent.keyDown(table, { key: 'Enter' });
    expect(said).toContain(
      'Links can’t be sorted by source URL: they sort by crawl order, or by a column of the target.',
    );
  });
});

describe('a link’s two ends', () => {
  it('opens the inspector on the end the cursor is in, and follows the cursor down that end', async () => {
    sidecar({}, inspectorRoutes({ urls: () => testUrlDetail() }));
    const { router } = await open(LINKS);
    await counted('13 links');
    const table = await grid();
    await within(table).findByText('About us');
    act(() => table.focus());
    // The cursor starts in the source's column: Enter opens the page the link is on.
    fireEvent.keyDown(table, { key: 'ArrowDown' });
    fireEvent.keyDown(table, { key: 'Enter' });
    await waitFor(() => expect(router.state.location.search).toMatchObject({ inspect: 1 }));
    // From the target's column, the link's target; and the inspector then follows the cursor down the targets.
    fireEvent.keyDown(table, { key: 'ArrowRight' });
    fireEvent.keyDown(table, { key: 'Enter' });
    await waitFor(() => expect(router.state.location.search).toMatchObject({ inspect: 2 }));
    fireEvent.keyDown(table, { key: 'ArrowDown' });
    await waitFor(() => expect(router.state.location.search).toMatchObject({ inspect: 3 }));
    fireEvent.keyDown(table, { key: 'ArrowDown' });
    await waitFor(() => expect(router.state.location.search).toMatchObject({ inspect: 4 }));
  });

  it('keeps the end the inspector is on in the route: the source by name, the target by default', async () => {
    sidecar({}, inspectorRoutes({ urls: () => testUrlDetail() }));
    const { router } = await open(LINKS);
    await counted('13 links');
    const table = await grid();
    await within(table).findByText('About us');
    act(() => table.focus());
    fireEvent.keyDown(table, { key: 'ArrowDown' });
    fireEvent.keyDown(table, { key: 'Enter' });
    await waitFor(() => expect(router.state.location.search).toEqual({ inspect: 1, end: 'from' }));
    fireEvent.keyDown(table, { key: 'ArrowRight' });
    fireEvent.keyDown(table, { key: 'Enter' });
    await waitFor(() => expect(router.state.location.search).toEqual({ inspect: 2 }));
  });

  it('goes on following the sources after a reload, back, forward or a link to the view', async () => {
    sidecar({}, inspectorRoutes({ urls: () => testUrlDetail() }));
    // The view as its address gives it, on a screen mounted afresh: the inspector on the home page, as the page the
    // first links are on.
    const { router } = await open(`${LINKS}?inspect=1&end=from`);
    await counted('13 links');
    const table = await grid();
    await within(table).findByText('About us');
    act(() => table.focus());
    // Seven rows down is the first link on another page: /about's link home. Its source is /about, its target the
    // home page the inspector is already on.
    for (let row = 0; row < 7; row++) fireEvent.keyDown(table, { key: 'ArrowDown' });
    await waitFor(() => expect(router.state.location.search).toEqual({ inspect: 2, end: 'from' }));
    // And a row back up, the home page again, as the page that row's link is on.
    fireEvent.keyDown(table, { key: 'ArrowUp' });
    await waitFor(() => expect(router.state.location.search).toEqual({ inspect: 1, end: 'from' }));
  });

  it('follows the targets from an address that names no end', async () => {
    sidecar({}, inspectorRoutes({ urls: () => testUrlDetail() }));
    const { router } = await open(`${LINKS}?inspect=1`);
    await counted('13 links');
    const table = await grid();
    await within(table).findByText('About us');
    act(() => table.focus());
    fireEvent.keyDown(table, { key: 'ArrowDown' });
    await waitFor(() => expect(router.state.location.search).toEqual({ inspect: 2 }));
    fireEvent.keyDown(table, { key: 'ArrowDown' });
    await waitFor(() => expect(router.state.location.search).toEqual({ inspect: 3 }));
  });

  it('opens either end from its URL, and the target from a double-click anywhere else on the row', async () => {
    sidecar({}, inspectorRoutes({ urls: () => testUrlDetail() }));
    const { router, user } = await open(LINKS);
    await counted('13 links');
    const table = await grid();
    const row = (await within(table).findByText('About us')).closest<HTMLElement>('[role="row"]')!;
    // Each end's URL is a button in the app: its name is the whole URL, whatever of it the cell has room for.
    const source = within(row).getByRole('button', { name: 'https://example.com/' });
    const target = within(row).getByRole('button', { name: 'https://example.com/about' });
    await user.click(target);
    await waitFor(() => expect(router.state.location.search).toMatchObject({ inspect: 2 }));
    await user.click(source);
    await waitFor(() => expect(router.state.location.search).toMatchObject({ inspect: 1 }));
    await user.dblClick(within(table).getByText('Old page'));
    await waitFor(() => expect(router.state.location.search).toMatchObject({ inspect: 3 }));
  });

  it('lists a URL’s inlinks in Links from the inspector, the same links by the same count', async () => {
    const inlinks = [
      testLinkRow({ id: 1, url: 'https://example.com/' }),
      testLinkRow({ id: 3, url: 'https://example.com/old' }),
    ];
    sidecar({}, inspectorRoutes({ urls: () => testUrlDetail(), inlinks: { 2: inlinks } }));
    const { router, user } = await open(`${LINKS}?inspect=2`);
    await counted('13 links');
    await user.click(await screen.findByRole('tab', { name: /^Inlinks/ }));
    const all = await screen.findByRole('link', { name: 'All 2 in Links' });
    await user.click(all);
    await counted('2 of 13 links');
    expect(router.state.location.search).toEqual({
      filter: [
        { column: 'to.id', op: 'eq', value: 2 },
        { column: 'selfLink', op: 'is', value: false },
      ],
    });
    const bar = screen.getByRole('group', { name: 'Filters' });
    expect(await within(bar).findByText('/about')).toBeVisible();
    expect(within(bar).getByText('doesn’t lead to')).toBeVisible();
  });

  it('says “Show it in Links” of a URL’s one link, never “All 1”, and lands on that link', async () => {
    const inlinks = [testLinkRow({ id: 1, url: 'https://example.com/' })];
    sidecar({}, inspectorRoutes({ urls: () => testUrlDetail(), inlinks: { 3: inlinks } }));
    const { router, user } = await open(`${LINKS}?inspect=3`);
    await counted('13 links');
    await user.click(await screen.findByRole('tab', { name: /^Inlinks/ }));
    const one = await screen.findByRole('link', { name: 'Show it in Links' });
    expect(screen.queryByRole('link', { name: /^All 1 / })).toBeNull();
    await user.click(one);
    await counted('1 of 13 links');
    expect(router.state.location.search).toEqual({
      filter: [
        { column: 'to.id', op: 'eq', value: 3 },
        { column: 'selfLink', op: 'is', value: false },
      ],
    });
  });
});

describe('the links’ columns', () => {
  it('shows and hides a column from the chooser, at once, and goes back to the default set', async () => {
    const { transport } = sidecar();
    const { router, user } = await open(LINKS);
    await counted('13 links');
    await user.click(screen.getByRole('button', { name: 'Columns' }));
    const chooser = await screen.findByRole('dialog', { name: 'Columns' });
    expect(within(chooser).getByRole('checkbox', { name: 'Follow' })).toBeChecked();
    await user.type(within(chooser).getByRole('searchbox', { name: 'Find a column' }), 'indexab');
    // Both ends have one: each under its own heading.
    const found = within(chooser).getAllByRole('checkbox');
    expect(found.map((box) => box.getAttribute('aria-checked'))).toEqual(['false', 'false', 'false', 'false']);
    await user.click(
      within(within(chooser).getByRole('group', { name: 'Its target' })).getByRole('checkbox', {
        name: 'Indexability',
      }),
    );
    await waitFor(() =>
      expect((router.state.location.search as { cols?: string[] }).cols).toEqual([
        'from.url',
        'to.url',
        'text',
        'kind',
        'position',
        'follow',
        'to.statusCode',
        'to.indexability',
      ]),
    );
    expect(await within(await grid()).findByRole('columnheader', { name: /Target indexability/ })).toBeVisible();
    await waitFor(() => expect(linkRequests(transport).at(-1)?.columns).toContain('to.indexability'));
    // The grid behind kept its count while the rows were read again with the new column.
    await counted('13 links');
    await user.click(within(chooser).getByRole('button', { name: 'Reset to the default columns' }));
    await waitFor(() => expect((router.state.location.search as { cols?: string[] }).cols).toBeUndefined());
    expect(within(chooser).getByRole('searchbox', { name: 'Find a column' })).toHaveFocus();
  });

  it('leaves out a column this engine doesn’t have, from a link made by another version', async () => {
    const { transport } = sidecar();
    await open(`${LINKS}?cols=${encodeURIComponent(JSON.stringify(['to.url', 'to.madeUp', 'kind']))}`);
    await counted('13 links');
    expect(
      within(await grid())
        .getAllByRole('columnheader')
        .map((cell) => cell.textContent),
    ).toEqual(['Target URL', 'Kind']);
    expect(linkRequests(transport).every((query) => !query.columns?.includes('to.madeUp'))).toBe(true);
  });
});

describe('copying from the links', () => {
  it('copies the target URLs of the view, each once', async () => {
    sidecar();
    const { user } = await open(LINKS);
    // The clipboard is user-event's own once it's set up: the copy is watched there.
    const writeText = vi.spyOn(navigator.clipboard, 'writeText').mockResolvedValue();
    await counted('13 links');
    await user.click(screen.getByRole('button', { name: 'Broken links' }));
    await counted('3 of 13 links');
    act(() => void commands.run('links.copyTargets'));
    await waitFor(() => expect(writeText).toHaveBeenCalledWith('https://example.com/gone\nhttps://example.com/error'));
    await waitFor(() => expect(said).toContain('Copied 2 target URLs.'));
  });

  it('says which links a copy is of when the view has more than it reads, in the command and when it’s done', async () => {
    // 10,003 links: the first 10,000 all lead to /about, as a view sorted by its target's status begins with one
    // target's links; the last three lead elsewhere.
    const facts = [
      ...Array.from({ length: 10_000 }, () => ({ ...LINKS_FACTS[1]! })),
      { ...LINKS_FACTS[2]! },
      { ...LINKS_FACTS[3]! },
      { ...LINKS_FACTS[9]! },
    ];
    sidecar({ links: testLinkRows(facts, LINKS_URLS) });
    const { user } = await open(LINKS);
    const writeText = vi.spyOn(navigator.clipboard, 'writeText').mockResolvedValue();
    await counted('10,003 links');
    // The commands say they are of the first 10,000 links: in the menu, and in the palette by the same title.
    await user.click(screen.getByRole('button', { name: 'Copy' }));
    const menu = await screen.findByRole('menu');
    expect(
      within(menu)
        .getAllByRole('menuitem')
        .map((item) => item.textContent.replace(/Ctrl.*$/, '')),
    ).toEqual([
      'Copy the first 10,000 links’ target URLs',
      'Copy the first 10,000 links’ source URLs',
      'Copy the Source URL column of the first 10,000 links',
    ]);
    expect(commands.get('links.copyTargets')?.title).toBe('Copy the first 10,000 links’ target URLs');
    await user.click(within(menu).getByRole('menuitem', { name: /target URLs/ }));
    await waitFor(() => expect(writeText).toHaveBeenCalledWith('https://example.com/about'));
    // One URL was copied, and it isn't said as the view's: the view's other targets weren't read.
    await waitFor(() =>
      expect(said).toContain(
        'Copied the first 10,000 links’ target URLs. 1 URL, each once. The view has 10,003 links: a copy takes no more than 10,000.',
      ),
    );
    expect(await screen.findByText('Copied the first 10,000 links’ target URLs')).toBeVisible();
    expect(said.join(' ')).not.toMatch(/Copied 1 target URL/);
  });

  it('names the copies plainly on a view a copy reads whole', async () => {
    sidecar();
    const { user } = await open(LINKS);
    await counted('13 links');
    await user.click(screen.getByRole('button', { name: 'Copy' }));
    const menu = await screen.findByRole('menu');
    expect(within(menu).getByRole('menuitem', { name: /^Copy the target URLs/ })).toBeVisible();
    expect(within(menu).getByRole('menuitem', { name: /^Copy the source URLs/ })).toBeVisible();
    expect(within(menu).queryByText(/first 10,000/)).toBeNull();
  });
});

describe('the Links grid’s states', () => {
  it('says what a crawl never analysed still has here, and offers to analyse it', async () => {
    const { analyse } = sidecar({ item: { analyzedAt: null }, summary: { available: false, reason: 'notAnalyzed' } });
    const { user } = await open(LINKS);
    await counted('13 links');
    const note = await screen.findByText(
      /This crawl hasn’t been analysed yet\. Every link is here to search, filter and sort/,
    );
    expect(note).toHaveTextContent(
      'The columns an analysis fills (inlinks, issues, where redirects end) are empty until it’s analysed.',
    );
    await user.click(screen.getByRole('button', { name: 'Analyse it' }));
    await waitFor(() => expect(analyse).toHaveBeenCalledOnce());
  });

  it('names the columns shown that wait for an analysis another version made', async () => {
    sidecar({ summary: { available: false, reason: 'engineChanged' } });
    await open(`${LINKS}?cols=${encodeURIComponent(JSON.stringify(['to.url', 'to.inlinks']))}`);
    await counted('13 links');
    expect(
      await screen.findByText(
        /Another version of Shouting Iguana analysed this crawl\. .*and Target inlinks is empty until it’s analysed again\./,
      ),
    ).toBeVisible();
    expect(screen.getByRole('button', { name: 'Analyse again' })).toBeVisible();
    // The column is there, with a dash and no 0.
    const table = await grid();
    expect(within(table).getByRole('columnheader', { name: /Target inlinks/ })).toBeVisible();
    await waitFor(() => expect(within(table).getAllByText('–').length).toBeGreaterThanOrEqual(5));
    expect(within(table).queryByText('0')).toBeNull();
  });

  it('turns a filter on such a column into the engine’s reason, with what can work', async () => {
    sidecar({ item: { analyzedAt: null }, summary: { available: false, reason: 'notAnalyzed' } });
    const filter = JSON.stringify([{ column: 'to.inlinks', op: 'gte', value: 2 }]);
    const { router, user } = await open(
      `${LINKS}?filter=${encodeURIComponent(filter)}`,
      createQueryClientWithoutBackoff(),
    );
    expect(
      await screen.findByText(
        'Filtering or sorting by these columns needs an analysis, and this crawl hasn’t been analysed yet.',
      ),
    ).toBeVisible();
    expect(screen.queryByRole('button', { name: 'Try again' })).toBeNull();
    const clear = screen.getByRole('button', { name: 'Clear the sort and filters' });
    clear.focus();
    await user.click(clear);
    await counted('13 links');
    expect(router.state.location.search).toEqual({});
    // The button went with what it cleared: the keyboard is in the grid that came.
    await waitFor(() => expect(screen.getByRole('grid', { name: 'Links' })).toHaveFocus());
  });

  it.each(['crawlChanged', 'engineChanged'] as const)(
    'offers to analyse again, in those words, a crawl that was analysed before (%s): never "Analyse it"',
    async (reason) => {
      const { analyse } = sidecar({ item: { outOfDate: true }, summary: { available: false, reason } });
      const filter = JSON.stringify([{ column: 'to.inlinks', op: 'gte', value: 2 }]);
      const { user } = await open(`${LINKS}?filter=${encodeURIComponent(filter)}`, createQueryClientWithoutBackoff());
      await screen.findByText(/^Filtering or sorting by these columns needs an analysis, and /);
      // One button for it in the view, worded as the crawl's own control words it.
      const main = screen.getByRole('main');
      const again = within(main).getByRole('button', { name: 'Analyse again' });
      expect(within(main).queryByRole('button', { name: 'Analyse it' })).toBeNull();
      await user.click(again);
      await waitFor(() => expect(analyse).toHaveBeenCalledOnce());
    },
  );

  it('offers to analyse a crawl that never was, in those words', async () => {
    sidecar({ item: { analyzedAt: null }, summary: { available: false, reason: 'notAnalyzed' } });
    const filter = JSON.stringify([{ column: 'to.inlinks', op: 'gte', value: 2 }]);
    await open(`${LINKS}?filter=${encodeURIComponent(filter)}`, createQueryClientWithoutBackoff());
    await screen.findByText(/^Filtering or sorting by these columns needs an analysis, and /);
    const main = screen.getByRole('main');
    expect(within(main).getByRole('button', { name: 'Analyse it' })).toBeVisible();
    expect(within(main).queryByRole('button', { name: 'Analyse again' })).toBeNull();
  });

  it('says the crawl is being analysed while it is, with nothing to press', async () => {
    sidecar({
      item: { status: 'analyzing', analyzedAt: null },
      summary: { available: false, reason: 'notAnalyzed' },
    });
    await open(LINKS);
    await counted('13 links');
    expect(await screen.findByText(/^This crawl is being analysed\. Every link is here to search/)).toBeVisible();
    expect(screen.queryByText(/hasn’t been analysed yet/)).toBeNull();
    expect(within(screen.getByRole('main')).queryByRole('button', { name: /^Analyse/ })).toBeNull();
  });

  it('still lists the links when the engine’s list of columns can’t be read, and says what isn’t there', async () => {
    sidecar({ columnsFail: true });
    const { user } = await open(LINKS, createQueryClientWithoutBackoff());
    await counted('13 links');
    const note = await screen.findByText(/The list of the links’ columns couldn’t be read\./);
    expect(note).toHaveTextContent('they can’t be sorted, given another column or filtered by one until it is');
    const table = await grid();
    expect(await within(table).findByText('About us')).toBeVisible();
    // Nothing is offered that can't be done: no sort buttons, no Add filter, and Columns can't be pressed.
    expect(within(table).queryAllByRole('button', { name: /Target status/ })).toHaveLength(0);
    expect(screen.queryByRole('button', { name: 'Add filter' })).toBeNull();
    expect(screen.getByRole('button', { name: 'Columns' })).toBeDisabled();
    // The presets and the facets are the engine's own filters: they still work.
    await user.click(screen.getByRole('button', { name: 'Broken links' }));
    await counted('3 of 13 links');
    // The list comes back: the grid sorts again, and the keyboard isn't left on a button that went.
    const { routes } = linksRoutes();
    setTransport(fakeSidecar(routes));
    const again = screen.getByRole('button', { name: 'Try again' });
    again.focus();
    await user.click(again);
    await waitFor(() => expect(screen.queryByText(/couldn’t be read/)).toBeNull());
    expect(await screen.findByRole('button', { name: 'Add filter' })).toBeVisible();
    await waitFor(() => expect(screen.getByRole('grid', { name: 'Links' })).toHaveFocus());
  });

  it('says there are no links, and why, by whether the crawl is still running', async () => {
    sidecar({ links: [] });
    await open(LINKS);
    expect(await screen.findByText('No links')).toBeVisible();
    expect(screen.getByText('The crawl stored none: it read no page with a link on it.')).toBeVisible();
    expect(screen.queryByRole('button', { name: 'Clear filters' })).toBeNull();
  });

  it('says nothing matches, shows the filters, and clears them in one press', async () => {
    sidecar();
    const filter = JSON.stringify([{ column: 'to.statusClass', op: 'in', values: ['other'] }]);
    const { router, user } = await open(`${LINKS}?filter=${encodeURIComponent(filter)}&q=rattan`);
    expect(await screen.findByText('No links match “rattan” with these filters.')).toBeVisible();
    await counted('0 of 13 links');
    const empty = screen.getByText('No links match “rattan” with these filters.').closest('div')!.parentElement!;
    const clear = within(empty).getByRole('button', { name: /Clear/ });
    clear.focus();
    await user.click(clear);
    await counted('13 links');
    expect(router.state.location.search).toEqual({});
    await waitFor(() => expect(screen.getByRole('grid', { name: 'Links' })).toHaveFocus());
  });

  it('says a read failed in plain words, and tries again', async () => {
    const failing = sidecar({
      linksProblem: { type: 'internal', title: 'Something went wrong in the engine.', status: 500 },
    });
    const { user } = await open(LINKS, createQueryClientWithoutBackoff());
    expect(await screen.findByText('Something went wrong in the engine.')).toBeVisible();
    // The engine comes back.
    const { routes } = linksRoutes();
    setTransport(fakeSidecar(routes));
    await user.click(screen.getByRole('button', { name: 'Try again' }));
    await counted('13 links');
    expect(failing.transport.requests.length).toBeGreaterThan(0);
    // The button went with the error it was in, and nothing navigated: the keyboard is in the grid that came.
    await waitFor(() => expect(screen.getByRole('grid', { name: 'Links' })).toHaveFocus());
  });

  it('turns a query the engine can’t run into a plain sentence, with a way back', async () => {
    sidecar();
    const { router, user } = await open(`${LINKS}?sort=-text`, createQueryClientWithoutBackoff());
    expect(await screen.findByText('This view’s query can’t be run.')).toBeVisible();
    // With what the engine said of it, in place: not only behind Details.
    expect(screen.getByText('The view can’t be sorted by “text”.')).toBeVisible();
    await user.click(screen.getByRole('button', { name: 'Reset the view' }));
    await counted('13 links');
    expect(router.state.location.search).toEqual({});
  });
});

describe('a filter the engine won’t run', () => {
  const BACKTRACKING =
    'This pattern needs backtracking (a backreference, a lookaround or an atomic group), which isn’t supported: rewrite it without.';
  const refusedView = (pattern: string) => {
    const filter = JSON.stringify([
      { column: 'kind', op: 'in', values: ['hyperlink'] },
      { column: 'text', op: 'regex', value: pattern },
    ]);
    const cols = JSON.stringify(['from.url', 'to.url', 'text', 'to.statusCode']);
    return `${LINKS}?filter=${encodeURIComponent(filter)}&cols=${encodeURIComponent(cols)}&sort=to.statusCode`;
  };
  /** The state in the rows' place, by its headline. */
  const refusal = async (headline: string | RegExp) =>
    (await screen.findByText(headline)).closest<HTMLElement>('[role="alert"]')!;

  it('says the engine’s own sentence in place, and opens the filter on its pattern to change it', async () => {
    sidecar();
    const { router, user } = await open(refusedView('(?=x)y'), createQueryClientWithoutBackoff());
    // Which filter, and why, in the engine's words: not a generic sentence with the reason behind Details.
    const alert = await refusal('This filter can’t be run: Anchor or alt text matches (?=x)y');
    expect(within(alert).getByText(BACKTRACKING)).toBeVisible();
    expect(screen.queryByRole('button', { name: 'Reset the view' })).toBeNull();
    // The other filter and the chosen columns are still the view's.
    const bar = screen.getByRole('group', { name: 'Filters' });
    expect(within(bar).getByRole('button', { name: 'Kind is Hyperlink' })).toBeVisible();

    await user.click(within(alert).getByRole('button', { name: 'Change the filter' }));
    const editing = await screen.findByRole('dialog', { name: 'Change the filter' });
    const pattern = within(editing).getByRole('textbox', { name: 'Pattern' });
    expect(pattern).toHaveValue('(?=x)y');
    // The keyboard is in the pattern, which says what the engine said of it.
    await waitFor(() => expect(pattern).toHaveFocus());
    expect(pattern).toBeInvalid();
    expect(pattern).toHaveAccessibleDescription(expect.stringContaining(BACKTRACKING));
    // Changing it takes the sentence away: it was about the pattern as it was.
    await user.clear(pattern);
    expect(within(editing).queryByText(BACKTRACKING)).toBeNull();
    // A pattern the engine runs and a browser wouldn't compile goes to the engine as typed.
    await user.type(pattern, '(?im)^ABOUT');
    await user.click(within(editing).getByRole('button', { name: 'Apply' }));
    await counted('2 of 13 links');
    expect(router.state.location.search).toEqual({
      filter: [
        { column: 'kind', op: 'in', values: ['hyperlink'] },
        { column: 'text', op: 'regex', value: '(?im)^ABOUT' },
      ],
      cols: ['from.url', 'to.url', 'text', 'to.statusCode'],
      sort: [{ column: 'to.statusCode', desc: false }],
    });
    expect(screen.queryByText(/^This filter can’t be run/)).toBeNull();
  });

  it('removes that one filter, and keeps the others, the sort and the columns', async () => {
    sidecar();
    const { router, user } = await open(refusedView('(unclosed'), createQueryClientWithoutBackoff());
    const alert = await refusal('This filter can’t be run: Anchor or alt text matches (unclosed');
    expect(within(alert).getByText(/^This isn’t a valid regular expression: /)).toBeVisible();
    const remove = within(alert).getByRole('button', { name: 'Remove the filter' });
    remove.focus();
    await user.click(remove);
    await counted('9 of 13 links');
    expect(router.state.location.search).toEqual({
      filter: [{ column: 'kind', op: 'in', values: ['hyperlink'] }],
      cols: ['from.url', 'to.url', 'text', 'to.statusCode'],
      sort: [{ column: 'to.statusCode', desc: false }],
    });
    expect(
      within(await grid())
        .getAllByRole('columnheader')
        .map((cell) => cell.textContent),
    ).toEqual(['Source URL', 'Target URL', 'Anchor or alt text', 'Target status']);
    // The button went with the state it was in: the keyboard isn't left on the page.
    await waitFor(() => expect(document.body).not.toHaveFocus());
  });

  it('offers only to remove a filter its editor has no fields for', async () => {
    sidecar();
    const filter = JSON.stringify([{ column: 'to.id', op: 'regex', value: 'x' }]);
    await open(`${LINKS}?filter=${encodeURIComponent(filter)}`, createQueryClientWithoutBackoff());
    const alert = await refusal(/^This filter can’t be run: Target URL /);
    expect(within(alert).getByRole('button', { name: 'Remove the filter' })).toBeVisible();
    expect(within(alert).queryByRole('button', { name: 'Change the filter' })).toBeNull();
  });
});

describe('a view with as many filters as a query takes', () => {
  const twenty = (): GridFilter[] =>
    Array.from({ length: 20 }, (_, index) => ({ column: 'text', op: 'notContains', value: `nothing-${index}` }));

  it('says why another can’t be added, in place of the editor, and when a facet’s value would be one too many', async () => {
    sidecar();
    const { router, user } = await open(`${LINKS}?filter=${encodeURIComponent(JSON.stringify(twenty()))}`);
    await counted('13 links');
    expect(filtersOf(router)).toHaveLength(20);
    await user.click(screen.getByRole('button', { name: 'Add filter' }));
    const adding = await screen.findByRole('dialog', { name: 'Add a filter' });
    expect(within(adding).getByText('A view takes at most 20 filters. Remove one to add another.')).toBeVisible();
    expect(within(adding).queryByRole('button', { name: 'Add filter' })).toBeNull();
    await user.keyboard('{Escape}');
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());

    // A facet's value would be the twenty-first: it isn't dropped on the way to the route without a word.
    await user.click(screen.getByRole('button', { name: 'Filter by kind' }));
    await user.click(within(await screen.findByRole('menu')).getByRole('menuitem', { name: /^Image/ }));
    expect(await screen.findByText('That filter wasn’t added')).toBeVisible();
    expect(screen.getByText('A view takes at most 20 filters. Remove one to add another.')).toBeVisible();
    expect(filtersOf(router)).toEqual(twenty());
    await counted('13 links');
  });
});

describe('where the keyboard starts in a popover', () => {
  it('is its first field, never the Close button of its head', async () => {
    sidecar();
    const { user } = await open(LINKS);
    await counted('13 links');
    await user.click(screen.getByRole('button', { name: 'Add filter' }));
    const adding = await screen.findByRole('dialog', { name: 'Add a filter' });
    await waitFor(() => expect(within(adding).getByRole('button', { name: /^About:/ })).toHaveFocus());
    await user.keyboard('{Escape}');
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());

    await user.click(screen.getByRole('button', { name: 'Columns' }));
    const columns = await screen.findByRole('dialog', { name: 'Columns' });
    await waitFor(() => expect(within(columns).getByRole('searchbox', { name: 'Find a column' })).toHaveFocus());
    await user.keyboard('{Escape}');
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
  });

  it('is the value of a filter being changed: that is what’s changed', async () => {
    sidecar();
    const filter = JSON.stringify([{ column: 'text', op: 'contains', value: 'about' }]);
    const { user } = await open(`${LINKS}?filter=${encodeURIComponent(filter)}`);
    await counted('2 of 13 links');
    await user.click(screen.getByRole('button', { name: 'Anchor or alt text contains about' }));
    const editing = await screen.findByRole('dialog', { name: 'Change the filter' });
    await waitFor(() => expect(within(editing).getByRole('textbox', { name: 'Text' })).toHaveFocus());
    // Under the text, how the engine matches it: the case of A to Z only, which is what it does.
    expect(within(editing).getByRole('textbox', { name: 'Text' })).toHaveAccessibleDescription(
      'A to Z match in capitals or small letters. Other letters, such as é and É, match only themselves.',
    );
    expect(within(editing).queryByText('Capitals and small letters match each other.')).toBeNull();
  });
});

describe('the Links grid while the crawl runs', () => {
  it('shows it’s live, takes new links in place, and reads no count out as it moves', async () => {
    const { state } = sidecar({
      item: { status: 'running', analyzedAt: null },
      summary: { available: false, reason: 'crawling' },
    });
    const { queryClient, user } = await open(LINKS);
    await counted('13 links');
    expect(within(await header()).getByText('Live')).toBeVisible();
    // No banner for a running crawl whose shown columns need no analysis: its status says it.
    expect(screen.queryByText(/fill in once the crawl has finished/)).toBeNull();
    const table = await grid();
    act(() => table.focus());
    fireEvent.keyDown(table, { key: 'ArrowDown' });
    fireEvent.keyDown(table, { key: 'ArrowDown' });
    const cursor = table.getAttribute('aria-activedescendant');

    // The crawl stores another page's links: what the live bridge does on a progress tick.
    state.links = testLinkRows(
      [...LINKS_FACTS, { ...LINKS_FACTS[1]!, fromId: 8, toId: 1, text: 'Back home' }],
      LINKS_URLS,
    );
    await act(() => queryClient.invalidateQueries(liveGridPagesOf(TEST_CRAWL_ID), { cancelRefetch: false }));
    await counted('14 links');
    // In place: no skeleton, no progress line, the cursor where it was.
    expect(table.isConnected).toBe(true);
    expect(table).toHaveAttribute('aria-busy', 'false');
    expect(table.getAttribute('aria-activedescendant')).toBe(cursor);
    expect(said).toEqual([]);
    // The facets moved with the rows.
    await user.click(screen.getByRole('button', { name: 'Filter by kind' }));
    expect(await within(await screen.findByRole('menu')).findByText('Kind, of 14 links')).toBeVisible();
  });

  it('counts the links by value only while a facet’s menu is open, however the crawl ticks', async () => {
    const { state, transport } = sidecar({
      item: { status: 'running', analyzedAt: null },
      summary: { available: false, reason: 'crawling' },
    });
    const facetReads = () => transport.requests.filter((request) => request.path.endsWith('/links/facets')).length;
    const pageReads = () => linkRequests(transport).length;
    const stored = [...LINKS_FACTS];
    /** What the live bridge does on a crawl's progress tick, with one more link stored. */
    const tick = async (text: string) => {
      stored.push({ ...LINKS_FACTS[1]!, text });
      state.links = testLinkRows(stored, LINKS_URLS);
      await act(() => queryClient.invalidateQueries(liveGridPagesOf(TEST_CRAWL_ID), { cancelRefetch: false }));
    };
    const { queryClient, user } = await open(LINKS);
    await counted('13 links');
    // The presets and the facets' buttons are drawn, and nothing has been counted by value: no menu is open.
    expect(screen.getByRole('button', { name: 'Broken links' })).toBeVisible();
    expect(screen.getByRole('button', { name: 'Filter by kind' })).toBeVisible();
    expect(facetReads()).toBe(0);
    // The crawl ticks, and ticks again: the rows are read each time, the facets never.
    const before = pageReads();
    await tick('One more');
    await counted('14 links');
    await tick('And another');
    await counted('15 links');
    expect(pageReads()).toBeGreaterThan(before);
    expect(facetReads()).toBe(0);

    // A menu opens: now they are counted, once.
    await user.click(screen.getByRole('button', { name: 'Filter by kind' }));
    const menu = await screen.findByRole('menu');
    expect(await within(menu).findByText('Kind, of 15 links')).toBeVisible();
    expect(facetReads()).toBe(1);
    // And kept fresh while it's open.
    await tick('A third');
    expect(await within(menu).findByText('Kind, of 16 links')).toBeVisible();
    expect(facetReads()).toBe(2);

    // Closed again, the ticks read the rows only.
    await user.keyboard('{Escape}');
    await waitFor(() => expect(screen.queryByRole('menu')).toBeNull());
    await tick('A fourth');
    await counted('17 links');
    await tick('A fifth');
    await counted('18 links');
    expect(facetReads()).toBe(2);
    // Opened again, the count is read again: what it had is of a crawl five links ago.
    await user.click(screen.getByRole('button', { name: 'Filter by position' }));
    expect(await within(await screen.findByRole('menu')).findByText('Position, of 18 links')).toBeVisible();
    expect(facetReads()).toBe(3);
  });

  it('reads no facets for a view until a menu is opened on it, on a crawl that stands still too', async () => {
    const { transport } = sidecar();
    const facetReads = () => transport.requests.filter((request) => request.path.endsWith('/links/facets')).length;
    const { user } = await open(LINKS);
    await counted('13 links');
    await user.click(screen.getByRole('button', { name: 'Broken links' }));
    await counted('3 of 13 links');
    await user.click(screen.getByRole('button', { name: 'External links' }));
    await counted('1 of 13 links');
    expect(facetReads()).toBe(0);
    await user.click(screen.getByRole('button', { name: 'Filter by kind' }));
    expect(await within(await screen.findByRole('menu')).findByText('Kind, of 1 link')).toBeVisible();
    expect(facetReads()).toBe(1);
  });
});

describe('a read of the links that takes a while', () => {
  it('says what it is doing after 150 ms, and is given up when the view changes', async () => {
    const { state, transport } = sidecar();
    const { user } = await open(LINKS);
    await counted('13 links');
    let release: () => void = () => undefined;
    state.hold = new Promise<void>((resolve) => (release = resolve));
    state.holds = (query) => query.search === 'gone';
    await user.type(screen.getByRole('searchbox', { name: 'Search anchor text and URLs' }), 'gone{Enter}');
    // Nothing for a moment, then what it's doing, over the grid's skeleton.
    const words = 'Reading all 13 links: a search looks at each link’s anchor text and both its URLs.';
    expect(screen.queryByText(words)).toBeNull();
    expect(await screen.findByText(words, undefined, { timeout: 2_000 })).toBeVisible();
    // Said once, by the screen, as the words show: its effect runs after the paint that shows them.
    await waitFor(() => expect(said).toContain(words));
    expect(said.filter((message) => message === words)).toHaveLength(1);
    const held = transport.requests.find(
      (request) => request.path.endsWith('/links') && (request.body as GridQuery).search === 'gone',
    );
    expect(held?.signal?.aborted).toBe(false);
    // The rest of the screen stays usable: another view gives the read up.
    await user.click(screen.getByRole('button', { name: 'Broken links' }));
    await counted('3 of 13 links');
    expect(held?.signal?.aborted).toBe(true);
    expect(screen.queryByText(words)).toBeNull();
    release();
  });
});
