import { createMemoryHistory } from '@tanstack/react-router';
import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it } from 'vitest';
import { drawAsTheAppForTests } from '../crawl/fileOpening';
import { commands } from '../keyboard/registry';
import { liveStore } from '../live/liveStore';
import { message, progressAt } from '../test/overviewSidecar';
import type { FakeRoute } from '../test/fakeSidecar';
import { setTransport } from '../api/transport';
import type { CrawlListItem } from '../api/types';
import { App } from '../app/App';
import { createQueryClient } from '../app/queryClient';
import { createAppRouter } from '../app/router';
import { wholeText } from '../test/text';
import { fakeSidecar, json, standardRoutes, testCrawlItem } from '../test/fakeSidecar';

async function open(library: () => Response | Promise<Response>, routes: FakeRoute[] = []) {
  const sidecar = fakeSidecar([...routes, ['GET', /^\/api\/crawls$/, library], ...standardRoutes()]);
  setTransport(sidecar);
  const router = createAppRouter(createQueryClient(), createMemoryHistory({ initialEntries: ['/'] }));
  render(<App router={router} queryClient={createQueryClient()} />);
  await act(() => router.load());
  return { sidecar, router };
}

const rowOf = async (name: string) => (await screen.findByRole('link', { name })).closest('tr')!;

afterEach(() => {
  setTransport(fakeSidecar([]));
  liveStore.reset();
  drawAsTheAppForTests(null);
});

describe('Home’s URL field', () => {
  it('has a Crawl button named by its word, its keycap shown and given as aria-keyshortcuts', async () => {
    await open(() => json([]));
    const crawl = await screen.findByRole('button', { name: 'Crawl' });
    expect(crawl).toHaveTextContent('CrawlEnter');
    expect(crawl).toHaveAttribute('aria-keyshortcuts', 'Enter');
  });

  it('has a palette button named by its words, its keys given as aria-keyshortcuts', async () => {
    await open(() => json([]));
    const palette = await screen.findByRole('button', { name: 'Search or run a command…' });
    expect(palette).toHaveAttribute('aria-keyshortcuts', 'Control+K');
  });
});

describe('Home’s library', () => {
  it('shows "–" for what a file that couldn’t be read can’t say, never 0', async () => {
    const unreadable: CrawlListItem = testCrawlItem({
      id: 'fedcba9876543210',
      site: 'broken.example',
      status: 'unreadable',
      fetchedUrlCount: 0,
      pageCount: 0,
      issues: null,
      error: 'Another program has the file locked.',
    });
    await open(() => json([unreadable]));
    const row = await rowOf('broken.example');
    const cells = within(row).getAllByRole('cell');
    expect(cells[2]).toHaveTextContent('–');
    expect(cells[3]).toHaveTextContent('–');
    expect(cells[4]).toHaveTextContent('–');
    expect(within(row).queryByText('Not analysed yet')).toBeNull();
    expect(within(row).getByText('Another program has the file locked.')).toBeVisible();
  });

  it('says "N URLs" as the URL grid lists them: the URLs that weren’t checked aren’t crawled', async () => {
    // 6,914 outcomes stored, 5 of them never asked as their host had stopped answering: the grid's `fetched` has 6,909.
    await open(() => json([testCrawlItem({ fetchedUrlCount: 6_914, crawledUrlCount: 6_909 })]));
    const cells = within(await rowOf('example.com')).getAllByRole('cell');
    expect(cells[2]).toHaveTextContent('6,909');
  });

  it('counts each severity in good English', async () => {
    await open(() => json([testCrawlItem({ issues: { error: 1, warning: 2, notice: 1 } })]));
    const row = await rowOf('example.com');
    expect(within(row).getByText(wholeText('1 error'))).toBeVisible();
    expect(within(row).getByText(wholeText('2 warnings'))).toBeVisible();
    expect(within(row).getByText(wholeText('1 notice'))).toBeVisible();
  });

  it('keeps its skeleton up for its minimum once shown, even when the library comes a moment later', async () => {
    let release: () => void = () => undefined;
    const arrived = new Promise<void>((resolve) => (release = resolve));
    await open(async () => {
      await arrived;
      return json([testCrawlItem()]);
    });
    const skeleton = await screen.findByRole('status', { name: 'Loading the library' });
    await act(async () => {
      release();
      await new Promise((resolve) => setTimeout(resolve, 30));
    });
    // The library is in, but the skeleton has had well under its 300 ms.
    expect(skeleton).toBeInTheDocument();
    expect(screen.queryByRole('table')).toBeNull();
    expect(await screen.findByRole('table', {}, { timeout: 1_000 })).toBeVisible();
    expect(screen.queryByRole('status', { name: 'Loading the library' })).toBeNull();
  });
});

const site = (id: string, name: string, startedAt: string, overrides: Partial<CrawlListItem> = {}) =>
  testCrawlItem({
    id,
    site: name,
    startUrl: `https://${name}/`,
    fileName: `${name}-${id}.sicrawl`,
    startedAt,
    ...overrides,
  });

