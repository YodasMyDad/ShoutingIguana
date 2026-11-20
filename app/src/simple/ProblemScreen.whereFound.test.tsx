// Simple's problem page says where to fix each broken address (docs/v2-ui-simple.md, "One problem"): the pages that
// link to it, the first few and how many more, in plain words; the one page linking to most of them; and, for a fault
// every page shares, where on the pages the shared link is.
import { createMemoryHistory } from '@tanstack/react-router';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
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
  // The UX audit of 2026-11-19 (S-02 to S-04, and its section 4): this pinned the dead address leading, with "Linked
  // from /garden-furniture/ (in the footer: “Our range”) and /sofas/" as words under it and the page unopenable. The
  // card now leads with the page to edit.
  it('lists a dead address by the page to edit: each page once, its links under it, and what nothing links to apart', async () => {
    const sidecar = await open(`${FIX}/internal-client-error`);
    const where = within(await main().findByRole('region', { name: 'Where to fix it' }));
    expect(await where.findByText('6 links to change on 3 pages · 1 address no page links to')).toBeVisible();
    const pages = within(where.getByRole('list', { name: 'Pages to edit' })).getAllByRole('listitem');
    // The page with the most links to change comes first: it clears the most.
    const [first, second, third] = pages.map((page) => within(page));
    expect(first!.getByRole('link', { name: /Garden Furniture/ })).toHaveAttribute(
      'href',
      'https://www.gardenfurnituresets.co.uk/garden-furniture/#:~:text=Our%20range',
    );
    expect(first!.getByText('3 links to change')).toBeVisible();
    expect(first!.getByRole('button', { name: 'Open page: Garden Furniture' })).toBeVisible();
    // Each link: what it says, where on the page, where it leads and what's wrong there.
    expect(
      first!.getByRole('row', { name: /“Our range” footer .*\/monaco-corner-sofa\/ missing \(404 Not Found\)/ }),
    ).toBeVisible();
    expect(first!.getAllByRole('link', { name: '“Our range”' })).toHaveLength(3);
    expect(second!.getByRole('link', { name: /Sofas/ })).toBeVisible();
    expect(second!.getByText('2 links to change')).toBeVisible();
    // A page without a title is named by its address; a link without words says so.
    expect(third!.getByRole('link', { name: /\/blog\/$/ })).toBeVisible();
    expect(third!.getByText('A link with no words')).toBeVisible();
    // What no page links to isn't lost: it says where it came from, with the file to take it out of.
    const unlinked = within(where.getByRole('region', { name: 'Not linked from any page, and still in your sitemap' }));
    expect(unlinked.getByRole('link', { name: /\/gone-for-good\/$/ })).toBeVisible();
    expect(unlinked.getByText(/No page links to it\. It’s listed in your sitemap/)).toBeVisible();
    expect(unlinked.getByRole('link', { name: '/sitemap.xml' })).toHaveAttribute(
      'href',
      'https://www.gardenfurnituresets.co.uk/sitemap.xml',
    );
    // The addresses shown in as few reads as the moment allows: never one an address.
    const reads = sidecar.requests.filter((request) => request.path.endsWith('/urls/found-on'));
    expect(reads.length).toBeLessThanOrEqual(2);
    expect(sidecar.requests.filter((request) => request.path.endsWith('/links'))).toHaveLength(1);
  });

  it('opens the page at the link’s words from Open page and from the link, and the dead address from its own', async () => {
    const opened: string[] = [];
    vi.stubGlobal('open', (url: string) => (opened.push(url), null));
    await open(`${FIX}/internal-client-error`);
    const where = within(await main().findByRole('region', { name: 'Where to fix it' }));
    fireEvent.click(await where.findByRole('button', { name: 'Open page: Garden Furniture' }));
    fireEvent.click(where.getAllByRole('link', { name: '“Our range”' })[0]!);
    fireEvent.click(where.getAllByRole('link', { name: /\/monaco-corner-sofa\/$/ })[0]!);
    expect(opened).toEqual([
      'https://www.gardenfurnituresets.co.uk/garden-furniture/#:~:text=Our%20range',
      'https://www.gardenfurnituresets.co.uk/garden-furniture/#:~:text=Our%20range',
      'https://www.gardenfurnituresets.co.uk/monaco-corner-sofa/',
    ]);
  });

  // It pinned a callout, "One page links to 3 of these 4 addresses": the page that links to most of them now leads
  // the list, with its links under it, so the callout says nothing the list doesn't.
  it('has no callout for the one page linking to most of them: that page leads the list', async () => {
    await open(`${FIX}/internal-client-error`);
    await main().findByRole('list', { name: 'Pages to edit' });
    expect(main().queryByText(/One page links to/)).toBeNull();
  });

  // The fixit index audit's X2: it told Jitsunami's owner to remove the Checkout and Cart links for blocked-by-robots.
  it('tells no one to remove the links to addresses search engines are only told not to visit', async () => {
    await open(`${FIX}/blocked-by-robots`, sameAddresses('blocked-by-robots', null));
    const where = within(main().getByRole('region', { name: 'Where it is' }));
    expect((await where.findAllByText('/old-range/', { exact: false }))[0]).toBeVisible();
    // Where each is linked from is still said, row by row; only the callout's advice goes.
    expect((await where.findAllByText(/^Linked from /))[0]).toBeVisible();
    // The page that links to it is a link that opens it, at the link's words (S-02).
    expect((await where.findAllByRole('link', { name: /\/garden-furniture\/$/ }))[0]).toHaveAttribute(
      'href',
      'https://www.gardenfurnituresets.co.uk/garden-furniture/#:~:text=Our%20range',
    );
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
