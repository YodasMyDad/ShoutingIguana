// The frame round 4 plugs Simple's screens into, tested with the table filled as round 4 will fill it: a row set shows
// its screen in Simple and the Advanced one in Advanced, on the same route, and the Report's row brings its sidebar
// item, its keys and its route. The table itself ships empty (every route shows its Advanced screen): see the last case.
import { createMemoryHistory } from '@tanstack/react-router';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { setTransport } from '../api/transport';
import { installKeyboard } from '../keyboard/registry';
import type { Mode } from '../mode/mode';
import { getSettings, replaceSettings } from '../settings/settingsStore';
import { TEST_CRAWL_ID, fakeSidecar, standardRoutes } from '../test/fakeSidecar';
import { issuesRoutes } from '../test/issuesSidecar';
import { App } from './App';
import { createQueryClient } from './queryClient';
import { createAppRouter } from './router';
import type * as SimpleScreensModule from './simpleScreens';

vi.mock('./simpleScreens', async (importOriginal) => {
  const original = await importOriginal<typeof SimpleScreensModule>();
  const { createElement } = await import('react');
  const screenNamed = (name: string) =>
    Object.assign(() => createElement('h1', null, name), { preload: () => Promise.resolve() });
  return {
    ...original,
    SIMPLE_SCREENS: {
      home: null,
      overview: screenNamed('A Simple Summary'),
      issues: screenNamed('A Simple What to fix'),
      report: screenNamed('The Report'),
    },
  };
});

const CRAWL = `/crawls/${TEST_CRAWL_ID}`;

async function open(path: string, mode: Mode) {
  replaceSettings({ mode });
  setTransport(fakeSidecar([...issuesRoutes(), ...standardRoutes()]));
  const router = createAppRouter(createQueryClient(), createMemoryHistory({ initialEntries: [path] }));
  render(<App router={router} queryClient={createQueryClient()} />);
  await act(() => router.load());
  return router;
}

const sidebar = () => within(screen.getByRole('navigation', { name: 'Main' }));
const modeSwitch = () => screen.getByRole('radiogroup', { name: 'Mode' });

let uninstall: () => void = () => undefined;
beforeEach(() => {
  uninstall = installKeyboard();
});
afterEach(() => {
  uninstall();
  setTransport(fakeSidecar([]));
});

describe('a route with its Simple screen in the table', () => {
  it('shows it in Simple, and the Advanced screen in Advanced, on the same route', async () => {
    const router = await open(`${CRAWL}/overview`, 'simple');
    expect(await screen.findByRole('heading', { level: 1, name: 'A Simple Summary' })).toBeVisible();
    fireEvent.click(within(modeSwitch()).getByRole('radio', { name: 'Advanced' }));
    // A screen's own chunk, loaded the first time: cold, it can take a while.
    expect(await screen.findByRole('heading', { level: 1, name: /Overview/ }, { timeout: 10_000 })).toBeVisible();
    expect(router.state.location.pathname).toBe(`${CRAWL}/overview`);
  });

  it('keeps the search params Simple doesn’t use, and the inspector away, so switching back restores them', async () => {
    const router = await open(`${CRAWL}/issues/internal-client-error?severity=error&inspect=1`, 'simple');
    expect(await screen.findByRole('heading', { level: 1, name: 'A Simple What to fix' })).toBeVisible();
    expect(screen.queryByRole('complementary', { name: 'Inspector' })).toBeNull();
    expect(router.state.location.search).toMatchObject({ severity: ['error'], inspect: 1 });

    fireEvent.click(within(modeSwitch()).getByRole('radio', { name: 'Advanced' }));
    expect(await screen.findByRole('complementary', { name: 'Inspector' }, { timeout: 10_000 })).toBeVisible();
    expect(router.state.location.search).toMatchObject({ severity: ['error'], inspect: 1 });
  });
});

describe('the Report’s row', () => {
  it('lists Report third in Simple, on Ctrl+3', async () => {
    const router = await open(`${CRAWL}/overview`, 'simple');
    const links = await sidebar().findAllByRole('link');
    expect(links.map((link) => link.textContent.replace(/\d+$/, ''))).toEqual([
      'Home',
      'Summary',
      'What to fix',
      'Report',
      'Settings',
    ]);
    fireEvent.keyDown(window, { key: '3', code: 'Digit3', ctrlKey: true });
    await waitFor(() => expect(router.state.location.pathname).toBe(`${CRAWL}/report`));
    expect(await screen.findByRole('heading', { level: 1, name: 'The Report' })).toBeVisible();
  });

  it('lists Report eighth in Advanced, after a rule, on Ctrl+8, the same screen', async () => {
    const router = await open(`${CRAWL}/overview`, 'advanced');
    expect(await sidebar().findByRole('link', { name: /Report/ })).toBeVisible();
    fireEvent.keyDown(window, { key: '8', code: 'Digit8', ctrlKey: true });
    await waitFor(() => expect(router.state.location.pathname).toBe(`${CRAWL}/report`));
    expect(await screen.findByRole('heading', { level: 1, name: 'The Report' })).toBeVisible();
    expect(getSettings().mode).toBe('advanced');
  });
});

describe('a crawl opened by its address', () => {
  it('opens on its last view in the mode: one Simple doesn’t have opens the Summary', async () => {
    replaceSettings({ lastViewByCrawl: { [TEST_CRAWL_ID]: 'urls' } });
    const router = await open(CRAWL, 'simple');
    await waitFor(() => expect(router.state.location.pathname).toBe(`${CRAWL}/overview`));
    expect(getSettings().mode).toBe('simple');
  });

  it('opens on the Report in Simple when that was last', async () => {
    replaceSettings({ lastViewByCrawl: { [TEST_CRAWL_ID]: 'report' } });
    const router = await open(CRAWL, 'simple');
    await waitFor(() => expect(router.state.location.pathname).toBe(`${CRAWL}/report`));
  });
});
