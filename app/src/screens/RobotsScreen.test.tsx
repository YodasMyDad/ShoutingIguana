import { createMemoryHistory } from '@tanstack/react-router';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { RobotsBlockedQuery, RobotsFile } from '../api/grids';
import { gridPagesOf, liveGridPagesOf } from '../api/queries';
import { setTransport, type TransportRequest } from '../api/transport';
import { App } from '../app/App';
import { createQueryClient } from '../app/queryClient';
import { createAppRouter } from '../app/router';
import { onAnnounce } from '../feedback/announce';
import type { SourceViewerProps } from '../inspector/SourceViewer';
import { commands, installKeyboard } from '../keyboard/registry';
import { TEST_CRAWL_ID, fakeSidecar, json, problem } from '../test/fakeSidecar';
import { resetPlaces } from './listPlace';
import { UNKNOWN_ACTION } from './robots/robotsWords';
import { createQueryClientWithoutBackoff } from '../test/queryClient';
import { wholeText } from '../test/text';
import {
  SITE,
  TEST_BLOCKED,
  TEST_FILE,
  TEST_ORIGINS,
  blockedPage,
  sitemapsRobotsRoutes,
  testOrigin,
  type SitemapsRobotsOptions,
} from '../test/sitemapsRobotsSidecar';

// CodeMirror needs a browser's layout: the viewer itself is covered end to end. Here it's a stand-in that shows what
// it was given: the text, each marked line's link as a button, and the line it was told is chosen.
vi.mock('../inspector/SourceViewer', () => ({
  default: (props: SourceViewerProps) => (
    <div
      data-testid="source-viewer"
      data-language={props.language}
      data-chosen={props.chosenLine ?? ''}
      data-query={props.query}
      aria-label={props.label}
    >
      {(props.lineMarks ?? []).map((mark) =>
        mark.action ? (
          <button
            key={mark.line}
            type="button"
            title={mark.actionTitle}
            onClick={() => props.onLineAction?.(mark.line)}
          >
            {`line ${mark.line}: ${mark.action}`}
          </button>
        ) : mark.note ? (
          <span key={mark.line}>{`line ${mark.line}: ${mark.note}`}</span>
        ) : null,
      )}
      <pre>{props.html}</pre>
    </div>
  ),
}));

const ROBOTS = `/crawls/${TEST_CRAWL_ID}/robots`;
const FILE = `${ROBOTS}/${encodeURIComponent(SITE)}`;

function sidecar(options: SitemapsRobotsOptions = {}) {
  const transport = fakeSidecar(sitemapsRobotsRoutes(options));
  setTransport(transport);
  return { transport };
}

async function open(path: string, queryClient = createQueryClient()) {
  const router = createAppRouter(queryClient, createMemoryHistory({ initialEntries: [path] }));
  render(<App router={router} queryClient={queryClient} />);
  await act(() => router.load());
  return { router, queryClient };
}

type Router = Awaited<ReturnType<typeof open>>['router'];
const searchOf = (router: Router) => router.state.location.search as Record<string, unknown>;
const pathOf = (router: Router) => router.state.location.pathname;
const rowOf = (text: string) => screen.getByText(text).closest<HTMLElement>('[role="row"]')!;
const blockedRequests = (transport: { requests: TransportRequest[] }) =>
  transport.requests
    .filter((request) => request.path.endsWith('/robots/blocked'))
    .map((request) => request.body as RobotsBlockedQuery);
const viewer = () => screen.getByTestId('source-viewer');

afterEach(() => {
  setTransport(fakeSidecar([]));
  resetPlaces();
});

