// Overview and the fix loop (docs/v2-fix-loop.md, section 4, Advanced, "Overview"): the comparison card under the
// totals with Compare with… at its right, each figure leading to Issues; Fix first's "was 9"; the Rechecks card and the
// crawl in a line; a first crawl with no card, Compare with… saying there's no other crawl.
import { createMemoryHistory } from '@tanstack/react-router';
import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import type { CheckChange, CrawlComparison } from '../api/fixLoop';
import { setTransport } from '../api/transport';
import type { CrawlListItem } from '../api/types';
import { App } from '../app/App';
import { createQueryClient } from '../app/queryClient';
import { createAppRouter } from '../app/router';
import { installKeyboard } from '../keyboard/registry';
import { liveStore } from '../live/liveStore';
import { updateSettings } from '../settings/settingsStore';
import { TEST_CRAWL_ID, fakeSidecar, json, testCrawlItem, type FakeRoute } from '../test/fakeSidecar';
import { changesRoute, gardenComparison, recheckRecord, recheckRoutes } from '../test/fixLoopSidecar';
import { overviewRoutes } from '../test/overviewSidecar';

const EARLIER = 'a1b2c3d4e5f60718';
const EARLIER_ITEM = testCrawlItem({ id: EARLIER, startedAt: '2026-10-17T09:00:00Z', score: null });
const PREVIOUS = {
  id: EARLIER,
  startedAt: '2026-10-17T09:00:00Z',
  score: null,
  comparable: 'full',
  needsAnalysis: false,
} as const;

const change = (checkId: string, patch: Partial<CheckChange>): CheckChange => ({
  checkId,
  severity: 'error',
  severityThen: 'error',
  state: 'same',
  then: 0,
  now: 0,
  fixed: 0,
  gone: 0,
  notChecked: 0,
  still: 0,
  new: 0,
  newUrl: 0,
  notComparedThen: 0,
  ...patch,
});

/** The Issues fixture's checks compared: one fewer (9 then), one new, one fixed. */
function comparison(): CrawlComparison {
  const base = gardenComparison();
  const checks = [
    change('internal-client-error', { state: 'fewer', then: 9, now: 2, fixed: 7, still: 2 }),
    change('empty-anchor-text', {
      severity: 'warning',
      severityThen: 'warning',
      state: 'new',
      then: 0,
      now: 6,
      new: 6,
    }),
    change('internal-server-error', { state: 'fixed', then: 4, now: 0, fixed: 4 }),
    change('title-too-long', { severity: 'notice', severityThen: 'notice', state: 'same', then: 3, now: 3, still: 3 }),
  ];
  return {
    ...base,
    crawlId: TEST_CRAWL_ID,
    with: { ...base.with, id: EARLIER, startedAt: '2026-10-17T09:00:00Z' },
    totals: {
      checks: { fixed: 1, new: 1, fewer: 1, more: 0, same: 1, newCheck: 0, notCheckedNow: 0 },
      issues: base.totals.issues,
    },
    checks,
  };
}

let uninstall: () => void = () => undefined;
beforeEach(() => {
  uninstall = installKeyboard();
});
afterEach(() => {
  uninstall();
  liveStore.reset();
  updateSettings({ compareWithByCrawl: {} });
  setTransport(fakeSidecar([]));
});

async function open(
  item: Partial<CrawlListItem>,
  extra: FakeRoute[] = [],
  path = `/crawls/${TEST_CRAWL_ID}/overview`,
  others: CrawlListItem[] = [EARLIER_ITEM],
) {
  const current = testCrawlItem(item);
  setTransport(
    fakeSidecar([['GET', /^\/api\/crawls$/, () => json([current, ...others])], ...extra, ...overviewRoutes({ item })]),
  );
  const router = createAppRouter(createQueryClient(), createMemoryHistory({ initialEntries: [path] }));
  render(<App router={router} queryClient={createQueryClient()} />);
  await act(() => router.load());
  await waitFor(() => expect(document.querySelector('[aria-label="Loading the overview"]')).toBeNull());
  return router;
}

