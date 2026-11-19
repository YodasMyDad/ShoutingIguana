import { createMemoryHistory } from '@tanstack/react-router';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it } from 'vitest';
import type { GridFilter, GridSort } from '../api/grids';
import { setTransport, type TransportRequest } from '../api/transport';
import { App } from '../app/App';
import { createQueryClient } from '../app/queryClient';
import { createAppRouter } from '../app/router';
import { TEST_CRAWL_ID, fakeSidecar } from '../test/fakeSidecar';
import { inspectorRoutes, testUrlDetail } from '../test/inspectorSidecar';
import { LINKS_URLS, linksRoutes, testLinkRows } from '../test/linksSidecar';
import { LINKS_FACTS } from '../test/linksSidecar';
import { wholeText } from '../test/text';
import { IMAGE_COLUMNS } from './links/presets';

const LINKS = `/crawls/${TEST_CRAWL_ID}/links`;

function sidecar(options: Parameters<typeof linksRoutes>[0] = {}) {
  const { routes, state } = linksRoutes(options);
  // The inspector on any of the crawl's URLs.
  const inspector = inspectorRoutes({
    urls: (urlId) => {
      const address = LINKS_URLS.find((candidate) => candidate.id === urlId)?.url;
      return address ? testUrlDetail({ id: urlId, url: address }) : undefined;
    },
  });
  const transport = fakeSidecar([...inspector, ...routes]);
  setTransport(transport);
  return { transport, state };
}

async function open(path: string) {
  const queryClient = createQueryClient();
  const history = createMemoryHistory({ initialEntries: [path] });
  const router = createAppRouter(queryClient, history);
  render(<App router={router} queryClient={queryClient} />);
  await act(() => router.load());
  return { router, user: userEvent.setup() };
}

interface Routed {
  state: { location: { search: unknown } };
}
const searchOf = (router: Routed) =>
  router.state.location.search as { by?: string; filter?: GridFilter[]; sort?: GridSort[]; cols?: string[] };
const header = async () => (await screen.findByRole('heading', { level: 1, name: 'Links' })).parentElement!;
const counted = async (words: string) =>
  expect(await within(await header()).findByText(wholeText(words))).toBeVisible();
const groupsGrid = () => screen.findByRole('treegrid', { name: 'Links grouped by target' });
const groupReads = (transport: { requests: TransportRequest[] }) =>
  transport.requests.filter((request) => request.path.endsWith('/links/groups'));
const cellOf = (row: HTMLElement) => row.querySelector<HTMLElement>('[role="gridcell"]') ?? row;
const level2 = (grid: HTMLElement) => [...grid.querySelectorAll<HTMLElement>('[role="row"][aria-level="2"]')];
const filterOf = (filters: GridFilter[]) => encodeURIComponent(JSON.stringify(filters));

afterEach(() => setTransport(fakeSidecar([])));

