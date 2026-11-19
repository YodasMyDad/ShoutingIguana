// The Summary since the site's last check (docs/v2-fix-loop.md, section 4, "The Summary"): one line under the
// sentence, each part a link, a caution line when the two checks aren't like for like, and the last check worked out
// again by itself when it was judged by other rules. A first check shows nothing.
import { createMemoryHistory } from '@tanstack/react-router';
import { act, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import type { CrawlComparison } from '../api/fixLoop';
import type { CrawlListItem } from '../api/types';
import { setTransport } from '../api/transport';
import { App } from '../app/App';
import { createAppRouter } from '../app/router';
import { liveStore } from '../live/liveStore';
import { replaceSettings } from '../settings/settingsStore';
import { resetLikeForLikeForTests } from '../changes/likeForLike';
import { TEST_CRAWL_ID, fakeSidecar, json, type FakeRoute, type FakeSidecar } from '../test/fakeSidecar';
import { EARLIER_ID, changesRoute, comparedGardenItem, gardenComparison } from '../test/fixLoopSidecar';
import { createQueryClientWithoutBackoff } from '../test/queryClient';
import { GARDEN_ANALYSIS, gardenItem, simpleRoutes } from '../test/simpleSidecar';
import { TABLE_STUBBED } from './tableStubbed';

const CRAWL = `/crawls/${TEST_CRAWL_ID}`;
const FIX = `${CRAWL}/issues`;

async function open(item: CrawlListItem, extra: FakeRoute[] = [], hidden: string[] = []): Promise<FakeSidecar> {
  replaceSettings({ mode: 'simple', hiddenChecks: hidden, hiddenChecksByCrawl: {} });
  const sidecar = fakeSidecar([...extra, ...simpleRoutes(item, GARDEN_ANALYSIS)]);
  setTransport(sidecar);
  const queryClient = createQueryClientWithoutBackoff();
  const router = createAppRouter(queryClient, createMemoryHistory({ initialEntries: [`${CRAWL}/overview`] }));
  render(<App router={router} queryClient={queryClient} />);
  await act(() => router.load());
  await screen.findByRole('heading', { level: 2, name: 'Good' });
  return sidecar;
}

const card = () => screen.getByRole('region', { name: 'Good' });

afterEach(() => {
  replaceSettings({ mode: 'advanced', hiddenChecks: [], hiddenChecksByCrawl: {} });
  liveStore.reset();
  resetLikeForLikeForTests();
});

describe.skipIf(TABLE_STUBBED)('the Summary while a recheck runs', () => {
  it('stays, never the checking screen, through the recheck’s analyses', async () => {
    await open(comparedGardenItem(), [changesRoute()]);
    act(() => {
      liveStore.ingest({
        event: 'crawl',
        id: '',
        data: JSON.stringify({ crawlId: TEST_CRAWL_ID, status: 'rechecking' }),
      });
      liveStore.ingest({
        event: 'recheck',
        id: '',
        data: JSON.stringify({
          crawlId: TEST_CRAWL_ID,
          recheckId: 1,
          status: 'completed',
          phase: 1,
          done: 8,
          total: 8,
          result: null,
        }),
      });
      liveStore.ingest({
        event: 'crawl',
        id: '',
        data: JSON.stringify({ crawlId: TEST_CRAWL_ID, status: 'analyzing' }),
      });
    });
    await act(() => new Promise((resolve) => setTimeout(resolve, 300)));
    expect(screen.getByRole('heading', { level: 2, name: 'Good' })).toBeInTheDocument();
    expect(screen.queryByRole('heading', { level: 1, name: /Checking|Working out/ })).toBeNull();
  });
});

describe.skipIf(TABLE_STUBBED)('the Summary since the last check', () => {
  it('says the score then and now, the problems fixed and new, each a link to What to fix', async () => {
    await open(comparedGardenItem(), [changesRoute()]);
    const line = await within(card()).findByText(/^Since your last check on 17 Oct:/);
    expect(line).toHaveTextContent(
      'Since your last check on 17 Oct: score 72 → 87, 3 problems fixed, 1 new. See what changed',
    );
    expect(within(line).getByRole('link', { name: '3 problems fixed' })).toHaveAttribute('href', `${FIX}#fixed-since`);
    expect(within(line).getByRole('link', { name: '1 new' })).toHaveAttribute('href', `${FIX}#new-since`);
    expect(within(line).getByRole('link', { name: 'See what changed' })).toHaveAttribute('href', FIX);
  });

  it('leaves the checks the user has hidden out of the counts', async () => {
    await open(comparedGardenItem(), [changesRoute()], ['title-missing']);
    expect(await within(card()).findByText(/^Since your last check/)).toHaveTextContent('2 problems fixed, 1 new');
  });

  it('says nothing for a site’s first check, and doesn’t ask for a comparison', async () => {
    const sidecar = await open(gardenItem({ previous: null, changes: null }), [
      changesRoute({ crawlId: TEST_CRAWL_ID, with: null }),
    ]);
    await act(() => new Promise((resolve) => setTimeout(resolve, 50)));
    expect(card()).not.toHaveTextContent(/since|fixed/i);
    expect(sidecar.requests.some((request) => request.path.endsWith('/changes'))).toBe(false);
  });

  it('shows the rest of the Summary while the comparison is on its way, without the line', async () => {
    await open(comparedGardenItem(), [changesRoute('never')]);
    expect(card()).toHaveTextContent('Your site is in good shape');
    expect(card()).not.toHaveTextContent(/Since your last check/);
  });

  it('says in one line what differs when the two checks aren’t like for like', async () => {
    const partial = gardenComparison({
      comparable: 'partial',
      differences: [{ code: 'coverage', then: 265, now: 50 }],
    });
    await open(comparedGardenItem({}, partial), [changesRoute(partial)]);
    expect(
      await within(card()).findByText(
        'Your last check covered more of the site (265 pages against 50 now), so some of it couldn’t be compared.',
      ),
    ).toBeInTheDocument();
  });

  it('says only the totals are compared when the start address differs', async () => {
    const countsOnly = gardenComparison({
      comparable: 'countsOnly',
      differences: [
        {
          code: 'startOrigin',
          then: 'http://www.gardenfurnituresets.co.uk/',
          now: 'https://www.gardenfurnituresets.co.uk/',
        },
      ],
    });
    await open(comparedGardenItem({}, countsOnly), [changesRoute(countsOnly)]);
    expect(await within(card()).findByText(/^Since your last check on 17 Oct:/)).toBeInTheDocument();
    expect(
      within(card()).getByText(
        'This check started at https://www.gardenfurnituresets.co.uk/, the last at http://www.gardenfurnituresets.co.uk/, so only the totals are compared.',
      ),
    ).toBeInTheDocument();
  });

  it('works the last check out again by itself when it was judged by other rules, then says the line', async () => {
    let analysed = false;
    const stale: CrawlComparison = gardenComparison({
      comparable: 'countsOnly',
      previousNeedsAnalysis: ['engineChanged'],
    });
    const sidecar = await open(comparedGardenItem({}, stale), [
      ['GET', /^\/api\/crawls\/[0-9a-f]{16}\/changes/, () => json(analysed ? gardenComparison() : stale)],
      [
        'POST',
        /^\/api\/crawls\/([0-9a-f]{16})\/analyze$/,
        () => {
          analysed = true;
          return new Response(null, { status: 202 });
        },
      ],
    ]);
    await waitFor(() =>
      expect(sidecar.requests.filter((request) => request.path === `/api/crawls/${EARLIER_ID}/analyze`)).toHaveLength(
        1,
      ),
    );
    // Not until it's done: no line meanwhile, counts only or not.
    expect(card()).not.toHaveTextContent(/Since your last check/);
    // Its analysis ends: every comparison is read again.
    act(() => {
      liveStore.ingest({ event: 'crawl', id: '', data: JSON.stringify({ crawlId: EARLIER_ID, status: 'analyzing' }) });
      liveStore.ingest({ event: 'crawl', id: '', data: JSON.stringify({ crawlId: EARLIER_ID, status: 'completed' }) });
    });
    const line = await within(card()).findByText(/^Since your last check on 17 Oct:/, {}, { timeout: 3_000 });
    // Its tooltip says the last check was worked out again (at once on keyboard focus).
    act(() => within(line).getByRole('link', { name: 'See what changed' }).focus());
    expect(await screen.findByRole('tooltip')).toHaveTextContent(
      'Your last check was worked out again with today’s checks.',
    );
    // Asked once only.
    expect(sidecar.requests.filter((request) => request.path === `/api/crawls/${EARLIER_ID}/analyze`)).toHaveLength(1);
  });
});

describe.skipIf(TABLE_STUBBED)('how the score is worked out, since the last check', () => {
  async function breakdown(item: CrawlListItem, extra: FakeRoute[]) {
    replaceSettings({ mode: 'simple', hiddenChecks: [], hiddenChecksByCrawl: {} });
    setTransport(fakeSidecar([...extra, ...simpleRoutes(item, GARDEN_ANALYSIS)]));
    const queryClient = createQueryClientWithoutBackoff();
    const router = createAppRouter(queryClient, createMemoryHistory({ initialEntries: [`${CRAWL}/overview?score=1`] }));
    render(<App router={router} queryClient={queryClient} />);
    await act(() => router.load());
    return within(await screen.findByRole('dialog', { name: 'How the score is worked out' }));
  }

  it('gives each line its points then, and folds the lines that took points then and none now', async () => {
    const dialog = await breakdown(comparedGardenItem(), [changesRoute()]);
    const broken = (await dialog.findByRole('link', { name: /Links that lead to broken pages/ })).closest('tr');
    expect(broken).toHaveTextContent('−4 · was −7');
    const fixed = dialog.getByRole('button', { name: 'Fixed since 17 Oct (3)' });
    expect(fixed).toHaveAttribute('aria-expanded', 'false');
    act(() => fixed.click());
    const list = dialog.getByRole('list', { name: 'Fixed since 17 Oct' });
    expect(
      within(list)
        .getAllByRole('listitem')
        .map((entry) => entry.textContent),
    ).toEqual([
      expect.stringContaining('−6 then'),
      expect.stringContaining('−5 then'),
      expect.stringContaining('−3 then'),
    ]);
  });

  it('says nothing of then for a first check, nor for one scored another way', async () => {
    const first = await breakdown(gardenItem({ previous: null, changes: null }), []);
    const broken = (await first.findByRole('link', { name: /Links that lead to broken pages/ })).closest('tr');
    expect(broken).not.toHaveTextContent('was');
    expect(first.queryByRole('button', { name: /Fixed since/ })).toBeNull();
  });
});

describe.skipIf(TABLE_STUBBED)('Fix these first, since the last check', () => {
  it('gives each row its change under its count', async () => {
    await open(comparedGardenItem(), [changesRoute()]);
    const first = screen.getByRole('region', { name: /Fix these first/ });
    await waitFor(() =>
      expect(within(first).getByRole('link', { name: /Pages or files that can't be found/ })).toHaveTextContent(
        '12 on 17 Oct',
      ),
    );
    expect(within(first).getByRole('link', { name: /Pages that are slow to answer/ })).toHaveTextContent(
      'Not looked for last time',
    );
  });
});
