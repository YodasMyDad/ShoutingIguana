import { createMemoryHistory } from '@tanstack/react-router';
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { GridQuery, SummaryState, UrlRow } from '../api/grids';
import { answerGridQuery } from '../api/mock/gridQuery';
import { URL_COLUMNS } from '../api/mock/urlColumns';
import { gridPagesOf } from '../api/queries';
import { setTransport, type TransportRequest } from '../api/transport';
import type { Checks, CrawlStatus } from '../api/types';
import { App } from '../app/App';
import { createQueryClient } from '../app/queryClient';
import { createAppRouter } from '../app/router';
import { onAnnounce } from '../feedback/announce';
import { installKeyboard } from '../keyboard/registry';
import { ISSUES_CHECKS } from '../test/issuesSidecar';
import { createQueryClientWithoutBackoff } from '../test/queryClient';
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

const URLS = `/crawls/${TEST_CRAWL_ID}/urls`;

/**
 * A sidecar whose URL grid answers as the mock does: the analysis's columns left out without a summary, a sort or
 * filter on them refused, a query it can't run refused.
 */
function sidecar(options: {
  status?: CrawlStatus;
  analyzedAt?: string | null;
  summary: SummaryState;
  rows?: UrlRow[];
  /** The catalog: the Issues tests' by default; `pending` never answers, `error` fails. */
  checks?: Checks | 'pending' | 'error';
  /** Reading a related URL's text (the grid filtered to its id) fails. */
  relatedFails?: boolean;
}) {
  const item = testCrawlItem({
    status: options.status ?? 'completed',
    analyzedAt: options.analyzedAt === undefined ? '2026-11-01T10:21:00Z' : options.analyzedAt,
  });
  const state: { rows: UrlRow[]; hold: Promise<void> | null } = { rows: options.rows ?? TEST_URL_ROWS, hold: null };
  const analyse = vi.fn(() => new Response(null, { status: 202 }));
  const routes = standardRoutes(item).filter(([, pattern]) => !pattern.source.endsWith('urls$'));
  const transport = fakeSidecar([
    [
      'POST',
      /^\/api\/crawls\/[0-9a-f]{16}\/urls$/,
      async (request: TransportRequest) => {
        const query = request.body as GridQuery;
        if (options.relatedFails && query.filters.some((filter) => filter.column === 'id')) {
          return problem({ type: 'internal', title: 'Something went wrong in the engine.', status: 500 });
        }
        if (state.hold) await state.hold;
        const answer = answerGridQuery(state.rows, request.body as GridQuery, options.summary);
        return answer.ok ? json(answer.page) : problem(answer.problem);
      },
    ],
    ['POST', /^\/api\/crawls\/[0-9a-f]{16}\/analyze$/, analyse],
    ['GET', /^\/api\/url-columns$/, () => json(URL_COLUMNS)],
    [
      'GET',
      /^\/api\/checks$/,
      () =>
        options.checks === 'pending'
          ? new Promise<Response>(() => undefined)
          : options.checks === 'error'
            ? problem({ type: 'internal', title: 'Something went wrong in the engine.', status: 500 })
            : json(options.checks ?? ISSUES_CHECKS),
    ],
    ...routes,
  ]);
  setTransport(transport);
  return { transport, analyse, state };
}

async function open(path: string, queryClient = createQueryClient()) {
  const router = createAppRouter(queryClient, createMemoryHistory({ initialEntries: [path] }));
  render(<App router={router} queryClient={queryClient} />);
  await act(() => router.load());
  return { router, queryClient };
}

afterEach(() => setTransport(fakeSidecar([])));

describe('the URL grid in a short window', () => {
  it('keeps its grid room for five rows under whatever its toolbars take, in the frame every grid screen shares', async () => {
    sidecar({ summary: { available: true, reason: null } });
    const filter = JSON.stringify([{ column: 'statusClass', op: 'in', values: ['2xx'] }]);
    await open(`${URLS}?filter=${encodeURIComponent(filter)}`);
    const table = await screen.findByRole('grid', { name: 'URLs' });
    // The header, five rows, the frame, and the footer's key hints (--grid-foot-height).
    expect(table.parentElement!.style.minHeight).toBe(`${32 + 5 * 32 + 12 + 36}px`);
    const frame = table.closest('.si-grid-screen')!;
    expect(frame).not.toBeNull();
    expect(table.closest('.si-grid-screen__body')?.parentElement).toBe(frame);
    expect(screen.getByRole('group', { name: 'Filters' }).parentElement).toBe(frame);
  });
});