describe('the Links grid grouped by target', () => {
  it('lists each target once, the most linked from pages first, with its pages, links, text and status', async () => {
    const { transport } = sidecar();
    await open(`${LINKS}?by=target`);
    await counted('8 targets · 13 links on 3 pages');
    const grid = await groupsGrid();
    const rows = within(grid).getAllByRole('row').slice(1);
    // /about has three pages linking to it; then the targets two pages link to, in the order the crawl found them.
    expect(rows[0]).toHaveTextContent('/about');
    expect(rows[1]).toHaveTextContent('/');
    expect(rows[2]).toHaveTextContent('/gone');
    const gone = rows[2]!;
    expect(gone).toHaveTextContent('404');
    expect(gone).toHaveTextContent('Gone');
    expect(gone).toHaveTextContent('+1 other text');
    expect(gone).toHaveTextContent('Content, Footer');
    // The pages that link to it are in its row (the UX round's D-03): the first, a link, and how many more.
    expect(within(grid).getByRole('columnheader', { name: /Linked from/ })).toHaveAttribute('aria-sort', 'descending');
    expect(within(grid).queryByRole('columnheader', { name: /Linking pages/ })).toBeNull();
    expect(await within(gone).findByRole('button', { name: 'https://example.com/' })).toBeVisible();
    expect(gone).toHaveTextContent('+1 more');
    // One read for the targets: the engine groups, never the screen over the links.
    expect(groupReads(transport)[0]?.body).toMatchObject({ filters: [], sort: [{ column: 'pages', desc: true }] });
    // And one link a drawn target, named by the target, for the page it's first linked from: never a pass over them.
    const reads = transport.requests.filter((request) => request.path.endsWith('/links'));
    expect(reads.length).toBeGreaterThan(0);
    for (const read of reads) {
      const body = read.body as { limit?: number; filters?: GridFilter[] };
      expect(body.limit).toBe(1);
      expect(body.filters?.some((filter) => filter.column === 'to.id' && filter.op === 'eq')).toBe(true);
    }
  });

  it('shows the page that links to a target in its row: a link that inspects it at the link, and opens it there', async () => {
    sidecar();
    const { router, user } = await open(`${LINKS}?by=target`);
    const grid = await groupsGrid();
    expect(grid.closest('.si-open-on-row')).not.toBeNull();
    // /old has one link, on the home page, that says "Old page".
    const old = await within(grid).findByRole('row', { name: /\/old/ });
    const page = await within(old).findByRole('button', { name: 'https://example.com/' });
    expect(old).not.toHaveTextContent('more');
    // Its open-in-browser button opens the page at the link's words.
    expect(page.closest('.si-url')?.querySelector('[data-open-url]')).toHaveAttribute(
      'data-open-url',
      'https://example.com/#:~:text=Old%20page',
    );
    await user.click(page);
    await waitFor(() => expect(router.state.location.search).toMatchObject({ inspect: 1, end: 'from' }));
    // The inspector arrives on the page's outlinks, with the link to /old marked.
    expect(router.state.location.state.inspectShow).toEqual({ section: 'outlinks', link: 3 });
    // The target itself arrives on its inlinks, with its one page marked.
    await user.click(within(old).getByRole('button', { name: 'https://example.com/old' }));
    await waitFor(() => expect(router.state.location.search).toMatchObject({ inspect: 3 }));
    expect(router.state.location.state.inspectShow).toEqual({ section: 'inlinks', link: 1 });
  });

  it('opens a target on a click anywhere on its row, and on “+1 more”; its lines’ pages are links', async () => {
    sidecar();
    const { router, user } = await open(`${LINKS}?by=target`);
    const grid = await groupsGrid();
    const gone = await within(grid).findByRole('row', { name: /\/gone/ });
    // A click on the row, away from its links: it opens. Again: it closes.
    const status = within(gone).getByText('404').closest<HTMLElement>('[role="gridcell"]')!;
    await user.click(status);
    await waitFor(() => expect(level2(grid)[0]).toHaveTextContent('“Gone”'));
    expect(gone).toHaveAttribute('aria-expanded', 'true');
    await user.click(status);
    await waitFor(() => expect(level2(grid)).toHaveLength(0));
    // A double-click opens it too, as it always did: it doesn't open and close again.
    await user.dblClick(status);
    await waitFor(() => expect(level2(grid)[0]).toHaveTextContent('“Gone”'));
    await user.dblClick(status);
    await waitFor(() => expect(level2(grid)).toHaveLength(0));
    // "+1 more" opens the rest in place.
    fireEvent.pointerDown(within(gone).getByText('+1 more'), { button: 0 });
    await waitFor(() => expect(level2(grid)).toHaveLength(3));
    // A click on a link in the row goes where the link goes, and leaves the row as it is.
    await user.click(within(gone).getByRole('button', { name: 'https://example.com/gone' }));
    await waitFor(() => expect(router.state.location.search).toMatchObject({ inspect: 4 }));
    expect(gone).toHaveAttribute('aria-expanded', 'true');
    // Each line's page is a link: it inspects the page, on its outlinks, with this link marked.
    const [, second] = level2(grid);
    await user.click(within(second!).getByRole('button', { name: 'https://example.com/about' }));
    await waitFor(() => expect(router.state.location.search).toMatchObject({ inspect: 2, end: 'from' }));
    expect(router.state.location.state.inspectShow).toEqual({ section: 'outlinks', link: 4 });
    expect(second!.querySelector('[data-open-url]')).toHaveAttribute(
      'data-open-url',
      'https://example.com/about#:~:text=Gone%20again',
    );
  });

  it('names both ends in a line’s menu, and the target alone in a target’s', async () => {
    sidecar();
    const { router, user } = await open(`${LINKS}?by=target`);
    const grid = await groupsGrid();
    const gone = await within(grid).findByRole('row', { name: /\/gone/ });
    const names = async () =>
      within(await screen.findByRole('menu'))
        .getAllByRole('menuitem')
        .map((item) => item.textContent.replace(/(Shift)?Enter$|CtrlC$/, ''));
    fireEvent.contextMenu(cellOf(gone));
    expect(await names()).toEqual([
      'Inspect the target',
      'Open the target in your browser',
      'Copy the target URL',
      'Copy the target',
      'Show all 2 links in the flat list',
    ]);
    await user.keyboard('{Escape}');
    await user.click(within(gone).getByText('404'));
    await waitFor(() => expect(level2(grid)).toHaveLength(3));
    fireEvent.contextMenu(cellOf(level2(grid)[1]!));
    expect(await names()).toEqual([
      'Inspect the source page',
      'Open the source page in your browser',
      'Copy the source URL',
      'Inspect the target',
      'Open the target in your browser',
      'Copy the target URL',
      'Show all 2 links in the flat list',
    ]);
    await user.click(within(screen.getByRole('menu')).getByRole('menuitem', { name: /^Inspect the source page/ }));
    await waitFor(() => expect(router.state.location.search).toMatchObject({ inspect: 2, end: 'from' }));
    expect(router.state.location.state.inspectShow).toEqual({ section: 'outlinks', link: 4 });
  });

  it('switches between one row a link and one a target, keeping the filters and a sort by the target', async () => {
    sidecar();
    const broken: GridFilter[] = [{ column: 'to.statusClass', op: 'in', values: ['4xx', '5xx'] }];
    const { router, user } = await open(`${LINKS}?filter=${filterOf(broken)}&sort=-id`);
    await counted('3 of 13 links');
    await user.click(screen.getByRole('button', { name: 'Group by target' }));
    await counted('2 targets · 3 links on 2 pages');
    expect(searchOf(router)).toMatchObject({ by: 'target', filter: broken });
    // Crawl order means nothing for targets: the grouped view's own order takes its place.
    expect(searchOf(router).sort).toBeUndefined();
    expect(screen.getByRole('button', { name: 'Group by target' })).toHaveAttribute('aria-pressed', 'true');
    // The column chooser is for one row a link.
    expect(screen.getByRole('button', { name: /Columns/ })).toBeDisabled();
    await user.click(screen.getByRole('button', { name: 'Group by target' }));
    await counted('3 of 13 links');
    expect(searchOf(router).by).toBeUndefined();
  });

  it('opens a target to its first links, and lists them all one a row from there', async () => {
    sidecar();
    const { router } = await open(`${LINKS}?by=target`);
    const grid = await groupsGrid();
    const gone = await within(grid).findByRole('row', { name: /\/gone/ });
    fireEvent.pointerDown(cellOf(gone), { button: 0 });
    act(() => grid.focus());
    fireEvent.keyDown(grid, { key: 'ArrowRight' });
    // Each of its links: the page it's on, what it says, where it sits.
    await waitFor(() => expect(level2(grid)[0]).toHaveTextContent('“Gone”'));
    const [first, second, more] = level2(grid);
    expect(level2(grid)).toHaveLength(3);
    expect(first).toHaveTextContent('onhttps://example.com/');
    expect(second).toHaveTextContent('https://example.com/about');
    expect(second).toHaveTextContent('“Gone again”');
    expect(second).toHaveTextContent('Footer');
    expect(more).toHaveTextContent('Show all 2 links in the flat list');
    fireEvent.pointerDown(cellOf(more!), { button: 0 });
    act(() => grid.focus());
    fireEvent.keyDown(grid, { key: 'Enter' });
    await waitFor(() => expect(searchOf(router).by).toBeUndefined());
    expect(searchOf(router).filter).toEqual([{ column: 'to.id', op: 'eq', value: 4 }]);
    await counted('2 of 13 links');
  });

  it('has the inspector follow the cursor, on the target of the row it’s on', async () => {
    sidecar();
    const { router } = await open(`${LINKS}?by=target&inspect=2`);
    const grid = await groupsGrid();
    await within(grid).findByRole('row', { name: /\/gone/ });
    act(() => grid.focus());
    fireEvent.keyDown(grid, { key: 'Home' });
    fireEvent.keyDown(grid, { key: 'ArrowDown' });
    await waitFor(() => expect((router.state.location.search as { inspect?: number }).inspect).toBe(1));
    fireEvent.keyDown(grid, { key: 'ArrowDown' });
    await waitFor(() => expect((router.state.location.search as { inspect?: number }).inspect).toBe(4));
    // Opened, the target's links: the page each is on, when the inspector is on the links' pages.
    fireEvent.keyDown(grid, { key: 'ArrowRight' });
    await waitFor(() => expect(level2(grid)[0]).toHaveTextContent('“Gone”'));
    fireEvent.keyDown(grid, { key: 'ArrowDown' });
    await waitFor(() => expect((router.state.location.search as { inspect?: number }).inspect).toBe(4));
  });

  it('leaves the inspector where it is while it’s closed', async () => {
    sidecar();
    const { router } = await open(`${LINKS}?by=target`);
    const grid = await groupsGrid();
    await within(grid).findByRole('row', { name: /\/gone/ });
    act(() => grid.focus());
    fireEvent.keyDown(grid, { key: 'ArrowDown' });
    await new Promise((resolve) => setTimeout(resolve, 50));
    expect((router.state.location.search as { inspect?: number }).inspect).toBeUndefined();
  });

  it('exports what’s shown, one row a target, while grouped', async () => {
    sidecar();
    const { user } = await open(`${LINKS}?by=target`);
    await counted('8 targets · 13 links on 3 pages');
    await user.click(screen.getByRole('button', { name: 'Export' }));
    const dialog = await screen.findByRole('dialog', { name: 'Export links by target' });
    expect(within(dialog).getByText(/8 targets, one row a target: the most linked from pages first/)).toBeVisible();
    // The dialog is the app's: closed, so the tests after this one start without it.
    await user.keyboard('{Escape}');
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
  });

  it('sorts the targets by a header, and back to the most linked from pages first', async () => {
    const { transport } = sidecar();
    const { router, user } = await open(`${LINKS}?by=target`);
    await user.click(within(await groupsGrid()).getByRole('button', { name: /^Links/ }));
    await waitFor(() => expect(searchOf(router).sort).toEqual([{ column: 'links', desc: true }]));
    // Another order is another grid: its headers are read again.
    await counted('8 targets · 13 links on 3 pages');
    await user.click(within(await groupsGrid()).getByRole('button', { name: /^Target/ }));
    await waitFor(() => expect(searchOf(router).sort).toEqual([{ column: 'to.url', desc: false }]));
    await waitFor(() =>
      expect(groupReads(transport).at(-1)?.body).toMatchObject({ sort: [{ column: 'to.url', desc: false }] }),
    );
  });
});

