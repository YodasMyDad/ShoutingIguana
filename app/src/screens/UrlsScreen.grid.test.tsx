// The URL grid's own pieces against a sidecar that answers as the mock does: header sort, presets and the chooser,
// facets read only while a menu is open, Ctrl+F, the row menu, saved views, Show full URLs, the whole view's URLs
// copied, and the cursor staying on its URL while a running crawl moves the rows.
import { createMemoryHistory } from '@tanstack/react-router';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { FacetsQuery, GridQuery, SummaryState, UrlColumn, UrlRow } from '../api/grids';
import { answerFacets, answerGridQuery } from '../api/mock/gridQuery';
import { URL_COLUMNS } from '../api/mock/urlColumns';
import { gridPagesOf } from '../api/queries';
import { setTransport, type TransportRequest } from '../api/transport';
import type { CrawlStatus } from '../api/types';
import { App } from '../app/App';
import { createQueryClient } from '../app/queryClient';
import { createAppRouter } from '../app/router';
import { commands, installKeyboard } from '../keyboard/registry';
import { getSettings, updateSettings } from '../settings/settingsStore';
import { ISSUES_CHECKS } from '../test/issuesSidecar';
import { wholeText } from '../test/text';
import {
  TEST_CRAWL_ID,
  TEST_URL_ROWS,
  fakeSidecar,
  json,
  problem,
  standardRoutes,
  testCrawlItem,
} from '../test/fakeSidecar';
import { URL_PRESETS } from './urls/urlColumns';

const URLS = `/crawls/${TEST_CRAWL_ID}/urls`;
const ANALYSED: SummaryState = { available: true, reason: null };

function sidecar(
  options: { status?: CrawlStatus; summary?: SummaryState; rows?: UrlRow[]; columns?: UrlColumn[] } = {},
) {
  const summary = options.summary ?? ANALYSED;
  const item = testCrawlItem({ status: options.status ?? 'completed', analyzedAt: '2026-11-01T10:21:00Z' });
  const state = { rows: options.rows ?? TEST_URL_ROWS };
  const routes = standardRoutes(item).filter(([, pattern]) => !pattern.source.endsWith('urls$'));
  const transport = fakeSidecar([
    [
      'POST',
      /^\/api\/crawls\/[0-9a-f]{16}\/urls$/,
      (request: TransportRequest) => {
        const answer = answerGridQuery(state.rows, request.body as GridQuery, summary);
        return answer.ok ? json(answer.page) : problem(answer.problem);
      },
    ],
    [
      'POST',
      /^\/api\/crawls\/[0-9a-f]{16}\/urls\/facets$/,
      (request: TransportRequest) => {
        const answer = answerFacets(state.rows, request.body as FacetsQuery, summary);
        return answer.ok ? json(answer.page) : problem(answer.problem);
      },
    ],
    ['GET', /^\/api\/url-columns$/, () => json(options.columns ?? URL_COLUMNS)],
    ['GET', /^\/api\/checks$/, () => json(ISSUES_CHECKS)],
    ...routes,
  ]);
  setTransport(transport);
  return { transport, state };
}

async function open(path: string) {
  const queryClient = createQueryClient();
  const history = createMemoryHistory({ initialEntries: [path] });
  const router = createAppRouter(queryClient, history);
  render(<App router={router} queryClient={queryClient} />);
  await act(() => router.load());
  const user = userEvent.setup();
  const grid = await screen.findByRole('grid', { name: 'URLs' });
  await within(grid).findAllByRole('row');
  return { router, queryClient, user, grid };
}

const urlQueries = (transport: { requests: TransportRequest[] }) =>
  transport.requests.filter((request) => request.path.endsWith('/urls')).map((request) => request.body as GridQuery);
const facetReads = (transport: { requests: TransportRequest[] }) =>
  transport.requests.filter((request) => request.path.endsWith('/urls/facets'));
const headers = (grid: HTMLElement) =>
  within(grid)
    .getAllByRole('columnheader')
    .map((cell) => cell.textContent);