describe('the URL grid without the URL summary', () => {
  it('says why the analysis’s columns are empty, and offers to analyse the crawl', async () => {
    const { analyse } = sidecar({ analyzedAt: null, summary: { available: false, reason: 'notAnalyzed' } });
    await open(URLS);
    await screen.findByText(/Inlinks and Issues need an analysis: this crawl hasn’t been analysed yet/);
    // The crawl's own columns are there; the analysis's are a dash, not a 0.
    const row = (await screen.findByRole('gridcell', { name: /\/a$/ })).closest<HTMLElement>('[role="row"]')!;
    expect(within(row).getAllByText('–').length).toBeGreaterThanOrEqual(2);
    fireEvent.click(within(screen.getByRole('main')).getByRole('button', { name: 'Analyse it' }));
    await waitFor(() => expect(analyse).toHaveBeenCalledOnce());
  });

  it('says the columns fill in after the crawl, and offers nothing it can’t do, while it runs', async () => {
    sidecar({ status: 'running', analyzedAt: null, summary: { available: false, reason: 'crawling' } });
    await open(URLS);
    const notice = await screen.findByText(/fill in once the crawl has finished and been analysed/);
    expect(within(notice).queryByRole('button')).toBeNull();
  });

  it('says the columns fill in when the analysis that’s running is done, with nothing to press, while it runs', async () => {
    sidecar({ status: 'analyzing', analyzedAt: null, summary: { available: false, reason: 'notAnalyzed' } });
    await open(URLS);
    const notice = await screen.findByText(/Inlinks and Issues fill in when the analysis that’s running is done/);
    expect(within(notice).queryByRole('button')).toBeNull();
    expect(screen.queryByText(/hasn’t been analysed yet/)).toBeNull();
  });

  it('turns a sort on such a column into the engine’s reason, with what can work instead of Try again', async () => {
    sidecar({ analyzedAt: null, summary: { available: false, reason: 'notAnalyzed' } });
    const { router } = await open(`${URLS}?sort=-issues`);
    expect(
      await screen.findByText(
        'Filtering or sorting by these columns needs an analysis, and this crawl hasn’t been analysed yet.',
      ),
    ).toBeVisible();
    expect(screen.queryByRole('button', { name: 'Try again' })).toBeNull();
    expect(screen.getByRole('button', { name: 'Analyse it' })).toBeVisible();
    fireEvent.click(screen.getByRole('button', { name: 'Clear the sort and filters' }));
    await waitFor(() => expect(router.state.location.search).toEqual({}));
    expect(await screen.findByRole('grid', { name: 'URLs' })).toBeVisible();
  });

  it('offers to analyse again, in those words, a crawl that was analysed before: never "Analyse it"', async () => {
    const { analyse } = sidecar({ summary: { available: false, reason: 'crawlChanged' } });
    await open(`${URLS}?sort=-issues`);
    await screen.findByText(/^Filtering or sorting by these columns needs an analysis, and /);
    const main = screen.getByRole('main');
    const again = within(main).getByRole('button', { name: 'Analyse again' });
    expect(within(main).queryByRole('button', { name: 'Analyse it' })).toBeNull();
    fireEvent.click(again);
    await waitFor(() => expect(analyse).toHaveBeenCalledOnce());
  });

  it('turns a query the engine can’t run into a plain sentence, with a way back', async () => {
    sidecar({ summary: { available: true, reason: null } });
    // `outcome` filters, but doesn't sort: shown, a sort by it is the engine's to refuse.
    const { router } = await open(`${URLS}?cols=url,outcome&sort=-outcome`);
    expect(await screen.findByText('This view’s query can’t be run.')).toBeVisible();
    // With what the engine said of it, in place: not only behind Details.
    // In words, never the engine's id for the column (gap 27).
    expect(screen.getByText('The URLs can’t be sorted by Outcome: filter by it instead.')).toBeVisible();
    expect(screen.queryByText(/“outcome”/)).toBeNull();
    expect(screen.queryByRole('button', { name: 'Try again' })).toBeNull();
    fireEvent.click(screen.getByRole('button', { name: 'Reset the view' }));
    await waitFor(() => expect(router.state.location.search).toEqual({}));
  });
});

