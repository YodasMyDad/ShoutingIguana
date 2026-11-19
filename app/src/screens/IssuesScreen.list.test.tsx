// A check's list on Advanced's Issues screen (the 2026-11-19 UX round, A-01 to A-07): open "Internal URL returns 4xx"
// and, with no expanding, each broken URL's row shows the page that holds the link to it, as a link, with the link's
// words and where on the page it is; that page's open button opens it at the link; "+2 more" opens a URL's other links
// under it in place; a page with several broken links is a line a link; and the List or Grouped view chosen for a
// check, with the rows opened and closed in it, is how the check is found again.
import { createMemoryHistory } from '@tanstack/react-router';
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import type { IssueRow } from '../api/grids';
import type { IssueTarget, LinkRow, UrlFoundOn } from '../api/inspector';
import type { Check } from '../api/types';
import { setTransport } from '../api/transport';
import { App } from '../app/App';
import { createQueryClient } from '../app/queryClient';
import { createAppRouter } from '../app/router';
import { installKeyboard } from '../keyboard/registry';
import { TEST_CRAWL_ID, fakeSidecar } from '../test/fakeSidecar';
import {
  FOUND_ON,
  ISSUES_ANALYSIS,
  ISSUES_CHECKS,
  ISSUE_ROWS,
  issuesRoutes,
  type IssuesSidecarOptions,
} from '../test/issuesSidecar';
import { forgetCheckPlaces } from './issues/issuesPlace';

const ISSUES = `/crawls/${TEST_CRAWL_ID}/issues`;
const SITE = 'https://example.com';
const GARDEN = `${SITE}/garden-furniture/`;