/** The text of the row the cursor is on, as assistive technology is told it. */
const cursorRowText = (grid: HTMLElement) =>
  document.getElementById(grid.getAttribute('aria-activedescendant') ?? '')?.closest('[role="row"]')?.textContent ??
  null;

let uninstall: () => void = () => undefined;
beforeEach(() => {
  uninstall = installKeyboard();
  updateSettings({ savedViews: [], showFullUrls: false, gridColumns: {} });
});
afterEach(() => {
  uninstall();
  setTransport(fakeSidecar([]));
});

describe('the URL grid’s columns', () => {
  it('shows the Overview preset by default, chosen, and another preset’s columns in the route', async () => {
    sidecar();
    const { router, user, grid } = await open(URLS);
    expect(screen.getByRole('button', { name: 'Overview' })).toHaveAttribute('aria-pressed', 'true');
    expect(headers(grid)[0]).toMatch(/^URL/);
    expect(headers(grid)).toHaveLength(URL_PRESETS[0]!.columns.length);
    await user.click(screen.getByRole('button', { name: 'Content' }));
    const content = URL_PRESETS.find((preset) => preset.id === 'content')!;
    await waitFor(() => expect(router.state.location.search).toMatchObject({ cols: content.columns }));
    expect(screen.getByRole('button', { name: 'Content' })).toHaveAttribute('aria-pressed', 'true');
    const after = await screen.findByRole('grid', { name: 'URLs' });
    await waitFor(() => expect(headers(after)).toContain('Title length'));
  });

  it('names the host once, in the URL column’s header, and shows the URLs whole with Show full URLs', async () => {
    sidecar();
    const { grid } = await open(URLS);
    expect(headers(grid)[0]).toBe('URLon example.com');
    act(() => void commands.run('urls.fullUrls'));
    expect(getSettings().showFullUrls).toBe(true);
    await waitFor(() => expect(headers(screen.getByRole('grid', { name: 'URLs' }))[0]).toBe('URL'));
  });

  it('drops a sort with its column when the chooser hides it', async () => {
    sidecar();
    const { router, user } = await open(`${URLS}?sort=-depth`);
    act(() => void commands.run('urls.columns'));
    const chooser = await screen.findByRole('dialog', { name: 'Columns' });
    await user.click(within(chooser).getByRole('checkbox', { name: 'Depth' }));
    await waitFor(() => expect(router.state.location.search.sort).toBeUndefined());
    expect(router.state.location.search.cols).not.toContain('depth');
  });
});

describe('a sort of several keys when one key’s column is hidden', () => {
  it('drops that key alone, keeping the others in their order', async () => {
    sidecar();
    const sort = [
      { column: 'depth', desc: false },
      { column: 'inlinks', desc: true },
    ];
    const { router, user } = await open(`${URLS}?sort=${encodeURIComponent(JSON.stringify(sort))}`);
    act(() => void commands.run('urls.columns'));
    const chooser = await screen.findByRole('dialog', { name: 'Columns' });
    await user.click(within(chooser).getByRole('checkbox', { name: 'Depth' }));
    await waitFor(() => expect(router.state.location.search.sort).toEqual([{ column: 'inlinks', desc: true }]));
  });

  it('keeps the shown keys of a view that arrives sorted by a hidden column too', async () => {
    sidecar();
    const sort = [
      { column: 'inlinks', desc: true },
      { column: 'outlinks', desc: false },
    ];
    const { router } = await open(`${URLS}?sort=${encodeURIComponent(JSON.stringify(sort))}`);
    await waitFor(() => expect(router.state.location.search.sort).toEqual([{ column: 'inlinks', desc: true }]));
    expect(await screen.findByRole('columnheader', { name: /^Inlinks/ })).toHaveAttribute('aria-sort', 'descending');
  });
});