describe('the URL grid refusing a column it was sent (round 5 review, also noted)', () => {
  it('names the column the engine refused, by its place in the list the query sent, not in the view’s order', async () => {
    const urls = fakeSidecar([
      [
        'POST',
        /^\/api\/crawls\/[0-9a-f]{16}\/urls$/,
        (request: TransportRequest) => {
          const sent = (request.body as GridQuery).columns ?? [];
          const at = sent.indexOf('title');
          return problem({
            type: 'invalid-query',
            title: 'The query is invalid.',
            status: 400,
            errors: { [`columns[${at}]`]: ['The grid can’t show this column here.'] },
          });
        },
      ],
      ['GET', /^\/api\/url-columns$/, () => json(URL_COLUMNS)],
      ['GET', /^\/api\/checks$/, () => json(ISSUES_CHECKS)],
      ...standardRoutes(testCrawlItem()).filter(([, pattern]) => !pattern.source.endsWith('urls$')),
    ]);
    setTransport(urls);
    // Shown as Title then Depth: the query sends its columns sorted, Title well after where the view has it.
    await open(`${URLS}?cols=title,depth,url`);
    expect(await screen.findByText('The URLs can’t show Title here.')).toBeVisible();
  });
});

describe('the URL grid while the crawl runs', () => {
  it('shows it’s live, and takes new URLs in place: no skeleton, no progress line', async () => {
    const { state } = sidecar({
      status: 'running',
      analyzedAt: null,
      summary: { available: false, reason: 'crawling' },
    });
    const { queryClient } = await open(URLS);
    const header = (await screen.findByRole('heading', { level: 1, name: 'URLs' })).parentElement!;
    expect(await within(header).findByText(wholeText('3 URLs found'))).toBeVisible();
    expect(within(header).getByText('Live')).toBeVisible();
    state.rows = [...TEST_URL_ROWS, { id: 4, url: 'https://example.com/c', statusCode: 200, statusClass: '2xx' }];
    let release: () => void = () => undefined;
    state.hold = new Promise<void>((resolve) => (release = resolve));
    // What the live bridge does on a progress tick.
    // Started inside act, not awaited: the refresh is held below, and an async act left open would keep the next
    // test's renders from committing.
    act(() => {
      void queryClient.invalidateQueries(gridPagesOf(TEST_CRAWL_ID), { cancelRefetch: false });
    });
    await waitFor(() => expect(queryClient.isFetching()).toBeGreaterThan(0));
    // While the refresh is on its way, what's shown stays, quietly.
    expect(screen.getByText('https://example.com/a')).toBeVisible();
    expect(screen.queryByRole('progressbar', { name: 'Refreshing' })).toBeNull();
    state.hold = null;
    act(() => release());
    expect(await within(header).findByText(wholeText('4 URLs found'))).toBeVisible();
    expect(await screen.findByText('https://example.com/c')).toBeVisible();
    expect(screen.getByRole('grid', { name: 'URLs' })).toHaveAttribute('aria-busy', 'false');
    expect(screen.queryByRole('progressbar', { name: 'Refreshing' })).toBeNull();
  });
});

