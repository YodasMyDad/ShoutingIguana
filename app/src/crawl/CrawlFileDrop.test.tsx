import { createMemoryHistory } from '@tanstack/react-router';
import { act, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { setTransport } from '../api/transport';
import { App } from '../app/App';
import { createQueryClient } from '../app/queryClient';
import { createAppRouter } from '../app/router';
import { updateSettings } from '../settings/settingsStore';
import { installFakeDrops, type FakeDrops } from '../test/fakeDrops';
import { fakeSidecar, json, problem, standardRoutes, testCrawlItem, type FakeSidecar } from '../test/fakeSidecar';

const DROPPED = testCrawlItem({ id: 'feedfacefeedface', site: 'dropped.example', fileName: 'dropped.sicrawl' });

let drops: FakeDrops | null = null;
afterEach(() => {
  drops?.restore();
  drops = null;
  updateSettings({ mode: 'advanced' });
  setTransport(fakeSidecar([]));
});

async function open(path = '/settings/general') {
  drops = installFakeDrops();
  const sidecar = fakeSidecar([
    [
      'POST',
      /^\/api\/crawls\/open$/,
      (request) =>
        JSON.stringify(request.body).includes('damaged')
          ? problem({ type: 'not-a-crawl', title: 'This file isn’t a crawl Shouting Iguana can read.', status: 422 })
          : json(DROPPED),
    ],
    ...standardRoutes(DROPPED),
  ]);
  setTransport(sidecar);
  const router = createAppRouter(createQueryClient(), createMemoryHistory({ initialEntries: [path] }));
  render(<App router={router} queryClient={createQueryClient()} />);
  await act(() => router.load());
  return { sidecar, router };
}

const opened = (sidecar: FakeSidecar) =>
  sidecar.requests.filter((request) => request.path === '/api/crawls/open').map((request) => request.body);

describe('a crawl file dropped on the window', () => {
  it('opens it from any screen: it joins the library and its crawl opens', async () => {
    const { sidecar, router } = await open();
    act(() => drops?.emit({ type: 'enter', paths: ['C:\\Crawls\\dropped.sicrawl'] }));
    expect(await screen.findByText('Drop to open the crawl file')).toBeVisible();
    act(() => drops?.emit({ type: 'drop', paths: ['C:\\Crawls\\dropped.sicrawl'] }));
    await waitFor(() => expect(router.state.location.pathname).toMatch(/^\/crawls\/feedfacefeedface/));
    expect(opened(sidecar)).toEqual([{ path: 'C:\\Crawls\\dropped.sicrawl' }]);
    expect(screen.queryByText('Drop to open the crawl file')).toBeNull();
  });

  it('says a file that isn’t a crawl file wasn’t opened, and why, without asking the engine', async () => {
    const { sidecar, router } = await open();
    act(() => drops?.emit({ type: 'enter', paths: ['C:\\Docs\\notes.txt'] }));
    expect(await screen.findByText('That isn’t a crawl file')).toBeVisible();
    act(() => drops?.emit({ type: 'drop', paths: ['C:\\Docs\\notes.txt'] }));
    expect(await screen.findByText('notes.txt isn’t a crawl file, so it wasn’t opened.')).toBeVisible();
    expect(opened(sidecar)).toEqual([]);
    expect(router.state.location.pathname).toBe('/settings/general');
  });

  it('says why a crawl file the engine can’t read wasn’t opened', async () => {
    await open();
    act(() => drops?.drop(['C:\\Crawls\\damaged.sicrawl']));
    expect(await screen.findByText('damaged.sicrawl couldn’t be opened.')).toBeVisible();
  });

  it('speaks Simple’s words in Simple', async () => {
    updateSettings({ mode: 'simple' });
    await open('/');
    act(() => drops?.emit({ type: 'enter', paths: ['C:\\Crawls\\a.sicrawl', 'C:\\Crawls\\b.sicrawl'] }));
    expect(await screen.findByText('Drop to open the 2 site check files')).toBeVisible();
    act(() => drops?.emit({ type: 'leave' }));
    await waitFor(() => expect(screen.queryByText('Drop to open the 2 site check files')).toBeNull());
  });
});
