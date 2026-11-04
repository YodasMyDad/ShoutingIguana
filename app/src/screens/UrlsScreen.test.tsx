import { createMemoryHistory } from '@tanstack/react-router';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { GridQuery, SummaryState, UrlRow } from '../api/grids';
import { answerGridQuery } from '../api/mock/gridQuery';
import { gridPagesOf } from '../api/queries';
import { setTransport, type TransportRequest } from '../api/transport';
import type { CrawlStatus } from '../api/types';
import { App } from '../app/App';
import { createQueryClient } from '../app/queryClient';
import { createAppRouter } from '../app/router';
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
        if (state.hold) await state.hold;
        const answer = answerGridQuery(state.rows, request.body as GridQuery, options.summary);
        return answer.ok ? json(answer.page) : problem(answer.problem);
      },
    ],
    ['POST', /^\/api\/crawls\/[0-9a-f]{16}\/analyze$/, analyse],
    ...routes,
  ]);
  setTransport(transport);
  return { transport, analyse, state };
}

async function open(path: string) {
  const queryClient = createQueryClient();
  const router = createAppRouter(queryClient, createMemoryHistory({ initialEntries: [path] }));
  render(<App router={router} queryClient={queryClient} />);
  await act(() => router.load());
  return { router, queryClient };
}

afterEach(() => setTransport(fakeSidecar([])));

describe('the URL grid without the URL summary', () => {
  it('says why the analysis’s columns are empty, and offers to analyse the crawl', async () => {
    const { analyse } = sidecar({ analyzedAt: null, summary: { available: false, reason: 'notAnalyzed' } });
    await open(URLS);
    const notice = await screen.findByText(/Inlinks and Issues need an analysis: this crawl hasn’t been analysed yet/);
    expect(await screen.findByText('https://example.com/a')).toBeVisible();
    // The crawl's own columns are there; the analysis's are a dash, not a 0.
    const row = screen.getByText('https://example.com/a').closest<HTMLElement>('[role="row"]')!;
    expect(within(row).getAllByText('–').length).toBeGreaterThanOrEqual(2);
    fireEvent.click(within(notice).getByRole('button', { name: 'Analyse it' }));
    await waitFor(() => expect(analyse).toHaveBeenCalledOnce());
  });

  it('says the columns fill in after the crawl, and offers nothing it can’t do, while it runs', async () => {
    sidecar({ status: 'running', analyzedAt: null, summary: { available: false, reason: 'crawling' } });
    await open(URLS);
    const notice = await screen.findByText(/fill in once the crawl has finished and been analysed/);
    expect(within(notice).queryByRole('button')).toBeNull();
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

  it('turns a query the engine can’t run into a plain sentence, with a way back', async () => {
    sidecar({ summary: { available: true, reason: null } });
    // `outcome` filters, but doesn't sort.
    const { router } = await open(`${URLS}?sort=-outcome`);
    expect(await screen.findByText('This view’s query can’t be run.')).toBeVisible();
    expect(screen.queryByRole('button', { name: 'Try again' })).toBeNull();
    fireEvent.click(screen.getByRole('button', { name: 'Reset the view' }));
    await waitFor(() => expect(router.state.location.search).toEqual({}));
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
    expect(await within(header).findByText(wholeText('3 URLs'))).toBeVisible();
    expect(within(header).getByText('Live')).toBeVisible();
    state.rows = [...TEST_URL_ROWS, { id: 4, url: 'https://example.com/c', statusCode: 200, statusClass: '2xx' }];
    let release: () => void = () => undefined;
    state.hold = new Promise<void>((resolve) => (release = resolve));
    // What the live bridge does on a progress tick.
    void act(() => queryClient.invalidateQueries(gridPagesOf(TEST_CRAWL_ID), { cancelRefetch: false }));
    await waitFor(() => expect(queryClient.isFetching()).toBeGreaterThan(0));
    // While the refresh is on its way, what's shown stays, quietly.
    expect(screen.getByText('https://example.com/a')).toBeVisible();
    expect(screen.queryByRole('progressbar', { name: 'Refreshing' })).toBeNull();
    state.hold = null;
    act(() => release());
    expect(await within(header).findByText(wholeText('4 URLs'))).toBeVisible();
    expect(await screen.findByText('https://example.com/c')).toBeVisible();
    expect(screen.getByRole('grid', { name: 'URLs' })).toHaveAttribute('aria-busy', 'false');
    expect(screen.queryByRole('progressbar', { name: 'Refreshing' })).toBeNull();
  });
});
