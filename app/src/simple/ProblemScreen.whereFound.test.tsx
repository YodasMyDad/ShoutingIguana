// Simple's problem page says where to fix each broken address (docs/v2-ui-simple.md, "One problem"): the pages that
// link to it, the first few and how many more, in plain words; the one page linking to most of them; and, for a fault
// every page shares, where on the pages the shared link is.
import { createMemoryHistory } from '@tanstack/react-router';
import { act, render, screen, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { setTransport } from '../api/transport';
import { App } from '../app/App';
import { createAppRouter } from '../app/router';
import { installKeyboard } from '../keyboard/registry';
import { liveStore } from '../live/liveStore';
import { replaceSettings } from '../settings/settingsStore';
import { TEST_CRAWL_ID, fakeSidecar } from '../test/fakeSidecar';
import { createQueryClientWithoutBackoff } from '../test/queryClient';
import { analysisWith, gardenItem, problemRoutes, simpleRoutes } from '../test/simpleSidecar';
import { TABLE_STUBBED } from './tableStubbed';

const FIX = `/crawls/${TEST_CRAWL_ID}/issues`;
const ANALYSIS = analysisWith({ 'internal-client-error': 4, 'empty-anchor-text': 265 }, {});

async function open(path: string) {
  replaceSettings({ mode: 'simple', hiddenChecks: [] });
  const sidecar = fakeSidecar([...problemRoutes(), ...simpleRoutes(gardenItem(), ANALYSIS)]);
  setTransport(sidecar);
  const queryClient = createQueryClientWithoutBackoff();
  const router = createAppRouter(queryClient, createMemoryHistory({ initialEntries: [path] }));
  render(<App router={router} queryClient={queryClient} />);
  await act(() => router.load());
  await screen.findByRole('heading', { level: 1 }, { timeout: 10_000 });
  return sidecar;
}

const main = () => within(screen.getByRole('main'));

let uninstall: () => void = () => undefined;
beforeEach(() => {
  uninstall = installKeyboard();
  liveStore.reset();
});
afterEach(() => {
  uninstall();
  setTransport(fakeSidecar([]));
});

describe.skipIf(TABLE_STUBBED)('where to fix a broken address, in Simple', () => {
  it('says under each broken address which pages link to it, the first few then how many more', async () => {
    const sidecar = await open(`${FIX}/internal-client-error`);
    const where = within(main().getByRole('region', { name: 'Where it is' }));
    // The first page with where on it the link is and what it says: the place to fix it.
    expect(await where.findByText('Linked from /garden-furniture/ (in the footer: “Our range”)')).toBeVisible();
    expect(
      await where.findByText('Linked from /garden-furniture/ (in the footer: “Our range”) and /sofas/'),
    ).toBeVisible();
    expect(
      await where.findByText('Linked from /garden-furniture/ (in the footer: “Our range”), /sofas/ and /blog/'),
    ).toBeVisible();
    expect(where.getByText(/And 1 more\./)).toBeVisible();
    // The addresses shown, and the first ones the One fix looks at, in as few reads as the moment allows: never one an address.
    const reads = sidecar.requests.filter((request) => request.path.endsWith('/urls/found-on'));
    expect(reads.length).toBeLessThanOrEqual(2);
  });

  it('names the one page linking to most of them, and where on it the links are', async () => {
    await open(`${FIX}/internal-client-error`);
    expect(await main().findByText('One page links to 3 of these 4 addresses')).toBeVisible();
    expect(
      main().getByText(/links to most of them, from its footer\. Fix or remove the links there first\./),
    ).toHaveTextContent(
      '/garden-furniture/ links to most of them, from its footer. Fix or remove the links there first.',
    );
  });

  it('says where on every page a shared link is, in its One fix', async () => {
    await open(`${FIX}/empty-anchor-text`);
    expect(await main().findByText('One fix clears 264 of these 265 pages')).toBeVisible();
    expect(await main().findByText(/It’s a link in the header, to/)).toHaveTextContent(
      /It’s a link in the header, to \/\.$/,
    );
  });
});