describe('robots.txt: the origins', () => {
  it('answers first, then lists the origins with what each answer meant to the crawl, in different words', async () => {
    sidecar();
    await open(ROBOTS);
    const answer = await screen.findByText(
      wholeText(
        'The crawl asked 4 origins for robots.txt, as ShoutingIguana. They kept it from 6 URLs. The site’s own come first, then the rest by the URLs they blocked.',
      ),
    );
    expect(answer).toBeVisible();
    // What it was kept from is a link to exactly those URLs: the ones whose outcome is "refused by robots.txt".
    const kept = decodeURIComponent(within(answer).getByRole('link', { name: '6 URLs' }).getAttribute('href') ?? '');
    expect(kept).toBe(
      `/crawls/${TEST_CRAWL_ID}/urls?filter=[{"column":"outcome","op":"in","values":["robotsDisallowed"]}]`,
    );
    const grid = await screen.findByRole('grid', { name: 'Origins' });
    await waitFor(() => expect(grid).toHaveAttribute('aria-rowcount', '5'));
    const state = (origin: string) => rowOf(origin).querySelector('[aria-colindex="2"]');
    expect(state(SITE)).toHaveTextContent('Read');
    expect(state('https://cdn.example.net')).toHaveTextContent('Couldn’t be read: URLs refused');
    expect(state('https://partner.example.org')).toHaveTextContent('Missing: nothing refused');
    expect(state('https://slow.example.io')).toHaveTextContent('No answer: crawled anyway');
    // The whole of it is a hover away.
    expect(within(rowOf('https://slow.example.io')).getByText('No answer: crawled anyway')).toHaveAttribute(
      'title',
      expect.stringContaining('gave no verdict and went ahead'),
    );
    expect(within(rowOf(SITE)).getByText('This site')).toBeVisible();
  });

  it('makes an origin’s blocked URLs a link to its file, and shows none as a plain 0', async () => {
    sidecar();
    await open(ROBOTS);
    const own = await waitFor(() => rowOf(SITE));
    expect(within(own).getByRole('link', { name: '4 URLs blocked' })).toHaveAttribute('href', FILE);
    // An origin that blocked nothing has no count to open: its only link is the origin itself.
    expect(
      within(rowOf('https://partner.example.org'))
        .getAllByRole('link')
        .map((link) => link.textContent),
    ).toEqual(['https://partner.example.org']);
    expect(rowOf('https://partner.example.org').querySelector('[aria-colindex="3"]')).toHaveTextContent('0');
  });

  it('opens an origin’s file on one click on the origin, one that blocked nothing too', async () => {
    sidecar();
    const { router } = await open(ROBOTS);
    await screen.findByText('https://partner.example.org');
    const origin = screen.getByRole('link', { name: 'https://partner.example.org' });
    expect(origin).toHaveAttribute('href', `${ROBOTS}/${encodeURIComponent('https://partner.example.org')}`);
    expect(origin).toHaveAttribute('tabindex', '-1');
    fireEvent.click(origin);
    await waitFor(() => expect(pathOf(router)).toBe(`${ROBOTS}/${encodeURIComponent('https://partner.example.org')}`));
  });

  it('opens an origin’s file on a click anywhere on its row, and leaves Shift and Ctrl clicks to select', async () => {
    sidecar();
    const { router } = await open(ROBOTS);
    await screen.findByText('https://slow.example.io');
    const state = () => within(rowOf('https://slow.example.io')).getByText('No answer: crawled anyway');
    fireEvent.click(state(), { shiftKey: true });
    fireEvent.click(state(), { ctrlKey: true });
    expect(pathOf(router)).toBe(ROBOTS);
    fireEvent.click(state());
    await waitFor(() => expect(pathOf(router)).toBe(`${ROBOTS}/${encodeURIComponent('https://slow.example.io')}`));
  });

  it('opens the live robots.txt in the browser from the button straight after the origin, without opening its row', async () => {
    sidecar();
    const opened = vi.spyOn(window, 'open').mockReturnValue(null);
    const { router } = await open(ROBOTS);
    const own = await waitFor(() => rowOf(SITE));
    fireEvent.click(within(own).getByRole('button', { name: `Open ${SITE}/robots.txt in your browser` }));
    expect(opened).toHaveBeenCalledWith(`${SITE}/robots.txt`, '_blank', 'noopener,noreferrer');
    expect(pathOf(router)).toBe(ROBOTS);
    opened.mockRestore();
  });

  it('has a menu on each row: open its robots.txt here, open it in the browser, copy the origin', async () => {
    sidecar();
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, 'clipboard', { configurable: true, value: { writeText } });
    const opened = vi.spyOn(window, 'open').mockReturnValue(null);
    const { router } = await open(ROBOTS);
    await screen.findByText('https://partner.example.org');
    const cell = () => rowOf('https://partner.example.org').querySelector<HTMLElement>('[data-col="0"]')!;
    fireEvent.contextMenu(cell());
    const menu = await screen.findByRole('menu', { name: 'Menu for https://partner.example.org' });
    expect(
      within(menu)
        .getAllByRole('menuitem')
        .map((item) => item.textContent),
    ).toEqual([
      expect.stringContaining('Open its robots.txt'),
      expect.stringContaining('Open robots.txt in the browser'),
      expect.stringContaining('Copy the origin'),
      expect.stringContaining('Copy the row'),
    ]);
    fireEvent.click(within(menu).getByRole('menuitem', { name: /Copy the origin/ }));
    await waitFor(() => expect(writeText).toHaveBeenCalledWith('https://partner.example.org'));
    fireEvent.contextMenu(cell());
    fireEvent.click(
      within(await screen.findByRole('menu')).getByRole('menuitem', { name: /Open robots.txt in the browser/ }),
    );
    expect(opened).toHaveBeenCalledWith('https://partner.example.org/robots.txt', '_blank', 'noopener,noreferrer');
    fireEvent.contextMenu(cell());
    fireEvent.click(within(await screen.findByRole('menu')).getByRole('menuitem', { name: /Open its robots.txt/ }));
    await waitFor(() => expect(pathOf(router)).toBe(`${ROBOTS}/${encodeURIComponent('https://partner.example.org')}`));
    opened.mockRestore();
  });

  it('starts, coming back from an origin opened by a click, on that origin’s row', async () => {
    sidecar();
    const { router } = await open(ROBOTS);
    await screen.findByText('https://partner.example.org');
    fireEvent.click(screen.getByRole('link', { name: 'https://partner.example.org' }));
    await waitFor(() => expect(pathOf(router)).toBe(`${ROBOTS}/${encodeURIComponent('https://partner.example.org')}`));
    act(() => router.history.back());
    const again = await screen.findByRole('grid', { name: 'Origins' });
    await screen.findByText('https://partner.example.org');
    act(() => again.focus());
    const cursor = document.getElementById(again.getAttribute('aria-activedescendant') ?? '');
    expect(cursor?.closest('[role="row"]')).toHaveTextContent('https://partner.example.org');
  });

  it('is one Tab stop, and Enter opens the origin’s file', async () => {
    sidecar();
    const { router } = await open(ROBOTS);
    const grid = await screen.findByRole('grid', { name: 'Origins' });
    await screen.findByText('https://cdn.example.net');
    for (const link of within(grid).getAllByRole('link')) expect(link).toHaveAttribute('tabindex', '-1');
    act(() => grid.focus());
    fireEvent.keyDown(grid, { key: 'ArrowDown' });
    fireEvent.keyDown(grid, { key: 'Enter' });
    await waitFor(() => expect(pathOf(router)).toBe(`${ROBOTS}/${encodeURIComponent('https://cdn.example.net')}`));
  });

  it('starts, coming back from an origin’s file, on the row it was opened from', async () => {
    sidecar();
    const { router } = await open(ROBOTS);
    const grid = await screen.findByRole('grid', { name: 'Origins' });
    await screen.findByText('https://partner.example.org');
    act(() => grid.focus());
    fireEvent.keyDown(grid, { key: 'ArrowDown' });
    fireEvent.keyDown(grid, { key: 'ArrowDown' });
    fireEvent.keyDown(grid, { key: 'Enter' });
    await waitFor(() => expect(pathOf(router)).toBe(`${ROBOTS}/${encodeURIComponent('https://partner.example.org')}`));
    act(() => router.history.back());
    const again = await screen.findByRole('grid', { name: 'Origins' });
    await screen.findByText('https://partner.example.org');
    act(() => again.focus());
    const cursor = document.getElementById(again.getAttribute('aria-activedescendant') ?? '');
    expect(cursor?.closest('[role="row"]')).toHaveTextContent('https://partner.example.org');
  });

  it('keeps the cursor on its origin when a running crawl asks one that sorts ahead of it: Enter opens that origin’s file', async () => {
    const running = { status: 'running', finishedAt: null } as const;
    const site = testOrigin({ origin: SITE, isInternal: true });
    const zebra = testOrigin({ origin: 'https://zebra.example' });
    const apple = testOrigin({ origin: 'https://apple.example' });
    sidecar({ item: running, origins: [site, zebra] });
    const { router, queryClient } = await open(ROBOTS);
    const grid = await screen.findByRole('grid', { name: 'Origins' });
    await screen.findByText('https://zebra.example');
    act(() => grid.focus());
    fireEvent.keyDown(grid, { key: 'ArrowDown' });
    const cursorRow = () =>
      document.getElementById(grid.getAttribute('aria-activedescendant') ?? '')?.closest('[role="row"]')?.textContent ??
      '';
    expect(cursorRow()).toContain('https://zebra.example');
    // The crawl asks another origin: by the engine's order (by name, among those that blocked nothing) it comes
    // first, and takes the row the cursor's origin was on.
    sidecar({ item: running, origins: [site, apple, zebra] });
    act(() => void queryClient.invalidateQueries(gridPagesOf(TEST_CRAWL_ID)));
    await screen.findByText('https://apple.example');
    await waitFor(() => expect(grid).toHaveAttribute('aria-rowcount', '4'));
    expect(cursorRow()).toContain('https://zebra.example');
    expect(cursorRow()).not.toContain('https://apple.example');
    fireEvent.keyDown(grid, { key: 'Enter' });
    await waitFor(() => expect(pathOf(router)).toBe(`${ROBOTS}/${encodeURIComponent('https://zebra.example')}`));
  });

  it('comes back from an origin’s file to that origin’s row, though the list grew above it meanwhile', async () => {
    const running = { status: 'running', finishedAt: null } as const;
    const site = testOrigin({ origin: SITE, isInternal: true });
    const zebra = testOrigin({ origin: 'https://zebra.example' });
    const apple = testOrigin({ origin: 'https://apple.example' });
    sidecar({ item: running, origins: [site, zebra], files: { [zebra.origin]: { ...TEST_FILE, ...zebra } } });
    const { router, queryClient } = await open(ROBOTS);
    const grid = await screen.findByRole('grid', { name: 'Origins' });
    await screen.findByText('https://zebra.example');
    act(() => grid.focus());
    fireEvent.keyDown(grid, { key: 'ArrowDown' });
    fireEvent.keyDown(grid, { key: 'Enter' });
    await waitFor(() => expect(pathOf(router)).toBe(`${ROBOTS}/${encodeURIComponent('https://zebra.example')}`));
    // While the file is open, the crawl asks an origin that sorts ahead of it.
    sidecar({ item: running, origins: [site, apple, zebra], files: { [zebra.origin]: { ...TEST_FILE, ...zebra } } });
    // The list is read afresh on the way back (its pages are kept only a few seconds once nothing shows them).
    queryClient.removeQueries(gridPagesOf(TEST_CRAWL_ID));
    act(() => {
      router.history.back();
    });
    const back = await screen.findByRole('grid', { name: 'Origins' });
    await screen.findByText('https://apple.example');
    act(() => back.focus());
    await waitFor(() =>
      expect(
        document.getElementById(back.getAttribute('aria-activedescendant') ?? '')?.closest('[role="row"]')?.textContent,
      ).toContain('https://zebra.example'),
    );
  });

  it('pages: an origin far down the list is read when it comes into view, never all at once', async () => {
    const origins = Array.from({ length: 2_400 }, (_, index) =>
      testOrigin({ origin: `https://site-${index}.example` }),
    );
    const { transport } = sidecar({ origins });
    await open(ROBOTS);
    const grid = await screen.findByRole('grid', { name: 'Origins' });
    await waitFor(() => expect(grid).toHaveAttribute('aria-rowcount', '2401'));
    expect(await screen.findByText(/The crawl asked 2,400 origins/)).toBeVisible();
    const asked = transport.requests
      .filter((request) => request.path.endsWith('/robots'))
      .map((request) => request.body as { offset: number; limit: number });
    expect(asked.every((page) => page.limit === 200)).toBe(true);
    expect(asked.some((page) => page.offset >= 1_000)).toBe(false);
  });

  it('says at the top when the crawl was told to ignore robots.txt, and doesn’t read that as “none blocked”', async () => {
    sidecar({ respected: false, origins: [testOrigin({ origin: SITE, isInternal: true })] });
    await open(ROBOTS);
    expect(await screen.findByText(/told to ignore robots\.txt, so nothing was blocked/)).toBeVisible();
    expect(screen.getByText('The crawl asked 1 origin for robots.txt, as ShoutingIguana.')).toBeVisible();
    // Its row doesn't say the crawl obeyed the file it read.
    const state = (await waitFor(() => rowOf(SITE))).querySelector('[aria-colindex="2"]');
    expect(state).toHaveTextContent('Read: rules ignored');
    expect(within(rowOf(SITE)).getByText('Read: rules ignored')).toHaveAttribute(
      'title',
      'The crawl read this file, and was told to ignore its rules.',
    );
  });

  it('says so when the crawl asked no origin', async () => {
    sidecar({ origins: [] });
    await open(ROBOTS);
    expect(await screen.findByRole('heading', { name: 'The crawl asked no origin for its robots.txt' })).toBeVisible();
  });

  it('says the origins couldn’t be read, with Try again', async () => {
    setTransport(
      fakeSidecar([
        [
          'POST',
          /\/robots$/,
          () => problem({ type: 'internal', title: 'Something went wrong in the engine.', status: 500 }),
        ],
        ...sitemapsRobotsRoutes(),
      ]),
    );
    await open(ROBOTS, createQueryClientWithoutBackoff());
    expect(await screen.findByText('The origins’ robots.txt couldn’t be read.')).toBeVisible();
    expect(screen.getByRole('button', { name: 'Try again' })).toBeVisible();
  });
});

