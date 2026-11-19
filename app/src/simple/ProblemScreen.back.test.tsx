// The way back from a problem's page to What to fix (the UX audit of 2026-11-19, S-01): the link above the title, the
// sidebar's item, the breadcrumb and Ctrl+2 all pointed at the page they were on, because the route's optional check
// was inherited from where they were shown.
import { createMemoryHistory } from '@tanstack/react-router';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
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
const ANALYSIS = analysisWith({ 'internal-client-error': 4, 'empty-anchor-text': 265 }, {});

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
