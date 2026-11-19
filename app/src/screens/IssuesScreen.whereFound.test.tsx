// Where each of a check's URLs is found, in its groups on Advanced's Issues screen (docs/v2-ui-ia.md, "Where it's
// found"; the 2026-11-19 UX round, A-01 to A-06): a 404's row draws the page linking to it as a link, with the link's
// words and where on that page it is, and "+2 more" opens the rest under it; each opens in the inspector or goes on
// to Links; a page under a group of the URL it names says how it links to it; and a page of rows is one read, never
// one a row. The same in the check's list is IssuesScreen.list.test.tsx's.
import { createMemoryHistory } from '@tanstack/react-router';
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { setTransport } from '../api/transport';
import { App } from '../app/App';
import { createQueryClient } from '../app/queryClient';
import { createAppRouter } from '../app/router';
import { installKeyboard } from '../keyboard/registry';
import { TEST_CRAWL_ID, fakeSidecar } from '../test/fakeSidecar';
import { issuesRoutes } from '../test/issuesSidecar';
import { forgetCheckPlaces } from './issues/issuesPlace';
import { BROKEN_LINKS } from './links/presets';

const ISSUES = `/crawls/${TEST_CRAWL_ID}/issues`;
const BROKEN_GROUP = encodeURIComponent('{"detail":"404 Not Found","relatedUrlId":null}');
const HOME_GROUP = encodeURIComponent('{"detail":null,"relatedUrlId":1}');
const GARDEN = 'https://example.com/garden-furniture/';

function sidecar() {
  const transport = fakeSidecar(issuesRoutes());
  setTransport(transport);
  return transport.requests;
}

async function open(path: string) {
  const queryClient = createQueryClient();
  const router = createAppRouter(queryClient, createMemoryHistory({ initialEntries: [path] }));
  render(<App router={router} queryClient={queryClient} />);
  await act(() => router.load());
  return router;
}

/** A row's first cell, where a press or a right-click lands. */
const cellOf = (row: HTMLElement) => row.querySelector<HTMLElement>('[role="gridcell"]') ?? row;
/** The rows under an expanded URL: the tree's third level. */
const level3 = (grid: HTMLElement) => [...grid.querySelectorAll<HTMLElement>('[role="row"][aria-level="3"]')];
async function findLevel3(grid: HTMLElement) {
  await waitFor(() => expect(level3(grid).length).toBeGreaterThan(0));
  return level3(grid);
}
/** A URL in a row: a link, named by the whole URL, that inspects it. */
const urlLink = (row: HTMLElement, url: string) => within(row).findByRole('button', { name: url });

let uninstall: () => void = () => undefined;
beforeEach(() => {
  uninstall = installKeyboard();
});
afterEach(() => {
  cleanup();
  uninstall();
  forgetCheckPlaces();
  setTransport(fakeSidecar([]));
});

