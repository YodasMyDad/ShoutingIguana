import { createBrowserHistory, createHistory, createMemoryHistory, type RouterHistory } from '@tanstack/react-router';
import { act, render, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { setTransport } from '../api/transport';
import { App } from '../app/App';
import { createQueryClient } from '../app/queryClient';
import { createAppRouter, type AppRouter } from '../app/router';
import { TEST_CRAWL_ID, fakeSidecar, standardRoutes } from '../test/fakeSidecar';
import {
  closeInspector,
  goToUrl,
  inspectFromGrid,
  inspectTrail,
  viewInspects,
  walkBack,
  walkTo,
  walkedSteps,
} from './inspector';

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

/**
 * History as a browser keeps it: at most `cap` entries (the oldest goes when another is pushed, as Chromium does at
 * 50), a `go` past either end does nothing and says nothing, and one in range lands a moment later.
 */
function browserLikeHistory(paths: readonly string[], cap = 50): RouterHistory {
  let entries: { path: string; state: unknown }[] = paths.map((path, at) => ({ path, state: { __TSR_index: at } }));
  let index = entries.length - 1;
  const go = (by: number) => {
    setTimeout(() => {
      const to = index + by;
      if (to < 0 || to >= entries.length) return;
      index = to;
      history.notify({ type: 'GO', index: by });
    }, 0);
  };
  const history = createHistory({
    getLocation: () => {
      const entry = entries[index]!;
      const url = new URL(entry.path, 'http://app');
      return {
        href: entry.path,
        pathname: url.pathname,
        search: url.search,
        hash: url.hash,
        state: entry.state as { __TSR_index: number },
      };
    },
    getLength: () => entries.length,
    pushState: (path, state: unknown) => {
      entries = [...entries.slice(0, index + 1), { path, state }].slice(-cap);
      index = entries.length - 1;
    },
    replaceState: (path, state: unknown) => {
      entries[index] = { path, state };
    },
    go,
    back: () => go(-1),
    forward: () => go(1),
    createHref: (path) => path,
    notifyOnIndexChange: false,
  });
  return history;
}

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
  it('opens as one new place: back closes it and returns to the view as it was', async () => {
    const history = createMemoryHistory({
      initialEntries: [`${CRAWL}/overview`, `${CRAWL}/urls?q=rattan`],
      initialIndex: 1,
    });
    const router = await open(history);
    await step(() => inspectFromGrid(router, 1));
    expect(at(router)).toEqual({ path: `${CRAWL}/urls`, inspect: 1 });
    expect(history.length).toBe(3);
    await step(() => history.back());
    // The view it opened from, untouched: its search is still there, and the inspector is closed.
    expect(at(router)).toEqual({ path: `${CRAWL}/urls`, inspect: undefined });
    expect((router.state.location.search as { q?: string }).q).toBe('rattan');
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
    expect(history.length).toBe(3);
    await step(() => history.back());
    expect(at(router)).toEqual({ path: `${CRAWL}/urls`, inspect: undefined });
  });

  it('hops inside it in place: however long the walk, back is one step to the view', async () => {
    const history = fromOverview();
    const router = await open(history);
    await step(() => inspectFromGrid(router, 1));
    await step(() => walkTo(router, 2));
    await step(() => walkTo(router, 3));
    expect(at(router).inspect).toBe(3);
    expect(history.length).toBe(3);
    expect(walkedSteps(router.state.location.state)).toBe(2);
    await step(() => history.back());
    expect(at(router)).toEqual({ path: `${CRAWL}/urls`, inspect: undefined });
  });

  it('keeps the hops for its own way back, with the section each was left on', async () => {
    const history = fromOverview();
    const router = await open(history);
    await step(() => inspectFromGrid(router, 1));
    await step(() => walkTo(router, 2, { leaving: 'inlinks' }));
    await step(() => walkTo(router, 3, { leaving: 'outlinks', show: { section: 'outlinks', link: 9 } }));
    expect(inspectTrail(router.state.location.state)).toEqual([
      { url: 1, section: 'inlinks' },
      { url: 2, section: 'outlinks' },
    ]);
    expect(router.state.location.state.inspectShow).toEqual({ section: 'outlinks', link: 9 });
    await step(() => walkBack(router));
    expect(at(router).inspect).toBe(2);
    // Back on the section it left, and what the hop asked to show isn't asked of this URL.
    expect(router.state.location.state.inspectShow).toEqual({ section: 'outlinks' });
    await step(() => walkBack(router));
    expect(at(router).inspect).toBe(1);
    expect(router.state.location.state.inspectShow).toEqual({ section: 'inlinks' });
    expect(walkedSteps(router.state.location.state)).toBe(0);
    // Nothing left to retrace: it stays.
    await step(() => walkBack(router));
    expect(at(router).inspect).toBe(1);
    expect(history.length).toBe(3);
  });

  it('ends the walk when the grid’s cursor moves it to another URL, and keeps it for the same one', async () => {
    const history = fromOverview();
    const router = await open(history);
    await step(() => inspectFromGrid(router, 1));
    await step(() => walkTo(router, 2));
    // The grid's cursor catching up with the hop: the same URL, the walk still there.
    await step(() => inspectFromGrid(router, 2));
    expect(walkedSteps(router.state.location.state)).toBe(1);
    await step(() => inspectFromGrid(router, 5));
    expect(walkedSteps(router.state.location.state)).toBe(0);
  });

  it('closes by stepping back to the view it opened from, leaving no second entry for it', async () => {
    const history = fromOverview();
    const router = await open(history);
    await step(() => inspectFromGrid(router, 1));
    await step(() => walkTo(router, 2));
    await step(() => closeInspector(router));
    expect(at(router)).toEqual({ path: `${CRAWL}/urls`, inspect: undefined });
    await step(() => history.back());
    expect(at(router).path).toBe(`${CRAWL}/overview`);
  });

  it('closes in place when the view changed while it was open: its filters aren’t undone', async () => {
    const history = fromOverview();
    const router = await open(history);
    await step(() => inspectFromGrid(router, 1));
    await step(() =>
      router.navigate({
        to: '.',
        search: ((previous: object) => ({ ...previous, q: 'rattan' })) as never,
        state: true,
        replace: true,
      }),
    );
    await step(() => closeInspector(router));
    expect(at(router)).toEqual({ path: `${CRAWL}/urls`, inspect: undefined });
    expect((router.state.location.search as { q?: string }).q).toBe('rattan');
  });

  it('closes in place where it was opened by a link, with nothing behind it', async () => {
    const history = createMemoryHistory({ initialEntries: [`${CRAWL}/urls?inspect=4`] });
    const router = await open(history);
    await step(() => closeInspector(router));
    expect(at(router)).toEqual({ path: `${CRAWL}/urls`, inspect: undefined });
    expect(history.length).toBe(1);
  });

  it('closes in place when history can’t step back', async () => {
    // A browser that dropped the entry behind: one entry kept.
    const history = browserLikeHistory([`${CRAWL}/urls`], 1);
    const router = await open(history);
    await step(() => inspectFromGrid(router, 1));
    await act(async () => {
      await closeInspector(router);
    });
    expect(at(router)).toEqual({ path: `${CRAWL}/urls`, inspect: undefined });
  });

  it('closes once when asked twice: the second Esc joins the close under way', async () => {
    // A browser goes back a moment after it's asked: the second Esc comes while the first close is on its way.
    const history = browserLikeHistory(['/', `${CRAWL}/overview`, `${CRAWL}/urls`]);
    const router = await open(history);
    await step(() => inspectFromGrid(router, 1));
    await step(() => walkTo(router, 2));
    await step(() => Promise.all([closeInspector(router), closeInspector(router)]));
    // Time for a second step back to land, had one been asked for.
    await step(() => new Promise((resolve) => setTimeout(resolve, 20)));
    expect(at(router)).toEqual({ path: `${CRAWL}/urls`, inspect: undefined });
    // A close after it is its own: nothing is left of the first.
    await step(() => inspectFromGrid(router, 4));
    await step(() => closeInspector(router));
    expect(at(router)).toEqual({ path: `${CRAWL}/urls`, inspect: undefined });
    history.back();
    await waitFor(() => expect(at(router).path).toBe(`${CRAWL}/overview`));
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

describe('“Go to URL…”', () => {
  it('knows the views that open the inspector', () => {
    for (const section of [
      'issues',
      'issues/title-too-long',
      'urls',
      'links',
      'sitemaps',
      'robots/https%3A%2F%2Fexample.com',
    ]) {
      expect(viewInspects(`${CRAWL}/${section}`, TEST_CRAWL_ID), section).toBe(true);
    }
    for (const path of [
      `${CRAWL}/overview`,
      `${CRAWL}/settings`,
      CRAWL,
      '/',
      '/settings',
      '/crawls/fedcba9876543210/urls',
    ]) {
      expect(viewInspects(path, TEST_CRAWL_ID), path).toBe(false);
    }
  });

  it('opens the inspector as one new place on a view that has it closed, then hops in place', async () => {
    const history = fromOverview();
    const router = await open(history);
    await step(() => goToUrl(router, TEST_CRAWL_ID, 2));
    expect(at(router)).toEqual({ path: `${CRAWL}/urls`, inspect: 2 });
    expect(history.length).toBe(3);
    await step(() => goToUrl(router, TEST_CRAWL_ID, 3));
    expect(history.length).toBe(3);
    expect(walkedSteps(router.state.location.state)).toBe(1);
    // Esc: the screen as it was before, and back from there leaves it.
    await step(() => closeInspector(router));
    expect(at(router)).toEqual({ path: `${CRAWL}/urls`, inspect: undefined });
    await step(() => history.back());
    expect(at(router).path).toBe(`${CRAWL}/overview`);
  });

  it('leaves no dead step: Go to URL with the inspector closed, Esc, then back leaves the view', async () => {
    const history = fromOverview();
    const router = await open(history);
    await step(() => walkTo(router, 2));
    await step(() => closeInspector(router));
    expect(at(router)).toEqual({ path: `${CRAWL}/urls`, inspect: undefined });
    await step(() => history.back());
    expect(at(router).path).toBe(`${CRAWL}/overview`);
  });

  it('opens the crawl’s URLs with the inspector from a view that has none', async () => {
    const history = createMemoryHistory({ initialEntries: [`${CRAWL}/overview`] });
    const router = await open(history);
    await step(() => goToUrl(router, TEST_CRAWL_ID, 2));
    expect(at(router)).toEqual({ path: `${CRAWL}/urls`, inspect: 2 });
    await step(() => closeInspector(router));
    expect(at(router)).toEqual({ path: `${CRAWL}/urls`, inspect: undefined });
    await step(() => history.back());
    expect(at(router).path).toBe(`${CRAWL}/overview`);
  });
});