describe('robots.txt: one origin’s file', () => {
  it('shows the file as text in the viewer, with what the answer meant and which lines the crawl obeyed', async () => {
    sidecar();
    await open(FILE);
    expect(await screen.findByRole('heading', { level: 1, name: `robots.txt of ${SITE}` })).toBeVisible();
    expect(await screen.findByText(/The crawl read this file and obeyed its rules for ShoutingIguana\./)).toBeVisible();
    await waitFor(() => expect(viewer()).toHaveAttribute('data-language', 'plain'));
    expect(viewer()).toHaveAttribute('aria-label', `robots.txt of ${SITE}`);
    // The text as kept: never markup, whatever is in it.
    expect(viewer().querySelector('pre')?.textContent).toBe(TEST_FILE.lines.map((line) => line.text).join('\n'));
    expect(
      screen.getByText(wholeText('12 lines, 164 bytes. As ShoutingIguana, the crawl obeyed lines 5 to 9.')),
    ).toBeVisible();
    expect(within(viewer()).getByText('line 10: Not used by the crawl')).toBeVisible();
  });

  it('lists the blocked URLs beside it, each with the line that decided it, or which case it is in words', async () => {
    sidecar();
    await open(FILE);
    const grid = await screen.findByRole('grid', { name: 'Blocked URLs' });
    await waitFor(() => expect(grid).toHaveAttribute('aria-rowcount', '5'));
    // The URL and what blocking it costs lead; the rule is the fifth column (the file shows it for the cursor's row).
    const why = (url: string) => rowOf(url).querySelector('[aria-colindex="5"]');
    // The column says the line; the rule's text is behind it, and in the file beside the list.
    // The rule itself, its line in the tooltip (the fix-it pass).
    expect(why(`${SITE}/admin/users`)).toHaveTextContent(/^Disallow: \/admin\/$/);
    expect(within(rowOf(`${SITE}/admin/users`)).getByText('Disallow: /admin/')).toHaveAttribute(
      'title',
      'Line 6 refused it: Disallow: /admin/',
    );
    expect(why(`${SITE}/old-private`)).toHaveTextContent('No matching rule');
    expect(within(rowOf(`${SITE}/old-private`)).getByText('No matching rule')).toHaveAttribute(
      'title',
      expect.stringContaining('has no rule that blocks it'),
    );
  });

  it('choosing a line lists exactly its URLs, marks it in the file, says the count once and keeps it in the route', async () => {
    const said: string[] = [];
    const stop = onAnnounce((message) => said.push(message));
    const { transport } = sidecar();
    const { router } = await open(FILE);
    const grid = await screen.findByRole('grid', { name: 'Blocked URLs' });
    await waitFor(() => expect(grid).toHaveAttribute('aria-rowcount', '5'));
    fireEvent.click(await within(viewer()).findByRole('button', { name: 'line 6: Blocked 3 URLs' }));
    await waitFor(() => expect(searchOf(router).line).toBe(6));
    await waitFor(() =>
      expect(screen.getByRole('grid', { name: 'Blocked URLs' })).toHaveAttribute('aria-rowcount', '4'),
    );
    expect(blockedRequests(transport).at(-1)).toMatchObject({ origin: SITE, line: 6 });
    expect(viewer()).toHaveAttribute('data-chosen', '6');
    expect(screen.queryByText(`${SITE}/old-private`)).toBeNull();
    const chip = screen.getByText('6: Disallow: /admin/').closest('.si-chip')!;
    expect(chip).toHaveTextContent('Line is 6: Disallow: /admin/');
    await waitFor(() => expect(said).toEqual(['3 of 4 blocked URLs']));
    stop();
  });

  it('choosing a URL shows the line that decided it, and none for a URL no line did', async () => {
    sidecar();
    await open(FILE);
    const grid = await screen.findByRole('grid', { name: 'Blocked URLs' });
    await screen.findByText(`${SITE}/admin/users`);
    expect(viewer()).toHaveAttribute('data-chosen', '');
    act(() => grid.focus());
    await waitFor(() => expect(viewer()).toHaveAttribute('data-chosen', '6'));
    fireEvent.keyDown(grid, { key: 'End', ctrlKey: true });
    // The last one is stored as refused, and no line kept refuses it.
    await waitFor(() => expect(viewer()).toHaveAttribute('data-chosen', ''));
    fireEvent.keyDown(grid, { key: 'ArrowUp' });
    await waitFor(() => expect(viewer()).toHaveAttribute('data-chosen', '6'));
  });

  it('says what blocking each URL costs: the pages linking to it, its sitemap, and a flag where the site asks for it', async () => {
    sidecar();
    const { router } = await open(FILE);
    const grid = await screen.findByRole('grid', { name: 'Blocked URLs' });
    await waitFor(() => expect(grid).toHaveAttribute('aria-rowcount', '5'));
    expect(
      within(grid)
        .getAllByRole('columnheader')
        .map((header) => header.textContent),
    ).toEqual(['Blocked URL', 'Conflict', 'Linked from', 'In a sitemap', 'Blocked by']);
    const cell = (url: string, column: number) => rowOf(url).querySelector(`[aria-colindex="${column}"]`);
    // In a few words, the whole sentence behind them (and read with them).
    expect(cell(`${SITE}/admin/`, 2)).toHaveTextContent('In menu');
    expect(within(rowOf(`${SITE}/admin/`)).getByTitle('In your menu but blocked')).toBeInTheDocument();
    expect(cell(`${SITE}/admin/orders?page=2`, 2)).toHaveTextContent('In sitemap');
    expect(within(rowOf(`${SITE}/admin/orders?page=2`)).getByTitle('In your sitemap but blocked')).toBeInTheDocument();
    expect(cell(`${SITE}/admin/users`, 2)).toHaveTextContent(/^$/);
    expect(cell(`${SITE}/admin/users`, 3)).toHaveTextContent('3 pages');
    expect(cell(`${SITE}/old-private`, 3)).toHaveTextContent('No pages');
    expect(cell(`${SITE}/admin/users`, 4)).toHaveTextContent('No');
    expect(cell(`${SITE}/admin/orders?page=2`, 4)).toHaveTextContent('Yes: pages.xml and 1 more');

    // The count leads to the links to it in Links; the sitemap to its page in Sitemaps.
    const linked = within(rowOf(`${SITE}/admin/`)).getByRole('link', {
      name: 'Linked from 1,284 pages: show the links in Links',
    });
    expect(linked.getAttribute('href')).toMatch(/\/links\?filter=/);
    expect(decodeURIComponent(linked.getAttribute('href') ?? '')).toContain('"value":21');
    const listed = within(rowOf(`${SITE}/admin/orders?page=2`)).getByRole('link', {
      name: 'Yes: pages.xml and 1 more',
    });
    expect(listed).toHaveAttribute('title', `Listed in 2 sitemaps, the first ${SITE}/sitemaps/pages.xml`);
    fireEvent.click(listed);
    await waitFor(() => expect(pathOf(router)).toBe(`/crawls/${TEST_CRAWL_ID}/sitemaps/2`));
  });

  it('lists the URLs the site asks for and blocks from their own link, and says when none is', async () => {
    const { transport } = sidecar();
    const { router } = await open(FILE);
    fireEvent.click(await screen.findByRole('link', { name: '2 URLs in your sitemap or menu but blocked' }));
    await waitFor(() => expect(searchOf(router).conflict).toBe(true));
    await waitFor(() =>
      expect(screen.getByRole('grid', { name: 'Blocked URLs' })).toHaveAttribute('aria-rowcount', '3'),
    );
    expect(blockedRequests(transport).at(-1)).toMatchObject({ origin: SITE, conflict: true });
    expect(screen.getByText('in your sitemap or menu but blocked').closest('.si-chip')).toHaveTextContent(
      'Conflict is in your sitemap or menu but blocked',
    );
    expect(screen.queryByText(`${SITE}/admin/users`)).toBeNull();

    // An origin none of whose blocked URLs anything asks for says so, quietly, and has no link.
    sidecar({
      blocked: { [SITE]: TEST_BLOCKED.map((row) => ({ ...row, inSitemap: false, inMenu: false, conflict: false })) },
    });
    await open(FILE);
    expect(await screen.findByText('None of them is in your sitemap or menu.')).toBeVisible();
  });

  it('says the costs are known once the crawl is analysed, while they aren’t', async () => {
    const unknown = {
      linkingPages: null,
      inSitemap: null,
      sitemaps: null,
      sitemap: null,
      inMenu: null,
      conflict: null,
    };
    sidecar({ blocked: { [SITE]: TEST_BLOCKED.map((row) => ({ ...row, ...unknown })) } });
    await open(FILE);
    const grid = await screen.findByRole('grid', { name: 'Blocked URLs' });
    await waitFor(() => expect(grid).toHaveAttribute('aria-rowcount', '5'));
    for (const column of [2, 3, 4]) {
      expect(rowOf(`${SITE}/admin/`).querySelector(`[aria-colindex="${column}"]`)).toHaveTextContent(
        'Known once the crawl is analysed as it stands.',
      );
    }
    expect(screen.queryByText(/in your sitemap or menu/)).toBeNull();
  });

  it('lists the URLs no line accounts for from their own link, in words that say which case they are', async () => {
    const { transport } = sidecar();
    const { router } = await open(FILE);
    const link = await screen.findByRole('link', {
      name: '1 URL was blocked by a rule that isn’t in this copy of the file',
    });
    fireEvent.click(link);
    await waitFor(() => expect(searchOf(router).reason).toBe('unknown'));
    await waitFor(() =>
      expect(screen.getByRole('grid', { name: 'Blocked URLs' })).toHaveAttribute('aria-rowcount', '2'),
    );
    expect(blockedRequests(transport).at(-1)).toMatchObject({ origin: SITE, reason: 'unknown' });
    expect(screen.getByText('no matching rule').closest('.si-chip')).toHaveTextContent('Reason is no matching rule');
    // What happened, and what to do, under the link.
    expect(screen.getByText(UNKNOWN_ACTION)).toBeVisible();
    expect(screen.getByText(`${SITE}/old-private`)).toBeVisible();
  });

  it('gives the list back when the chip goes, and focus to the title rather than to nothing', async () => {
    sidecar();
    const { router } = await open(`${FILE}?line=6`);
    const remove = await screen.findByRole('button', { name: 'Remove the filter Line is 6: Disallow: /admin/' });
    act(() => remove.focus());
    fireEvent.click(remove);
    await waitFor(() => expect(searchOf(router).line).toBeUndefined());
    await waitFor(() =>
      expect(screen.getByRole('grid', { name: 'Blocked URLs' })).toHaveAttribute('aria-rowcount', '5'),
    );
    await waitFor(() => expect(screen.getByRole('heading', { level: 1 })).toHaveFocus());
  });

  it('leads back to the origins from its own link and the sidebar’s: never to the origin it’s on', async () => {
    sidecar();
    await open(FILE);
    expect(await screen.findByRole('link', { name: 'All origins' })).toHaveAttribute('href', ROBOTS);
    const entry = within(screen.getByRole('navigation', { name: 'Main' })).getByRole('link', { name: /^robots\.txt/ });
    expect(entry).toHaveAttribute('href', ROBOTS);
    expect(entry).toHaveAttribute('aria-current', 'page');
  });

  it('opens the inspector on a blocked URL with Enter, in place', async () => {
    sidecar();
    const { router } = await open(FILE);
    const grid = await screen.findByRole('grid', { name: 'Blocked URLs' });
    await screen.findByText(`${SITE}/admin/users`);
    act(() => grid.focus());
    fireEvent.keyDown(grid, { key: 'ArrowDown' });
    fireEvent.keyDown(grid, { key: 'Enter' });
    await waitFor(() => expect(searchOf(router).inspect).toBe(22));
    expect(pathOf(router)).toBe(FILE);
  });

  it('says a line that blocked nothing did, with the way back to every blocked URL', async () => {
    sidecar();
    const { router } = await open(`${FILE}?line=7`);
    expect(await screen.findByText('Line 7 blocked no URLs.')).toBeVisible();
    fireEvent.click(screen.getByRole('button', { name: 'List every blocked URL' }));
    await waitFor(() => expect(searchOf(router).line).toBeUndefined());
  });

  it('never calls a line that isn’t a rule one, and says so of a line the file doesn’t have', async () => {
    sidecar();
    // Line 2 is "User-agent: *": no rule, so it blocked nothing, and the chip doesn't call it a rule.
    const { router } = await open(`${FILE}?line=2`);
    expect(await screen.findByText('Line 2 isn’t a rule, so it blocked no URLs.')).toBeVisible();
    const blocked = screen.getByRole('region', { name: 'Blocked URLs' });
    await waitFor(() => expect(blocked.querySelector('.si-chip')).toHaveTextContent('Line is 2: User-agent: *'));
    expect(blocked).not.toHaveTextContent(/Rule is/);
    // A line past the file's end (a link from before the site was crawled again).
    act(() => {
      router.history.push(`${FILE}?line=999`);
    });
    expect(await screen.findByText('This file has no line 999.')).toBeVisible();
    await waitFor(() => expect(blocked.querySelector('.si-chip')).toHaveTextContent('Line is 999'));
    expect(screen.queryByText(/Line 999 blocked no URLs/)).toBeNull();
  });

  it('reaches the file’s search with Ctrl+F from anywhere on the page, and from the palette’s command', async () => {
    const uninstall = installKeyboard();
    sidecar();
    await open(FILE);
    const search = await screen.findByRole('searchbox', { name: 'Search the file' });
    const grid = await screen.findByRole('grid', { name: 'Blocked URLs' });
    await waitFor(() => expect(grid).toHaveAttribute('aria-rowcount', '5'));
    const press = (from: HTMLElement) => fireEvent.keyDown(from, { key: 'f', code: 'KeyF', ctrlKey: true });
    // From the blocked URLs, beside the file.
    act(() => grid.focus());
    expect(grid).toHaveFocus();
    press(grid);
    await waitFor(() => expect(search).toHaveFocus());
    // From the page's title, and from nowhere in particular.
    const title = screen.getByRole('heading', { level: 1 });
    act(() => title.focus());
    press(title);
    await waitFor(() => expect(search).toHaveFocus());
    act(() => search.blur());
    press(document.body);
    await waitFor(() => expect(search).toHaveFocus());
    // What was searched for last is selected, to type over.
    fireEvent.change(search, { target: { value: 'admin' } });
    act(() => grid.focus());
    press(grid);
    await waitFor(() => expect(search).toHaveFocus());
    expect([(search as HTMLInputElement).selectionStart, (search as HTMLInputElement).selectionEnd]).toEqual([0, 5]);
    // The palette lists it, and running it from there goes to the field too.
    const command = commands.get('robots.search');
    expect(command).toMatchObject({ title: 'Search the file', group: 'robots.txt', keys: ['mod+f'] });
    act(() => grid.focus());
    act(() => void commands.run('robots.search'));
    await waitFor(() => expect(search).toHaveFocus());
    uninstall();
  });

  it('has no search to go to on an origin with no file to show', async () => {
    sidecar({
      files: { [SITE]: { ...TEST_FILE, state: 'missing', statusCode: 404, bytes: null, lines: [], blocked: 0 } },
      blocked: {},
    });
    await open(FILE);
    await screen.findByRole('heading', { name: 'No file to show' });
    const command = commands.get('robots.search');
    expect(command && commands.isEnabled(command)).toBe(false);
    expect(commands.run('robots.search')).toBe(false);
  });

  it('copies the file as it is from its button', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, 'clipboard', { configurable: true, value: { writeText } });
    sidecar();
    await open(FILE);
    await screen.findByTestId('source-viewer');
    fireEvent.click(screen.getByRole('button', { name: 'Copy the file' }));
    await waitFor(() => expect(writeText).toHaveBeenCalledWith(TEST_FILE.lines.map((line) => line.text).join('\n')));
  });

  it('copies the blocked URLs the list is narrowed to, one per line, by key', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, 'clipboard', { configurable: true, value: { writeText } });
    const uninstall = installKeyboard();
    sidecar();
    await open(`${FILE}?line=6`);
    const grid = await screen.findByRole('grid', { name: 'Blocked URLs' });
    await waitFor(() => expect(grid).toHaveAttribute('aria-rowcount', '4'));
    fireEvent.keyDown(document.body, { key: 'C', code: 'KeyC', ctrlKey: true, shiftKey: true });
    await waitFor(() =>
      expect(writeText).toHaveBeenCalledWith(
        TEST_BLOCKED.filter((row) => row.line === 6)
          .map((row) => row.url)
          .join('\n'),
      ),
    );
    uninstall();
  });
});

