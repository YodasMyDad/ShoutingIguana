import { createMemoryHistory } from '@tanstack/react-router';
import { act, render, screen, within } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { setTransport } from '../api/transport';
import type { CrawlListItem } from '../api/types';
import { App } from '../app/App';
import { createQueryClient } from '../app/queryClient';
import { createAppRouter } from '../app/router';
import { wholeText } from '../test/text';
import { fakeSidecar, json, standardRoutes, testCrawlItem } from '../test/fakeSidecar';

async function open(library: () => Response | Promise<Response>) {
  setTransport(fakeSidecar([['GET', /^\/api\/crawls$/, library], ...standardRoutes()]));
  const router = createAppRouter(createQueryClient(), createMemoryHistory({ initialEntries: ['/'] }));
  render(<App router={router} queryClient={createQueryClient()} />);
  await act(() => router.load());
}

const rowOf = async (name: string) => (await screen.findByRole('link', { name })).closest('tr')!;

afterEach(() => setTransport(fakeSidecar([])));

describe('Home’s URL field', () => {
  it('has a Crawl button named by its word, its keycap shown and given as aria-keyshortcuts', async () => {
    await open(() => json([]));
    const crawl = await screen.findByRole('button', { name: 'Crawl' });
    expect(crawl).toHaveTextContent('CrawlEnter');
    expect(crawl).toHaveAttribute('aria-keyshortcuts', 'Enter');
  });

  it('has a palette button named by its words, its keys given as aria-keyshortcuts', async () => {
    await open(() => json([]));
    const palette = await screen.findByRole('button', { name: 'Search or run a command' });
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
