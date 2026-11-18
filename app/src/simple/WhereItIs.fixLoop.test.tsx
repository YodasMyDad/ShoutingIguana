// Where it is, since the site's last check (docs/v2-fix-loop.md, section 4, "One problem"): each cause says whether it's
// still there or new, and the causes fixed since and those not checked this time are folded below them.
import { createMemoryHistory } from '@tanstack/react-router';
import { act, fireEvent, render, screen, within } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { setTransport } from '../api/transport';
import { App } from '../app/App';
import { createAppRouter } from '../app/router';
import { resetLikeForLikeForTests } from '../changes/likeForLike';
import { liveStore } from '../live/liveStore';
import { replaceSettings } from '../settings/settingsStore';
import { TEST_CRAWL_ID, fakeSidecar, json, type FakeRoute } from '../test/fakeSidecar';
import {
  BROKEN_LINKS_CLEARED,
  changesRoute,
  compared,
  comparedGardenItem,
  fixedRoute,
  gardenComparison,
  recheckRoutes,
} from '../test/fixLoopSidecar';
import { createQueryClientWithoutBackoff } from '../test/queryClient';
import { GARDEN_ANALYSIS, gardenItem, problemRoutes, simpleRoutes } from '../test/simpleSidecar';
import { TABLE_STUBBED } from './tableStubbed';

const PROBLEM = `/crawls/${TEST_CRAWL_ID}/issues/broken-internal-links`;

async function open(extra: FakeRoute[], item = comparedGardenItem(), path = PROBLEM) {
  replaceSettings({ mode: 'simple', hiddenChecks: [] });
  setTransport(
    fakeSidecar([
      ...extra,
      ...recheckRoutes(),
      ...compared(problemRoutes()),
      ['GET', /^\/api\/crawls\/([0-9a-f]{16})$/, () => json(item)],
      ...simpleRoutes(item, GARDEN_ANALYSIS),
    ]),
  );
  const queryClient = createQueryClientWithoutBackoff();
  const router = createAppRouter(queryClient, createMemoryHistory({ initialEntries: [path] }));
  render(<App router={router} queryClient={queryClient} />);
  await act(() => router.load());
  return within(await screen.findByRole('region', { name: 'Where it is' }, { timeout: 10_000 }));
}

afterEach(() => {
  replaceSettings({ mode: 'advanced', hiddenChecks: [] });
  liveStore.reset();
  resetLikeForLikeForTests();
});

describe.skipIf(TABLE_STUBBED)('Where it is since the last check', () => {
  it('says under each cause whether it’s still there or new', async () => {
    const where = await open([changesRoute(), fixedRoute()]);
    expect((await where.findAllByText('Still there since 17 Oct')).length).toBeGreaterThan(0);
    // The check had one new row, so its causes match as the sidecar's do: none is new here, as none is absent then.
    expect(where.queryByText('New since 17 Oct')).toBeNull();
  });

  it('folds the causes fixed since, each with a tick and how, and those not checked this time apart', async () => {
    const where = await open([
      changesRoute(),
      fixedRoute({
        'broken-internal-links': [
          ...BROKEN_LINKS_CLEARED,
          {
            ...BROKEN_LINKS_CLEARED[0]!,
            thenUrlId: 99,
            state: 'notChecked',
            relatedUrlThen: 'https://www.gardenfurnituresets.co.uk/gone-quiet/',
            reason: { code: 'notRead', text: 'Not read' },
          },
        ],
      }),
    ]);
    const fixed = await where.findByRole('button', { name: 'Fixed since 17 Oct (2)' });
    fireEvent.click(fixed);
    const list = where.getByRole('list', { name: 'Fixed since 17 Oct' });
    expect(list).toHaveTextContent('/old-range/· Fixed');
    expect(list).toHaveTextContent('/summer-sale/· Fixed');
    fireEvent.click(where.getByRole('button', { name: 'Not checked this time (1)' }));
    expect(where.getByRole('list', { name: 'Not checked this time' })).toHaveTextContent(
      '/gone-quiet/· Its page wasn’t read this time',
    );
  });

  // The fixit page's leftovers: a cause fixed since was named by the engine's detail ("H1 → H3").
  it('names a cause fixed since by its detail in plain words', async () => {
    const comparison = gardenComparison();
    // The sidecar's groups of a check grouped by what was found: by their detail then.
    const fixedGroups: FakeRoute = [
      'POST',
      /\/issues\/heading-levels-skipped\/fixed$/,
      () =>
        json({
          checkId: 'heading-levels-skipped',
          compared: {
            with: comparison.with.id,
            startedAt: comparison.with.startedAt,
            comparable: comparison.comparable,
          },
          level: 'groups',
          total: 1,
          offset: 0,
          groups: [
            { detail: 'H1 → H3', relatedUrl: null, sharedText: null, then: 2, fixed: 2, gone: 0, notChecked: 0 },
          ],
        }),
    ];
    const where = await open(
      [changesRoute(comparison), fixedGroups],
      comparedGardenItem(),
      `/crawls/${TEST_CRAWL_ID}/issues/heading-levels-skipped`,
    );
    fireEvent.click(await where.findByRole('button', { name: 'Fixed since 17 Oct (1)' }));
    const list = where.getByRole('list', { name: 'Fixed since 17 Oct' });
    expect(list).toHaveTextContent('It goes from the main heading to a level-3 heading, skipping level 2');
    expect(list).not.toHaveTextContent('H1 → H3');
  });

  it('marks nothing when only the totals are compared, nor for a first check', async () => {
    const countsOnly = gardenComparison({
      comparable: 'countsOnly',
      differences: [{ code: 'startOrigin', then: 'http://a/', now: 'https://a/' }],
    });
    const where = await open(
      [changesRoute(countsOnly), fixedRoute(undefined, countsOnly)],
      comparedGardenItem({}, countsOnly),
    );
    await where.findAllByRole('cell');
    expect(where.queryByText(/since 17 Oct/)).toBeNull();
    expect(where.queryByRole('button', { name: /^Fixed since/ })).toBeNull();
  });

  it('marks nothing for a site’s first check', async () => {
    const where = await open(
      [changesRoute({ crawlId: TEST_CRAWL_ID, with: null })],
      gardenItem({ previous: null, changes: null }),
    );
    await where.findAllByRole('cell');
    expect(where.queryByText(/since/)).toBeNull();
  });
});
