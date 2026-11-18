// One problem only the site's platform can change (the Shopify lane, phase B): who sets it and why, as the sidecar says
// it, beside the check; what was found and where; never steps to fix it, a recheck to make, or points to win back.
import { createMemoryHistory } from '@tanstack/react-router';
import { act, render, screen, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { setTransport } from '../api/transport';
import type { Analysis } from '../api/types';
import { App } from '../app/App';
import { createAppRouter } from '../app/router';
import { resetPendingRechecks } from '../crawl/recheck';
import { liveStore } from '../live/liveStore';
import { replaceSettings } from '../settings/settingsStore';
import { TEST_CRAWL_ID, fakeSidecar } from '../test/fakeSidecar';
import { recheckRoutes } from '../test/fixLoopSidecar';
import { SHOPIFY_OWNER, platformOwned } from '../test/platformSidecar';
import { createQueryClientWithoutBackoff } from '../test/queryClient';
import { GARDEN_ANALYSIS, gardenItem, simpleRoutes } from '../test/simpleSidecar';
import { TABLE_STUBBED } from './tableStubbed';

const PROBLEM = `/crawls/${TEST_CRAWL_ID}/issues/cookies-insecure`;
const TITLE = 'Cookies your site sets without basic protection';

async function open(analysis: Analysis) {
  replaceSettings({ mode: 'simple', hiddenChecks: [] });
  setTransport(fakeSidecar([...recheckRoutes(), ...simpleRoutes(gardenItem(), analysis)]));
  const queryClient = createQueryClientWithoutBackoff();
  const router = createAppRouter(queryClient, createMemoryHistory({ initialEntries: [PROBLEM] }));
  render(<App router={router} queryClient={queryClient} />);
  await act(() => router.load());
  await screen.findByRole('heading', { level: 1, name: TITLE }, { timeout: 10_000 });
}

const main = () => within(screen.getByRole('main'));

beforeEach(() => {
  liveStore.reset();
  resetPendingRechecks();
});
afterEach(() => {
  replaceSettings({ mode: 'advanced', hiddenChecks: [] });
  liveStore.reset();
  resetPendingRechecks();
});

describe.skipIf(TABLE_STUBBED)('one problem the site’s platform sets', () => {
  it('says who sets it and why, as given, beside it, with what was found and where', async () => {
    await open(platformOwned(GARDEN_ANALYSIS));
    expect(main().getByText(SHOPIFY_OWNER.reason)).toBeVisible();
    expect(main().getByText(/^These pages store small files in visitors' browsers \(cookies\)/)).toBeVisible();
    expect(main().getByText('Doesn’t change the score')).toBeVisible();
    expect(await main().findByRole('region', { name: 'Where it is' })).toBeVisible();
  });

  it('never tells the owner to fix it: no steps, no one to fix it, no recheck, no points back', async () => {
    await open(platformOwned(GARDEN_ANALYSIS));
    expect(main().queryByRole('heading', { name: 'How to fix it' })).toBeNull();
    expect(main().queryByText(/Send the list to your developer|Ask them to/)).toBeNull();
    expect(main().queryByText(/^Usually fixed by/)).toBeNull();
    expect(main().queryByRole('region', { name: 'Fixed it?' })).toBeNull();
    expect(main().queryByRole('button', { name: /Recheck/ })).toBeNull();
    expect(main().queryByText(/points? back|off the score/)).toBeNull();
    // Nothing its words say contradicts Shopify's: no later sentence saying a developer can make the change.
    expect(main().queryByText(/developer/)).toBeNull();
  });

  it('says how to fix it, and offers the recheck, for an analysis that names no platform', async () => {
    await open(GARDEN_ANALYSIS);
    expect(main().getByRole('heading', { name: 'How to fix it' })).toBeVisible();
    expect(main().queryByText(SHOPIFY_OWNER.reason)).toBeNull();
    expect(await main().findByRole('region', { name: 'Fixed it?' })).toBeVisible();
  });
});
