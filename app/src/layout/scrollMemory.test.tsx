import { createMemoryHistory } from '@tanstack/react-router';
import { act, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { setTransport } from '../api/transport';
import { App } from '../app/App';
import { createQueryClient } from '../app/queryClient';
import { createAppRouter } from '../app/router';
import { replaceSettings } from '../settings/settingsStore';
import { TEST_CRAWL_ID, fakeSidecar, standardRoutes } from '../test/fakeSidecar';
import { forgetScrollForTests } from './scrollMemory';

const CRAWL = `/crawls/${TEST_CRAWL_ID}`;

beforeEach(() => {
  replaceSettings({ mode: 'advanced' });
  setTransport(fakeSidecar(standardRoutes()));
});

afterEach(() => {
  forgetScrollForTests();
  setTransport(fakeSidecar([]));
});

async function open(path: string) {
  const router = createAppRouter(createQueryClient(), createMemoryHistory({ initialEntries: [path] }));
  render(<App router={router} queryClient={createQueryClient()} />);
  await act(() => router.load());
  return router;
}

const main = () => document.getElementById('main')!;

/** The user scrolls: the browser moves the pane and says so. */
function scrollTo(top: number) {
  act(() => {
    main().scrollTop = top;
    main().dispatchEvent(new Event('scroll'));
  });
}

// The audit of 2026-11-19 (H-02): scrolled to Status codes on Overview, a figure opened URLs, and Back came back to the
// top of Overview: every figure below the fold cost a scroll per look.
describe('the main pane’s scroll', () => {
  it('starts a new place at the top, and Back returns to where the last one was left', async () => {
    const router = await open(`${CRAWL}/overview`);
    await screen.findByRole('heading', { level: 1, name: 'Overview' });
    scrollTo(1300);
    await act(() => router.navigate({ to: '/crawls/$crawl/urls', params: { crawl: TEST_CRAWL_ID } }));
    await screen.findByRole('grid', { name: 'URLs' });
    await waitFor(() => expect(main().scrollTop).toBe(0));
    act(() => router.history.back());
    await screen.findByRole('heading', { level: 1, name: 'Overview' });
    await waitFor(() => expect(main().scrollTop).toBe(1300));
    // And Forward again: the grid's screen, at its top.
    act(() => router.history.forward());
    await screen.findByRole('grid', { name: 'URLs' });
    await waitFor(() => expect(main().scrollTop).toBe(0));
  });

  it('stays where it is when only the search changes: a sheet opened, a filter set', async () => {
    const router = await open(`${CRAWL}/overview`);
    await screen.findByRole('heading', { level: 1, name: 'Overview' });
    scrollTo(900);
    await act(() =>
      router.navigate({ to: '/crawls/$crawl/overview', params: { crawl: TEST_CRAWL_ID }, search: { score: 1 } }),
    );
    await act(() => new Promise((resolve) => setTimeout(resolve, 20)));
    expect(main().scrollTop).toBe(900);
  });

  it('keeps each mode’s place on a screen both modes have: Advanced again is where Advanced was left', async () => {
    await open(`${CRAWL}/overview`);
    await screen.findByRole('heading', { level: 1, name: 'Overview' });
    scrollTo(700);
    act(() => replaceSettings({ mode: 'simple' }));
    await act(() => new Promise((resolve) => setTimeout(resolve, 20)));
    act(() => replaceSettings({ mode: 'advanced' }));
    await screen.findByRole('heading', { level: 1, name: 'Overview' });
    await waitFor(() => expect(main().scrollTop).toBe(700));
  });
});