describe('sorting the URL grid from its headers', () => {
  it('sorts by a column alone on a click, and adds a key with Shift, showing each key’s place', async () => {
    const { transport } = sidecar();
    const { router, user, grid } = await open(URLS);
    await user.click(within(grid).getByRole('button', { name: /^Depth/ }));
    await waitFor(() => expect(router.state.location.search.sort).toEqual([{ column: 'depth', desc: false }]));
    await user.keyboard('{Shift>}');
    await user.click(within(screen.getByRole('grid', { name: 'URLs' })).getByRole('button', { name: /^Inlinks/ }));
    await user.keyboard('{/Shift}');
    await waitFor(() =>
      expect(router.state.location.search.sort).toEqual([
        { column: 'depth', desc: false },
        { column: 'inlinks', desc: false },
      ]),
    );
    await waitFor(() =>
      expect(urlQueries(transport).at(-1)?.sort).toEqual([
        { column: 'depth', desc: false },
        { column: 'inlinks', desc: false },
      ]),
    );
    const sorted = screen.getByRole('grid', { name: 'URLs' });
    const depth = within(sorted).getByRole('columnheader', { name: /^Depth/ });
    const inlinks = within(sorted).getByRole('columnheader', { name: /^Inlinks/ });
    // The first key is the grid's sort; the second says its place in words.
    expect(depth).toHaveAttribute('aria-sort', 'ascending');
    expect(inlinks).not.toHaveAttribute('aria-sort');
    expect(inlinks).toHaveTextContent(/sorted second, ascending/);
  });

  it('says why a column can’t be sorted, on its header', async () => {
    sidecar();
    const { grid } = await open(URLS);
    const title = within(grid).getByRole('columnheader', { name: 'Title' });
    expect(within(title).queryByRole('button')).toBeNull();
    expect(title).toHaveAttribute('title', expect.stringMatching(/^URLs can’t be sorted by title/));
  });
});

describe('the URL grid’s facets', () => {
  it('are read only once a menu opens, and a value lands on its count', async () => {
    const { transport } = sidecar();
    const { router, user } = await open(URLS);
    expect(facetReads(transport)).toHaveLength(0);
    await user.click(screen.getByRole('button', { name: 'Filter by status' }));
    const menu = await screen.findByRole('menu');
    const value = await within(menu).findByRole('menuitem', { name: /^2xx/ });
    expect(facetReads(transport)).toHaveLength(1);
    await user.click(value);
    await waitFor(() =>
      expect(router.state.location.search.filter).toEqual([{ column: 'statusClass', op: 'in', values: ['2xx'] }]),
    );
    const header = screen.getByRole('heading', { level: 1, name: 'URLs' }).parentElement!;
    expect(await within(header).findByText(wholeText('2 of 3 URLs found'))).toBeVisible();
  });
});

describe('Ctrl+F on the URL grid', () => {
  it('puts the keyboard in the search with its text selected, from the grid', async () => {
    sidecar();
    const { user, grid } = await open(`${URLS}?q=example`);
    act(() => grid.focus());
    await user.keyboard('{Control>}f{/Control}');
    const field = screen.getByRole('searchbox', { name: 'Search URLs and titles' });
    expect(field).toHaveFocus();
    expect((field as HTMLInputElement).selectionStart).toBe(0);
    expect((field as HTMLInputElement).selectionEnd).toBe('example'.length);
  });
});

