// The crawl switcher's score (the fix loop, Advanced: "The crawl switcher adds the score to each row").
import { createMemoryHistory } from '@tanstack/react-router';
import { act, render, screen, within } from '@testing-library/react';
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
import { TEST_CRAWL_ID, fakeSidecar, json, standardRoutes, testCrawlItem } from '../test/fakeSidecar';

const OPEN = testCrawlItem({ score: { value: 86, band: 'good', partial: false } });
const STOPPED = testCrawlItem({
  id: 'fedcba9876543210',
  site: 'oakmere.example',
  fileName: 'oakmere.example.sicrawl',
  startUrl: 'https://oakmere.example/',
  startedAt: '2026-11-10T09:00:00Z',
  status: 'stopped',
  score: { value: 64, band: 'needsWork', partial: true },
});
const NEVER = testCrawlItem({
  id: 'aaaaaaaaaaaaaaaa',
  site: 'fernleigh.example',
  fileName: 'fernleigh.example.sicrawl',
  startUrl: 'https://fernleigh.example/',
  startedAt: '2026-11-09T09:00:00Z',
  analyzedAt: null,
  issues: null,
  score: null,
});

let uninstall: () => void = () => undefined;

beforeEach(() => {
  setTransport(fakeSidecar([['GET', /^\/api\/crawls$/, () => json([OPEN, STOPPED, NEVER])], ...standardRoutes(OPEN)]));
  uninstall = installKeyboard();
});

afterEach(() => {
  uninstall();
  resetOverlaysForTests();
  resetConnectionForTests();
  updateSettings({ mode: 'advanced', sidebarCollapsed: false });
});

async function openMenu() {
  const router = createAppRouter(
    createQueryClient(),
    createMemoryHistory({ initialEntries: [`/crawls/${TEST_CRAWL_ID}/overview`] }),
  );
  render(<App router={router} queryClient={createQueryClient()} />);
  await act(() => router.load());
  const sidebar = screen.getByRole('navigation', { name: 'Main' });
  await userEvent.click(await within(sidebar).findByRole('button', { name: /^Switch (crawl|site):/ }));
  return screen.findByRole('menu');
}

describe('the crawl switcher’s score', () => {
  it('gives each crawl its score in Advanced, "so far" for one that didn’t read the whole site', async () => {
    updateSettings({ mode: 'advanced' });
    const menu = await openMenu();
    const [stopped, never, open] = within(menu).getAllByRole('menuitemradio');
    expect(open).toHaveTextContent(/Score 86$/);
    expect(stopped).toHaveTextContent(/Score 64 so far$/);
    // No score: nothing said, never 0.
    expect(never).not.toHaveTextContent(/Score/);
  });

  it('leaves it out in Simple, whose rows say the score as its ring does elsewhere', async () => {
    updateSettings({ mode: 'simple' });
    const menu = await openMenu();
    expect(menu).not.toHaveTextContent(/Score/);
  });
});