function sidecar(options: IssuesSidecarOptions = {}) {
  const transport = fakeSidecar(issuesRoutes(options));
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

const headers = (grid: HTMLElement) =>
  within(grid)
    .getAllByRole('columnheader')
    .map((header) => header.textContent);
/** The list's rows, each with its level: a URL's own row is 1, a row under an open one 2. */
const levels = (grid: HTMLElement) =>
  [...grid.querySelectorAll<HTMLElement>('[role="row"][aria-level]')].map((row) => row.getAttribute('aria-level'));
const level2 = (grid: HTMLElement) => [...grid.querySelectorAll<HTMLElement>('[role="row"][aria-level="2"]')];
const cells = (row: HTMLElement) =>
  within(row)
    .getAllByRole('gridcell')
    .map((cell) => cell.textContent);
const cellOf = (row: HTMLElement) => row.querySelector<HTMLElement>('[role="gridcell"]') ?? row;
/** A URL's open-in-browser button in a row, by the URL it's of. */
const openButton = (row: HTMLElement, url: string) =>
  within(row).getByRole('button', { name: `Open ${url} in your browser` });
/** A press on a row's "+2 more" or "Hide 2". */
const press = (toggle: HTMLElement) => fireEvent.pointerDown(toggle, { button: 0 });

/** The list of the 4xx check, once where its URLs are found has come. */
async function brokenList() {
  const grid = await screen.findByRole('treegrid', { name: 'Internal URL returns 4xx: affected URLs' });
  const old = await within(grid).findByRole('row', { name: /example\.com\/old\// });
  await within(old).findByRole('button', { name: GARDEN });
  return { grid, old, gone: within(grid).getByRole('row', { name: /example\.com\/gone\// }) };
}

const sourceLink = (id: number, path: string, position: LinkRow['position'], text: string | null): LinkRow => ({
  id,
  url: `${SITE}${path}`,
  kind: 'hyperlink',
  text,
  position,
  flags: [],
  followed: true,
  fragment: null,
  statusCode: 200,
  indexable: true,
  fetched: true,
  outcome: 'success',
});

const foundOn = (urlId: number, links: number, sources: LinkRow[]): UrlFoundOn => ({
  urlId,
  links,
  pages: links,
  pagesExact: true,
  sources,
  sitemaps: { total: 0, sitemaps: [] },
  start: false,
});

let uninstall: () => void = () => undefined;
beforeEach(() => {
  uninstall = installKeyboard();
});
afterEach(() => {
  cleanup();
  uninstall();
  // Where a check was left is kept for the session: each test starts with every check as if never opened.
  forgetCheckPlaces();
  setTransport(fakeSidecar([]));
});

describe('a check’s list: where each URL is found, in its row', () => {
  it('shows for each broken URL the page linking to it, as a link, with the link’s words and its place, with no expanding', async () => {
    const requests = sidecar();
    const router = await open(`${ISSUES}/internal-client-error`);
    const { grid, old, gone } = await brokenList();
    // The list, not the groups: no group is named in the address, and List is the view chosen.
    expect(router.state.location.search).not.toHaveProperty('group');
    expect(screen.getByRole('radio', { name: 'List' })).toHaveAttribute('aria-checked', 'true');
    expect(headers(grid)).toEqual(['URL', 'Status', 'Linked from', 'Link text', 'Where on the page']);
    // Each URL's own row: the URL, its status, the page that links to it, what the link says, where on that page.
    expect(cells(old)).toEqual([
      `${SITE}/old//old/`,
      '404 Not Found',
      `${GARDEN}/garden-furniture/Hide 2`,
      '“Old range”',
      'Footer',
    ]);
    expect(cells(gone)).toEqual([
      `${SITE}/gone//gone/`,
      '404 Not Found',
      `${GARDEN}/garden-furniture/+1 more`,
      '“Gone for good”',
      'Content',
    ]);
    // The linking page is a link like the URL itself, not words to read.
    expect(within(old).getByRole('button', { name: GARDEN })).toHaveClass('si-url__go');
    expect(within(gone).getByRole('button', { name: GARDEN })).toHaveClass('si-url__go');
    expect(within(grid).queryByText(/Found on/)).toBeNull();
    // Where both are found came in one request, of both their ids.
    const reads = requests.filter((request) => request.path.endsWith('/urls/found-on'));
    expect(reads).toHaveLength(1);
    expect(reads[0]?.body).toEqual({ urlIds: [10, 11], limit: 5 });
  });

  it('inspects the linking page from its address, and the broken URL from its own', async () => {
    sidecar();
    const router = await open(`${ISSUES}/internal-client-error`);
    const { old } = await brokenList();
    fireEvent.click(within(old).getByRole('button', { name: GARDEN }));
    await waitFor(() => expect(router.state.location.search).toMatchObject({ inspect: 20 }));
    fireEvent.click(within(old).getByRole('button', { name: `${SITE}/old/` }));
    await waitFor(() => expect(router.state.location.search).toMatchObject({ inspect: 10 }));
  });

  it('opens the linking page at the link’s words from its open button', async () => {
    sidecar();
    await open(`${ISSUES}/internal-client-error`);
    const { grid, old, gone } = await brokenList();
    expect(openButton(old, GARDEN)).toHaveAttribute('data-open-url', `${GARDEN}#:~:text=Old%20range`);
    expect(openButton(gone, GARDEN)).toHaveAttribute('data-open-url', `${GARDEN}#:~:text=Gone%20for%20good`);
    expect(openButton(old, GARDEN).getAttribute('data-open-url')).toMatch(/#:~:text=Old%20range$/);
    // The broken URL itself opens as it is.
    expect(openButton(old, `${SITE}/old/`)).toHaveAttribute('data-open-url', `${SITE}/old/`);
    // Under it, each other page at its own link; one whose link says nothing opens at its top.
    const [sofas, blog] = level2(grid);
    expect(openButton(sofas!, `${SITE}/sofas/`)).toHaveAttribute(
      'data-open-url',
      `${SITE}/sofas/#:~:text=See%20the%20old%20range`,
    );
    expect(openButton(blog!, `${SITE}/blog/`)).toHaveAttribute('data-open-url', `${SITE}/blog/`);
  });

  it('opens every row with more links by itself in a list of up to 20 rows, the other links under the row', async () => {
    sidecar();
    await open(`${ISSUES}/internal-client-error`);
    const { grid, old, gone } = await brokenList();
    // /old/ is linked from three pages: its row shows the first, and the other two follow it, a level down.
    expect(old).toHaveAttribute('aria-expanded', 'true');
    expect(levels(grid)).toEqual(['1', '2', '2', '1']);
    // The URL is named once: under it, the pages, their links' words and their places, in the same columns.
    expect(level2(grid).map(cells)).toEqual([
      ['', '', `${SITE}/sofas//sofas/`, '“See the old range”', 'Content'],
      ['', '', `${SITE}/blog//blog/`, 'No text', 'Content'],
    ]);
    // All three links are in view, so there's no "Show all 3 links in Links" to go on to.
    expect(within(grid).queryByRole('link', { name: /in Links$/ })).toBeNull();
    // /gone/ has one link, and a sitemap lists it: a sitemap alone doesn't open the row.
    expect(gone).toHaveAttribute('aria-expanded', 'false');
  });

  it('closes a row’s other links from “Hide 2” and opens them again from “+2 more”', async () => {
    sidecar();
    await open(`${ISSUES}/internal-client-error`);
    const { grid, old } = await brokenList();
    const hide = within(old).getByText('Hide 2');
    expect(hide).toHaveAttribute('data-tree-toggle');
    expect(hide).toHaveClass('si-more-toggle');
    press(hide);
    await waitFor(() => expect(old).toHaveAttribute('aria-expanded', 'false'));
    expect(levels(grid)).toEqual(['1', '1']);
    expect(within(grid).queryByRole('button', { name: `${SITE}/sofas/` })).toBeNull();

    press(within(old).getByText('+2 more'));
    await waitFor(() => expect(old).toHaveAttribute('aria-expanded', 'true'));
    expect(within(old).getByText('Hide 2')).toBeVisible();
    expect(levels(grid)).toEqual(['1', '2', '2', '1']);
    expect(within(grid).getByRole('button', { name: `${SITE}/sofas/` })).toBeVisible();
  });

  it('opens the sitemap that lists a URL from its “+1 more”, without a way on to the one link its row shows', async () => {
    sidecar();
    await open(`${ISSUES}/internal-client-error`);
    const { grid, gone } = await brokenList();
    press(within(gone).getByText('+1 more'));
    await waitFor(() => expect(gone).toHaveAttribute('aria-expanded', 'true'));
    expect(within(gone).getByText('Hide 1')).toBeVisible();
    const under = level2(grid).at(-1)!;
    expect(under).toHaveTextContent(`Listed in the sitemap${SITE}/sitemap.xml/sitemap.xml`);
    // "Show the link in Links" is for links there's no room for here: its one link is in its row.
    expect(within(grid).queryByRole('link', { name: /in Links$/ })).toBeNull();
  });

  it('opens and closes a row with Right and Left, and Enter on a row under it inspects the page that row names', async () => {
    sidecar();
    const router = await open(`${ISSUES}/internal-client-error`);
    const { grid, old } = await brokenList();
    fireEvent.pointerDown(cellOf(old), { button: 0 });
    act(() => grid.focus());
    fireEvent.keyDown(grid, { key: 'ArrowLeft' });
    await waitFor(() => expect(old).toHaveAttribute('aria-expanded', 'false'));
    fireEvent.keyDown(grid, { key: 'ArrowRight' });
    await waitFor(() => expect(old).toHaveAttribute('aria-expanded', 'true'));
    // Enter on the URL's own row inspects the URL; on the row under it, the page linking to it.
    fireEvent.keyDown(grid, { key: 'Enter' });
    await waitFor(() => expect(router.state.location.search).toMatchObject({ inspect: 10 }));
    fireEvent.keyDown(grid, { key: 'ArrowDown' });
    fireEvent.keyDown(grid, { key: 'Enter' });
    await waitFor(() => expect(router.state.location.search).toMatchObject({ inspect: 21 }));
  });

  it('says its keys under its rows', async () => {
    sidecar();
    await open(`${ISSUES}/internal-client-error`);
    const { grid } = await brokenList();
    const frame = grid.closest<HTMLElement>('section') ?? document.body;
    for (const words of ['Move', 'Inspect', 'Open in the browser', 'Show more', 'Copy for Excel']) {
      expect(within(frame).getAllByText(words).length).toBeGreaterThan(0);
    }
  });
});

describe('a list too long to open whole', () => {
  /** 21 broken URLs, the first linked from three pages. */
  const rows: IssueRow[] = Array.from({ length: 21 }, (_, index) => ({
    id: 100 + index,
    url: `${SITE}/broken-${index}/`,
    detail: '404 Not Found',
  }));
  const found: Record<number, UrlFoundOn> = Object.fromEntries(
    rows.map((row, index): [number, UrlFoundOn] => [
      row.id,
      index === 0
        ? foundOn(row.id, 3, [
            sourceLink(20, '/garden-furniture/', 'footer', 'Old range'),
            sourceLink(21, '/sofas/', 'content', 'See the old range'),
            sourceLink(22, '/blog/', 'sidebar', null),
          ])
        : foundOn(row.id, 1, [sourceLink(20, '/garden-furniture/', 'footer', `Range ${index}`)]),
    ]),
  );
  const options: IssuesSidecarOptions = {
    analysis: {
      ...ISSUES_ANALYSIS,
      counts: ISSUES_ANALYSIS.counts.map((count) =>
        count.checkId === 'internal-client-error' ? { ...count, count: 21 } : count,
      ),
    },
    rows: { ...ISSUE_ROWS, 'internal-client-error': rows },
    foundOn: found,
  };

  it('leaves every row closed past 20 rows, each still showing its first link, and “+2 more” opens the others under it', async () => {
    sidecar(options);
    await open(`${ISSUES}/internal-client-error`);
    const grid = await screen.findByRole('treegrid', { name: 'Internal URL returns 4xx: affected URLs' });
    const first = await within(grid).findByRole('row', { name: /\/broken-0\// });
    const more = await within(first).findByText('+2 more');
    expect(first).toHaveAttribute('aria-expanded', 'false');
    expect(level2(grid)).toEqual([]);
    // With no expanding: the first page linking to it, its words and its place.
    expect(cells(first).slice(2)).toEqual([`${GARDEN}/garden-furniture/+2 more`, '“Old range”', 'Footer']);

    press(more);
    await waitFor(() => expect(first).toHaveAttribute('aria-expanded', 'true'));
    expect(within(first).getByText('Hide 2')).toBeVisible();
    expect(level2(grid).map(cells)).toEqual([
      ['', '', `${SITE}/sofas//sofas/`, '“See the old range”', 'Content'],
      ['', '', `${SITE}/blog//blog/`, 'No text', 'Sidebar'],
    ]);
    // The next URL's row follows them, as it was.
    const next = within(grid).getByRole('row', { name: /\/broken-1\// });
    expect(next).toHaveAttribute('aria-level', '1');
    expect(within(next).queryByText(/more$/)).toBeNull();

    press(within(first).getByText('Hide 2'));
    await waitFor(() => expect(first).toHaveAttribute('aria-expanded', 'false'));
    expect(level2(grid)).toEqual([]);
    expect(within(first).getByText('+2 more')).toBeVisible();
  });
});

describe('a URL with more links than its rows show', () => {
  it('goes on to all of them in Links from the last row under it', async () => {
    const seven = foundOn(
      10,
      7,
      [1, 2, 3, 4, 5, 6].map((at) => sourceLink(30 + at, `/page-${at}/`, 'content', `Link ${at}`)),
    );
    sidecar({ foundOn: { ...FOUND_ON, 10: seven } });
    const router = await open(`${ISSUES}/internal-client-error`);
    const grid = await screen.findByRole('treegrid', { name: 'Internal URL returns 4xx: affected URLs' });
    const old = await within(grid).findByRole('row', { name: /example\.com\/old\// });
    // Seven links: its row shows one, and says the six more.
    expect(await within(old).findByText('Hide 6')).toBeVisible();
    // The first five are read: one in its row, four under it, then the way to all seven.
    await waitFor(() => expect(level2(grid)).toHaveLength(5));
    const link = within(level2(grid).at(-1)!).getByRole('link', { name: 'Show all 7 links in Links' });
    fireEvent.click(link);
    await waitFor(() => expect(router.state.location.pathname).toBe(`/crawls/${TEST_CRAWL_ID}/links`));
    expect(router.state.location.search).toMatchObject({
      filter: [
        { column: 'to.id', op: 'eq', value: 10 },
        { column: 'selfLink', op: 'is', value: false },
      ],
    });
  });
});

describe('a URL’s row menu in the list', () => {
  it('offers the one page linking to a URL', async () => {
    const one = foundOn(11, 1, [sourceLink(20, '/garden-furniture/', 'content', 'Gone for good')]);
    sidecar({ foundOn: { ...FOUND_ON, 11: one } });
    const router = await open(`${ISSUES}/internal-client-error`);
    const { gone, old } = await brokenList();
    expect(within(gone).queryByText(/more$/)).toBeNull();
    fireEvent.contextMenu(cellOf(gone));
    const menu = await screen.findByRole('menu', { name: 'Menu for /gone/' });
    expect(within(menu).getByRole('menuitem', { name: 'Open the linking page at the link' })).toBeVisible();
    fireEvent.click(within(menu).getByRole('menuitem', { name: 'Inspect the linking page' }));
    await waitFor(() => expect(router.state.location.search).toMatchObject({ inspect: 20 }));

    // A URL linked from several pages has them under its row instead.
    fireEvent.contextMenu(cellOf(old));
    const other = await screen.findByRole('menu', { name: 'Menu for /old/' });
    expect(within(other).queryByRole('menuitem', { name: 'Inspect the linking page' })).toBeNull();
    expect(within(other).getByRole('menuitem', { name: 'Show where it’s found in Links' })).toBeVisible();
  });

  // Fails at 5a52c0ee, and is left failing on purpose (see the round's report): the list's menu says "Show the page
  // in URLs" of a URL of a check that can flag any URL, where the same row's menu in the check's groups says "Show it
  // in URLs" (IssuesScreen.rows.test.tsx). IssueRowsGrid doesn't tell IssuePageMenu what the check flags (`isPage`).
  it('calls a URL “it”, not “the page”, in its row’s menu, as the same row’s menu in the groups does', async () => {
    sidecar();
    await open(`${ISSUES}/internal-client-error`);
    const { old } = await brokenList();
    fireEvent.contextMenu(cellOf(old));
    const menu = await screen.findByRole('menu', { name: 'Menu for /old/' });
    expect(within(menu).getByRole('menuitem', { name: 'Show it in URLs' })).toBeVisible();
    expect(within(menu).queryByRole('menuitem', { name: 'Show the page in URLs' })).toBeNull();
  });
});

describe('a page that holds several broken links', () => {
  const BROKEN_LINKS_CHECK: Check = {
    ...ISSUES_CHECKS.checks[2]!,
    id: 'broken-internal-links',
    severity: 'error',
    title: 'Links to broken internal URLs',
    grouping: 'none',
    references: 'targets',
    targetFacts: ['statusCode', 'text', 'position'],
  };
  const POST = `${SITE}/blog/post/`;
  const ABOUT = `${SITE}/about/`;
  const broken = (
    id: number,
    path: string,
    statusCode: number,
    text: string | null,
    position: IssueTarget['position'],
  ): IssueTarget => ({
    id,
    url: `${SITE}${path}`,
    statusCode,
    fetched: true,
    outcome: 'clientError',
    kind: 'hyperlink',
    text,
    position,
  });
  const options: IssuesSidecarOptions = {
    checks: { ...ISSUES_CHECKS, checks: [...ISSUES_CHECKS.checks, BROKEN_LINKS_CHECK] },
    analysis: {
      ...ISSUES_ANALYSIS,
      counts: [...ISSUES_ANALYSIS.counts, { checkId: 'broken-internal-links', count: 2 }],
    },
    rows: {
      ...ISSUE_ROWS,
      'broken-internal-links': [
        { id: 40, url: POST, detail: '3 broken URLs' },
        { id: 41, url: ABOUT, detail: '1 broken URL' },
      ],
    },
    targets: {
      'broken-internal-links': {
        40: {
          urlId: 40,
          total: 3,
          targets: [
            broken(10, '/old/', 404, 'Old range', 'footer'),
            broken(11, '/gone/', 404, 'Gone for good', 'content'),
            broken(12, '/lost/', 410, null, 'sidebar'),
          ],
        },
        41: { urlId: 41, total: 1, targets: [broken(10, '/old/', 404, 'Our old range', 'navigation')] },
      },
    },
  };

  it('is listed with one line a link: the page, the URL it links to with its status, the link’s words and its place', async () => {
    const requests = sidecar(options);
    await open(`${ISSUES}/broken-internal-links`);
    const grid = await screen.findByRole('treegrid', { name: 'Links to broken internal URLs: affected pages' });
    await within(grid).findByRole('button', { name: `${SITE}/lost/` });
    // A count of what its lines list ("3 broken URLs") is no column of its own.
    expect(headers(grid)).toEqual(['Page', 'Links to', 'Link text', 'Where on the page', 'Inlinks']);
    // Three lines for the page with three, each naming the page again so they read alike; one for the page with one.
    expect(levels(grid)).toEqual(['1', '2', '2', '1']);
    const lines = [...grid.querySelectorAll<HTMLElement>('[role="row"][aria-level]')];
    expect(lines.map((line) => cells(line).slice(0, 4))).toEqual([
      [`${POST}/blog/post/`, `${SITE}/old//old/404Hide 2`, '“Old range”', 'Footer'],
      [`${POST}/blog/post/`, `${SITE}/gone//gone/404`, '“Gone for good”', 'Content'],
      [`${POST}/blog/post/`, `${SITE}/lost//lost/410`, 'No text', 'Sidebar'],
      [`${ABOUT}/about/`, `${SITE}/old//old/404`, '“Our old range”', 'Navigation'],
    ]);
    // What each names came in one request, of both pages.
    const reads = requests.filter((request) => request.path.endsWith('/issues/broken-internal-links/targets'));
    expect(reads).toHaveLength(1);
    expect(reads[0]?.body).toMatchObject({ urlIds: [40, 41] });
  });

  it('opens the page at each line’s own link, and inspects the page or the broken URL from their addresses', async () => {
    sidecar(options);
    const router = await open(`${ISSUES}/broken-internal-links`);
    const grid = await screen.findByRole('treegrid', { name: 'Links to broken internal URLs: affected pages' });
    await within(grid).findByRole('button', { name: `${SITE}/lost/` });
    const [old, gone, lost] = [...grid.querySelectorAll<HTMLElement>('[role="row"][aria-level]')];
    expect(openButton(old!, POST)).toHaveAttribute('data-open-url', `${POST}#:~:text=Old%20range`);
    expect(openButton(gone!, POST)).toHaveAttribute('data-open-url', `${POST}#:~:text=Gone%20for%20good`);
    // A link with no words to look for: the page, at its top.
    expect(openButton(lost!, POST)).toHaveAttribute('data-open-url', POST);
    // The broken URL opens as itself.
    expect(openButton(gone!, `${SITE}/gone/`)).toHaveAttribute('data-open-url', `${SITE}/gone/`);

    fireEvent.click(within(gone!).getByRole('button', { name: `${SITE}/gone/` }));
    await waitFor(() => expect(router.state.location.search).toMatchObject({ inspect: 11 }));
    fireEvent.click(within(gone!).getByRole('button', { name: POST }));
    await waitFor(() => expect(router.state.location.search).toMatchObject({ inspect: 40 }));
  });

  it('folds a page’s other links under “Hide 2”, and shows them again', async () => {
    sidecar(options);
    await open(`${ISSUES}/broken-internal-links`);
    const grid = await screen.findByRole('treegrid', { name: 'Links to broken internal URLs: affected pages' });
    const hide = await within(grid).findByText('Hide 2');
    press(hide);
    await waitFor(() => expect(levels(grid)).toEqual(['1', '1']));
    press(within(grid).getByText('+2 more'));
    await waitFor(() => expect(levels(grid)).toEqual(['1', '2', '2', '1']));
  });
});

describe('the view chosen for a check', () => {
  it('switches between its list and its groups, the groups naming one in the address and the list none', async () => {
    sidecar();
    const router = await open(`${ISSUES}/internal-client-error`);
    await brokenList();
    const views = screen.getByRole('radiogroup', { name: 'How the rows are shown' });
    const list = within(views).getByRole('radio', { name: 'List' });
    const grouped = within(views).getByRole('radio', { name: 'Grouped by cause' });
    expect(screen.getByText('2 URLs, most used first')).toBeVisible();

    fireEvent.click(grouped);
    const tree = await screen.findByRole('treegrid', { name: /grouped by cause$/ });
    expect(grouped).toHaveAttribute('aria-checked', 'true');
    expect(list).toHaveAttribute('aria-checked', 'false');
    expect(screen.queryByRole('treegrid', { name: 'Internal URL returns 4xx: affected URLs' })).toBeNull();
    expect(await screen.findByText('2 URLs in 1 group')).toBeVisible();
    // Its only group opens by itself, and is the address's.
    await waitFor(() => expect(router.state.location.search).toMatchObject({ group: { detail: '404 Not Found' } }));
    expect(await within(tree).findByRole('row', { name: /example\.com\/old\// })).toHaveAttribute('aria-level', '2');

    fireEvent.click(list);
    const { grid } = await brokenList();
    expect(list).toHaveAttribute('aria-checked', 'true');
    expect(grid).toBeVisible();
    expect(screen.queryByRole('treegrid', { name: /grouped by/ })).toBeNull();
    // The list is of the whole check: the group leaves the address with the groups.
    await waitFor(() => expect(router.state.location.search).not.toHaveProperty('group'));
    expect(screen.getByText('2 URLs, most used first')).toBeVisible();
  });

  it('is kept for the check: leaving it for another check and coming back finds it as it was chosen', async () => {
    sidecar();
    const router = await open(`${ISSUES}/internal-client-error`);
    await brokenList();
    fireEvent.click(screen.getByRole('radio', { name: 'Grouped by cause' }));
    await screen.findByRole('treegrid', { name: /grouped by cause$/ });

    // To another check, which opens as its own default says, not as this one was left.
    fireEvent.click(screen.getByRole('option', { name: /Internal links without anchor text/ }));
    await waitFor(() => expect(router.state.location.pathname).toBe(`${ISSUES}/empty-anchor-text`));
    expect(await screen.findByRole('radio', { name: 'List' })).toHaveAttribute('aria-checked', 'true');
    expect(
      await screen.findByRole('treegrid', { name: 'Internal links without anchor text: affected pages' }),
    ).toBeVisible();

    // And back: grouped, as it was left, though the address names no group on the way in.
    fireEvent.click(screen.getByRole('option', { name: /Internal URL returns 4xx/ }));
    await waitFor(() => expect(router.state.location.pathname).toBe(`${ISSUES}/internal-client-error`));
    expect(await screen.findByRole('treegrid', { name: /grouped by cause$/ })).toBeVisible();
    expect(screen.getByRole('radio', { name: 'Grouped by cause' })).toHaveAttribute('aria-checked', 'true');

    // The list, chosen, is kept the same way.
    fireEvent.click(screen.getByRole('radio', { name: 'List' }));
    await brokenList();
    fireEvent.click(screen.getByRole('option', { name: /Title too long/ }));
    await waitFor(() => expect(router.state.location.pathname).toBe(`${ISSUES}/title-too-long`));
    await screen.findByRole('grid', { name: 'Title too long: affected pages' });
    fireEvent.click(screen.getByRole('option', { name: /Internal URL returns 4xx/ }));
    await waitFor(() => expect(router.state.location.pathname).toBe(`${ISSUES}/internal-client-error`));
    await brokenList();
    expect(screen.getByRole('radio', { name: 'List' })).toHaveAttribute('aria-checked', 'true');
    expect(router.state.location.search).not.toHaveProperty('group');
  });

  it('keeps the rows closed by hand closed when the check is come back to', async () => {
    sidecar();
    const router = await open(`${ISSUES}/internal-client-error`);
    const { grid, old } = await brokenList();
    expect(old).toHaveAttribute('aria-expanded', 'true');
    press(within(old).getByText('Hide 2'));
    await waitFor(() => expect(levels(grid)).toEqual(['1', '1']));

    fireEvent.click(screen.getByRole('option', { name: /Title too long/ }));
    await waitFor(() => expect(router.state.location.pathname).toBe(`${ISSUES}/title-too-long`));
    await screen.findByRole('grid', { name: 'Title too long: affected pages' });
    fireEvent.click(screen.getByRole('option', { name: /Internal URL returns 4xx/ }));
    await waitFor(() => expect(router.state.location.pathname).toBe(`${ISSUES}/internal-client-error`));

    const back = await brokenList();
    expect(back.old).toHaveAttribute('aria-expanded', 'false');
    expect(within(back.old).getByText('+2 more')).toBeVisible();
    expect(levels(back.grid)).toEqual(['1', '1']);
  });

  it('starts every check as if never opened once the places are forgotten', async () => {
    sidecar();
    await open(`${ISSUES}/internal-client-error`);
    await brokenList();
    fireEvent.click(screen.getByRole('radio', { name: 'Grouped by cause' }));
    await screen.findByRole('treegrid', { name: /grouped by cause$/ });
    cleanup();
    forgetCheckPlaces();

    sidecar();
    await open(`${ISSUES}/internal-client-error`);
    await brokenList();
    expect(screen.getByRole('radio', { name: 'List' })).toHaveAttribute('aria-checked', 'true');
  });
});