describe('the URL grid’s count', () => {
  it('isn’t read out as a running crawl moves it, and is said once for a search the user makes', async () => {
    const heard: string[] = [];
    const stop = onAnnounce((message) => heard.push(message));
    const { state } = sidecar({
      status: 'running',
      analyzedAt: null,
      summary: { available: false, reason: 'crawling' },
    });
    const { queryClient } = await open(URLS);
    const header = (await screen.findByRole('heading', { level: 1, name: 'URLs' })).parentElement!;
    const liveRegions = () => header.querySelectorAll('[aria-live], [role="status"], [role="alert"], [role="log"]');
    expect(await within(header).findByText(wholeText('3 URLs found'))).toBeVisible();
    // A screen reader reads what changes in a live region: about once a second here, for as long as the crawl runs.
    expect(liveRegions()).toHaveLength(0);
    /** What the live bridge does on a progress tick, the crawl having found another URL. */
    const tick = (row: UrlRow) => {
      state.rows = [...state.rows, row];
      return act(() => queryClient.invalidateQueries(gridPagesOf(TEST_CRAWL_ID), { cancelRefetch: false }));
    };
    await tick({ id: 4, url: 'https://example.com/c', statusCode: 200, statusClass: '2xx' });
    expect(await within(header).findByText(wholeText('4 URLs found'))).toBeVisible();
    expect(liveRegions()).toHaveLength(0);
    expect(heard).toEqual([]);

    // A search is the user's own change: its count is said, once.
    const field = screen.getByRole('searchbox', { name: 'Search URLs and titles' });
    fireEvent.change(field, { target: { value: 'example.com/a' } });
    fireEvent.keyDown(field, { key: 'Enter' });
    expect(await within(header).findByText(wholeText('1 of 4 URLs found'))).toBeVisible();
    await waitFor(() => expect(heard).toEqual(['1 of 4 URLs found']));
    // The crawl goes on under the search: the count moves, and nothing more is said.
    await tick({ id: 5, url: 'https://example.com/a/b', statusCode: 200, statusClass: '2xx' });
    expect(await within(header).findByText(wholeText('2 of 5 URLs found'))).toBeVisible();
    expect(heard).toEqual(['1 of 4 URLs found']);
    stop();
  });
});

describe('the URL grid’s search', () => {
  it('keeps focus in the search field when Enter applies it: the field isn’t made anew', async () => {
    sidecar({ summary: { available: true, reason: null } });
    const { router } = await open(URLS);
    const field = await screen.findByRole('searchbox', { name: 'Search URLs and titles' });
    act(() => field.focus());
    fireEvent.change(field, { target: { value: 'example.com/a' } });
    fireEvent.keyDown(field, { key: 'Enter' });
    await waitFor(() => expect((router.state.location.search as { q?: string }).q).toBe('example.com/a'));
    expect(field).toBeInTheDocument();
    expect(field).toHaveFocus();
    expect(screen.getByRole('searchbox', { name: 'Search URLs and titles' })).toBe(field);
    expect(field).toHaveValue('example.com/a');
    // Back takes the search away again, and the field shows that.
    act(() => {
      router.history.back();
    });
    await waitFor(() => expect(field).toHaveValue(''));
  });
});