describe('the URL grid’s row menu', () => {
  it('opens on Shift+F10 for the row, and gives the keyboard back to the grid when it closes', async () => {
    sidecar();
    const { user, grid } = await open(URLS);
    act(() => grid.focus());
    await user.keyboard('{ArrowDown}{Shift>}{F10}{/Shift}');
    const menu = await screen.findByRole('menu', { name: 'Menu for /a' });
    expect(
      within(menu)
        .getAllByRole('menuitem')
        .map((item) => item.textContent.replace(/(Shift\+?)?Enter|Ctrl.*$/, '')),
    ).toEqual([
      'Inspect',
      'Open in the browser',
      'Copy the URL',
      'Copy the row',
      // The cell the keys opened it on: the URL's (gap 9).
      'Filter by this valueURL is https://example.com/a',
      'Exclude this valueURL is not https://example.com/a',
      'Show its inlinks in Links',
    ]);
    await user.keyboard('{Escape}');
    await waitFor(() => expect(screen.queryByRole('menu')).toBeNull());
    await waitFor(() => expect(grid).toHaveFocus());
    expect(cursorRowText(grid)).toMatch(/\/a/);
  });

  it('acts on the selection the row is in: its URLs copied, each once', async () => {
    sidecar();
    const { user, grid } = await open(URLS);
    const writeText = vi.spyOn(navigator.clipboard, 'writeText').mockResolvedValue();
    act(() => grid.focus());
    await user.keyboard('{Shift>}{ArrowDown}{ArrowDown}{/Shift}{ContextMenu}');
    const menu = await screen.findByRole('menu', { name: 'Menu for 3 URLs' });
    await user.click(within(menu).getByRole('menuitem', { name: 'Copy 3 URLs' }));
    await waitFor(() =>
      expect(writeText).toHaveBeenCalledWith('https://example.com/\nhttps://example.com/a\nhttps://example.com/b'),
    );
  });

  it('shows a URL’s inlinks in Links', async () => {
    sidecar();
    const { router, user, grid } = await open(URLS);
    act(() => grid.focus());
    await user.keyboard('{ArrowDown}{Shift>}{F10}{/Shift}');
    const menu = await screen.findByRole('menu');
    await user.click(within(menu).getByRole('menuitem', { name: 'Show its inlinks in Links' }));
    await waitFor(() => expect(router.state.location.pathname).toBe(`/crawls/${TEST_CRAWL_ID}/links`));
    expect(router.state.location.search).toMatchObject({
      filter: [
        { column: 'to.id', op: 'eq', value: 2 },
        { column: 'selfLink', op: 'is', value: false },
      ],
    });
  });
});

describe('the URL grid under a running crawl', () => {
  it('keeps the cursor on its URL when a new URL sorts in above it', async () => {
    const { state, transport } = sidecar({ status: 'running', summary: { available: false, reason: 'crawling' } });
    const { queryClient, user, grid } = await open(`${URLS}?sort=url`);
    act(() => grid.focus());
    await user.keyboard('{ArrowDown}{ArrowDown}');
    expect(cursorRowText(grid)).toMatch(/\/b/);
    // The crawl finds a URL that sorts ahead of the cursor's: the rows move down one.
    state.rows = [...state.rows, { id: 4, url: 'https://example.com/0', statusCode: 200, statusClass: '2xx' }];
    await act(() => queryClient.invalidateQueries(gridPagesOf(TEST_CRAWL_ID), { cancelRefetch: false }));
    const header = screen.getByRole('heading', { level: 1, name: 'URLs' }).parentElement!;
    expect(await within(header).findByText(wholeText('4 URLs found'))).toBeVisible();
    await within(grid).findByText('https://example.com/0');
    expect(cursorRowText(grid)).toMatch(/\/b/);
    // With every facet's menu closed, a tick reads no facet: nothing on screen shows one.
    expect(facetReads(transport)).toHaveLength(0);
  });
});

