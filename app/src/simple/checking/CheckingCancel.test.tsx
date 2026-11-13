// Cancelling the results being worked out, from the checking screen: announced once, in Simple's words, and truly
// (a first analysis cancelled leaves no results; a later one leaves the last results).
import type { QueryClient } from '@tanstack/react-query';
import { createMemoryHistory } from '@tanstack/react-router';
import { act, render, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { announce } from '../../feedback/announce';
vi.mock('../../feedback/announce', async (original) => ({ ...(await original<object>()), announce: vi.fn() }));
import { queryKeys } from '../../api/queries';
import { setTransport } from '../../api/transport';
import type { CrawlListItem, CrawlStatus } from '../../api/types';
import { App } from '../../app/App';
import { createQueryClient } from '../../app/queryClient';
import { createAppRouter } from '../../app/router';
import { liveStore, PUBLISH_INTERVAL_MS } from '../../live/liveStore';
import { replaceSettings } from '../../settings/settingsStore';
import { fakeSidecar, TEST_CRAWL_ID } from '../../test/fakeSidecar';
import { message, overviewRoutes, progressAt, type CrawlScript } from '../../test/overviewSidecar';
import { TABLE_STUBBED } from '../tableStubbed';

const OVERVIEW = `/crawls/${TEST_CRAWL_ID}/overview`;
const POLITE: CrawlScript = { rate: 2, size: 6_286, maxUrls: null };

async function open(analyzedAt: string | null) {
  replaceSettings({ mode: 'simple' });
  const item = { analyzedAt, issues: null, status: 'running', finishedAt: null } as const;
  setTransport(fakeSidecar(overviewRoutes({ analysis: null, item })));
  const queryClient = createQueryClient();
  const router = createAppRouter(queryClient, createMemoryHistory({ initialEntries: [OVERVIEW] }));
  render(<App router={router} queryClient={queryClient} />);
  await act(() => router.load());
  return queryClient;
}

async function heard(...events: ReturnType<typeof message>[]) {
  await act(async () => {
    events.forEach((event) => liveStore.ingest(event));
    await new Promise((resolve) => setTimeout(resolve, PUBLISH_INTERVAL_MS + 50));
  });
}

function setStatus(queryClient: QueryClient, status: CrawlStatus) {
  act(() => {
    queryClient.setQueryData<CrawlListItem>(queryKeys.crawlItem(TEST_CRAWL_ID), (item) =>
      item ? { ...item, status } : item,
    );
  });
}

const main = () => within(document.getElementById('main') ?? document.body);

async function cancelTheAnalysis(queryClient: QueryClient): Promise<string[]> {
  await heard(message('progress', progressAt(100, POLITE)));
  setStatus(queryClient, 'analyzing');
  await heard(
    message('crawl', { crawlId: TEST_CRAWL_ID, status: 'completed' }),
    message('crawl', { crawlId: TEST_CRAWL_ID, status: 'analyzing' }),
  );
  vi.mocked(announce).mockClear();
  const cancel = await main().findByRole('button', { name: 'Cancel' });
  act(() => cancel.click());
  setStatus(queryClient, 'completed');
  await heard(message('crawl', { crawlId: TEST_CRAWL_ID, status: 'completed' }));
  await waitFor(() => expect(main().queryByRole('list', { name: 'Steps' })).toBeNull());
  return vi.mocked(announce).mock.calls.map((call) => call[0]);
}

afterEach(() => {
  replaceSettings({ mode: 'advanced' });
  liveStore.reset();
});

describe.skipIf(TABLE_STUBBED)('cancelling the results being worked out, in Simple', () => {
  it('a first time: says the results still haven’t been worked out', async () => {
    const said = await cancelTheAnalysis(await open(null));
    expect(said.filter((words) => /cancel/i.test(words))).toEqual([
      'Working out the results was cancelled. The results haven’t been worked out for this check.',
    ]);
  });

  it('again: says the results shown are the ones worked out before', async () => {
    const said = await cancelTheAnalysis(await open('2026-10-31T15:00:00Z'));
    expect(said.filter((words) => /cancel/i.test(words))).toEqual([
      'Working out the results was cancelled. These are the results worked out before.',
    ]);
  });
});
