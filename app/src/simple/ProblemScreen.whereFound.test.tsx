// Simple's problem page says where to fix each broken address (docs/v2-ui-simple.md, "One problem"): the pages that
// link to it, the first few and how many more, in plain words; the one page linking to most of them; and, for a fault
// every page shares, where on the pages the shared link is.
import { createMemoryHistory } from '@tanstack/react-router';
import { act, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { setTransport } from '../api/transport';
import { App } from '../app/App';
import { createAppRouter } from '../app/router';
import { installKeyboard } from '../keyboard/registry';
import { liveStore } from '../live/liveStore';
import { replaceSettings } from '../settings/settingsStore';
import { TEST_CRAWL_ID, fakeSidecar, json, type FakeRoute } from '../test/fakeSidecar';
import { createQueryClientWithoutBackoff } from '../test/queryClient';
import {
  BROKEN_ADDRESSES,
  analysisWith,
  gardenItem,
  groupPage,
  issuePage,
  problemRoutes,
  rowsPage,
  simpleRoutes,
} from '../test/simpleSidecar';
import { TABLE_STUBBED } from './tableStubbed';

const FIX = `/crawls/${TEST_CRAWL_ID}/issues`;
const ANALYSIS = analysisWith(
  {
    'internal-client-error': 4,
    'empty-anchor-text': 265,
    'blocked-by-robots': 4,
    'non-indexable-inlinks-only': 4,
  },
  {},
);

/** The four addresses /garden-furniture/'s footer links to (three of them), as another check's rows. */
function sameAddresses(checkId: string, detail: string | null): FakeRoute[] {
  const rows = BROKEN_ADDRESSES.map((path, index) => ({ ...issuePage(60 + index, path, null), detail }));
  const group = { detail, relatedUrlId: null, relatedUrl: null, pages: 4, urls: 4, share: 1, likelyTemplate: false };
  return [
    ['POST', new RegExp(`/issues/${checkId}/groups$`), () => json(groupPage(checkId, [group], 4))],
    ['POST', new RegExp(`/issues/${checkId}/rows$`), () => json(rowsPage(rows, rows.length))],
  ];
}

async function open(path: string, extra: FakeRoute[] = []) {
  replaceSettings({ mode: 'simple', hiddenChecks: [] });
  const sidecar = fakeSidecar([...extra, ...problemRoutes(), ...simpleRoutes(gardenItem(), ANALYSIS)]);
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

  // The fixit index audit's X2: it told Jitsunami's owner to remove the Checkout and Cart links for blocked-by-robots.
  it('tells no one to remove the links to addresses search engines are only told not to visit', async () => {
    await open(`${FIX}/blocked-by-robots`, sameAddresses('blocked-by-robots', null));
    const where = within(main().getByRole('region', { name: 'Where it is' }));
    expect((await where.findAllByText('/old-range/', { exact: false }))[0]).toBeVisible();
    // Where each is linked from is still said, row by row; only the callout's advice goes.
    expect((await where.findAllByText(/^Linked from /))[0]).toBeVisible();
    expect(main().queryByText(/One page links to/)).toBeNull();
    expect(main().queryByText(/Fix or remove the links/)).toBeNull();
  });

  // The fixit links audit's item 5: removing the links would make these pages worse off, not better.
  it('tells the owner of pages linked only from hidden pages to link to them from pages that show in search', async () => {
    await open(`${FIX}/non-indexable-inlinks-only`, sameAddresses('non-indexable-inlinks-only', null));
    expect(await main().findByText('One page links to 3 of these 4 pages')).toBeVisible();
    expect(main().getByText(/links to most of them, from its footer\./)).toHaveTextContent(
      '/garden-furniture/ links to most of them, from its footer. It’s kept out of search results itself, so link to these pages from one that shows in search too.',
    );
    expect(main().queryByText(/Fix or remove the links/)).toBeNull();
  });

  it('says where on every page a shared link is, in its One fix', async () => {
    await open(`${FIX}/empty-anchor-text`);
    expect(
      await main().findByText('One fix covers 264 of 265 pages', { selector: '.si-callout__title' }),
    ).toBeVisible();
    // What the pages share, exactly, then the one change that fixes them all.
    const callout = (await main().findByText('One fix covers 264 of 265 pages', { selector: '.si-callout__title' }))
      .parentElement!;
    await waitFor(() =>
      expect(callout).toHaveTextContent(
        /^One fix covers 264 of 265 pagesThe same link, to your home page, is in the header on 264 of these 265 pages\. .*Give it some words once there and all 264 are fixed\.$/,
      ),
    );
  });
});
