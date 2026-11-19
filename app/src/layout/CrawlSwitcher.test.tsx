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
import type { CrawlListItem } from '../api/types';
import { TEST_CRAWL_ID, fakeSidecar, json, standardRoutes, testCrawlItem } from '../test/fakeSidecar';

const OPEN = testCrawlItem();
const NEWER = testCrawlItem({
  id: 'fedcba9876543210',
  site: 'oakmere.example',
  fileName: 'oakmere.example.sicrawl',
  startUrl: 'https://oakmere.example/',
  startedAt: '2026-11-10T09:00:00Z',
});

let uninstall: () => void = () => undefined;

beforeEach(() => {
  setTransport(
    fakeSidecar([
      ['GET', /^\/api\/crawls$/, () => json([OPEN, NEWER])],
      ['GET', /^\/api\/crawls\/fedcba9876543210$/, () => json(NEWER)],
      ...standardRoutes(OPEN),
    ]),
  );
  uninstall = installKeyboard();
});

afterEach(() => {
  uninstall();
  resetOverlaysForTests();
  resetConnectionForTests();
  updateSettings({ mode: 'advanced', sidebarCollapsed: false });
});

async function open(path: string) {
  const router = createAppRouter(createQueryClient(), createMemoryHistory({ initialEntries: [path] }));
  render(<App router={router} queryClient={createQueryClient()} />);
  await act(() => router.load());
  return router;
}

const sidebar = () => screen.getByRole('navigation', { name: 'Main' });

describe('the crawl switcher', () => {
  it('is a button naming the open crawl and its date, that opens a menu', async () => {
    await open(`/crawls/${TEST_CRAWL_ID}/overview`);
    const button = await within(sidebar()).findByRole('button', { name: /^Switch crawl:\s*example\.com\s*,/ });
    expect(button).toHaveAttribute('aria-haspopup', 'menu');
    expect(button).toHaveAttribute('aria-expanded', 'false');
    // The crawl's region keeps its name and a heading for a screen reader's list of them.
    const region = within(sidebar()).getByRole('region', { name: 'example.com' });
    expect(within(region).getByRole('heading', { level: 2, name: 'example.com' })).toBeInTheDocument();
  });

  it('opens from the keyboard on the recent crawls, newest first, the open one ticked, then Home', async () => {
    await open(`/crawls/${TEST_CRAWL_ID}/overview`);
    const button = await within(sidebar()).findByRole('button', { name: /^Switch crawl:/ });
    act(() => button.focus());
    await userEvent.keyboard('{Enter}');
    const menu = await screen.findByRole('menu');
    const crawls = within(menu).getAllByRole('menuitemradio');
    expect(crawls.map((item) => item.textContent)).toEqual([
      expect.stringContaining('oakmere.example'),
      expect.stringContaining('example.com'),
    ]);
    expect(crawls[0]).toHaveAttribute('aria-checked', 'false');
    expect(crawls[1]).toHaveAttribute('aria-checked', 'true');
    expect(within(menu).getByRole('menuitem', { name: 'All crawls, on Home' })).toBeInTheDocument();
    expect(within(menu).getByText('Recent crawls')).toBeInTheDocument();
    await waitFor(() => expect(crawls[0]).toHaveFocus());
    // Esc closes it and gives focus back to its button.
    await userEvent.keyboard('{Escape}');
    await waitFor(() => expect(screen.queryByRole('menu')).not.toBeInTheDocument());
    expect(button).toHaveFocus();
  });

  it('opens the crawl chosen on the section you’re on', async () => {
    const router = await open(`/crawls/${TEST_CRAWL_ID}/urls`);
    const button = await within(sidebar()).findByRole('button', { name: /^Switch crawl:/ });
    act(() => button.focus());
    await userEvent.keyboard('{Enter}');
    await screen.findByRole('menu');
    await waitFor(() => expect(screen.getAllByRole('menuitemradio')[0]).toHaveFocus());
    await userEvent.keyboard('{Enter}');
    await waitFor(() => expect(router.state.location.pathname).toBe('/crawls/fedcba9876543210/urls'));
  });

  it('goes Home from its last item', async () => {
    const router = await open(`/crawls/${TEST_CRAWL_ID}/issues`);
    await userEvent.click(await within(sidebar()).findByRole('button', { name: /^Switch crawl:/ }));
    await userEvent.click(await screen.findByRole('menuitem', { name: 'All crawls, on Home' }));
    await waitFor(() => expect(router.state.location.pathname).toBe('/'));
  });

  it('is the site’s icon alone in a collapsed sidebar, named the same, and says "site" in Simple', async () => {
    updateSettings({ mode: 'simple' });
    updateSettings({ sidebarCollapsed: true });
    await open(`/crawls/${TEST_CRAWL_ID}/overview`);
    const button = await within(sidebar()).findByRole('button', { name: /^Switch site:\s*example\.com\s*,/ });
    expect(button).toHaveClass('si-switcher--collapsed');
    expect(within(button).queryByText('example.com')).not.toBeInTheDocument();
    await userEvent.click(button);
    const menu = await screen.findByRole('menu');
    expect(within(menu).getByText('Recent checks')).toBeInTheDocument();
    expect(within(menu).getByRole('menuitem', { name: 'All your sites, on Home' })).toBeInTheDocument();
  });

  it('is not there outside a crawl', async () => {
    await open('/');
    await within(sidebar()).findByRole('link', { name: 'Home' });
    expect(within(sidebar()).queryByRole('button', { name: /^Switch/ })).not.toBeInTheDocument();
  });
});