describe('Home’s library, as designed (the closing audit’s gap 4)', () => {
  const OLDER = site('a000000000000001', 'garden.example', '2026-10-01T10:00:00Z');
  const LATEST = site('a000000000000002', 'garden.example', '2026-11-01T10:00:00Z', { outOfDate: true });
  const STOPPED = site('b000000000000001', 'oakmere.example', '2026-10-20T10:00:00Z', { status: 'stopped' });
  const FRESH = site('c000000000000001', 'fernleigh.example', '2026-10-10T10:00:00Z', {
    issues: null,
    analyzedAt: null,
  });

  it('groups crawls by site, the latest on top with the older ones folded under it', async () => {
    await open(() => json([OLDER, LATEST, STOPPED, FRESH]));
    const library = await screen.findByRole('region', { name: 'Library' });
    expect(await within(library).findByText('3 sites, 4 crawls')).toBeVisible();
    // A row a site, and the header.
    expect(within(library).getAllByRole('row')).toHaveLength(4);
    const fold = within(await rowOf('garden.example')).getByRole('button', { name: /1 older crawl/ });
    expect(fold).toHaveAttribute('aria-expanded', 'false');
    await userEvent.click(fold);
    expect(fold).toHaveAttribute('aria-expanded', 'true');
    expect(within(library).getAllByRole('row')).toHaveLength(5);
    expect(within(library).getByRole('link', { name: /^garden\.example, 1 Oct 2026/ })).toBeVisible();
  });

  it('gives each state the one action it needs beside its status', async () => {
    const { sidecar } = await open(
      () => json([LATEST, STOPPED, FRESH]),
      [
        ['POST', /\/analyze$/, () => new Response(null, { status: 202 })],
        ['POST', /\/continue$/, () => new Response(null, { status: 202 })],
      ],
    );
    const garden = await rowOf('garden.example');
    expect(within(garden).getByText(/Analysis out of date/)).toBeVisible();
    await userEvent.click(within(garden).getByRole('button', { name: 'Analyse garden.example again' }));
    await userEvent.click(
      within(await rowOf('oakmere.example')).getByRole('button', { name: 'Continue oakmere.example' }),
    );
    await userEvent.click(
      within(await rowOf('fernleigh.example')).getByRole('button', { name: 'Analyse fernleigh.example' }),
    );
    await waitFor(() =>
      expect(sidecar.requests.filter((request) => request.method === 'POST').map((request) => request.path)).toEqual([
        '/api/crawls/a000000000000002/analyze',
        '/api/crawls/b000000000000001/continue',
        '/api/crawls/c000000000000001/analyze',
      ]),
    );
  });

  it('lists every action in a row’s menu, and asks before deleting a file, naming the crawl and its file', async () => {
    const { sidecar } = await open(
      () => json([LATEST]),
      [['POST', /\/delete-file$/, () => new Response(null, { status: 204 })]],
    );
    await userEvent.click(
      within(await rowOf('garden.example')).getByRole('button', { name: 'Actions for garden.example' }),
    );
    const menu = await screen.findByRole('menu');
    expect(
      within(menu)
        .getAllByRole('menuitem')
        .map((item) => item.textContent),
    ).toEqual(['Open', 'Crawl again…', 'Analyse again', 'Remove from library', 'Delete file…']);
    await userEvent.click(within(menu).getByRole('menuitem', { name: 'Delete file…' }));
    const dialog = await screen.findByRole('dialog', { name: 'Delete garden.example’s crawl file?' });
    expect(dialog).toHaveTextContent('garden.example-a000000000000002.sicrawl is deleted from the disk');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Delete file' }));
    await waitFor(() =>
      expect(sidecar.requests.some((request) => request.path === '/api/crawls/a000000000000002/delete-file')).toBe(
        true,
      ),
    );
  });

  it('pins a crawl running now above the library as a card, with its controls and live figures', async () => {
    const RUNNING = site('e000000000000001', 'garden.example', '2026-11-02T10:00:00Z', { status: 'running' });
    liveStore.ingest(message('hello', { live: [RUNNING.id] }));
    liveStore.ingest(message('progress', progressAt(60, { rate: 20, size: 4_000, crawlId: RUNNING.id })));
    await open(() => json([LATEST, RUNNING]));
    const card = await screen.findByRole('article', { name: 'garden.example' });
    expect(screen.getByRole('heading', { level: 2, name: 'Crawling now' })).toBeVisible();
    expect(within(card).getByRole('button', { name: 'Pause garden.example' })).toBeVisible();
    expect(within(card).getByRole('button', { name: 'Stop garden.example' })).toBeVisible();
    expect(within(card).getByRole('link', { name: 'Open garden.example' })).toBeVisible();
    expect(within(card).getAllByText('Crawled')[0]).toBeVisible();
    // The site's earlier crawl stays in the table, marked as having one running now.
    const library = screen.getByRole('region', { name: 'Library' });
    const row = within(library).getByRole('link', { name: 'garden.example' }).closest('tr')!;
    expect(within(row).getByText('Crawling now')).toBeVisible();
  });

  it('shows Open… in the app (the audit’s gap 31), and puts each site’s crawl in the palette', async () => {
    drawAsTheAppForTests(true);
    await open(() => json([LATEST, STOPPED]));
    expect(await screen.findByRole('button', { name: /^Open…/ })).toBeVisible();
    await waitFor(() =>
      expect(commands.list().map((command) => command.title)).toEqual(
        expect.arrayContaining([
          'Search the library',
          'Open garden.example',
          'Analyse garden.example again',
          'Continue oakmere.example',
          'Remove oakmere.example from the library',
        ]),
      ),
    );
  });
});
