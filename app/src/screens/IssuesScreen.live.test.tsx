// The Issues screen while its crawl runs: the live store's 4 Hz progress ticks don't draw the screen again (they'd
// redraw a check with thousands of groups four times a second). Its own file, as it counts the check's renders.
import { createMemoryHistory } from '@tanstack/react-router';
import { act, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { setTransport } from '../api/transport';
import { App } from '../app/App';
import { createQueryClient } from '../app/queryClient';
import { createAppRouter } from '../app/router';
import { PUBLISH_INTERVAL_MS, liveStore } from '../live/liveStore';
import { TEST_CRAWL_ID, fakeSidecar } from '../test/fakeSidecar';
import { issuesRoutes } from '../test/issuesSidecar';
import type * as CheckDetailModule from './issues/CheckDetail';
import { CheckDetail } from './issues/CheckDetail';

vi.mock('./issues/CheckDetail', async (importOriginal) => {
  const actual = await importOriginal<typeof CheckDetailModule>();
  return { ...actual, CheckDetail: vi.fn(actual.CheckDetail) };
});

/** A progress tick, as the sidecar's stream sends it, published once the store's interval has passed. */
async function tick(fetched: number) {
  await act(async () => {
    liveStore.ingest({
      event: 'progress',
      id: String(fetched),
      data: JSON.stringify({ crawlId: TEST_CRAWL_ID, status: 'running', fetched, discovered: fetched * 2 }),
    });
    await new Promise((resolve) => setTimeout(resolve, PUBLISH_INTERVAL_MS + 50));
  });
}

afterEach(() => {
  liveStore.reset();
  setTransport(fakeSidecar([]));
});

describe('the Issues screen while the crawl runs', () => {
  it('isn’t drawn again by the crawl’s progress ticks', async () => {
    setTransport(fakeSidecar(issuesRoutes({ item: { status: 'running' } })));
    const queryClient = createQueryClient();
    const router = createAppRouter(
      queryClient,
      createMemoryHistory({ initialEntries: [`/crawls/${TEST_CRAWL_ID}/issues/internal-client-error`] }),
    );
    render(<App router={router} queryClient={queryClient} />);
    await act(() => router.load());
    expect(await screen.findByRole('heading', { level: 2, name: 'Internal URL returns 4xx' })).toBeVisible();
    await tick(10);
    const renders = vi.mocked(CheckDetail).mock.calls.length;
    await tick(20);
    await tick(30);
    await tick(40);
    expect(vi.mocked(CheckDetail).mock.calls.length).toBe(renders);
  });
});
