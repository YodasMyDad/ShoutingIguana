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

let uninstall: () => void = () => undefined;

beforeEach(() => {
  setTransport(fakeSidecar(standardRoutes()));
  uninstall = installKeyboard();
});

afterEach(() => {
  uninstall();
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

const sidebar = () => screen.getByRole('navigation', { name: 'Main' });
const appSettings = () => within(sidebar()).getByRole('link', { name: 'Settings' });

describe('the app’s Settings, opened from a crawl (the audit of 2026-11-19, H-10)', () => {
  it('keeps the crawl’s switcher and sections on the sidebar, so the way back is one click', async () => {
    const router = await open(`/crawls/${TEST_CRAWL_ID}/urls`);
    await within(sidebar()).findByRole('button', { name: /^Switch crawl:/ });
    await userEvent.click(appSettings());
    await waitFor(() => expect(router.state.location.pathname).toMatch(/^\/settings/));
    expect(within(sidebar()).getByRole('button', { name: /^Switch crawl:\s*example\.com/ })).toBeVisible();
    // No crawl's section is the current place: Settings is.
    const urls = within(sidebar()).getByRole('link', { name: /^URLs/ });
    expect(urls).not.toHaveAttribute('aria-current');
    expect(appSettings()).toHaveAttribute('aria-current', 'page');
    await userEvent.click(urls);
    await waitFor(() => expect(router.state.location.pathname).toBe(`/crawls/${TEST_CRAWL_ID}/urls`));
  });

  it('keeps the crawl’s keys working there: Ctrl+1 goes to its Overview', async () => {
    const router = await open(`/crawls/${TEST_CRAWL_ID}/urls`);
    await within(sidebar()).findByRole('button', { name: /^Switch crawl:/ });
    await userEvent.click(appSettings());
    await waitFor(() => expect(router.state.location.pathname).toMatch(/^\/settings/));
    await userEvent.keyboard('{Control>}1{/Control}');
    await waitFor(() => expect(router.state.location.pathname).toBe(`/crawls/${TEST_CRAWL_ID}/overview`));
  });

  it('shows no crawl when Settings is opened from Home', async () => {
    const router = await open(`/crawls/${TEST_CRAWL_ID}/overview`);
    await within(sidebar()).findByRole('button', { name: /^Switch crawl:/ });
    await userEvent.click(within(sidebar()).getByRole('link', { name: 'Home' }));
    await waitFor(() => expect(router.state.location.pathname).toBe('/'));
    await userEvent.click(appSettings());
    await waitFor(() => expect(router.state.location.pathname).toMatch(/^\/settings/));
    expect(within(sidebar()).queryByRole('button', { name: /^Switch crawl:/ })).not.toBeInTheDocument();
  });
});

describe('the two settings in a crawl’s sidebar', () => {
  it('have two names: the crawl’s own, and the app’s', async () => {
    await open(`/crawls/${TEST_CRAWL_ID}/overview`);
    const own = await within(sidebar()).findByRole('link', { name: 'This crawl’s settings' });
    expect(own).toHaveAttribute('href', `/crawls/${TEST_CRAWL_ID}/settings`);
    expect(appSettings()).toHaveAttribute('href', '/settings');
  });
});