describe('where an issue’s URL is found, in its group', () => {
  it('draws on each broken URL’s row the page linking to it, as a link, with the link’s words and its place, in one read for the rows', async () => {
    const requests = sidecar();
    await open(`${ISSUES}/internal-client-error?group=${BROKEN_GROUP}`);
    const grid = await screen.findByRole('treegrid', { name: /grouped by cause/ });
    const old = await within(grid).findByRole('row', { name: /\/old\// });
    // The page that holds the link is a link itself, led by how it stands to the URL, then its words and its place.
    expect(await urlLink(old, GARDEN)).toHaveClass('si-url__go');
    expect(within(old).getByText('Linked from')).toBeVisible();
    expect(within(old).getByText('“Old range”')).toBeVisible();
    expect(within(old).getByText('Footer')).toBeVisible();
    // Three links to it: the two the row doesn't show are a press away.
    expect(within(old).getByText('+2 more')).toHaveAttribute('data-tree-toggle');
    const gone = within(grid).getByRole('row', { name: /\/gone\// });
    expect(await urlLink(gone, GARDEN)).toBeVisible();
    expect(within(gone).getByText('“Gone for good”')).toBeVisible();
    expect(within(gone).getByText('Content')).toBeVisible();
    // One link and the sitemap that lists it.
    expect(within(gone).getByText('+1 more')).toBeVisible();
    // No one-line "Found on 3 pages · …" stands in for the parts, and a followed link doesn't say so.
    expect(within(grid).queryByText(/Found on/)).toBeNull();
    expect(within(grid).queryByText(/Followed/)).toBeNull();
    // Both rows' answers came in one request, of both their ids.
    const reads = requests.filter((request) => request.path.endsWith('/urls/found-on'));
    expect(reads).toHaveLength(1);
    expect(reads[0]?.body).toEqual({ urlIds: [10, 11], limit: 5 });
  });

  it('opens the page linking to a URL at the link’s words from its open button, and inspects it from its address', async () => {
    sidecar();
    const router = await open(`${ISSUES}/internal-client-error?group=${BROKEN_GROUP}`);
    const grid = await screen.findByRole('treegrid', { name: /grouped by cause/ });
    const old = await within(grid).findByRole('row', { name: /\/old\// });
    const link = await urlLink(old, GARDEN);
    const openButton = within(old).getByRole('button', { name: `Open ${GARDEN} in your browser` });
    expect(openButton).toHaveAttribute('data-open-url', `${GARDEN}#:~:text=Old%20range`);
    // The broken URL's own button opens it as it is.
    expect(within(old).getByRole('button', { name: 'Open https://example.com/old/ in your browser' })).toHaveAttribute(
      'data-open-url',
      'https://example.com/old/',
    );
    // No button at the row's end inspects it: its address does, and so does the address of the page linking to it.
    expect(within(grid).queryByRole('button', { name: /^Inspect / })).toBeNull();
    fireEvent.click(link);
    await waitFor(() => expect(router.state.location.search).toMatchObject({ inspect: 20 }));
    fireEvent.click(await urlLink(old, 'https://example.com/old/'));
    await waitFor(() => expect(router.state.location.search).toMatchObject({ inspect: 10 }));
  });

  it('opens a row with Right to list the pages linking to it, where on them and what each says, and each opens in the inspector', async () => {
    sidecar();
    const router = await open(`${ISSUES}/internal-client-error?group=${BROKEN_GROUP}`);
    const grid = await screen.findByRole('treegrid');
    const page = await within(grid).findByRole('row', { name: /\/old\// });
    await urlLink(page, GARDEN);
    expect(page).toHaveAttribute('aria-expanded', 'false');
    expect(page).toHaveAttribute('aria-level', '2');

    fireEvent.pointerDown(cellOf(page), { button: 0 });
    act(() => grid.focus());
    fireEvent.keyDown(grid, { key: 'ArrowRight' });
    await waitFor(() => expect(page).toHaveAttribute('aria-expanded', 'true'));
    // Open, its toggle says what a press hides.
    expect(within(page).getByText('Hide 2')).toBeVisible();
    const sources = await findLevel3(grid);
    // Each URL is the Url component's: the full URL for a screen reader, then the path shown. Then what the link says
    // and where on that page it is; a link that's followed doesn't say so.
    expect(sources.map((row) => row.textContent)).toEqual([
      'Linked fromhttps://example.com/garden-furniture//garden-furniture/“Old range”Footer',
      'Linked fromhttps://example.com/sofas//sofas/“See the old range”Content',
      'Linked fromhttps://example.com/blog//blog/No textContent',
      'Show all 3 links in Links',
    ]);
    // Each page is a link, and its open button opens it at the link's words; a link without words opens the page.
    expect(within(sources[1]!).getByRole('button', { name: 'https://example.com/sofas/' })).toHaveClass('si-url__go');
    expect(
      within(sources[1]!).getByRole('button', { name: 'Open https://example.com/sofas/ in your browser' }),
    ).toHaveAttribute('data-open-url', 'https://example.com/sofas/#:~:text=See%20the%20old%20range');
    expect(
      within(sources[2]!).getByRole('button', { name: 'Open https://example.com/blog/ in your browser' }),
    ).toHaveAttribute('data-open-url', 'https://example.com/blog/');

    // Down onto the first, Enter: the page linking to it, in the inspector.
    fireEvent.keyDown(grid, { key: 'ArrowDown' });
    fireEvent.keyDown(grid, { key: 'Enter' });
    await waitFor(() => expect(router.state.location.search).toMatchObject({ inspect: 20 }));

    // Left goes back to the URL's row, and Left again closes it.
    fireEvent.keyDown(grid, { key: 'ArrowLeft' });
    fireEvent.keyDown(grid, { key: 'ArrowLeft' });
    await waitFor(() => expect(page).toHaveAttribute('aria-expanded', 'false'));
    expect(level3(grid)).toEqual([]);
    expect(within(page).getByText('+2 more')).toBeVisible();
  });

  it('opens a row from its “+1 more” to list the sitemap that lists the URL, and goes on to every link in Links', async () => {
    sidecar();
    const router = await open(`${ISSUES}/internal-client-error?group=${BROKEN_GROUP}`);
    const grid = await screen.findByRole('treegrid');
    const gone = await within(grid).findByRole('row', { name: /\/gone\// });
    fireEvent.pointerDown(await within(gone).findByText('+1 more'), { button: 0 });
    await waitFor(() => expect(gone).toHaveAttribute('aria-expanded', 'true'));
    expect(within(gone).getByText('Hide 1')).toBeVisible();
    const rows = await findLevel3(grid);
    expect(rows.map((row) => row.textContent)).toEqual([
      'Linked fromhttps://example.com/garden-furniture//garden-furniture/“Gone for good”Content',
      'Listed in the sitemaphttps://example.com/sitemap.xml/sitemap.xml',
      'Show the link in Links',
    ]);
    fireEvent.click(within(grid).getByRole('link', { name: 'Show the link in Links' }));
    await waitFor(() => expect(router.state.location.pathname).toBe(`/crawls/${TEST_CRAWL_ID}/links`));
    expect(router.state.location.search).toMatchObject({
      filter: [
        { column: 'to.id', op: 'eq', value: 11 },
        { column: 'selfLink', op: 'is', value: false },
      ],
    });
  });

  it('says how each page under a group of the URL it names links to it, and where the template’s link is', async () => {
    sidecar();
    await open(`${ISSUES}/empty-anchor-text?group=${HOME_GROUP}`);
    const grid = await screen.findByRole('treegrid');
    // The group's row names the URL, a link like any other, under a header that says what it is.
    expect(within(grid).getByRole('columnheader', { name: 'Linked URL' })).toBeVisible();
    const group = await within(grid).findByRole('row', { expanded: true });
    expect(await urlLink(group, 'https://example.com/')).toBeVisible();
    // Each of its five pages says its own link to it: no words, in the header. Not the URL again.
    await waitFor(() => expect(within(grid).getAllByText('No text')).toHaveLength(5));
    const pages = [...grid.querySelectorAll<HTMLElement>('[role="row"][aria-level="2"]')].slice(0, 5);
    for (const page of pages) {
      expect(within(page).getByText('Header')).toBeVisible();
      expect(within(page).queryByRole('button', { name: 'https://example.com/' })).toBeNull();
    }
    // The group every page shares: the callout says where on the pages its link is.
    expect(await screen.findByText(/It’s a link in the header\./)).toBeVisible();
  });

  it('has a menu item for a URL’s links in Links', async () => {
    sidecar();
    const router = await open(`${ISSUES}/internal-client-error?group=${BROKEN_GROUP}`);
    const grid = await screen.findByRole('treegrid');
    const old = await within(grid).findByRole('row', { name: /\/old\// });
    await urlLink(old, GARDEN);
    fireEvent.contextMenu(cellOf(old));
    const links = await screen.findByRole('menuitem', { name: 'Show where it’s found in Links' });
    // A broken URL may be no page: the menu's way to it in URLs doesn't call it one.
    expect(screen.getByRole('menuitem', { name: 'Show it in URLs' })).toBeVisible();
    expect(screen.queryByRole('menuitem', { name: 'Show the page in URLs' })).toBeNull();
    fireEvent.click(links);
    await waitFor(() => expect(router.state.location.pathname).toBe(`/crawls/${TEST_CRAWL_ID}/links`));
    expect(router.state.location.search).toMatchObject({ filter: [{ column: 'to.id', value: 10 }, {}] });
  });
});

describe('the one page most of a check’s URLs are linked from', () => {
  it('says so above the check’s URLs, and to fix the links there for a broken URL', async () => {
    sidecar();
    await open(`${ISSUES}/internal-client-error`);
    const callout = await waitFor(() => {
      const found = document.querySelector<HTMLElement>('[data-shared-source]');
      if (!found) throw new Error('No shared source yet');
      return found;
    });
    expect(within(callout).getByText('links to 2 of these 2 URLs')).toBeVisible();
    expect(
      within(callout).getByText('Most of them are linked from there: fix or remove the links there first.'),
    ).toBeInTheDocument();
  });

  it('names the page as a link that inspects it, and goes on to its broken links in Links', async () => {
    sidecar();
    const router = await open(`${ISSUES}/internal-client-error`);
    const callout = await waitFor(() => {
      const found = document.querySelector<HTMLElement>('[data-shared-source]');
      if (!found) throw new Error('No shared source yet');
      return found;
    });
    fireEvent.click(within(callout).getByRole('button', { name: GARDEN }));
    await waitFor(() => expect(router.state.location.search).toMatchObject({ inspect: 20 }));
    fireEvent.click(within(callout).getByRole('link', { name: 'Show its 2 links in Links' }));
    await waitFor(() => expect(router.state.location.pathname).toBe(`/crawls/${TEST_CRAWL_ID}/links`));
    // That page's links, narrowed to the ones the broken link checks count.
    expect(router.state.location.search).toMatchObject({
      filter: [
        { column: 'from.id', op: 'eq', value: 20 },
        { column: 'selfLink', op: 'is', value: false },
        ...BROKEN_LINKS,
      ],
    });
  });
});