const main = () => within(document.getElementById('main') ?? document.body);

describe('Overview’s comparison (Advanced)', () => {
  it('shows the card under the totals, Compare with… at its right, each move leading to Issues narrowed to it', async () => {
    await open({ previous: PREVIOUS }, [changesRoute(comparison()), ...recheckRoutes()]);
    const card = within(await main().findByRole('region', { name: /^Since 17 Oct, \d\d:\d\d$/ }));
    expect(card.getByRole('button', { name: /^Compared with 17 Oct/ })).toBeVisible();
    const fixed = await card.findByRole('link', { name: '1 fixed' });
    const href = new URL((fixed as HTMLAnchorElement).href);
    expect(href.pathname).toBe(`/crawls/${TEST_CRAWL_ID}/issues`);
    expect(href.searchParams.get('change')).toBe('fixed');
    expect(card.getByRole('link', { name: '1 new' })).toBeVisible();
    expect(card.getByRole('link', { name: '1 fewer' })).toBeVisible();
  });

  it('says in Fix first what a check’s count was', async () => {
    await open({ previous: PREVIOUS }, [changesRoute(comparison()), ...recheckRoutes()]);
    const fixFirst = within(await main().findByRole('region', { name: 'Fix first' }));
    expect(await fixFirst.findByText(/was 9/)).toBeVisible();
  });

  it('has no card for a site’s only crawl, nor Compare with…: there’s nothing to pick', async () => {
    await open({ previous: null }, recheckRoutes(), undefined, []);
    await main().findByRole('region', { name: 'Fix first' });
    expect(main().queryByRole('button', { name: /^Compare with/ })).toBeNull();
    expect(main().queryByRole('region', { name: /^Since/ })).toBeNull();
  });

  it('says Don’t compare in a line with Compare with…, where the card would be', async () => {
    updateSettings({ compareWithByCrawl: { [TEST_CRAWL_ID]: 'none' } });
    await open({ previous: PREVIOUS }, [changesRoute(comparison()), ...recheckRoutes()]);
    expect(await main().findByText('Not compared with another crawl.')).toBeVisible();
    await userEvent.click(main().getByRole('button', { name: 'Not compared' }));
    await userEvent.click(await screen.findByRole('menuitemradio', { name: /^17 Oct 2026/ }));
    expect(await main().findByRole('region', { name: /^Since 17 Oct/ })).toBeVisible();
  });

  it('opens the picker from the library’s Compare with… (?compare=1)', async () => {
    await open(
      { previous: PREVIOUS },
      [changesRoute(comparison()), ...recheckRoutes()],
      `/crawls/${TEST_CRAWL_ID}/overview?compare=1`,
    );
    const menu = await screen.findByRole('menu');
    expect(within(menu).getByText('Compare with')).toBeVisible();
  });
});

describe('Overview’s rechecks (Advanced)', () => {
  it('lists the crawl’s rechecks in a card, and says them in the crawl’s line', async () => {
    await open(
      {
        previous: null,
        rechecks: {
          count: 1,
          last: { id: 1, endedAt: '2026-11-17T14:02:31Z', checkId: 'broken-internal-links', judged: 5, fixed: 4 },
        },
      },
      recheckRoutes({ rechecks: [recheckRecord()] }),
    );
    const card = within(await main().findByRole('region', { name: 'Rechecks' }));
    expect(await card.findByText('4 of 5 fixed · 1 still · 0 new · Score 86 → 88')).toBeVisible();
    expect(main().getByText(/Rechecked once, on 17 Nov\./)).toBeVisible();
  });

  it('has no Rechecks card for a crawl never rechecked', async () => {
    await open({ previous: null }, recheckRoutes());
    await main().findByRole('region', { name: 'Fix first' });
    expect(main().queryByRole('region', { name: 'Rechecks' })).toBeNull();
  });
});
