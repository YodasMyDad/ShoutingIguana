import { createMemoryHistory } from '@tanstack/react-router';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { RobotsBlockedQuery, RobotsFile } from '../api/grids';
import { gridPagesOf } from '../api/queries';
import { setTransport, type TransportRequest } from '../api/transport';
import { App } from '../app/App';
import { createQueryClient } from '../app/queryClient';
import { createAppRouter } from '../app/router';
import { onAnnounce } from '../feedback/announce';
import type { SourceViewerProps } from '../inspector/SourceViewer';
import { installKeyboard } from '../keyboard/registry';
import { TEST_CRAWL_ID, fakeSidecar, json, problem } from '../test/fakeSidecar';
import { resetPlaces } from './listPlace';
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
    expect(within(rowOf('https://partner.example.org')).queryByRole('link')).toBeNull();
    expect(rowOf('https://partner.example.org').querySelector('[aria-colindex="3"]')).toHaveTextContent('0');
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
    const why = (url: string) => rowOf(url).querySelector('[aria-colindex="2"]');
    expect(why(`${SITE}/admin/users`)).toHaveTextContent('Line 6: Disallow: /admin/');
    expect(why(`${SITE}/old-private`)).toHaveTextContent('No rule kept');
    expect(within(rowOf(`${SITE}/old-private`)).getByText('No rule kept')).toHaveAttribute(
      'title',
      expect.stringContaining('doesn’t refuse it'),
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
    const chip = screen.getByText('line 6: Disallow: /admin/').closest('.si-chip')!;
    expect(chip).toHaveTextContent('Rule is line 6: Disallow: /admin/');
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

  it('lists the URLs no line accounts for from their own link, in words that say which case they are', async () => {
    const { transport } = sidecar();
    const { router } = await open(FILE);
    const link = await screen.findByRole('link', {
      name: '1 URL is stored as refused, though nothing kept here refuses it',
    });
    fireEvent.click(link);
    await waitFor(() => expect(searchOf(router).reason).toBe('unknown'));
    await waitFor(() =>
      expect(screen.getByRole('grid', { name: 'Blocked URLs' })).toHaveAttribute('aria-rowcount', '2'),
    );
    expect(blockedRequests(transport).at(-1)).toMatchObject({ origin: SITE, reason: 'unknown' });
    expect(screen.getByText('no rule kept').closest('.si-chip')).toHaveTextContent('Reason is no rule kept');
    expect(screen.getByText(`${SITE}/old-private`)).toBeVisible();
  });

  it('gives the list back when the chip goes, and focus to the title rather than to nothing', async () => {
    sidecar();
    const { router } = await open(`${FILE}?line=6`);
    const remove = await screen.findByRole('button', { name: 'Remove the filter Rule is line 6: Disallow: /admin/' });
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
      error: 'HTTP 503',
    },
    {
      id: 32,
      url: 'https://cdn.example.net/b.js',
      reason: 'unreachable' as const,
      line: null,
      rule: null,
      error: 'HTTP 503',
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
      (await screen.findAllByText(/robots\.txt couldn’t be read \(HTTP 503\), so the crawl refused this origin’s URLs/))
        .length,
    ).toBeGreaterThan(0);
    expect(await screen.findByRole('heading', { name: 'No file to show' })).toBeVisible();
    const row = await waitFor(() => rowOf('https://cdn.example.net/a.js'));
    expect(row.querySelector('[aria-colindex="2"]')).toHaveTextContent('robots.txt couldn’t be read');
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
      await screen.findByRole('link', { name: '1 URL is stored as refused, though nothing kept here refuses it' }),
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
      (await screen.findAllByText(/There’s no robots\.txt here \(HTTP 404\), so nothing on this origin is refused\./))
        .length,
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
});