describe('robots.txt: an origin without a file to show', () => {
  const file = (origin: (typeof TEST_ORIGINS)[number]): RobotsFile => ({
    ...origin,
    userAgent: 'ShoutingIguana',
    lines: [],
    unmatched: { unreachable: origin.state === 'unreachable' ? origin.blocked : 0, unknown: 0 },
  });
  const refused = [
    {
      id: 31,
      url: 'https://cdn.example.net/a.js',
      reason: 'unreachable' as const,
      line: null,
      rule: null,
      error: '503 Service Unavailable',
    },
    {
      id: 32,
      url: 'https://cdn.example.net/b.js',
      reason: 'unreachable' as const,
      line: null,
      rule: null,
      error: '503 Service Unavailable',
    },
  ];
  const options: SitemapsRobotsOptions = {
    files: Object.fromEntries(TEST_ORIGINS.map((origin) => [origin.origin, file(origin)])),
    blocked: { 'https://cdn.example.net': refused },
  };
  const at = (origin: string) => `${ROBOTS}/${encodeURIComponent(origin)}`;

  it('says an unreachable robots.txt refused the origin’s URLs, and lists them with that reason', async () => {
    sidecar(options);
    await open(at('https://cdn.example.net'));
    expect(
      (
        await screen.findAllByText(
          /robots\.txt couldn’t be read \(503 Service Unavailable\), so the crawl refused this origin’s URLs/,
        )
      ).length,
    ).toBeGreaterThan(0);
    expect(await screen.findByRole('heading', { name: 'No file to show' })).toBeVisible();
    const row = await waitFor(() => rowOf('https://cdn.example.net/a.js'));
    expect(row.querySelector('[aria-colindex="5"]')).toHaveTextContent('Couldn’t be read');
    expect(screen.getByRole('link', { name: '2 URLs were refused while robots.txt couldn’t be read' })).toBeVisible();
    expect(screen.queryByTestId('source-viewer')).toBeNull();
  });

  it('says an unanswered robots.txt refused nothing, in words of its own', async () => {
    sidecar(options);
    await open(at('https://slow.example.io'));
    expect(
      (
        await screen.findAllByText(
          /robots\.txt didn’t answer \(it timed out\)\. The crawl gave no verdict and went ahead/,
        )
      ).length,
    ).toBeGreaterThan(0);
    expect(
      await screen.findByText('robots.txt didn’t answer, and the crawl went ahead: nothing was refused.'),
    ).toBeVisible();
    expect(screen.queryByText(/refused this origin’s URLs/)).toBeNull();
  });

  it('doesn’t say the crawl went ahead and refused nothing beside a URL stored as refused: the answer is the last one', async () => {
    // The garden crawl's origin: no answer, and 1 URL stored as refused that nothing kept accounts for.
    const origin = testOrigin({
      origin: 'https://slow.example.io',
      state: 'unanswered',
      statusCode: null,
      error: null,
      bytes: null,
      blocked: 1,
    });
    const stored = [
      { id: 41, url: 'https://slow.example.io/', reason: 'unknown' as const, line: null, rule: null, error: null },
    ];
    sidecar({
      origins: [TEST_ORIGINS[0]!, origin],
      files: { [origin.origin]: { ...file(origin), unmatched: { unreachable: 0, unknown: 1 } } },
      blocked: { [origin.origin]: stored },
    });
    const { router } = await open(ROBOTS);
    // The list: the state beside "1" doesn't say "crawled anyway".
    const row = await waitFor(() => rowOf(origin.origin));
    expect(row.querySelector('[aria-colindex="2"]')).toHaveTextContent('No answer when last asked');
    expect(row.querySelector('[aria-colindex="3"]')).toHaveTextContent('1');
    // The file's page: the last answer, then where the stored URL comes from.
    await act(() => router.navigate({ to: at(origin.origin) }));
    const page = screen.getByRole('main');
    await waitFor(() =>
      expect(page).toHaveTextContent(
        'When the crawl last asked, robots.txt didn’t answer, so it gave no verdict and went ahead. 1 URL on this origin is stored as refused all the same: it was refused earlier in the crawl, or by an earlier version of Shouting Iguana.',
      ),
    );
    expect(
      await screen.findByRole('link', { name: '1 URL was blocked by a rule that isn’t in this copy of the file' }),
    ).toBeVisible();
    expect(page).not.toHaveTextContent(/this origin’s URLs show their own errors/);
  });

  it('says how robots.txt failed in plain words, with the crawl’s own text in the tooltip and the description only', async () => {
    const recorded = 'TLS handshake failed: Cannot determine the frame size or a corrupted frame was received.';
    const origin = testOrigin({
      origin: 'https://slow.example.io',
      state: 'unanswered',
      statusCode: null,
      error: recorded,
      bytes: null,
    });
    sidecar({ origins: [TEST_ORIGINS[0]!, origin], files: { [origin.origin]: file(origin) }, blocked: {} });
    const { router } = await open(ROBOTS);
    // The list's cell: its tooltip is the sentence in plain words, then what the crawl recorded.
    const cell = within(await waitFor(() => rowOf(origin.origin))).getByText('No answer: crawled anyway');
    expect(cell).toHaveAttribute(
      'title',
      `robots.txt didn’t answer (a secure connection couldn’t be made). The crawl gave no verdict and went ahead: this origin’s URLs show their own errors. As the crawl recorded it: ${recorded}`,
    );
    // The file's page: the sentence in plain words, the crawl's own text behind it.
    await act(() => router.navigate({ to: at(origin.origin) }));
    const sentence = await screen.findByText(/^robots\.txt didn’t answer \(a secure connection couldn’t be made\)\./);
    expect(sentence).toHaveAttribute('title', recorded);
    expect(sentence).toHaveAccessibleDescription(recorded);
    expect(screen.getByRole('main')).not.toHaveTextContent(/TLS handshake|frame size/);
  });

  it('says a missing robots.txt refuses nothing', async () => {
    sidecar(options);
    await open(at('https://partner.example.org'));
    expect(
      (
        await screen.findAllByText(
          /There’s no robots\.txt here \(404 Not Found\), so nothing on this origin is refused\./,
        )
      ).length,
    ).toBeGreaterThan(0);
    expect(await screen.findByText('There’s no robots.txt here, so nothing is refused.')).toBeVisible();
  });

  it('says an empty file is empty', async () => {
    sidecar({ files: { [SITE]: { ...file(TEST_ORIGINS[0]!), bytes: 0, blocked: 0 } }, blocked: {} });
    await open(FILE);
    expect(await screen.findByRole('heading', { name: 'The file is empty' })).toBeVisible();
    expect(await screen.findByText('The file is empty, so it refuses nothing.')).toBeVisible();
  });

  it('opens an origin spelled another way (capitals, a slash after it) as the origin the crawl has', async () => {
    const { transport } = sidecar();
    await open(`${ROBOTS}/${encodeURIComponent('HTTPS://EXAMPLE.com/')}`);
    expect(await screen.findByTestId('source-viewer')).toBeVisible();
    expect(screen.queryByRole('heading', { name: 'This crawl has no robots.txt for that origin' })).toBeNull();
    // The engine is asked for the origin as it spells it, by the file and by the list beside it.
    const asked = transport.requests
      .filter((request) => /\/robots\/(file|blocked)$/.test(request.path))
      .map((request) => (request.body as { origin: string }).origin);
    expect(asked.length).toBeGreaterThanOrEqual(2);
    expect(new Set(asked)).toEqual(new Set([SITE]));
    expect(await screen.findByText(`${SITE}/admin/users`)).toBeVisible();
  });

  it('says an origin the crawl has no robots.txt for isn’t there, and offers the list', async () => {
    sidecar();
    const { router } = await open(at('https://nowhere.example'));
    expect(await screen.findByRole('heading', { name: 'This crawl has no robots.txt for that origin' })).toBeVisible();
    fireEvent.click(screen.getByRole('button', { name: 'Show every origin' }));
    await waitFor(() => expect(pathOf(router)).toBe(ROBOTS));
    expect(await screen.findByRole('grid', { name: 'Origins' })).toBeVisible();
  });

  it('says “not so far” of an origin a crawl that hasn’t ended hasn’t asked, and fills in once it does', async () => {
    const running = { status: 'running', finishedAt: null } as const;
    const late = 'https://late.example';
    sidecar({ item: running });
    const { queryClient } = await open(at(late));
    expect(
      await screen.findByRole('heading', { name: 'The crawl hasn’t asked that origin for a robots.txt so far' }),
    ).toBeVisible();
    expect(screen.getByText(/It asks each origin when it first comes to a URL on it\./)).toBeVisible();
    // Never "from before the site was crawled again": the crawl is still at it.
    expect(screen.queryByText(/crawled again/)).toBeNull();
    // The crawl comes to a URL on it. The once-a-second refresh of the crawl's lists leaves an origin's file out,
    // so the page looks for it itself, every two seconds: nothing else tells it, and the page is the origin's.
    const { transport } = sidecar({
      item: running,
      files: { [late]: { ...TEST_FILE, origin: late, isInternal: false } },
    });
    await act(() => queryClient.invalidateQueries(liveGridPagesOf(TEST_CRAWL_ID), { cancelRefetch: false }));
    expect(transport.requests.filter((request) => request.path.endsWith('/robots/file'))).toHaveLength(0);
    expect(await screen.findByTestId('source-viewer', {}, { timeout: 5_000 })).toBeVisible();
    expect(screen.queryByRole('heading', { name: /hasn’t asked that origin/ })).toBeNull();
  }, 10_000);

  it('says a crawl that has ended has no robots.txt for an origin it never asked', async () => {
    sidecar();
    await open(at('https://nowhere.example'));
    expect(await screen.findByRole('heading', { name: 'This crawl has no robots.txt for that origin' })).toBeVisible();
    expect(screen.getByText(/It may be from before the site was crawled again\./)).toBeVisible();
  });

  it('offers, on an origin the crawl has no robots.txt for, only the command that can run: the way back', async () => {
    sidecar();
    const { router } = await open(`${at('https://nowhere.example')}?line=3`);
    await screen.findByRole('heading', { name: 'This crawl has no robots.txt for that origin' });
    const mine = commands.list().filter((command) => command.group === 'robots.txt');
    expect(mine.length).toBeGreaterThan(3);
    // No file to copy, open or list the URLs of: each is greyed in the palette, and its key does nothing.
    expect(mine.filter((command) => commands.isEnabled(command)).map((command) => command.id)).toEqual(['robots.list']);
    expect(commands.run('robots.open')).toBe(false);
    expect(commands.run('robots.allBlocked')).toBe(false);
    expect(searchOf(router).line).toBe(3);
    // On an origin the crawl has, they run.
    act(() => {
      router.history.push(`${FILE}?line=6`);
    });
    await screen.findByTestId('source-viewer');
    expect(commands.isEnabled(commands.get('robots.open')!)).toBe(true);
    expect(commands.isEnabled(commands.get('robots.allBlocked')!)).toBe(true);
  });

  it('keeps the blocked URLs when the file can’t be read, and the file when they can’t', async () => {
    const failing = problem({ type: 'internal', title: 'Something went wrong in the engine.', status: 500 });
    sidecar({ files: { [SITE]: failing } });
    await open(FILE, createQueryClientWithoutBackoff());
    expect(await screen.findByText('The file couldn’t be read.')).toBeVisible();
    expect(await screen.findByText(`${SITE}/admin/users`)).toBeVisible();
    setTransport(fakeSidecar(sitemapsRobotsRoutes({ blocked: { [SITE]: failing } })));
    fireEvent.click(
      within(screen.getByRole('region', { name: 'The file' })).getByRole('button', { name: 'Try again' }),
    );
    expect(await screen.findByTestId('source-viewer')).toBeVisible();
  });

  it('says at the top of an origin’s file, too, that the crawl ignored robots.txt', async () => {
    sidecar({ respected: false });
    await open(FILE);
    expect(await screen.findByText(/told to ignore robots\.txt, so nothing was blocked/)).toBeVisible();
  });

  it('never says a crawl told to ignore robots.txt obeyed the file: not above it, in it, or of its empty list', async () => {
    sidecar({ respected: false, files: { [SITE]: { ...TEST_FILE, blocked: 0 } }, blocked: {} });
    await open(FILE);
    expect(await screen.findByText(/told to ignore robots\.txt, so nothing was blocked/)).toBeVisible();
    await screen.findByTestId('source-viewer');
    const page = screen.getByRole('main');
    expect(page).toHaveTextContent('The crawl read this file, and was told to ignore its rules.');
    expect(page).toHaveTextContent(
      'The crawl was told to ignore robots.txt. As ShoutingIguana, it would have obeyed lines 5 to 9.',
    );
    expect(await screen.findByText('This crawl was told to ignore robots.txt, so nothing was refused.')).toBeVisible();
    expect(page).not.toHaveTextContent(/obeyed its rules/);
    expect(page).not.toHaveTextContent(/the crawl obeyed/);
    expect(page).not.toHaveTextContent(/rules it obeyed/);
  });

  it('holds what the file meant to the crawl until it’s known whether the crawl obeyed robots.txt at all', async () => {
    // The file answers at once; whether the crawl obeyed robots.txt (the origins' read) takes longer.
    let release: () => void = () => undefined;
    const held = new Promise<void>((resolve) => (release = resolve));
    const routes = sitemapsRobotsRoutes({ respected: false });
    setTransport(
      fakeSidecar([
        [
          'POST',
          /^\/api\/crawls\/[0-9a-f]{16}\/robots$/,
          async (request) => {
            await held;
            const route = routes.find(([method, path]) => method === 'POST' && path.test(request.path));
            return route ? route[2](request, [] as unknown as RegExpMatchArray) : json({});
          },
        ],
        ...routes,
      ]),
    );
    await open(FILE);
    await screen.findByText(`${SITE}/admin/users`);
    // Nothing said yet of what the file meant: neither "obeyed" nor "ignored".
    expect(screen.getByRole('main')).not.toHaveTextContent(/obeyed|ignore/);
    expect(screen.queryByTestId('source-viewer')).toBeNull();
    await act(async () => {
      release();
      await held;
    });
    await waitFor(() =>
      expect(screen.getByRole('main')).toHaveTextContent('The crawl read this file, and was told to ignore its rules.'),
    );
    expect(await screen.findByTestId('source-viewer')).toBeVisible();
    expect(screen.getByRole('main')).not.toHaveTextContent(/obeyed its rules/);
  });

  it('says no more than holds either way when whether the crawl obeyed robots.txt can’t be read', async () => {
    const routes = sitemapsRobotsRoutes();
    setTransport(
      fakeSidecar([
        [
          'POST',
          /^\/api\/crawls\/[0-9a-f]{16}\/robots$/,
          () => problem({ type: 'internal', title: 'Something went wrong in the engine.', status: 500 }),
        ],
        ...routes,
      ]),
    );
    await open(FILE, createQueryClientWithoutBackoff());
    await screen.findByTestId('source-viewer');
    const page = screen.getByRole('main');
    await waitFor(() => expect(page).toHaveTextContent('The crawl read this file. Read'));
    expect(page).toHaveTextContent('The rules for ShoutingIguana are lines 5 to 9.');
    expect(page).not.toHaveTextContent(/obeyed|ignore/);
  });
});

