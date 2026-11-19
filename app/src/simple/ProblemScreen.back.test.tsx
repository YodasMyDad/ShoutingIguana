// The way back from a problem's page to What to fix (the UX audit of 2026-11-19, S-01): the link above the title, the
// sidebar's item, the breadcrumb and Ctrl+2 all pointed at the page they were on, because the route's optional check
// was inherited from where they were shown.
import { createMemoryHistory } from '@tanstack/react-router';
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { setTransport } from '../api/transport';
import { App } from '../app/App';
import { createAppRouter } from '../app/router';
import { installKeyboard } from '../keyboard/registry';
import { liveStore } from '../live/liveStore';
import { replaceSettings } from '../settings/settingsStore';
import { TEST_CRAWL_ID, fakeSidecar, type FakeRoute } from '../test/fakeSidecar';
import { createQueryClientWithoutBackoff } from '../test/queryClient';
import { analysisWith, gardenItem, problemRoutes, simpleRoutes } from '../test/simpleSidecar';
import { TABLE_STUBBED } from './tableStubbed';

const FIX = `/crawls/${TEST_CRAWL_ID}/issues`;
const ANALYSIS = analysisWith({ 'internal-client-error': 4, 'empty-anchor-text': 265, 'slow-response': 3 }, {});

async function open(path: string, extra: FakeRoute[] = []) {
  replaceSettings({ mode: 'simple', hiddenChecks: [] });
  setTransport(fakeSidecar([...extra, ...problemRoutes(), ...simpleRoutes(gardenItem(), ANALYSIS)]));
  const queryClient = createQueryClientWithoutBackoff();
  const router = createAppRouter(queryClient, createMemoryHistory({ initialEntries: [path] }));
  render(<App router={router} queryClient={queryClient} />);
  await act(() => router.load());
  await screen.findByRole('heading', { level: 1 }, { timeout: 10_000 });
  return router;
}

const onTheList = async (router: Awaited<ReturnType<typeof open>>) => {
  await waitFor(() => expect(router.state.location.pathname).toBe(FIX));
  expect(await screen.findByRole('heading', { level: 1, name: 'What to fix' })).toBeVisible();
};

let uninstall: () => void = () => undefined;
beforeEach(() => {
  uninstall = installKeyboard();
  liveStore.reset();
});
afterEach(() => {
  uninstall();
  setTransport(fakeSidecar([]));
});

describe.skipIf(TABLE_STUBBED)('the way back from a problem to What to fix', () => {
  it('goes to the list from the link above the problem’s title', async () => {
    const router = await open(`${FIX}/internal-client-error`);
    const back = within(screen.getByRole('main')).getByRole('link', { name: 'What to fix' });
    expect(back).toHaveAttribute('href', FIX);
    fireEvent.click(back);
    await onTheList(router);
  });

  it('goes to the list from the sidebar’s What to fix', async () => {
    const router = await open(`${FIX}/internal-client-error`);
    const item = within(screen.getByRole('navigation', { name: 'Main' })).getByRole('link', {
      name: /What to fix/,
    });
    expect(item).toHaveAttribute('href', FIX);
    fireEvent.click(item);
    await onTheList(router);
  });

  it('goes to the list from the breadcrumb, which names the problem’s list as a link', async () => {
    const router = await open(`${FIX}/internal-client-error`);
    const crumb = within(screen.getByRole('navigation', { name: 'Breadcrumb' })).getByRole('link', {
      name: 'What to fix',
    });
    expect(crumb).toHaveAttribute('href', FIX);
    fireEvent.click(crumb);
    await onTheList(router);
  });

  it('goes to the list on Ctrl+2', async () => {
    const router = await open(`${FIX}/internal-client-error`);
    fireEvent.keyDown(document.body, { key: '2', code: 'Digit2', ctrlKey: true });
    await onTheList(router);
  });

  it('keeps the breadcrumb’s What to fix as where you are on the list itself', async () => {
    await open(FIX);
    const crumbs = within(screen.getByRole('navigation', { name: 'Breadcrumb' }));
    expect(crumbs.queryByRole('link', { name: 'What to fix' })).toBeNull();
    expect(crumbs.getByText('What to fix').closest('li')).toHaveAttribute('aria-current', 'page');
  });
});

// S-14: at 1280 × 720 the list a problem's page exists for started below the fold, and "Fixed it?" was under the list.
describe.skipIf(TABLE_STUBBED)('the ways down a problem’s page, from its head', () => {
  it('jumps to where to fix it and to Fixed it?, putting the keys there', async () => {
    Element.prototype.scrollIntoView = () => undefined;
    await open(`${FIX}/internal-client-error`);
    const head = within(screen.getByRole('main').querySelector('header')!);
    fireEvent.click(head.getByRole('button', { name: 'Jump to where to fix it' }));
    expect(screen.getByRole('heading', { level: 2, name: 'Where to fix it' })).toHaveFocus();
    fireEvent.click(head.getByRole('button', { name: 'Fixed it? Check it again' }));
    expect(screen.getByRole('heading', { level: 2, name: 'Fixed it?' })).toHaveFocus();
  });

  it('names the list by what it is on a problem that isn’t a link', async () => {
    await open(`${FIX}/empty-anchor-text`);
    expect(screen.getByRole('button', { name: 'Jump to where to fix it' })).toBeVisible();
    cleanup();
    await open(`${FIX}/slow-response`);
    expect(screen.getByRole('button', { name: 'Jump to the list' })).toBeVisible();
  });
});
