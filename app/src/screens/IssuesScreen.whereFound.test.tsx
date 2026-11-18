// Where each of a check's URLs is found, on Advanced's Issues screen (docs/v2-ui-ia.md, "Where it's found"): a 404's
// row says how many pages link to it and the first, opens to list them, and each opens in the inspector or goes on to
// Links; a page holding the bad links says what it links to; and a page of rows is one read, never one a row.
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

const ISSUES = `/crawls/${TEST_CRAWL_ID}/issues`;
const BROKEN_GROUP = encodeURIComponent('{"detail":"404 Not Found","relatedUrlId":null}');
const HOME_GROUP = encodeURIComponent('{"detail":null,"relatedUrlId":1}');

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

let uninstall: () => void = () => undefined;
beforeEach(() => {
  uninstall = installKeyboard();
});
afterEach(() => {
  cleanup();
  uninstall();
  setTransport(fakeSidecar([]));
});

describe('where an issue’s URL is found', () => {
  it('says on each broken URL’s row how many pages link to it and the first, in one read for the rows', async () => {
    const requests = sidecar();
    await open(`${ISSUES}/internal-client-error?group=${BROKEN_GROUP}`);
    const grid = await screen.findByRole('treegrid');
    expect(
      await within(grid).findByText('Found on 3 pages · /garden-furniture/ (Footer · “Old range”) +2'),
    ).toBeVisible();
    expect(within(grid).getByText('Found on 1 page · /garden-furniture/ (Content · “Gone for good”)')).toBeVisible();
    // Both rows' answers came in one request, of both their ids.
    const reads = requests.filter((request) => request.path.endsWith('/urls/found-on'));
    expect(reads).toHaveLength(1);
    expect(reads[0]?.body).toEqual({ urlIds: [10, 11], limit: 5 });
  });

  it('opens a row with Right to list the pages linking to it, where on them and how, and each opens in the inspector', async () => {
    sidecar();
    const router = await open(`${ISSUES}/internal-client-error?group=${BROKEN_GROUP}`);
    const grid = await screen.findByRole('treegrid');
    await within(grid).findByText('Found on 3 pages · /garden-furniture/ (Footer · “Old range”) +2');
    const page = within(grid).getByRole('row', { name: /\/old\// });
    expect(page).toHaveAttribute('aria-expanded', 'false');
    expect(page).toHaveAttribute('aria-level', '2');

    fireEvent.pointerDown(cellOf(page), { button: 0 });
    act(() => grid.focus());
    fireEvent.keyDown(grid, { key: 'ArrowRight' });
    await waitFor(() => expect(page).toHaveAttribute('aria-expanded', 'true'));
    const sources = await findLevel3(grid);
    // Each URL is the Url component's: the full URL for a screen reader, then the path shown.
    expect(sources.map((row) => row.textContent)).toEqual([
      'Linked fromhttps://example.com/garden-furniture//garden-furniture/Footer · “Old range”Followed',
      'Linked fromhttps://example.com/sofas//sofas/Content · “See the old range”Followed',
      'Linked fromhttps://example.com/blog//blog/ContentFollowed',
      'Show all 3 links in Links',
    ]);

    // Down onto the first, Enter: the page linking to it, in the inspector.
    fireEvent.keyDown(grid, { key: 'ArrowDown' });
    fireEvent.keyDown(grid, { key: 'Enter' });
    await waitFor(() => expect(router.state.location.search).toMatchObject({ inspect: 20 }));

    // Left goes back to the URL's row, and Left again closes it.
    fireEvent.keyDown(grid, { key: 'ArrowLeft' });
    fireEvent.keyDown(grid, { key: 'ArrowLeft' });
    await waitFor(() => expect(page).toHaveAttribute('aria-expanded', 'false'));
    expect(level3(grid)).toEqual([]);
  });

  it('lists the sitemap that lists a URL, and goes on to every link in Links', async () => {
    sidecar();
    const router = await open(`${ISSUES}/internal-client-error?group=${BROKEN_GROUP}`);
    const grid = await screen.findByRole('treegrid');
    await within(grid).findByText('Found on 1 page · /garden-furniture/ (Content · “Gone for good”)');
    const gone = within(grid).getByRole('row', { name: /\/gone\// });
    fireEvent.pointerDown(within(gone).getByText('', { selector: '[data-tree-toggle]' }), { button: 0 });
    await waitFor(() => expect(gone).toHaveAttribute('aria-expanded', 'true'));
    const rows = await findLevel3(grid);
    expect(rows.map((row) => row.textContent)).toEqual([
      'Linked fromhttps://example.com/garden-furniture//garden-furniture/Content · “Gone for good”Followed',
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

  it('says what a page holding the bad links links to, and where the template’s link is', async () => {
    sidecar();
    await open(`${ISSUES}/empty-anchor-text?group=${HOME_GROUP}`);
    const grid = await screen.findByRole('treegrid');
    await waitFor(() => expect(within(grid).getAllByText('Links to / (200)')).toHaveLength(5));
    // The group every page shares: the callout says where on the pages its link is.
    expect(await screen.findByText(/It’s a link in the header\./)).toBeVisible();
  });

  it('has a menu item for a URL’s links in Links', async () => {
    sidecar();
    const router = await open(`${ISSUES}/internal-client-error?group=${BROKEN_GROUP}`);
    const grid = await screen.findByRole('treegrid');
    await within(grid).findByText('Found on 3 pages · /garden-furniture/ (Footer · “Old range”) +2');
    fireEvent.contextMenu(cellOf(within(grid).getByRole('row', { name: /\/old\// })));
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Show where it’s found in Links' }));
    await waitFor(() => expect(router.state.location.pathname).toBe(`/crawls/${TEST_CRAWL_ID}/links`));
    expect(router.state.location.search).toMatchObject({ filter: [{ column: 'to.id', value: 10 }, {}] });
  });
});

describe('the one page most of a check’s URLs are linked from', () => {
  it('says so above the check’s URLs, and to fix the links there for a broken URL', async () => {
    sidecar();
    await open(`${ISSUES}/internal-client-error`);
    expect(await screen.findByText('/garden-furniture/ links to 2 of these 2 URLs')).toBeVisible();
    expect(screen.getByText('Most of them are linked from there: fix or remove the links there first.')).toBeVisible();
  });
});
