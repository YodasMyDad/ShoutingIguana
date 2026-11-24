import { createMemoryHistory } from '@tanstack/react-router';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { setTransport } from '../api/transport';
import { App } from '../app/App';
import { resetOverlaysForTests } from '../app/overlays';
import { createQueryClient } from '../app/queryClient';
import { createAppRouter } from '../app/router';
import { installKeyboard } from '../keyboard/registry';
import { updateSettings } from '../settings/settingsStore';
import { applyShellState, resetConnectionForTests } from '../shell/connection';
import { liveStore } from '../live/liveStore';
import { TEST_CRAWL_ID, fakeSidecar, standardRoutes, testCrawlItem } from '../test/fakeSidecar';
import { paletteChunk, shortcutsChunk } from './overlayChunks';

const CRAWL = `/crawls/${TEST_CRAWL_ID}`;

let uninstall: () => void = () => undefined;

beforeEach(() => {
  setTransport(fakeSidecar(standardRoutes()));
  uninstall = installKeyboard();
});

afterEach(() => {
  uninstall();
  resetOverlaysForTests();
  resetConnectionForTests();
});

async function open(path: string) {
  const router = createAppRouter(createQueryClient(), createMemoryHistory({ initialEntries: [path] }));
  render(<App router={router} queryClient={createQueryClient()} />);
  await act(() => router.load());
  return router;
}

/** A key pressed where focus is, as a user would: it reaches the focused element, then the window. */
const press = (key: string, init: KeyboardEventInit = {}) =>
  fireEvent.keyDown(document.activeElement ?? document.body, { key, ...init });

describe('the overlays', () => {
  // The first Ctrl+K of a session is as quick as the next. Their code is fetched while the app is idle; with
  // `lazy` and Suspense the first render suspended all the same, showed the (empty) fallback, and React held the
  // palette back for 300 ms after it.
  it('are in the page in the turn that opens them, once their code is there: no fallback first', async () => {
    await Promise.all([paletteChunk.load(), shortcutsChunk.load()]);
    await open(`${CRAWL}/urls`);
    const grid = await screen.findByRole('grid', { name: 'URLs' });
    act(() => grid.focus());
    press('k', { ctrlKey: true });
    // Not `findBy`: nothing is waited for.
    expect(screen.getByRole('dialog', { name: 'Command palette' })).toBeVisible();
    expect(screen.getByRole('combobox')).toHaveFocus();
    press('Escape');
    await waitFor(() => expect(grid).toHaveFocus());
    press('?', { shiftKey: true });
    expect(screen.getByRole('dialog', { name: 'Keyboard shortcuts' })).toBeVisible();
  });

  it('give focus back to the grid when the palette closes with Esc', async () => {
    await open(`${CRAWL}/urls`);
    const grid = await screen.findByRole('grid', { name: 'URLs' });
    act(() => grid.focus());
    press('k', { ctrlKey: true });
    expect(await screen.findByRole('dialog', { name: 'Command palette' })).toBeVisible();
    expect(document.activeElement).toBe(screen.getByRole('combobox'));
    press('Escape');
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    await waitFor(() => expect(document.activeElement).toBe(grid));
  });

  it('give focus back where it was when the shortcut sheet closes', async () => {
    await open(`${CRAWL}/urls`);
    const grid = await screen.findByRole('grid', { name: 'URLs' });
    act(() => grid.focus());
    press('?', { shiftKey: true });
    expect(await screen.findByRole('dialog', { name: 'Keyboard shortcuts' })).toBeVisible();
    press('Escape');
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    await waitFor(() => expect(document.activeElement).toBe(grid));
  });

  it('make the app behind them inert, and keep its keys from acting', async () => {
    const router = await open(`${CRAWL}/overview`);
    await screen.findByRole('heading', { level: 1, name: 'Overview' });
    const shell = screen.getByRole('navigation', { name: 'Main' }).parentElement;
    expect(shell).not.toHaveAttribute('inert');
    act(() => document.getElementById('main')?.focus());
    press('k', { ctrlKey: true });
    await screen.findByRole('dialog', { name: 'Command palette' });
    expect(shell).toHaveAttribute('inert');
    // Ctrl+3 is "Go to URLs": not behind the palette.
    const key = press('3', { ctrlKey: true });
    expect(key).toBe(false); // still the app's key, not the browser's
    await act(() => new Promise((resolve) => setTimeout(resolve, 20)));
    expect(router.state.location.pathname).toBe(`${CRAWL}/overview`);
    // Ctrl+K still closes it.
    press('k', { ctrlKey: true });
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    expect(shell).not.toHaveAttribute('inert');
    press('3', { ctrlKey: true });
    await waitFor(() => expect(router.state.location.pathname).toBe(`${CRAWL}/urls`));
  });
});