describe('robots.txt: live and refreshing', () => {
  it('reads the file once while a crawl runs, not each second with the list, and again when what it blocked changes', async () => {
    const running = { status: 'running', finishedAt: null } as const;
    const { transport } = sidecar({ item: running });
    const reads = (path: string, from: { requests: TransportRequest[] } = transport) =>
      from.requests.filter((request) => request.path.endsWith(path)).length;
    const { queryClient } = await open(FILE);
    await screen.findByText(`${SITE}/old-private`);
    await screen.findByTestId('source-viewer');
    expect(reads('/robots/file')).toBe(1);
    // The crawl's tick, as the live bridge makes it each second: the list is read again, the file isn't.
    const listed = reads('/robots/blocked');
    for (let tick = 0; tick < 3; tick++) {
      await act(() => queryClient.invalidateQueries(liveGridPagesOf(TEST_CRAWL_ID), { cancelRefetch: false }));
    }
    await waitFor(() => expect(reads('/robots/blocked')).toBeGreaterThan(listed));
    expect(reads('/robots/file')).toBe(1);
    // The crawl is kept from another URL on this origin: the list's count of them moves, so the file's lines'
    // counts have too, and it's read again for them, once.
    const more = [
      ...TEST_BLOCKED,
      { id: 25, url: `${SITE}/admin/new`, reason: 'rule' as const, line: 6, rule: 'Disallow: /admin/', error: null },
    ];
    const grown = sidecar({
      item: running,
      files: {
        [SITE]: {
          ...TEST_FILE,
          blocked: 5,
          lines: TEST_FILE.lines.map((line) => (line.number === 6 ? { ...line, blocked: 4 } : line)),
        },
      },
      blocked: { [SITE]: more },
    }).transport;
    await act(() => queryClient.invalidateQueries(liveGridPagesOf(TEST_CRAWL_ID), { cancelRefetch: false }));
    expect(await screen.findByRole('button', { name: 'line 6: Blocked 4 URLs' })).toBeVisible();
    expect(reads('/robots/file', grown)).toBe(1);
    // And not again while nothing more changes.
    await act(() => queryClient.invalidateQueries(liveGridPagesOf(TEST_CRAWL_ID), { cancelRefetch: false }));
    expect(reads('/robots/file', grown)).toBe(1);
  });

  it('keeps the file and the list on screen while they’re read again', async () => {
    let hold: (() => void) | null = null;
    let calls = 0;
    setTransport(
      fakeSidecar([
        [
          'POST',
          /\/robots\/blocked$/,
          async (request) => {
            calls++;
            if (calls > 1) await new Promise<void>((resolve) => (hold = resolve));
            return json(
              blockedPage(request.body as RobotsBlockedQuery, calls > 1 ? TEST_BLOCKED.slice(0, 2) : TEST_BLOCKED),
            );
          },
        ],
        ...sitemapsRobotsRoutes({ item: { status: 'running', finishedAt: null } }),
      ]),
    );
    const { queryClient } = await open(FILE);
    await screen.findByText(`${SITE}/old-private`);
    expect(screen.getByText('Live')).toBeVisible();
    act(() => void queryClient.invalidateQueries(gridPagesOf(TEST_CRAWL_ID)));
    await waitFor(() => expect(hold).not.toBeNull());
    expect(screen.getByText(`${SITE}/old-private`)).toBeVisible();
    expect(screen.getByTestId('source-viewer')).toBeVisible();
    expect(screen.queryByLabelText('Loading the file')).toBeNull();
    await act(async () => {
      hold?.();
      await Promise.resolve();
    });
    await waitFor(() => expect(screen.queryByText(`${SITE}/old-private`)).toBeNull());
  });

  it('doesn’t look for an origin’s file while the window is hidden, and does once it’s shown again', async () => {
    const running = { status: 'running', finishedAt: null } as const;
    const late = 'https://late.example';
    sidecar({ item: running });
    await open(`${ROBOTS}/${encodeURIComponent(late)}`);
    expect(
      await screen.findByRole('heading', { name: 'The crawl hasn’t asked that origin for a robots.txt so far' }),
    ).toBeVisible();
    // The window goes to the tray; the crawl comes to a URL on the origin meanwhile.
    let hidden = true;
    vi.spyOn(document, 'hidden', 'get').mockImplementation(() => hidden);
    vi.spyOn(document, 'visibilityState', 'get').mockImplementation(() => (hidden ? 'hidden' : 'visible'));
    document.dispatchEvent(new Event('visibilitychange'));
    const { transport } = sidecar({
      item: running,
      files: { [late]: { ...TEST_FILE, origin: late, isInternal: false } },
    });
    const looks = () => transport.requests.filter((request) => request.path.endsWith('/robots/file')).length;
    // Longer than the two seconds between its looks: hidden, it makes none.
    await act(() => new Promise((resolve) => setTimeout(resolve, 2_600)));
    expect(looks()).toBe(0);
    expect(screen.getByRole('heading', { name: /hasn’t asked that origin/ })).toBeVisible();
    // Shown again, the next look finds the file.
    hidden = false;
    document.dispatchEvent(new Event('visibilitychange'));
    expect(await screen.findByTestId('source-viewer', {}, { timeout: 5_000 })).toBeVisible();
    expect(looks()).toBeGreaterThan(0);
  }, 15_000);
});

