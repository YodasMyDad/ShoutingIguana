// What to fix since the site's last check (docs/v2-fix-loop.md, section 4, "What to fix"): each row's change in its
// count's second line, the checks fixed since folded below the severities, the Summary's links landing on them, Copy
// this list saying what moved, and a first check's Fixed group from its rechecks.
import { createMemoryHistory } from '@tanstack/react-router';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { Recheck } from '../api/fixLoop';
import type { CrawlListItem } from '../api/types';
import { setTransport } from '../api/transport';
import { App } from '../app/App';
import { createAppRouter } from '../app/router';
import { resetLikeForLikeForTests } from '../changes/likeForLike';
import * as clipboard from '../lib/clipboard';
import { liveStore } from '../live/liveStore';
import { replaceSettings } from '../settings/settingsStore';
import { TEST_CRAWL_ID, fakeSidecar, json, type FakeRoute } from '../test/fakeSidecar';
import { changesRoute, comparedGardenItem, recheckRecord, recheckResult, recheckRoutes } from '../test/fixLoopSidecar';
import { createQueryClientWithoutBackoff } from '../test/queryClient';
import { GARDEN_ANALYSIS, gardenItem, simpleRoutes } from '../test/simpleSidecar';

const FIX = `/crawls/${TEST_CRAWL_ID}/issues`;

async function open(item: CrawlListItem, extra: FakeRoute[] = [], path = FIX) {
  replaceSettings({ mode: 'simple', hiddenChecks: [], hiddenChecksByCrawl: {} });
  setTransport(fakeSidecar([...extra, ...simpleRoutes(item, GARDEN_ANALYSIS)]));
  const queryClient = createQueryClientWithoutBackoff();
  const router = createAppRouter(queryClient, createMemoryHistory({ initialEntries: [path] }));
  render(<App router={router} queryClient={queryClient} />);
  await act(() => router.load());
  await screen.findByRole('heading', { level: 1, name: 'What to fix' });
  return router;
}

/** A problem's row in the lists (not the Fixed group, nor a related problem's link). */
const row = (name: string) =>
  screen.getAllByRole('link', { name: new RegExp(name) }).find((link) => link.classList.contains('si-plain-row')) ??
  screen.getByRole('link', { name: new RegExp(name) });

afterEach(() => {
  replaceSettings({ mode: 'advanced', hiddenChecks: [], hiddenChecksByCrawl: {} });
  liveStore.reset();
  resetLikeForLikeForTests();
});

describe('What to fix since the last check', () => {
  it('gives each row its change in its count’s second line', async () => {
    await open(comparedGardenItem(), [changesRoute()]);
    await waitFor(() => expect(row('Links that lead to broken pages')).toHaveTextContent('9 on 17 Oct'));
    expect(row('Later pages of a list')).toHaveTextContent('New since 17 Oct');
    expect(row('Pages that are slow to answer')).toHaveTextContent('Not looked for last time');
    expect(row('Pages that share the same title')).toHaveTextContent('same as 17 Oct');
    expect(row('Large images that slow pages down')).toHaveTextContent('58 on 17 Oct');
  });

  it('folds the checks fixed since below the severities, each with its count then', async () => {
    await open(comparedGardenItem(), [changesRoute()]);
    const toggle = await screen.findByRole('button', { name: /^Fixed since your last check on 17 Oct \(4\)/ });
    expect(toggle).toHaveAttribute('aria-expanded', 'false');
    fireEvent.click(toggle);
    const fixed = screen.getByRole('list', { name: 'Fixed since your last check on 17 Oct' });
    const items = within(fixed).getAllByRole('link');
    expect(items.map((link) => link.textContent)).toEqual([
      // Problems first: Serious, then Worth fixing, then the things worth a look.
      expect.stringContaining('Pages or files that fail with a server error'),
      expect.stringContaining('Pages with no title'),
      expect.stringContaining('Addresses that forward more than once'),
      expect.stringContaining('Pages with more than one main heading'),
    ]);
    expect(items[0]).toHaveTextContent('2 addresses on 17 Oct');
    expect(items[0]).toHaveAttribute('href', `${FIX}/internal-server-error`);
  });

  it('opens the Fixed group when the Summary’s "problems fixed" lands on it', async () => {
    await open(comparedGardenItem(), [changesRoute()], `${FIX}#fixed-since`);
    const toggle = await screen.findByRole('button', { name: /^Fixed since your last check/ });
    await waitFor(() => expect(toggle).toHaveAttribute('aria-expanded', 'true'));
    expect(screen.getByRole('list', { name: 'Fixed since your last check on 17 Oct' })).toBeInTheDocument();
  });

  it('puts the keys on the new problem when the Summary’s "1 new" lands', async () => {
    await open(comparedGardenItem(), [changesRoute()], `${FIX}#new-since`);
    await waitFor(() => expect(row('Later pages of a list')).toHaveAttribute('aria-current', 'true'));
    expect(document.activeElement).toBe(row('Later pages of a list'));
  });

  it('says what moved when the list is copied', async () => {
    const copy = vi.spyOn(clipboard, 'copyText').mockResolvedValue(true);
    await open(comparedGardenItem(), [changesRoute()]);
    await waitFor(() => expect(row('Links that lead to broken pages')).toHaveTextContent('9 on 17 Oct'));
    fireEvent.click(screen.getByRole('button', { name: 'Copy this list' }));
    await waitFor(() => expect(copy).toHaveBeenCalled());
    const text = String(copy.mock.calls[0]?.[0]);
    expect(text).toMatch(/Pages or files that can't be found: 9 addresses \(was 12\)/);
    expect(text).toMatch(/Fixed along with it: Links that lead to broken pages, 5 pages \(was 9\)\./);
    expect(text).toMatch(/Later pages of a list that point Google at the first page: 1 page \(new since 17 Oct\)/);
  });

  it('says nothing of changes for a first check', async () => {
    await open(gardenItem({ previous: null, changes: null }), recheckRoutes({ rechecks: [] }));
    await screen.findAllByRole('link', { name: /Links that lead to broken pages/ });
    expect(row('Links that lead to broken pages')).not.toHaveTextContent(/on \d|since/);
    expect(screen.queryByRole('button', { name: /^Fixed/ })).toBeNull();
  });

  it('gives a first check a Fixed group from its rechecks: what one cleared', async () => {
    const cleared: Recheck = recheckRecord({
      checkId: 'title-missing',
      countBefore: 3,
      judged: 3,
      result: recheckResult({ fixed: 3, still: 0 }),
    });
    const notCleared: Recheck = recheckRecord({ id: 2, checkId: 'broken-internal-links', countBefore: 6 });
    await open(gardenItem({ previous: null, changes: null }), [
      ['GET', /^\/api\/crawls\/[0-9a-f]{16}\/rechecks$/, () => json({ rechecks: [notCleared, cleared] })],
    ]);
    const toggle = await screen.findByRole('button', { name: /^Fixed by a recheck \(1\)/ });
    fireEvent.click(toggle);
    const fixed = screen.getByRole('list', { name: 'Fixed by a recheck' });
    expect(within(fixed).getByRole('link')).toHaveTextContent('Pages with no title');
    expect(within(fixed).getByRole('link')).toHaveTextContent('3 pages before the recheck on 17 Nov');
  });
});