describe('stopping crawls while the app quits', () => {
  it('leaves nothing behind the "Stopping crawls…" state to reach, by key or by focus', async () => {
    const router = await open(`${CRAWL}/overview`);
    await screen.findByRole('heading', { level: 1, name: 'Overview' });
    const shell = screen.getByRole('navigation', { name: 'Main' }).parentElement;
    act(() => applyShellState({ state: 'stopping', seq: 1, generation: 1 }));
    const dialog = await screen.findByRole('alertdialog', { name: 'Stopping crawls…' });
    expect(document.activeElement).toBe(dialog);
    expect(shell).toHaveAttribute('inert');
    press('3', { ctrlKey: true });
    await act(() => new Promise((resolve) => setTimeout(resolve, 20)));
    expect(router.state.location.pathname).toBe(`${CRAWL}/overview`);
  });
});

describe('the sidebar, collapsed', () => {
  afterEach(() => updateSettings({ sidebarCollapsed: false }));

  it('names each place by its label, and its tooltip doesn’t read the label again', async () => {
    updateSettings({ sidebarCollapsed: true });
    await open('/');
    const home = within(screen.getByRole('navigation', { name: 'Main' })).getByRole('link', { name: 'Home' });
    act(() => home.focus());
    expect(await screen.findByRole('tooltip')).toBeInTheDocument();
    expect(home).toHaveAccessibleDescription('');
  });
});

describe('the sidebar, open', () => {
  // H-01: the tooltip with an item's keys opened below it, over the next item, and took that item's click.
  it('shows an item’s keys beside it, never over the item below', async () => {
    await open(`${CRAWL}/overview`);
    const urls = await within(screen.getByRole('navigation', { name: 'Main' })).findByRole('link', { name: 'URLs' });
    act(() => urls.focus());
    const tooltip = await screen.findByRole('tooltip');
    expect(tooltip.closest('.si-tooltip')).toHaveAttribute('data-side', 'right');
  });
});

describe('the sidebar’s custom extraction item', () => {
  afterEach(() => updateSettings({ mode: 'advanced' }));

  // It had no link of its own, so it led to the Overview: "Custom data" was drawn current on the Summary beside
  // Summary itself, and a click on it went nowhere.
  it.each([
    ['simple', 'Custom data', 'Summary'],
    ['advanced', 'Custom extraction', 'Overview'],
  ] as const)('leads to its own screen in %s, and isn’t current on the first one', async (mode, name, first) => {
    updateSettings({ mode });
    await open(`${CRAWL}/overview`);
    const nav = within(screen.getByRole('navigation', { name: 'Main' }));
    const item = await nav.findByRole('link', { name });
    expect(item).toHaveAttribute('href', `${CRAWL}/extraction`);
    expect(item).not.toHaveAttribute('aria-current');
    const current = nav.getAllByRole('link').filter((element) => element.classList.contains('si-current'));
    expect(current.map((element) => element.textContent)).toEqual([first]);
  });
});

describe('the sidebar, on a running crawl’s Overview', () => {
  afterEach(() => liveStore.reset());

  it('draws one current place: the crawl’s Overview, not also its running-crawl link', async () => {
    setTransport(fakeSidecar(standardRoutes(testCrawlItem({ status: 'running' }))));
    await open(`${CRAWL}/overview`);
    act(() => liveStore.ingest({ event: 'hello', id: '', data: JSON.stringify({ live: [TEST_CRAWL_ID] }) }));
    const running = await screen.findByRole('region', { name: 'Running crawls' });
    const link = within(running).getByRole('link');
    expect(link).toHaveAttribute('aria-current', 'page');
    expect(link).not.toHaveClass('si-current');
    const current = within(screen.getByRole('navigation', { name: 'Main' }))
      .getAllByRole('link')
      .filter((element) => element.classList.contains('si-current'));
    expect(current.map((element) => element.textContent)).toEqual(['Overview']);
  });
});