describe('saved views', () => {
  it('saves the view under a name, opens it from the menu, and deletes it with Undo', async () => {
    sidecar();
    const filter = [{ column: 'statusClass', op: 'in', values: ['4xx'] }];
    const { router, user } = await open(`${URLS}?filter=${encodeURIComponent(JSON.stringify(filter))}&sort=-depth`);
    act(() => void commands.run('urls.saveView'));
    const dialog = await screen.findByRole('dialog', { name: 'Save this view' });
    await user.click(within(dialog).getByRole('button', { name: 'Save' }));
    expect(within(dialog).getByText('Give the view a name.')).toBeVisible();
    await user.type(within(dialog).getByRole('textbox', { name: 'Name' }), 'Broken by depth{Enter}');
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    // The command that opened the dialog has no place to give the keyboard back to: the grid takes it.
    await waitFor(() => expect(screen.getByRole('grid', { name: 'URLs' })).toHaveFocus());
    expect(getSettings().savedViews).toEqual([
      expect.objectContaining({ name: 'Broken by depth', search: { filter, sort: [{ column: 'depth', desc: true }] } }),
    ]);
    // The menu's button names the view the grid is on.
    expect(screen.getByRole('button', { name: 'Broken by depth' })).toBeVisible();

    await act(() => router.navigate({ to: '/crawls/$crawl/urls', params: { crawl: TEST_CRAWL_ID }, search: {} }));
    await user.click(await screen.findByRole('button', { name: 'Saved views' }));
    await user.click(await screen.findByRole('menuitemradio', { name: 'Broken by depth' }));
    await waitFor(() =>
      expect(router.state.location.search).toMatchObject({ filter, sort: [{ column: 'depth', desc: true }] }),
    );

    act(() => void commands.run('urls.deleteView'));
    expect(getSettings().savedViews).toEqual([]);
    const toast = await screen.findByText('Deleted the view “Broken by depth”');
    // The toast's own pointer handling needs a real pointer: a click is what a press gives it.
    fireEvent.click(within(toast.closest('li')!).getByRole('button', { name: 'Undo' }));
    await waitFor(() => expect(getSettings().savedViews).toHaveLength(1));
  });
});

describe('the words for an enum’s values', () => {
  it('are the engine’s, in the cell, the facet, the filter editor and the chip alike', async () => {
    const columns = URL_COLUMNS.map((column) =>
      column.id === 'type' ? { ...column, valueTitles: { page: 'Web page', media: 'Audio or video' } } : column,
    );
    sidecar({ columns, rows: [{ id: 1, url: 'https://example.com/clip.mp4', type: 'media' }] });
    const filter = [{ column: 'type', op: 'in', values: ['media'] }];
    const { user, grid } = await open(`${URLS}?filter=${encodeURIComponent(JSON.stringify(filter))}`);
    await within(grid).findByText('Audio or video');
    const chips = screen.getByRole('group', { name: 'Filters' });
    expect(within(chips).getByRole('button', { name: 'Remove the filter Type is audio or video' })).toBeVisible();
    await user.click(screen.getByRole('button', { name: 'Filter by type' }));
    expect(
      await within(await screen.findByRole('menu')).findByRole('menuitem', { name: /^Audio or video/ }),
    ).toBeVisible();
  });
});

describe('round 5: the URL grid’s row menu filters by the value under it (gap 9)', () => {
  it('filters by the cell right-clicked, or leaves it out, beside the view’s filters', async () => {
    sidecar();
    const { router, user, grid } = await open(URLS);
    const statusOfA = within(grid).getByText('/a').closest('[role="row"]')!.querySelector('[data-col="1"]')!;
    fireEvent.contextMenu(statusOfA, { clientX: 300, clientY: 200 });
    const menu = await screen.findByRole('menu');
    await user.click(within(menu).getByRole('menuitem', { name: /^Filter by this value/ }));
    await waitFor(() =>
      expect(router.state.location.search.filter).toEqual([{ column: 'statusCode', op: 'eq', value: 404 }]),
    );
    const header = screen.getByRole('heading', { level: 1, name: 'URLs' }).parentElement!;
    expect(await within(header).findByText(wholeText('1 of 3 URLs found'))).toBeVisible();

    await act(() => router.navigate({ to: '/crawls/$crawl/urls', params: { crawl: TEST_CRAWL_ID }, search: {} }));
    const fresh = await screen.findByRole('grid', { name: 'URLs' });
    await within(fresh).findByText('/a');
    fireEvent.contextMenu(within(fresh).getByText('/a').closest('[role="row"]')!.querySelector('[data-col="1"]')!);
    await user.click(await screen.findByRole('menuitem', { name: /^Exclude this value/ }));
    await waitFor(() =>
      expect(router.state.location.search.filter).toEqual([{ column: 'statusCode', op: 'ne', value: 404 }]),
    );
  });
});