describe('the Images preset', () => {
  // The crawl's two image links, of one image: a file of 1.8 MB, 4000 by 3000 pixels.
  const urls = LINKS_URLS.map((url) =>
    url.id === 5 ? { ...url, sizeBytes: 1_887_437, pixelWidth: 4000, pixelHeight: 3000 } : url,
  );

  it('shows the image links, the largest file first, with the image’s own columns', async () => {
    sidecar({ urls, links: testLinkRows(LINKS_FACTS, urls) });
    const { router, user } = await open(LINKS);
    await counted('13 links');
    await user.click(screen.getByRole('button', { name: 'Images' }));
    await counted('2 of 13 links');
    expect(searchOf(router)).toMatchObject({
      filter: [{ column: 'kind', op: 'in', values: ['image'] }],
      cols: [...IMAGE_COLUMNS],
      sort: [{ column: 'to.sizeBytes', desc: true }],
    });
    const grid = await screen.findByRole('grid', { name: 'Links' });
    const names = within(grid)
      .getAllByRole('columnheader')
      .map((cell) => cell.textContent);
    ['Image', 'Size', 'Pixel size', 'Alt text', 'Shown on'].forEach((name, index) =>
      expect(names[index]).toContain(name),
    );
    // Both links show the one file: its size and pixels on each, and the alt text each gives it.
    const [first, second] = within(grid).getAllByRole('row').slice(1);
    for (const row of [first, second]) {
      expect(row).toHaveTextContent('1.8 MB');
      expect(row).toHaveTextContent('4000×3000 px');
      // Named by its file, its folder after it.
      expect(row?.querySelector('.si-url__file')).toHaveTextContent('logo.png');
    }
    expect(within(grid).getByText('Logo')).toBeVisible();
    // Another preset after it puts the default columns back.
    await user.click(screen.getByRole('button', { name: 'Broken links' }));
    await waitFor(() => expect(searchOf(router).cols).toBeUndefined());
  });

  it('grouped, is one row an image, with the pages it’s used on', async () => {
    sidecar({ urls, links: testLinkRows(LINKS_FACTS, urls) });
    await open(`${LINKS}?by=target&filter=${filterOf([{ column: 'kind', op: 'in', values: ['image'] }])}`);
    await counted('1 image · 2 links on 2 pages');
    const grid = await screen.findByRole('treegrid', { name: 'Images, one row an image' });
    expect(within(grid).getByRole('columnheader', { name: /Used on/ })).toBeVisible();
    const logo = within(grid).getAllByRole('row')[1]!;
    expect(logo.querySelector('.si-url__file')).toHaveTextContent('logo.png');
    expect(logo.querySelector('.si-url__rest')).toHaveTextContent('/img/');
    expect(logo).toHaveTextContent('1.8 MB');
    expect(logo).toHaveTextContent('4000×3000 px');
    expect(logo).toHaveTextContent('Logo');
  });

  it('leaves out the pixel size, and says so once, for a crawl that didn’t measure it', async () => {
    // The crawl's image has no pixel size: the crawl was made before they were read.
    sidecar();
    const { user } = await open(LINKS);
    await counted('13 links');
    await user.click(screen.getByRole('button', { name: 'Images' }));
    await counted('2 of 13 links');
    const grid = await screen.findByRole('grid', { name: 'Links' });
    await waitFor(() => expect(within(grid).getAllByRole('row')).toHaveLength(3));
    expect(within(grid).queryByRole('columnheader', { name: /Pixel size/ })).toBeNull();
    expect(screen.getByText('Image dimensions weren’t measured in this crawl.')).toBeVisible();
    // Grouped, the same.
    await user.click(screen.getByRole('button', { name: 'Group by target' }));
    const tree = await screen.findByRole('treegrid', { name: 'Images, one row an image' });
    await within(tree).findByRole('row', { name: /logo\.png/ });
    expect(within(tree).queryByRole('columnheader', { name: /Pixel size/ })).toBeNull();
    expect(screen.getByText('Image dimensions weren’t measured in this crawl.')).toBeVisible();
  });
});