describe('the sidebar, while a crawl is rechecked (the fix loop)', () => {
  afterEach(() => liveStore.reset());

  it('lists it as rechecking its site, under Checking now in Simple', async () => {
    updateSettings({ mode: 'simple' });
    try {
      setTransport(fakeSidecar(standardRoutes(testCrawlItem({ status: 'rechecking' }))));
      await open('/');
      act(() => {
        liveStore.ingest({ event: 'hello', id: '', data: JSON.stringify({ live: [TEST_CRAWL_ID] }) });
        liveStore.ingest({
          event: 'crawl',
          id: '',
          data: JSON.stringify({ crawlId: TEST_CRAWL_ID, status: 'rechecking' }),
        });
      });
      const running = await screen.findByRole('region', { name: 'Checking now' });
      expect(await within(running).findByRole('link', { name: /^Rechecking example\.com/ })).toBeInTheDocument();
    } finally {
      updateSettings({ mode: 'advanced' });
    }
  });
});

describe('Esc in the URL grid', () => {
  it('closes the inspector first, after a row was clicked and inspected; the next Esc clears the selection', async () => {
    const router = await open(`${CRAWL}/urls`);
    const grid = await screen.findByRole('grid', { name: 'URLs' });
    const cell = await screen.findByText('https://example.com/a');
    act(() => grid.focus());
    fireEvent.pointerDown(cell, { button: 0 });
    press('Enter');
    expect(await screen.findByRole('complementary', { name: 'Inspector' })).toBeVisible();
    expect(router.state.location.search).toMatchObject({ inspect: 2 });
    const selected = () => screen.getAllByRole('row').filter((row) => row.getAttribute('aria-selected') === 'true');
    expect(selected()).toHaveLength(1);
    press('Escape');
    await waitFor(() => expect(screen.queryByRole('complementary', { name: 'Inspector' })).toBeNull());
    expect(router.state.location.search).not.toHaveProperty('inspect');
    expect(selected()).toHaveLength(1);
    press('Escape');
    expect(selected()).toHaveLength(0);
  });

  /** The URL grid with its second row inspected. */
  async function inspect() {
    const router = await open(`${CRAWL}/urls`);
    const grid = await screen.findByRole('grid', { name: 'URLs' });
    const cell = await screen.findByText('https://example.com/a');
    act(() => grid.focus());
    fireEvent.pointerDown(cell, { button: 0 });
    press('Enter');
    const inspector = await screen.findByRole('complementary', { name: 'Inspector' });
    return { router, grid, inspector };
  }

  it('closes a tooltip and the inspector with one Esc, from a button in the inspector', async () => {
    const { router, inspector } = await inspect();
    const dock = within(inspector).getByRole('button', { name: 'Dock at the bottom' });
    // Focus from the keyboard opens the button's tooltip at once.
    act(() => dock.focus());
    expect(await screen.findByRole('tooltip')).toBeInTheDocument();
    press('Escape');
    await waitFor(() => expect(screen.queryByRole('complementary', { name: 'Inspector' })).toBeNull());
    expect(screen.queryByRole('tooltip')).toBeNull();
    expect(router.state.location.search).not.toHaveProperty('inspect');
  });

  it('closes the inspector with one Esc in the grid while a tooltip shows elsewhere', async () => {
    const { router, grid } = await inspect();
    act(() => grid.focus());
    fireEvent.pointerMove(screen.getByRole('button', { name: 'Back' }), { pointerType: 'mouse' });
    expect(await screen.findByRole('tooltip', {}, { timeout: 1500 })).toBeInTheDocument();
    expect(grid).toHaveFocus();
    press('Escape');
    await waitFor(() => expect(screen.queryByRole('complementary', { name: 'Inspector' })).toBeNull());
    expect(router.state.location.search).not.toHaveProperty('inspect');
    // The grid's selection is the next Esc's.
    expect(screen.getAllByRole('row').filter((row) => row.getAttribute('aria-selected') === 'true')).toHaveLength(1);
  });
});