describe('the URL grid opened from a check’s group', () => {
  it('shows its filters as chips that read as sentences, the check and its group each removable', async () => {
    sidecar({ summary: { available: true, reason: null } });
    const filter = [
      { column: 'check', op: 'in', values: ['empty-anchor-text'], group: { detail: null, relatedUrlId: 1 } },
    ];
    const { router } = await open(`${URLS}?filter=${encodeURIComponent(JSON.stringify(filter))}`);
    const chips = await screen.findByRole('group', { name: 'Filters' });
    // The check by its title, never its id.
    expect(await within(chips).findByText('Internal links without anchor text')).toBeVisible();
    expect(await within(chips).findByText('/', { exact: true })).toBeVisible();
    fireEvent.click(within(chips).getByRole('button', { name: 'Remove the filter Related URL is /' }));
    await waitFor(() =>
      expect(router.state.location.search).toEqual({
        filter: [{ column: 'check', op: 'in', values: ['empty-anchor-text'] }],
      }),
    );
    fireEvent.click(
      within(chips).getByRole('button', { name: 'Remove the filter Check is Internal links without anchor text' }),
    );
    await waitFor(() => expect(router.state.location.search).toEqual({}));
  });

  it('reads the filters Overview’s “Pages” links with as sentences, never as the engine’s ids', async () => {
    sidecar({ summary: { available: true, reason: null } });
    const filter = [
      { column: 'isInternal', op: 'is', value: true },
      { column: 'type', op: 'in', values: ['page'] },
      { column: 'statusClass', op: 'in', values: ['2xx'] },
      { column: 'wordCount', op: 'isNotEmpty' },
    ];
    await open(`${URLS}?filter=${encodeURIComponent(JSON.stringify(filter))}`);
    const chips = await screen.findByRole('group', { name: 'Filters' });
    expect(within(chips).getByRole('button', { name: 'Remove the filter HTML was read' })).toBeVisible();
    expect(within(chips).getByRole('button', { name: 'Remove the filter Status class is 2xx' })).toBeVisible();
    expect(within(chips).queryByText(/wordCount/)).toBeNull();
  });

  it('reads the response times Overview links with as times, with their unit', async () => {
    sidecar({ summary: { available: true, reason: null } });
    const slow = [{ column: 'responseMs', op: 'gte', value: 1000 }];
    await open(`${URLS}?filter=${encodeURIComponent(JSON.stringify(slow))}`);
    const chips = await screen.findByRole('group', { name: 'Filters' });
    expect(
      within(chips).getByRole('button', { name: 'Remove the filter Response time is at least 1,000 ms' }),
    ).toBeVisible();
    cleanup();
    sidecar({ summary: { available: true, reason: null } });
    const band = [{ column: 'responseMs', op: 'between', min: 250, max: 499 }];
    await open(`${URLS}?filter=${encodeURIComponent(JSON.stringify(band))}`);
    expect(
      within(await screen.findByRole('group', { name: 'Filters' })).getByRole('button', {
        name: 'Remove the filter Response time is between 250 ms and 499 ms',
      }),
    ).toBeVisible();
  });

  it('shows a placeholder while the names are read, and plain words when they can’t be, never ids', async () => {
    const filter = [
      { column: 'check', op: 'in', values: ['empty-anchor-text'], group: { detail: null, relatedUrlId: 1 } },
    ];
    sidecar({ summary: { available: true, reason: null }, checks: 'pending' });
    await open(`${URLS}?filter=${encodeURIComponent(JSON.stringify(filter))}`);
    const chips = await screen.findByRole('group', { name: 'Filters' });
    expect(within(chips).getByRole('button', { name: 'Remove the filter Check is Loading…' })).toBeVisible();
    expect(within(chips).queryByText('empty-anchor-text')).toBeNull();
    cleanup();
    sidecar({ summary: { available: true, reason: null }, checks: 'error', relatedFails: true });
    // Reads that fail for good: their tries come at once, without the app's back-off between them.
    await open(`${URLS}?filter=${encodeURIComponent(JSON.stringify(filter))}`, createQueryClientWithoutBackoff());
    const failed = await screen.findByRole('group', { name: 'Filters' });
    expect(await within(failed).findByText('a check whose name couldn’t be read')).toBeVisible();
    expect(await within(failed).findByText('a URL that couldn’t be read')).toBeVisible();
    expect(within(failed).queryByText(/URL 1$/)).toBeNull();
  });
});

describe('the inspector on the URL grid', () => {
  let uninstall: () => void = () => undefined;
  beforeEach(() => {
    uninstall = installKeyboard();
  });
  afterEach(() => uninstall());

  it('opens on Enter and follows the cursor in place, so back after Esc leaves the screen', async () => {
    sidecar({ summary: { available: true, reason: null } });
    const history = createMemoryHistory({
      initialEntries: [`/crawls/${TEST_CRAWL_ID}/overview`, URLS],
      initialIndex: 1,
    });
    const queryClient = createQueryClient();
    const router = createAppRouter(queryClient, history);
    render(<App router={router} queryClient={queryClient} />);
    await act(() => router.load());
    const grid = await screen.findByRole('grid', { name: 'URLs' });
    await within(grid).findByText('https://example.com/a');
    act(() => grid.focus());
    fireEvent.keyDown(grid, { key: 'Enter' });
    expect(await screen.findByRole('complementary', { name: 'Inspector' })).toBeVisible();
    expect(router.state.location.search).toMatchObject({ inspect: TEST_URL_ROWS[0]?.id });
    // The cursor moves, and the inspector with it, in place.
    fireEvent.keyDown(grid, { key: 'ArrowDown' });
    await waitFor(() => expect(router.state.location.search).toMatchObject({ inspect: TEST_URL_ROWS[1]?.id }));
    expect(history.length).toBe(2);
    fireEvent.keyDown(grid, { key: 'Escape' });
    await waitFor(() => expect(router.state.location.search).not.toHaveProperty('inspect'));
    await act(async () => {
      history.back();
      await router.load();
    });
    expect(router.state.location.pathname).toBe(`/crawls/${TEST_CRAWL_ID}/overview`);
  });
});