describe('the crawl switcher’s rows (gallery 6: every test-site crawl read "127.0.0.1", two crawls alike)', () => {
  const site = (id: string, status: CrawlListItem['status'], port: number) =>
    testCrawlItem({
      id,
      site: '127.0.0.1',
      fileName: `127.0.0.1-${id}.sicrawl`,
      startUrl: `http://127.0.0.1:${port}/`,
      startedAt: '2026-11-15T20:20:00Z',
      status,
    });
  const PAUSED = site('aaaa000000000001', 'paused', 62801);
  const STOPPED = site('aaaa000000000002', 'stopped', 62801);
  const RUNNING = site('aaaa000000000003', 'running', 62803);
  const DONE = testCrawlItem({ startUrl: 'https://example.com:8443/' });

  beforeEach(() => {
    setTransport(
      fakeSidecar([['GET', /^\/api\/crawls$/, () => json([DONE, PAUSED, STOPPED, RUNNING])], ...standardRoutes(DONE)]),
    );
  });

  async function rows(mode: 'simple' | 'advanced') {
    updateSettings({ mode });
    await open(`/crawls/${TEST_CRAWL_ID}/overview`);
    await userEvent.click(await within(sidebar()).findByRole('button', { name: /^Switch (crawl|site):/ }));
    const menu = await screen.findByRole('menu');
    return within(menu).getAllByRole('menuitemradio');
  }

  it('name each site as Home does, with a port of its own, and the button too', async () => {
    const items = await rows('advanced');
    // The name is the row's first line (the old one, and the new one, both truncate a long name).
    const names = items.map((item) => item.querySelector('.truncate')?.textContent);
    expect(names).toEqual(['127.0.0.1:62801', '127.0.0.1:62801', '127.0.0.1:62803', 'example.com:8443']);
    expect(within(sidebar()).getByRole('button', { name: /^Switch crawl:\s*example\.com:8443\s*,/ })).toBeVisible();
  });

  it('say the state of each crawl that isn’t simply finished, in Advanced’s words', async () => {
    const items = await rows('advanced');
    const states = items.map((item) => item.querySelector('.si-switcher-menu__state')?.textContent ?? null);
    expect(states).toEqual(['Paused', 'Stopped', 'Running', null]);
    // A screen reader hears it with the crawl: the site, when, and the state.
    expect(items[0]).toHaveAccessibleName(/127\.0\.0\.1:62801.*Paused/);
  });

  it('and in Simple’s', async () => {
    const items = await rows('simple');
    const states = items.map((item) => item.querySelector('.si-switcher-menu__state')?.textContent ?? null);
    expect(states).toEqual(['Paused', 'Stopped early', 'Checking', null]);
  });
});

describe('the crawl switcher and the screen’s filters (the audit of 2026-11-19, H-16)', () => {
  const EARLIER = testCrawlItem({
    id: 'aaaa00000000000a',
    fileName: 'example.com-earlier.sicrawl',
    startedAt: '2026-10-01T09:00:00Z',
  });

  beforeEach(() => {
    setTransport(
      fakeSidecar([
        ['GET', /^\/api\/crawls$/, () => json([OPEN, NEWER, EARLIER])],
        ['GET', /^\/api\/crawls\/fedcba9876543210$/, () => json(NEWER)],
        ['GET', /^\/api\/crawls\/aaaa00000000000a$/, () => json(EARLIER)],
        ...standardRoutes(OPEN),
      ]),
    );
  });

  const FILTERED = `/crawls/${TEST_CRAWL_ID}/urls?filter=${encodeURIComponent(
    JSON.stringify([{ column: 'statusClass', op: 'in', values: ['4xx'] }]),
  )}&inspect=12`;

  async function switchTo(name: RegExp) {
    await userEvent.click(await within(sidebar()).findByRole('button', { name: /^Switch crawl:/ }));
    const menu = await screen.findByRole('menu');
    const row = within(menu)
      .getAllByRole('menuitemradio')
      .find((item) => name.test(item.textContent) && item.getAttribute('aria-checked') === 'false');
    if (!row) throw new Error('no such crawl in the switcher');
    await userEvent.click(row);
  }

  it('another crawl of the same site opens on the same screen with the same filter, less the inspected row', async () => {
    const router = await open(FILTERED);
    await switchTo(/example\.com/);
    await waitFor(() => expect(router.state.location.pathname).toBe('/crawls/aaaa00000000000a/urls'));
    expect(router.state.location.search).toEqual({
      filter: [{ column: 'statusClass', op: 'in', values: ['4xx'] }],
    });
  });

  it('another site’s crawl opens on the same screen, unfiltered: its filters were about this site', async () => {
    const router = await open(FILTERED);
    await switchTo(/oakmere\.example/);
    await waitFor(() => expect(router.state.location.pathname).toBe('/crawls/fedcba9876543210/urls'));
    expect(router.state.location.search).toEqual({});
  });

  it('keeps the check and its severity filter on Issues', async () => {
    const router = await open(
      `/crawls/${TEST_CRAWL_ID}/issues/broken-internal?severity=${encodeURIComponent('["error"]')}`,
    );
    await switchTo(/example\.com/);
    await waitFor(() => expect(router.state.location.pathname).toBe('/crawls/aaaa00000000000a/issues/broken-internal'));
    expect(router.state.location.search).toMatchObject({ severity: ['error'] });
  });
});
