import { QueryClientProvider } from '@tanstack/react-query';
import { RouterContextProvider, createMemoryHistory, createRootRoute, createRouter } from '@tanstack/react-router';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it } from 'vitest';
import type { RecheckPlan } from '../../api/fixLoop';
import { setTransport } from '../../api/transport';
import type { Check } from '../../api/types';
import { resetPendingRechecks } from '../../crawl/recheck';
import { liveStore } from '../../live/liveStore';
import { TEST_CRAWL_ID, fakeSidecar, type FakeSidecar } from '../../test/fakeSidecar';
import { recheckPlan, recheckRoutes } from '../../test/fixLoopSidecar';
import { SIMPLE_CHECKS } from '../../test/simpleSidecar';
import { createQueryClientWithoutBackoff } from '../../test/queryClient';
import { RecheckControl } from './RecheckControl';

const CHECK = {
  id: 'broken-internal-links',
  title: 'Broken internal links',
  subject: 'page',
  references: 'targets',
  recheck: ['urls'],
} as Check;

function show({
  check = CHECK,
  plan = recheckPlan(),
  group,
  selection,
}: {
  check?: Check;
  plan?: RecheckPlan;
  group?: { detail: string | null; relatedUrlId: number | null; urls: number } | undefined;
  selection?: number[] | undefined;
} = {}): FakeSidecar {
  const sidecar = fakeSidecar(recheckRoutes({ plan }));
  setTransport(sidecar);
  const router = createRouter({ routeTree: createRootRoute(), history: createMemoryHistory() });
  render(
    <QueryClientProvider client={createQueryClientWithoutBackoff()}>
      <RouterContextProvider router={router}>
        <RecheckControl crawlId={TEST_CRAWL_ID} check={check} group={group} selection={selection} />
      </RouterContextProvider>
    </QueryClientProvider>,
  );
  return sidecar;
}

const started = (sidecar: FakeSidecar) =>
  sidecar.requests.filter((request) => request.method === 'POST' && request.path.endsWith('/rechecks'));

afterEach(() => {
  resetPendingRechecks();
  liveStore.reset();
  setTransport(fakeSidecar([]));
});

describe('Recheck in a check’s head (Advanced)', () => {
  it('rechecks the check’s URLs, saying what it fetches and about how long', async () => {
    const sidecar = show();
    const button = await screen.findByRole('button', { name: 'Recheck 5 URLs' });
    expect(screen.getByText('Fetches 8 URLs again (about 20 seconds). Other URLs stay as crawled.')).toBeVisible();
    await userEvent.click(button);
    await waitFor(() => expect(started(sidecar)).toHaveLength(1));
    expect(started(sidecar)[0]?.body).toEqual({ checkId: 'broken-internal-links' });
    // Under way, it can't be asked again.
    expect(await screen.findByRole('button', { name: 'Rechecking…' })).toBeDisabled();
  });

  it('rechecks the open group, or the grid’s selection, from its menu', async () => {
    const sidecar = show({ group: { detail: null, relatedUrlId: 812, urls: 3 }, selection: [31, 40] });
    await userEvent.click(await screen.findByRole('button', { name: 'More ways to recheck' }));
    const menu = await screen.findByRole('menu');
    expect(within(menu).getByRole('menuitem', { name: 'Recheck 2 selected URLs' })).toBeVisible();
    await userEvent.click(within(menu).getByRole('menuitem', { name: 'Recheck this group (3 URLs)' }));
    await waitFor(() => expect(started(sidecar)).toHaveLength(1));
    expect(started(sidecar)[0]?.body).toEqual({
      checkId: 'broken-internal-links',
      group: { detail: null, relatedUrlId: 812 },
    });
  });

  it('offers neither with no group open and nothing selected', async () => {
    show();
    await userEvent.click(await screen.findByRole('button', { name: 'More ways to recheck' }));
    const menu = await screen.findByRole('menu');
    expect(within(menu).getByRole('menuitem', { name: 'Recheck this group' })).toHaveAttribute('aria-disabled', 'true');
    expect(within(menu).getByRole('menuitem', { name: 'Recheck selected URLs' })).toHaveAttribute(
      'aria-disabled',
      'true',
    );
  });

  it('is disabled with the sidecar’s reason when it can’t run now', async () => {
    show({
      plan: recheckPlan({
        refused: { type: 'recheck-not-latest', title: 'Recheck works on the site’s latest check.' },
      }),
    });
    expect(await screen.findByText('Recheck works on the site’s latest check.')).toBeVisible();
    expect(screen.getByRole('button', { name: 'Recheck 5 URLs' })).toBeDisabled();
  });

  it('says past the cap how many it takes', async () => {
    show({
      plan: recheckPlan({ judged: 4_000, capped: true, notRechecked: 3_000, phase1: 1_000, estimateMs: 540_000 }),
    });
    expect(await screen.findByRole('button', { name: 'Recheck 1,000 of 4,000 URLs' })).toBeVisible();
    expect(
      screen.getByText(
        'Fetches 1,000 URLs again (about 9 minutes). Other URLs stay as crawled. 3,000 past the cap stay as crawled.',
      ),
    ).toBeVisible();
  });

  // Every check of the catalog a recheck can't settle (its `recheck` is empty): Check the site again in its place.
  for (const check of SIMPLE_CHECKS.checks.filter((entry) => entry.recheck?.length === 0)) {
    it(`offers Check the site again in its place for ${check.id}, which only checking the whole site can settle`, async () => {
      const sidecar = show({ check });
      const again = await screen.findByRole('link', { name: 'Check the site again' });
      expect(new URL((again as HTMLAnchorElement).href).searchParams.get('from')).toBe(TEST_CRAWL_ID);
      expect(screen.getByText('Only checking the whole site again can show whether this is fixed.')).toBeVisible();
      expect(screen.queryByRole('button', { name: /^Recheck/ })).toBeNull();
      expect(screen.queryByRole('button', { name: 'More ways to recheck' })).toBeNull();
      // Nothing to plan.
      expect(sidecar.requests.some((request) => request.path.endsWith('/rechecks/plan'))).toBe(false);
    });
  }
});