describe('round 5: clearing the filters (gap 24)', () => {
  it('keeps the sort and the columns', async () => {
    sidecar();
    const filter = [{ column: 'title', op: 'eq', value: 'Nothing has this title' }];
    const { router, user } = await open(
      `${URLS}?filter=${encodeURIComponent(JSON.stringify(filter))}&sort=-inlinks&cols=url,inlinks`,
    );
    await user.click(await screen.findByRole('button', { name: 'Clear the filters' }));
    await waitFor(() =>
      expect(router.state.location.search).toEqual({
        sort: [{ column: 'inlinks', desc: true }],
        cols: ['url', 'inlinks'],
      }),
    );
  });
});

describe('round 5: the URL grid’s columns laid out (gap 10)', () => {
  it('moves a column with the header’s keys, in the route, and keeps a width with the preferences', async () => {
    sidecar();
    const { router, user, grid } = await open(`${URLS}?cols=url,statusCode,title`);
    act(() => grid.focus());
    // Up to the header, right to Status, then move it right and widen it.
    await user.keyboard('{ArrowUp}{ArrowRight}{Control>}{Shift>}{ArrowRight}{/Shift}{/Control}');
    await waitFor(() => expect(router.state.location.search.cols).toEqual(['url', 'title', 'statusCode']));
    await user.keyboard('{Shift>}{ArrowRight}{/Shift}');
    expect(getSettings().gridColumns.urls?.widths).toEqual({ statusCode: 120 });
  });

  it('saves a view with its columns’ widths and pins, and lays them out so when it opens', async () => {
    sidecar();
    updateSettings({ gridColumns: { urls: { widths: { title: 300 }, pinned: ['url'] } } });
    const { router, user } = await open(`${URLS}?sort=-depth`);
    act(() => void commands.run('urls.saveView'));
    const dialog = await screen.findByRole('dialog', { name: 'Save this view' });
    await user.type(within(dialog).getByRole('textbox', { name: 'Name' }), 'Deep{Enter}');
    expect(getSettings().savedViews[0]?.layout).toEqual({ widths: { title: 300 }, pinned: ['url'] });
    updateSettings({ gridColumns: {} });
    await act(() => router.navigate({ to: '/crawls/$crawl/urls', params: { crawl: TEST_CRAWL_ID }, search: {} }));
    await user.click(await screen.findByRole('button', { name: 'Saved views' }));
    await user.click(await screen.findByRole('menuitemradio', { name: 'Deep' }));
    await waitFor(() => expect(getSettings().gridColumns.urls).toEqual({ widths: { title: 300 }, pinned: ['url'] }));
    const grid = await screen.findByRole('grid', { name: 'URLs' });
    expect(within(grid).getAllByRole('columnheader')[0]).toHaveAttribute('data-pinned', 'true');
  });
});

describe('round 5: the facets’ buttons', () => {
  it('open on the first click whatever was open before (the audit’s Minor, never reproduced)', async () => {
    sidecar();
    const { user } = await open(URLS);
    await user.click(screen.getByRole('button', { name: 'Filter by status' }));
    await screen.findByRole('menu');
    await user.click(screen.getByRole('button', { name: 'Filter by type' }));
    expect(await screen.findByRole('menu')).toHaveTextContent(/^Type/);
    await user.keyboard('{Escape}');
    await user.click(screen.getByRole('searchbox', { name: 'Search URLs and titles' }));
    await user.keyboard('abc');
    await user.click(screen.getByRole('button', { name: 'Filter by type' }));
    expect(await screen.findByRole('menu')).toHaveTextContent(/^Type/);
  });
});

describe('round 5: the URL grid’s search (gap 23)', () => {
  it('says Enter applies what’s typed, until it does', async () => {
    sidecar();
    const { user } = await open(URLS);
    const field = screen.getByRole('searchbox', { name: 'Search URLs and titles' });
    expect(field).toHaveAccessibleDescription('Press Enter to search.');
    const end = () => field.parentElement!.querySelector('.si-field__hint[title="Press Enter to search"]');
    expect(end()).toBeNull();
    await user.type(field, 'home');
    expect(end()).not.toBeNull();
    await user.keyboard('{Enter}');
    await waitFor(() => expect(end()).toBeNull());
  });
});
