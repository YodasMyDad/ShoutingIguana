import { createBrowserHistory, createMemoryHistory, type RouterHistory } from '@tanstack/react-router';
import { act, render, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { setTransport } from '../api/transport';
import { App } from '../app/App';
import { createQueryClient } from '../app/queryClient';
import { createAppRouter, type AppRouter } from '../app/router';
import { TEST_CRAWL_ID, fakeSidecar, standardRoutes } from '../test/fakeSidecar';
import { closeInspector, inspectFromGrid, walkTo, walkedSteps } from './inspector';

const CRAWL = `/crawls/${TEST_CRAWL_ID}`;

async function open(history: RouterHistory) {
  setTransport(fakeSidecar(standardRoutes()));
  const queryClient = createQueryClient();
  const router = createAppRouter(queryClient, history);
  render(<App router={router} queryClient={queryClient} />);
  await act(() => router.load());
  return router;
}

/** Overview, then URLs: the screen the inspector opens on, with a place before it to go back to. */
const fromOverview = () =>
  createMemoryHistory({ initialEntries: [`${CRAWL}/overview`, `${CRAWL}/urls`], initialIndex: 1 });

const at = (router: AppRouter) => ({
  path: router.state.location.pathname,
  inspect: (router.state.location.search as { inspect?: number }).inspect,
});

const step = (action: () => unknown) =>
  act(async () => {
    await action();
  });

afterEach(() => setTransport(fakeSidecar([])));

describe('the inspector in history', () => {
  it('opens from a grid in place, so back after closing it leaves the screen', async () => {
    const history = fromOverview();
    const router = await open(history);
    await step(() => inspectFromGrid(router, 1));
    expect(at(router)).toEqual({ path: `${CRAWL}/urls`, inspect: 1 });
    expect(history.length).toBe(2);
    await step(() => closeInspector(router));
    expect(at(router)).toEqual({ path: `${CRAWL}/urls`, inspect: undefined });
    await step(() => history.back());
    expect(at(router).path).toBe(`${CRAWL}/overview`);
  });

  it('follows the grid’s cursor in place', async () => {
    const history = fromOverview();
    const router = await open(history);
    await step(() => inspectFromGrid(router, 1));
    await step(() => inspectFromGrid(router, 2));
    await step(() => inspectFromGrid(router, 3));
    expect(at(router).inspect).toBe(3);
    expect(history.length).toBe(2);
  });

  it('retraces a walk with back and forward, and Esc closes to the screen as it was before the inspector', async () => {
    const history = fromOverview();
    const router = await open(history);
    await step(() => inspectFromGrid(router, 1));
    await step(() => walkTo(router, 2));
    await step(() => walkTo(router, 3));
    expect(walkedSteps(router.state.location.state)).toBe(2);
    await step(() => history.back());
    expect(at(router).inspect).toBe(2);
    await step(() => history.forward());
    expect(at(router).inspect).toBe(3);
    await step(() => closeInspector(router));
    expect(at(router)).toEqual({ path: `${CRAWL}/urls`, inspect: undefined });
    expect(walkedSteps(router.state.location.state)).toBe(0);
    await step(() => history.back());
    expect(at(router).path).toBe(`${CRAWL}/overview`);
  });

  it('keeps the steps walked when the cursor moves the inspector to another URL', async () => {
    const history = fromOverview();
    const router = await open(history);
    await step(() => inspectFromGrid(router, 1));
    await step(() => walkTo(router, 2));
    await step(() => inspectFromGrid(router, 5));
    expect(walkedSteps(router.state.location.state)).toBe(1);
    await step(() => closeInspector(router));
    expect(at(router)).toEqual({ path: `${CRAWL}/urls`, inspect: undefined });
    await step(() => history.back());
    expect(at(router).path).toBe(`${CRAWL}/overview`);
  });

  it('never goes further back than history goes', async () => {
    const history = createMemoryHistory({ initialEntries: [`${CRAWL}/urls`] });
    const router = await open(history);
    await step(() =>
      router.navigate({
        to: '.',
        search: { inspect: 4 } as never,
        state: { inspectSteps: 3 },
        replace: true,
      }),
    );
    await step(() => closeInspector(router));
    expect(at(router)).toEqual({ path: `${CRAWL}/urls`, inspect: undefined });
  });

  it('does the same over the browser’s history, whose back is asynchronous', async () => {
    window.history.replaceState(null, '', `${CRAWL}/overview`);
    const history = createBrowserHistory();
    const router = await open(history);
    await step(() => router.navigate({ to: '/crawls/$crawl/urls', params: { crawl: TEST_CRAWL_ID } }));
    await step(() => inspectFromGrid(router, 1));
    await step(() => walkTo(router, 2));
    await step(() => walkTo(router, 3));
    await step(() => closeInspector(router));
    expect(at(router)).toEqual({ path: `${CRAWL}/urls`, inspect: undefined });
    await step(() => history.back());
    await waitFor(() => expect(at(router).path).toBe(`${CRAWL}/overview`));
    history.destroy();
  });
});
