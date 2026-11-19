import { createMemoryHistory } from '@tanstack/react-router';
import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { setTransport } from '../api/transport';
import { App } from '../app/App';
import { resetOverlaysForTests } from '../app/overlays';
import { createQueryClient } from '../app/queryClient';
import { createAppRouter } from '../app/router';
import { installKeyboard } from '../keyboard/registry';
import { updateSettings } from '../settings/settingsStore';
import { resetConnectionForTests } from '../shell/connection';
import { TEST_CRAWL_ID, fakeSidecar, standardRoutes } from '../test/fakeSidecar';
import { resetContextCrawlForTests } from './contextCrawl';
import { setSwitcherOpen } from './switcherOpen';

let uninstall: () => void = () => undefined;

beforeEach(() => {
  setTransport(fakeSidecar(standardRoutes()));
  uninstall = installKeyboard();
});

afterEach(() => {
  uninstall();
  act(() => setSwitcherOpen(false));
  resetOverlaysForTests();
  resetConnectionForTests();
  resetContextCrawlForTests();
  updateSettings({ mode: 'advanced', sidebarCollapsed: false });
});

async function open(path: string) {
  const router = createAppRouter(createQueryClient(), createMemoryHistory({ initialEntries: [path] }));
  render(<App router={router} queryClient={createQueryClient()} />);
  await act(() => router.load());
  return router;
}

const crumbs = () => screen.getByRole('navigation', { name: 'Breadcrumb' });
const bar = () => {
  const header = crumbs().closest('header');
  if (!header) throw new Error('the breadcrumb is outside the top bar');
  return header;
};

describe('the top bar’s crumbs (the audit of 2026-11-19, H-17: only Home was a link)', () => {
  it('the site opens its crawl’s Overview', async () => {
    const router = await open(`/crawls/${TEST_CRAWL_ID}/urls`);
    const site = await within(crumbs()).findByRole('link', { name: 'example.com' });
    await userEvent.click(site);
    await waitFor(() => expect(router.state.location.pathname).toBe(`/crawls/${TEST_CRAWL_ID}/overview`));
  });

  it('the crawl’s date opens the switcher, which lists the crawls to change to', async () => {
    await open(`/crawls/${TEST_CRAWL_ID}/urls`);
    const date = await within(crumbs()).findByRole('button', { name: /: switch crawl$/ });
    expect(screen.queryByRole('menu')).not.toBeInTheDocument();
    await userEvent.click(date);
    const menu = await screen.findByRole('menu');
    expect(within(menu).getByText('Recent crawls')).toBeInTheDocument();
    await userEvent.keyboard('{Escape}');
    await waitFor(() => expect(screen.queryByRole('menu')).not.toBeInTheDocument());
  });

  it('the status leads to what the crawl did: Overview’s "The crawl"', async () => {
    await open(`/crawls/${TEST_CRAWL_ID}/urls`);
    const status = await within(bar()).findByRole('link', { name: /Done/ });
    expect(status).toHaveAttribute('href', `/crawls/${TEST_CRAWL_ID}/overview#the-crawl`);
  });
});

describe('Back and Forward', () => {
  const back = () => within(bar()).getByRole('button', { name: 'Back' });
  const forward = () => within(bar()).getByRole('button', { name: 'Forward' });

  it('are dimmed where there’s nowhere to go, and lit where there is', async () => {
    const router = await open('/');
    expect(back()).toHaveAttribute('aria-disabled', 'true');
    expect(forward()).toHaveAttribute('aria-disabled', 'true');

    await act(() => router.navigate({ to: '/new' }));
    expect(back()).not.toHaveAttribute('aria-disabled');
    expect(forward()).toHaveAttribute('aria-disabled', 'true');

    await userEvent.click(back());
    await waitFor(() => expect(router.state.location.pathname).toBe('/'));
    expect(back()).toHaveAttribute('aria-disabled', 'true');
    expect(forward()).not.toHaveAttribute('aria-disabled');

    // Dimmed, Back does nothing: it never leaves the app's first place.
    await userEvent.click(back());
    expect(router.state.location.pathname).toBe('/');
    await userEvent.click(forward());
    await waitFor(() => expect(router.state.location.pathname).toBe('/new'));
  });
});