describe('robots.txt in a short or narrow window', () => {
  /** jsdom lays nothing out: the width a frame measures itself at, as a real window would give it. */
  const frameWidth = (width: number) => vi.spyOn(HTMLElement.prototype, 'clientWidth', 'get').mockReturnValue(width);
  const headersOf = (grid: HTMLElement) =>
    within(grid)
      .getAllByRole('columnheader')
      .map((header) => header.textContent);
  const many = Array.from({ length: 9 }, (_unused, index) =>
    testOrigin({ origin: `https://host-${index}.example`, isInternal: index === 0, blocked: 9 - index }),
  );

  it('keeps a long list of origins room for five rows, in the frame every grid screen shares', async () => {
    sidecar({ origins: many });
    await open(ROBOTS);
    const grid = await screen.findByRole('grid', { name: 'Origins' });
    await waitFor(() => expect(grid).toHaveAttribute('aria-rowcount', '10'));
    expect(grid.parentElement!.style.minHeight).toBe(`${32 + 5 * 32 + 12}px`);
    const frame = grid.closest('.si-grid-screen')!;
    expect(frame).not.toBeNull();
    expect(screen.getByRole('heading', { name: 'robots.txt', level: 1 }).closest('.si-grid-screen > *')).not.toBeNull();
    expect(grid.parentElement!.parentElement).toHaveClass('shrink');
    expect(grid.parentElement!.parentElement!.parentElement).toBe(frame);
  });

  it('never squeezes a list of five origins or fewer: it stays as tall as its rows', async () => {
    // Four origins.
    sidecar();
    await open(ROBOTS);
    const grid = await screen.findByRole('grid', { name: 'Origins' });
    await waitFor(() => expect(grid).toHaveAttribute('aria-rowcount', '5'));
    const list = grid.parentElement!.parentElement!;
    expect(list).toHaveClass('shrink-0');
    expect(list.style.height).toBe(`${4 * 32 + 32 + 2}px`);
    expect(grid.parentElement!.style.minHeight).toBe('');
  });

  it('puts what each origin blocked straight after it in a frame too narrow for every column', async () => {
    // 200% zoom of a 1280 px window leaves the list 534 px.
    frameWidth(534);
    sidecar();
    await open(ROBOTS);
    const grid = await screen.findByRole('grid', { name: 'Origins' });
    await waitFor(() => expect(grid).toHaveAttribute('aria-rowcount', '5'));
    expect(headersOf(grid)).toEqual(['Origin', 'Blocked URLs', 'robots.txt', 'Read', 'Size']);
    expect(within(rowOf(SITE)).getByRole('link', { name: '4 URLs blocked' })).toBeVisible();
    expect(rowOf(SITE).querySelector('[aria-colindex="2"]')).toHaveTextContent('4');
    expect(rowOf(SITE).querySelector('[aria-colindex="3"]')).toHaveTextContent('Read');
  });

  it('keeps what an origin’s answer meant before what it blocked where every column fits', async () => {
    frameWidth(1_006);
    sidecar();
    await open(ROBOTS);
    const grid = await screen.findByRole('grid', { name: 'Origins' });
    expect(headersOf(grid)).toEqual(['Origin', 'robots.txt', 'Blocked URLs', 'Read', 'Size']);
  });

  it('keeps an origin’s blocked URLs room for five rows beside its file, in the frame every grid screen shares', async () => {
    sidecar();
    await open(FILE);
    const grid = await screen.findByRole('grid', { name: 'Blocked URLs' });
    await screen.findByText(`${SITE}/old-private`);
    // The header, five rows, the frame, and the footer's key hints (--grid-foot-height).
    expect(grid.parentElement!.style.minHeight).toBe(`${32 + 5 * 32 + 12 + 36}px`);
    const frame = grid.closest('.si-grid-screen')!;
    expect(frame).not.toBeNull();
    // The file and the blocked URLs are the frame's body; the title and what the answer meant are over it.
    const body = grid.closest('.si-grid-screen__body')!;
    expect(body.parentElement).toBe(frame);
    expect(body).toContainElement(screen.getByTestId('source-viewer'));
    expect(body).not.toContainElement(screen.getByRole('heading', { level: 1 }));
  });
});
