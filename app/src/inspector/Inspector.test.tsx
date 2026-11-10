// The inspector in the app, over a sidecar of the test's own: its sections, its states, its keys, and walking from
// URL to URL with back, forward and Esc (docs/v2-ui-ia.md, flow 4).
import { createMemoryHistory, type RouterHistory } from '@tanstack/react-router';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { UrlDetail } from '../api/inspector';
import { queryKeys } from '../api/queries';
import { setTransport } from '../api/transport';
import { App } from '../app/App';
import { createQueryClient } from '../app/queryClient';
import { createAppRouter } from '../app/router';
import { commands, installKeyboard } from '../keyboard/registry';
import { liveStore } from '../live/liveStore';
import { inspectFromGrid } from '../routing/inspector';
import { DEFAULT_SETTINGS } from '../settings/schema';
import { getSettings, updateSettings } from '../settings/settingsStore';
import { TEST_CRAWL_ID, fakeSidecar, type FakeSidecar } from '../test/fakeSidecar';
import { ISSUES_CHECKS, issuesRoutes, type IssuesSidecarOptions } from '../test/issuesSidecar';
import {
  inspectorRoutes,
  testLinkRow,
  testResponse,
  testUrlDetail,
  type InspectorSidecarOptions,
} from '../test/inspectorSidecar';
import { resetLinkKinds, resetLinkRows } from './links';
import { resetRememberedSection } from './sections';

// CodeMirror needs a browser's layout: the viewer itself is covered end to end. Here it's a stand-in that shows what
// it was given.
vi.mock('./SourceViewer', () => ({
  default: (props: {
    html: string;
    query: string;
    current: number;
    wrap: boolean;
    onMatches: (n: number, c: boolean) => void;
  }) => (
    <div
      data-testid="source-viewer"
      data-query={props.query}
      data-current={props.current}
      data-wrap={String(props.wrap)}
    >
      <button type="button" onClick={() => props.onMatches(3, false)}>
        report three matches
      </button>
      {props.html}
    </div>
  ),
}));

const URLS = `/crawls/${TEST_CRAWL_ID}/urls`;
const HOME = testUrlDetail({ id: 1, url: 'https://example.com/', issues: [] });
const PAGE = testUrlDetail({
  issues: [
    { checkId: 'title-too-long', severity: 'notice', detail: '61 characters', related: null },
    {
      checkId: 'empty-anchor-text',
      severity: 'notice',
      detail: 'Links to 1 URL',
      related: { id: 1, url: 'https://example.com/' },
    },
  ],
});
const OTHER = testUrlDetail({ id: 3, url: 'https://example.com/b', issues: [] });

const SITE: InspectorSidecarOptions = {
  urls: { 1: HOME, 2: PAGE, 3: OTHER },
  inlinks: {
    2: [
      testLinkRow({ id: 1, url: 'https://example.com/', text: 'Patio set' }),
      testLinkRow({ id: 3, url: 'https://example.com/b', text: null, kind: 'image', followed: null }),
    ],
    1: [testLinkRow({ id: 3, url: 'https://example.com/b', text: 'Home' })],
  },
  outlinks: { 1: [testLinkRow({ id: 2, url: PAGE.url, text: 'Patio set', position: 'content' })] },
  lookup: { '/b': { id: 3, url: 'https://example.com/b', requested: null } },
};

let sidecar: FakeSidecar;
let uninstall: () => void = () => undefined;

beforeEach(() => {
  uninstall = installKeyboard();
  resetRememberedSection();
  resetLinkKinds();
  resetLinkRows();
  liveStore.reset();
  updateSettings({ ...DEFAULT_SETTINGS });
});

afterEach(() => {
  uninstall();
  setTransport(fakeSidecar([]));
});

async function open(
  path: string,
  options: InspectorSidecarOptions = SITE,
  history?: RouterHistory,
  issues: IssuesSidecarOptions = {},
) {
  sidecar = fakeSidecar([...inspectorRoutes(options), ...issuesRoutes(issues)]);
  setTransport(sidecar);
  const queryClient = createQueryClient();
  const router = createAppRouter(queryClient, history ?? createMemoryHistory({ initialEntries: [path] }));
  render(<App router={router} queryClient={queryClient} />);
  await act(() => router.load());
  const inspector = await screen.findByRole('complementary', { name: 'Inspector' });
  return { router, queryClient, inspector, user: userEvent.setup() };
}

const inspected = (router: { state: { location: { search: unknown } } }) =>
  (router.state.location.search as { inspect?: number }).inspect;

/** The group a link to Issues opens, from its address. */
const groupOf = (link: HTMLElement): unknown => {
  const group = new URL(link.getAttribute('href') ?? '', 'http://app').searchParams.get('group');
  return group === null ? null : JSON.parse(group);
};

const showTab = async (user: ReturnType<typeof userEvent.setup>, name: RegExp) => {
  await user.click(screen.getByRole('tab', { name }));
  return screen.findByRole('tabpanel', { name });
};

describe('the summary', () => {
  it('names the URL, how it answered, and whether it can be indexed, with the four tests', async () => {
    const { inspector } = await open(`${URLS}?inspect=2`);
    expect(await within(inspector).findByRole('heading', { level: 2, name: PAGE.url })).toBeVisible();
    expect(within(inspector).getByText('Page, depth 2')).toBeVisible();
    expect(within(inspector).getByRole('heading', { name: 'Indexable' })).toBeVisible();
    const tests = within(inspector).getByRole('list', { name: 'Indexability tests' });
    expect(
      within(tests)
        .getAllByRole('listitem')
        .map((item) => item.textContent),
    ).toEqual([
      'Answered 2xx: Passed.Answered 200 OK',
      'Allowed by robots.txt: Passed.',
      'No noindex: Passed.',
      'Canonical is itself: Passed.',
    ]);
  });

  it('draws the title’s length on the analysis’s range, and names the check it trips', async () => {
    const { inspector } = await open(`${URLS}?inspect=2`);
    const meter = await within(inspector).findByRole('meter', { name: 'Title length' });
    expect(meter).toHaveAttribute('aria-valuetext', '61 characters, 30 to 60 characters, over the limit');
    expect(within(inspector).getByText('1 over the 60 limit')).toBeVisible();
    // The check's title comes from the catalog, and leads to it on Issues.
    const links = within(inspector).getAllByRole('link', { name: 'Title too long' });
    expect(links[0]).toHaveAttribute('href', `/crawls/${TEST_CRAWL_ID}/issues/title-too-long`);
    expect(within(inspector).getByRole('meter', { name: 'Response time' })).toHaveAttribute(
      'aria-valuetext',
      '117 ms, Limit 1 s, within the range',
    );
  });

  it('doesn’t name a check the analysis didn’t flag the URL with, only how far out the value is', async () => {
    const quiet = testUrlDetail({ issues: [] });
    const { inspector } = await open(`${URLS}?inspect=2`, { urls: { 2: quiet } });
    expect(await within(inspector).findByText('1 over the 60 limit')).toBeVisible();
    expect(within(inspector).queryByRole('link', { name: 'Title too long' })).toBeNull();
    expect(within(inspector).getByRole('heading', { name: 'No issues' })).toBeVisible();
  });

  it('marks the test that failed, and walks to where the canonical points', async () => {
    const canonicalised = testUrlDetail({
      indexability: {
        indexable: false,
        reason: 'canonicalised',
        judgedBy: 'analysis',
        tests: [
          { test: 'answered', outcome: 'pass', statusCode: 200 },
          { test: 'robotsTxt', outcome: 'pass' },
          { test: 'noindex', outcome: 'pass', meta: false, header: false },
          {
            test: 'canonical',
            outcome: 'fail',
            canonical: { id: 1, url: HOME.url, statusCode: 200 },
            canonicalUrl: HOME.url,
          },
        ],
      },
    });
    const { inspector, router, user } = await open(`${URLS}?inspect=2`, { urls: { 1: HOME, 2: canonicalised } });
    expect(await within(inspector).findByRole('heading', { name: 'Not indexable' })).toBeVisible();
    const failed = within(within(inspector).getByRole('list', { name: 'Indexability tests' })).getAllByRole(
      'listitem',
    )[3]!;
    expect(failed).toHaveTextContent('Canonical is itself: Failed.');
    expect(failed).toHaveTextContent('Its canonical is another URL');
    await user.click(within(failed).getByRole('button', { name: HOME.url }));
    await waitFor(() => expect(inspected(router)).toBe(1));
    expect(await within(inspector).findByRole('heading', { level: 2, name: HOME.url })).toBeVisible();
  });

  it('links each issue to its check on Issues, with the group it’s in', async () => {
    const { inspector } = await open(`${URLS}?inspect=2`);
    const issues = await within(inspector).findByRole('list', { name: 'This URL’s issues' });
    const anchor = within(issues).getByRole('link', { name: 'Internal links without anchor text' });
    expect(anchor.getAttribute('href')).toContain(`/crawls/${TEST_CRAWL_ID}/issues/empty-anchor-text?group=`);
    expect(groupOf(anchor)).toEqual({ detail: null, relatedUrlId: 1 });
    expect(within(issues).getByText('Links to 1 URL')).toBeVisible();
  });

  it('opens a duplicate’s cluster from its first page: the group named after the page itself', async () => {
    const first = testUrlDetail({
      issues: [
        {
          checkId: 'title-duplicate',
          severity: 'warning',
          detail: '1 other page',
          related: { id: 3, url: OTHER.url },
        },
      ],
      namedBy: { total: 1, checks: [{ checkId: 'title-duplicate', count: 1 }] },
    });
    const duplicate = { ...ISSUES_CHECKS.checks[2]!, id: 'title-duplicate', title: 'Duplicate title' };
    const { inspector } = await open(`${URLS}?inspect=2`, { urls: { 2: first, 3: OTHER } }, undefined, {
      checks: { ...ISSUES_CHECKS, checks: [...ISSUES_CHECKS.checks, duplicate] },
    });
    const issues = await within(inspector).findByRole('list', { name: 'This URL’s issues' });
    const link = await within(issues).findByRole('link', { name: 'Duplicate title' });
    expect(groupOf(link)).toEqual({ detail: null, relatedUrlId: 2 });
  });

  it('gives the Issues section the focus “Open Issues” loses when it shows it', async () => {
    const { inspector, user } = await open(`${URLS}?inspect=2`);
    const button = await within(inspector).findByRole('button', { name: 'Open Issues' });
    act(() => button.focus());
    await user.keyboard('{Enter}');
    const panel = await within(inspector).findByRole('tabpanel', { name: /^Issues/ });
    await waitFor(() => expect(panel).toHaveFocus());
  });

  it('uses the group the engine gives with an issue', async () => {
    const grouped = testUrlDetail({
      issues: [
        {
          checkId: 'internal-client-error',
          severity: 'error',
          detail: 'its own detail',
          related: null,
          group: { detail: 'HTTP 404', relatedUrlId: null },
        },
      ],
    });
    const { inspector } = await open(`${URLS}?inspect=2`, { urls: { 2: grouped } });
    const link = await within(inspector).findByRole('link', { name: 'Internal URL returns 4xx' });
    expect(link.getAttribute('href')).toContain(`/crawls/${TEST_CRAWL_ID}/issues/internal-client-error?group=`);
    expect(groupOf(link)).toEqual({ detail: 'HTTP 404', relatedUrlId: null });
  });
});

describe('a URL the crawl didn’t read', () => {
  const state = async (detail: UrlDetail) => {
    const { inspector } = await open(`${URLS}?inspect=2`, { urls: { 2: detail } });
    await within(inspector).findByRole('heading', { level: 2, name: detail.url });
    return inspector;
  };
  const notFetched = (discovery: Partial<UrlDetail['discovery']>) =>
    testUrlDetail({
      response: null,
      content: null,
      headers: [],
      structuredData: [],
      issues: [],
      indexability: {
        indexable: null,
        reason: null,
        judgedBy: 'crawl',
        tests: (['answered', 'robotsTxt', 'noindex', 'canonical'] as const).map((test) => ({
          test,
          outcome: 'notChecked',
        })),
      },
      discovery: { ...testUrlDetail().discovery, ...discovery },
    });

  it('says a crawl that stopped never reached a URL still queued, in every section that has nothing to show', async () => {
    const inspector = await state(notFetched({ state: 'queued' }));
    expect(within(inspector).getByText('Not fetched')).toBeVisible();
    expect(within(inspector).getByText('The crawl stopped before it reached this URL.')).toBeVisible();
    // Nothing is on its way: the crawl isn't running.
    expect(within(inspector).queryByText(/It’ll show here once the crawl has fetched it/)).toBeNull();
    expect(within(inspector).queryByText(/It’s in the queue/)).toBeNull();
    expect(within(inspector).getByRole('heading', { name: 'Not judged' })).toBeVisible();
    // Nothing it doesn't have is shown as 0.
    expect(within(inspector).queryByText('Words')).toBeNull();
    const user = userEvent.setup();
    for (const section of [/^Headers/, /^Structured data/, /^Hreflang/]) {
      const panel = await showTab(user, section);
      expect(within(panel).getByText('The crawl stopped before it reached this URL.')).toBeVisible();
    }
  });

  it('says why a skipped URL wasn’t fetched', async () => {
    const inspector = await state(notFetched({ state: 'skipped', notFetchedBecause: 'depthLimit' }));
    expect(within(inspector).getByText('Not fetched')).toBeVisible();
    expect(within(inspector).getByText('It’s deeper than the crawl’s depth limit.')).toBeVisible();
    // Its Redirects says the same, never that it "answers for itself": it never answered.
    const panel = await showTab(userEvent.setup(), /^Redirects/);
    expect(within(panel).getByRole('heading', { name: 'Not fetched' })).toBeVisible();
    expect(
      within(panel).getByText(
        'It’s deeper than the crawl’s depth limit. So the crawl doesn’t know whether it redirects.',
      ),
    ).toBeVisible();
    expect(within(panel).queryByText(/answers for itself/)).toBeNull();
  });

  it('says robots.txt blocked a URL the engine marks fetched with no status', async () => {
    const inspector = await state(
      testUrlDetail({
        response: testResponse({ fetched: true, outcome: 'robotsDisallowed', statusCode: null }),
        content: null,
        headers: [],
        structuredData: [],
      }),
    );
    expect(within(inspector).getByText('Blocked by robots.txt')).toBeVisible();
    expect(within(inspector).getByText('robots.txt disallows it, so the crawl didn’t request it.')).toBeVisible();
    expect(within(inspector).queryByText('Timings and size')).toBeNull();
  });

  it('says a page answered with no body, and still shows how it answered', async () => {
    const inspector = await state(
      testUrlDetail({ response: testResponse({ bodyBytes: 0, sizeBytes: 0 }), content: null, structuredData: [] }),
    );
    expect(within(inspector).getByText('Answered with no body')).toBeVisible();
    expect(within(inspector).getByText('It answered 200 with nothing to read.')).toBeVisible();
    expect(within(inspector).getByText('Timings and size')).toBeVisible();
    expect(within(inspector).queryByText('Title')).toBeNull();
  });
});

describe('the sections', () => {
  it('moves between tabs with the arrow keys, and remembers the one chosen while walking', async () => {
    const { inspector, user } = await open(`${URLS}?inspect=2`);
    const summary = await within(inspector).findByRole('tab', { name: /^Summary/ });
    expect(summary).toHaveAttribute('aria-selected', 'true');
    act(() => summary.focus());
    await user.keyboard('{ArrowRight}');
    expect(within(inspector).getByRole('tab', { name: /^Issues/ })).toHaveAttribute('aria-selected', 'true');
    await user.keyboard('{End}');
    expect(within(inspector).getByRole('tab', { name: /^Source/ })).toHaveAttribute('aria-selected', 'true');
    await user.keyboard('{Home}{ArrowRight}{ArrowRight}');
    expect(within(inspector).getByRole('tab', { name: /^Inlinks/ })).toHaveAttribute('aria-selected', 'true');
    // The counts: what each section lists.
    expect(within(inspector).getByRole('tab', { name: /^Issues/ })).toHaveTextContent('Issues 2');
    expect(within(inspector).getByRole('tab', { name: /^Headers/ })).toHaveTextContent('Headers 4');
    await waitFor(() =>
      expect(within(inspector).getByRole('tab', { name: /^Inlinks/ })).toHaveTextContent('Inlinks 2'),
    );
    expect(within(inspector).getByRole('tab', { name: /^Hreflang/ })).toHaveTextContent(/^Hreflang$/);
  });

  it('keeps the link counts on the tabs while the inspector moves to another URL, until that URL’s are read', async () => {
    const { router, inspector } = await open(`${URLS}?inspect=2`);
    const tab = await within(inspector).findByRole('tab', { name: 'Inlinks 2' });
    // Everything the tab says on the way to the next URL.
    const said: string[] = [];
    const observer = new MutationObserver(() => said.push(tab.textContent));
    observer.observe(tab, { childList: true, characterData: true, subtree: true });
    await act(() => inspectFromGrid(router, 1));
    await within(inspector).findByRole('heading', { level: 2, name: HOME.url });
    await waitFor(() => expect(tab).toHaveAccessibleName('Inlinks 1'));
    observer.disconnect();
    // Never without its count: the tabs after it would move sideways, and back when the count came.
    expect(said).not.toContain('Inlinks');
  });

  it('lists the issues, and says so when the crawl was never analysed', async () => {
    const { inspector, user } = await open(`${URLS}?inspect=2`, {
      urls: {
        2: testUrlDetail({
          issues: null,
          namedBy: null,
          links: null,
          summary: { available: false, reason: 'notAnalyzed' },
        }),
      },
    });
    const panel = await showTab(user, /^Issues/);
    expect(within(panel).getByText('Not analysed yet')).toBeVisible();
    expect(within(panel).getByRole('button', { name: 'Analyse it' })).toBeVisible();
    expect(within(inspector).getByRole('tab', { name: /^Issues/ })).toHaveTextContent(/^Issues$/);
  });

  it('lists the issues on other URLs that name this one, each leading to its group', async () => {
    const { user } = await open(`${URLS}?inspect=2`, {
      urls: {
        2: testUrlDetail({ namedBy: { total: 5, checks: [{ checkId: 'empty-anchor-text', count: 5 }] } }),
      },
    });
    const panel = await showTab(user, /^Issues/);
    const named = within(panel).getByRole('list', { name: 'Issues on other URLs that name this one' });
    expect(groupOf(within(named).getByRole('link', { name: 'Internal links without anchor text' }))).toEqual({
      detail: null,
      relatedUrlId: 2,
    });
    expect(within(named).getByText('5 URLs')).toBeVisible();
  });

  it('draws the redirect chain hop by hop, and calls out a loop', async () => {
    const looping = testUrlDetail({
      id: 2,
      url: 'https://example.com/old/',
      response: testResponse({ outcome: 'redirect', statusCode: 301 }),
      content: null,
      redirects: {
        hops: [
          { id: 3, url: 'https://example.com/b', statusCode: 302, outcome: 'redirect', via: 'http' },
          { id: 2, url: 'https://example.com/old/', statusCode: 301, outcome: 'redirect', via: 'metaRefresh' },
        ],
        final: { id: 2, url: 'https://example.com/old/', statusCode: 301 },
        loop: true,
        tooLong: false,
      },
    });
    const { user, router } = await open(`${URLS}?inspect=2`, { urls: { 2: looping, 3: OTHER } });
    const panel = await showTab(user, /^Redirects/);
    expect(within(panel).getByRole('heading', { name: 'Redirects twice' })).toBeVisible();
    expect(within(panel).getByText('A redirect loop')).toBeVisible();
    const steps = within(within(panel).getByRole('list', { name: 'Redirect chain' })).getAllByRole('listitem');
    expect(steps).toHaveLength(3);
    expect(steps[0]).toHaveTextContent('301 Moved Permanently');
    expect(steps[0]).toHaveTextContent('This URL');
    expect(steps[1]).toHaveTextContent('Redirects to');
    expect(steps[1]).toHaveTextContent('302 Found');
    expect(steps[2]).toHaveTextContent('Meta refresh to');
    expect(steps[2]).toHaveTextContent('Back at this URL');
    // A hop is a link that walks to it; the URL itself isn't.
    expect(within(steps[0]!).queryByRole('button', { name: 'https://example.com/old/' })).toHaveAttribute(
      'class',
      expect.not.stringContaining('si-url__go'),
    );
    await user.click(within(steps[1]!).getByRole('button', { name: 'https://example.com/b' }));
    await waitFor(() => expect(inspected(router)).toBe(3));
    // The link went with the URL it was on: its section's panel has the focus, never the view behind.
    await screen.findByRole('heading', { level: 2, name: OTHER.url });
    await waitFor(() => expect(screen.getByRole('tabpanel', { name: /^Redirects/ })).toHaveFocus());
  });

  it('marks where a chain ends, but not the last hop of one too long to follow', async () => {
    const hops = (count: number) =>
      Array.from({ length: count }, (_, index) => ({
        id: 100 + index,
        url: `https://example.com/hop-${index + 1}/`,
        statusCode: index === count - 1 && count < 20 ? 200 : 301,
        outcome: index === count - 1 && count < 20 ? ('success' as const) : ('redirect' as const),
        via: 'http' as const,
      }));
    const chain = (count: number, tooLong: boolean) =>
      testUrlDetail({
        response: testResponse({ outcome: 'redirect', statusCode: 301 }),
        content: null,
        redirects: {
          hops: hops(count),
          final: { id: 99 + count, url: `https://example.com/hop-${count}/`, statusCode: tooLong ? 301 : 200 },
          loop: false,
          tooLong,
        },
      });
    const { user, router } = await open(`${URLS}?inspect=2`, { urls: { 2: chain(2, false), 3: chain(20, true) } });
    const ends = await showTab(user, /^Redirects/);
    expect(within(ends).getAllByRole('listitem')).toHaveLength(3);
    expect(within(within(ends).getAllByRole('listitem')[2]!).getByText('Where it ends')).toBeVisible();
    await act(() => inspectFromGrid(router, 3));
    const long = await screen.findByRole('tabpanel', { name: /^Redirects/ });
    expect(await within(long).findByText('Too long a chain')).toBeVisible();
    expect(within(long).getAllByRole('listitem')).toHaveLength(21);
    // The twentieth hop is a redirect the crawl stopped at, not where the chain ends.
    expect(within(long).queryByText('Where it ends')).toBeNull();
  });

  it('merges a header sent twice, filters, and says when nothing matches', async () => {
    const { user } = await open(`${URLS}?inspect=2`);
    const panel = await showTab(user, /^Headers/);
    expect(
      within(panel)
        .getAllByRole('term')
        .map((term) => term.textContent),
    ).toEqual(['content-type', 'cache-control', 'x-frame-optionssent 2 times']);
    await user.type(within(panel).getByRole('searchbox', { name: 'Filter the headers' }), 'frame');
    expect(within(panel).getAllByRole('term')).toHaveLength(1);
    await user.type(within(panel).getByRole('searchbox', { name: 'Filter the headers' }), 'zzz');
    expect(within(panel).getByText('No header matches that.')).toBeVisible();
    await user.click(within(panel).getByRole('button', { name: 'Clear the filter' }));
    expect(within(panel).getAllByRole('term')).toHaveLength(3);
  });

  it('folds a long header value until it’s asked for', async () => {
    const { user } = await open(`${URLS}?inspect=2`, {
      urls: {
        2: testUrlDetail({ headers: [{ name: 'content-security-policy', value: `default-src ${'x'.repeat(400)}` }] }),
      },
    });
    const panel = await showTab(user, /^Headers/);
    const toggle = within(panel).getByRole('button', { name: 'Show it all of content-security-policy' });
    expect(toggle).toHaveAttribute('aria-expanded', 'false');
    await user.click(toggle);
    expect(within(panel).getByRole('button', { name: 'Show less of content-security-policy' })).toHaveAttribute(
      'aria-expanded',
      'true',
    );
  });

  it('shows structured data as a tree that opens and closes, and a block that doesn’t parse as its text', async () => {
    const data = testUrlDetail({
      structuredData: [
        ...testUrlDetail().structuredData,
        {
          valid: false,
          types: [],
          json: '{"@type": <b>bold</b>',
          error: "'<' is an invalid start of a value.",
          truncated: false,
        },
      ],
    });
    const { user } = await open(`${URLS}?inspect=2`, { urls: { 2: data } });
    const panel = await showTab(user, /^Structured data/);
    expect(within(panel).getByText('2 blocks: Product and 1 that doesn’t parse.')).toBeVisible();
    const tree = within(panel).getByRole('list', { name: 'Product properties' });
    expect(within(tree).getByText('"Harpswell Metal Patio Set"')).toBeVisible();
    // The offer is one level down: open, with its own properties; closing it takes them away.
    const offers = within(tree).getByRole('button', { name: /offers/ });
    expect(offers).toHaveAttribute('aria-expanded', 'true');
    expect(within(tree).getByText('499.99')).toBeVisible();
    await user.click(offers);
    expect(within(tree).queryByText('499.99')).toBeNull();
    // The block that doesn't parse: the parser's words, and its text as text.
    expect(within(panel).getByText("'<' is an invalid start of a value.")).toBeVisible();
    expect(within(panel).getByText('{"@type": <b>bold</b>')).toBeVisible();
    expect(panel.querySelector('b')).toBeNull();
  });

  it('says so when a page has no structured data or hreflang', async () => {
    const { user } = await open(`${URLS}?inspect=2`, {
      urls: { 2: testUrlDetail({ structuredData: [], hreflang: [] }) },
    });
    expect(within(await showTab(user, /^Structured data/)).getByText('The page has no JSON-LD blocks.')).toBeVisible();
    expect(
      within(await showTab(user, /^Hreflang/)).getByText(
        'The page names no other language or region version of itself.',
      ),
    ).toBeVisible();
  });

  it('lists hreflang annotations with what’s wrong with where they point', async () => {
    const { user } = await open(`${URLS}?inspect=2`, {
      urls: {
        2: testUrlDetail({
          hreflang: [
            {
              language: 'en-GB',
              href: PAGE.url,
              url: PAGE.url,
              target: { id: 2, url: PAGE.url, statusCode: 200 },
              inHead: true,
              fromHttpHeader: false,
            },
            {
              language: 'fr',
              href: '/fr/',
              url: 'https://example.com/fr/',
              target: { id: 9, url: 'https://example.com/fr/', statusCode: 404 },
              inHead: true,
              fromHttpHeader: false,
            },
            {
              language: 'de',
              href: '/de/',
              url: 'https://example.com/de/',
              target: null,
              inHead: true,
              fromHttpHeader: false,
            },
          ],
        }),
      },
    });
    const panel = await showTab(user, /^Hreflang/);
    const rows = within(within(panel).getByRole('list', { name: 'Hreflang annotations' })).getAllByRole('listitem');
    expect(rows).toHaveLength(3);
    expect(rows[0]).not.toHaveTextContent('Answers');
    expect(rows[1]).toHaveTextContent('Answers 404');
    expect(rows[2]).toHaveTextContent('Not in this crawl');
  });
});

describe('the source', () => {
  it('reads the HTML only when the section opens, searches once typing pauses, and steps through matches', async () => {
    const { user } = await open(`${URLS}?inspect=2`);
    await screen.findByRole('heading', { level: 2, name: PAGE.url });
    expect(sidecar.requests.some((request) => request.path.endsWith('/urls/source'))).toBe(false);
    const panel = await showTab(user, /^Source/);
    const viewer = await within(panel).findByTestId('source-viewer');
    expect(viewer).toHaveTextContent('<h1>Harpswell Metal Patio Set</h1>');
    expect(viewer.querySelector('h1')).toBeNull();
    const field = within(panel).getByRole('searchbox', { name: 'Search the HTML source' });
    await user.type(field, 'patio');
    await waitFor(() => expect(viewer).toHaveAttribute('data-query', 'patio'));
    await user.click(within(viewer).getByRole('button', { name: 'report three matches' }));
    expect(within(panel).getByRole('status')).toHaveTextContent('1 of 3');
    await user.click(field);
    await user.keyboard('{Enter}');
    expect(viewer).toHaveAttribute('data-current', '1');
    await user.keyboard('{Shift>}{Enter}{/Shift}{Shift>}{Enter}{/Shift}');
    expect(viewer).toHaveAttribute('data-current', '2');
    expect(within(panel).getByRole('status')).toHaveTextContent('3 of 3');
    await user.click(within(panel).getByRole('button', { name: 'Next match' }));
    expect(viewer).toHaveAttribute('data-current', '0');
    // Emptied, the marks go at once.
    await user.clear(field);
    expect(viewer).toHaveAttribute('data-query', '');
    expect(within(panel).getByRole('status')).toHaveTextContent('');
  });

  it('takes Ctrl+F for its own search, and wraps lines until told not to', async () => {
    const { user } = await open(`${URLS}?inspect=2`);
    const panel = await showTab(user, /^Source/);
    const viewer = await within(panel).findByTestId('source-viewer');
    const wrap = within(panel).getByRole('switch', { name: 'Wrap lines' });
    expect(viewer).toHaveAttribute('data-wrap', 'true');
    await user.click(wrap);
    expect(viewer).toHaveAttribute('data-wrap', 'false');
    act(() => wrap.focus());
    fireEvent.keyDown(wrap, { key: 'f', code: 'KeyF', ctrlKey: true });
    expect(within(panel).getByRole('searchbox', { name: 'Search the HTML source' })).toHaveFocus();
  });

  it('gives its search field focus once when the palette asks, never again on another URL or back on the tab', async () => {
    const { router, inspector, user } = await open(`${URLS}?inspect=2`);
    await within(inspector).findByRole('heading', { level: 2, name: PAGE.url });
    act(() => void commands.run('inspector.searchSource'));
    const name = { name: 'Search the HTML source' };
    await waitFor(() => expect(within(inspector).getByRole('searchbox', name)).toHaveFocus());

    // The grid's cursor moves on: the inspector follows with the next URL's source, and focus stays where it is.
    const close = within(inspector).getByRole('button', { name: 'Close the inspector' });
    act(() => close.focus());
    await act(() => inspectFromGrid(router, 3));
    await within(inspector).findByRole('heading', { level: 2, name: OTHER.url });
    await within(inspector).findByRole('searchbox', name);
    expect(close).toHaveFocus();

    // Shown again by its tab, the section leaves focus on the tab.
    await user.click(within(inspector).getByRole('tab', { name: /^Summary/ }));
    await user.click(within(inspector).getByRole('tab', { name: /^Source/ }));
    await within(inspector).findByRole('searchbox', name);
    expect(within(inspector).getByRole('tab', { name: /^Source/ })).toHaveFocus();

    // Asked again, it takes focus again.
    act(() => void commands.run('inspector.searchSource'));
    await waitFor(() => expect(within(inspector).getByRole('searchbox', name)).toHaveFocus());
  });

  it('says why there’s no HTML, and fails on its own', async () => {
    const { user } = await open(`${URLS}?inspect=2`, {
      urls: { 2: PAGE, 3: OTHER },
      source: { 2: { stored: false, reason: 'notStored' }, 3: 'error' },
      inlinks: { 2: [testLinkRow({ id: 3, url: OTHER.url })] },
    });
    const panel = await showTab(user, /^Source/);
    expect(await within(panel).findByText('The HTML wasn’t kept')).toBeVisible();
    // Another URL's source fails: its section says so, with a retry; the rest of the inspector is fine.
    await user.click(screen.getByRole('tab', { name: /^Inlinks/ }));
    const grid = await screen.findByRole('grid', { name: 'Inlinks' });
    await user.click(await within(grid).findByRole('button', { name: OTHER.url }));
    await screen.findByRole('heading', { level: 2, name: OTHER.url });
    const failed = await showTab(user, /^Source/);
    expect(await within(failed).findByText('The HTML source couldn’t be read.')).toBeVisible();
    expect(within(failed).getByRole('button', { name: 'Try again' })).toBeVisible();
    await user.click(screen.getByRole('tab', { name: /^Summary/ }));
    expect(await screen.findByRole('heading', { name: 'Indexable' })).toBeVisible();
  });
});

describe('walking the site', () => {
  it('walks an inlink then an outlink, back and forward retrace it, and Esc closes to where it opened', async () => {
    const history = createMemoryHistory({
      initialEntries: [`/crawls/${TEST_CRAWL_ID}/overview`, `${URLS}?inspect=2`],
      initialIndex: 1,
    });
    const { router, inspector, user } = await open('', SITE, history);
    await within(inspector).findByRole('heading', { level: 2, name: PAGE.url });
    const before = history.length;

    await user.click(within(inspector).getByRole('tab', { name: /^Inlinks/ }));
    const inlinks = await within(inspector).findByRole('grid', { name: 'Inlinks' });
    await within(inlinks).findByText('Patio set');
    expect(within(inspector).getByText('2 inlinks')).toBeVisible();
    // The keyboard: the first row, Enter.
    act(() => inlinks.focus());
    fireEvent.keyDown(inlinks, { key: 'Enter' });
    await waitFor(() => expect(inspected(router)).toBe(1));
    expect(history.length).toBe(before + 1);
    await within(inspector).findByRole('heading', { level: 2, name: HOME.url });
    // Still on Inlinks, in the new URL's grid, with the keyboard still in it.
    expect(within(inspector).getByRole('tab', { name: /^Inlinks/ })).toHaveAttribute('aria-selected', 'true');
    await waitFor(() => expect(within(inspector).getByRole('grid', { name: 'Inlinks' })).toHaveFocus());

    await user.click(within(inspector).getByRole('tab', { name: /^Outlinks/ }));
    const outlinks = await within(inspector).findByRole('grid', { name: 'Outlinks' });
    await user.dblClick(await within(outlinks).findByText('Patio set'));
    await waitFor(() => expect(inspected(router)).toBe(2));
    expect(history.length).toBe(before + 2);

    await act(async () => {
      history.back();
      await router.load();
    });
    expect(inspected(router)).toBe(1);
    await within(inspector).findByRole('heading', { level: 2, name: HOME.url });
    await act(async () => {
      history.forward();
      await router.load();
    });
    expect(inspected(router)).toBe(2);

    // Esc goes back over both steps and closes it there: the screen as it was before the inspector opened.
    fireEvent.keyDown(document.body, { key: 'Escape' });
    await waitFor(() => expect(screen.queryByRole('complementary', { name: 'Inspector' })).toBeNull());
    expect(router.state.location.pathname).toBe(URLS);
    expect(inspected(router)).toBeUndefined();
    await act(async () => {
      history.back();
      await router.load();
    });
    expect(router.state.location.pathname).toBe(`/crawls/${TEST_CRAWL_ID}/overview`);
  });

  it('walks one step for a double-click on a link grid’s URL, whichever grid its second click lands on', async () => {
    const history = createMemoryHistory({ initialEntries: [`${URLS}?inspect=2`] });
    const { router, inspector, user } = await open('', SITE, history);
    await user.click(await within(inspector).findByRole('tab', { name: /^Inlinks/ }));
    const inlinks = await within(inspector).findByRole('grid', { name: 'Inlinks' });
    // Both clicks before the next URL's grid is there: the first walks, the second and the double-click don't.
    await user.dblClick(await within(inlinks).findByRole('button', { name: HOME.url }));
    await waitFor(() => expect(inspected(router)).toBe(1));
    await within(inspector).findByRole('heading', { level: 2, name: HOME.url });
    expect(history.length).toBe(2);

    // The second click landing on the grid that came, on a row beside its URL: one press there, then the
    // double-click, which isn't this grid's.
    const next = within(inspector).getByRole('grid', { name: 'Inlinks' });
    const cell = await within(next).findByText('Home');
    fireEvent.pointerDown(cell);
    fireEvent.click(cell, { detail: 2 });
    fireEvent.doubleClick(cell);
    // Or on its URL: a double-click's second click is no go of its own.
    const link = within(next).getByRole('button', { name: OTHER.url });
    fireEvent.pointerDown(link);
    fireEvent.click(link, { detail: 2 });
    fireEvent.doubleClick(link);
    await act(() => new Promise((resolve) => setTimeout(resolve, 50)));
    expect(inspected(router)).toBe(1);
    expect(history.length).toBe(2);

    // A double-click of its own on a row still walks.
    await user.dblClick(within(next).getByText('Home'));
    await waitFor(() => expect(inspected(router)).toBe(3));
    expect(history.length).toBe(3);
  });

  it('keeps the keyboard in the link grid, on the row it was left on, back and forward along a walk', async () => {
    const history = createMemoryHistory({ initialEntries: [`${URLS}?inspect=2`] });
    const { router, inspector, user } = await open('', SITE, history);
    await user.click(await within(inspector).findByRole('tab', { name: /^Inlinks/ }));
    const cursorRow = () => {
      const grid = within(inspector).getByRole('grid', { name: 'Inlinks' });
      const cell = document.getElementById(grid.getAttribute('aria-activedescendant') ?? '');
      return cell?.closest('[role="row"]')?.getAttribute('aria-rowindex');
    };
    const inlinks = await within(inspector).findByRole('grid', { name: 'Inlinks' });
    await within(inlinks).findByText('Patio set');
    // The second inlink (the header is row 1), then Enter: a walk to it.
    act(() => inlinks.focus());
    fireEvent.keyDown(inlinks, { key: 'ArrowDown' });
    expect(cursorRow()).toBe('3');
    fireEvent.keyDown(inlinks, { key: 'Enter' });
    await waitFor(() => expect(inspected(router)).toBe(3));
    await within(inspector).findByRole('heading', { level: 2, name: OTHER.url });

    // Back: the first URL's inlinks, the keyboard still in them, on the row the walk left from.
    await act(async () => {
      history.back();
      await router.load();
    });
    await within(inspector).findByRole('heading', { level: 2, name: PAGE.url });
    await waitFor(() => expect(within(inspector).getByRole('grid', { name: 'Inlinks' })).toHaveFocus());
    await waitFor(() => expect(cursorRow()).toBe('3'));
    // Forward again: the keyboard stays in the inspector, so an arrow key never moves the view behind it.
    await act(async () => {
      history.forward();
      await router.load();
    });
    await within(inspector).findByRole('heading', { level: 2, name: OTHER.url });
    await waitFor(() => expect(within(inspector).getByRole('grid', { name: 'Inlinks' })).toHaveFocus());
  });

  it('keeps focus in the inspector when back takes away a source that had it', async () => {
    const history = createMemoryHistory({ initialEntries: [`${URLS}?inspect=2`] });
    const { router, inspector, user } = await open('', SITE, history);
    await within(inspector).findByRole('heading', { level: 2, name: PAGE.url });
    await act(
      () =>
        inspector.querySelector('[role="tabpanel"]') && router.navigate({ to: '.', search: { inspect: 3 } as never }),
    );
    await within(inspector).findByRole('heading', { level: 2, name: OTHER.url });
    const panel = await showTab(user, /^Source/);
    const field = await within(panel).findByRole('searchbox', { name: 'Search the HTML source' });
    act(() => field.focus());
    await act(async () => {
      history.back();
      await router.load();
    });
    await within(inspector).findByRole('heading', { level: 2, name: PAGE.url });
    await waitFor(() => expect(within(inspector).getByRole('tabpanel')).toHaveFocus());
  });

  it('filters the links by kind, remembers it, and says when there are none of a kind', async () => {
    const { inspector, user } = await open(`${URLS}?inspect=2`);
    await user.click(await within(inspector).findByRole('tab', { name: /^Inlinks/ }));
    const grid = await within(inspector).findByRole('grid', { name: 'Inlinks' });
    await within(grid).findByText('Patio set');
    expect(within(grid).getByText('Image')).toBeVisible();
    await user.click(within(inspector).getByRole('button', { name: /Kind of link/ }));
    await user.click(await screen.findByRole('menuitemradio', { name: 'Hyperlink' }));
    expect(await within(inspector).findByText('1 hyperlink inlink')).toBeVisible();
    // The grid's own read, not the tab's count (one row of every kind).
    const kinds = sidecar.requests
      .filter((request) => request.path.endsWith('/urls/inlinks') && (request.body as { limit?: number }).limit !== 1)
      .at(-1)?.body;
    expect(kinds).toMatchObject({ urlId: 2, kinds: ['hyperlink'] });
    await user.click(within(inspector).getByRole('button', { name: /Kind of link/ }));
    await user.click(await screen.findByRole('menuitemradio', { name: 'Script' }));
    expect(await within(inspector).findByText('No script inlinks')).toBeVisible();
    await user.click(within(inspector).getByRole('button', { name: 'Show every kind' }));
    expect(await within(inspector).findByText('2 inlinks')).toBeVisible();
    // The button went with the empty state it was in: the grid that came has the keyboard.
    await waitFor(() => expect(within(inspector).getByRole('grid', { name: 'Inlinks' })).toHaveFocus());
  });

  it('says a link’s URL has no status when the crawl has an outcome for it without a code, and no more', async () => {
    const blocked = testLinkRow({ id: 3, url: OTHER.url, statusCode: null, fetched: true, indexable: null });
    const unfetched = testLinkRow({ id: 1, url: HOME.url, statusCode: null, fetched: false, indexable: null });
    const { inspector, user } = await open(`${URLS}?inspect=2`, { ...SITE, outlinks: { 2: [blocked, unfetched] } });
    await user.click(await within(inspector).findByRole('tab', { name: /^Outlinks/ }));
    const grid = await within(inspector).findByRole('grid', { name: 'Outlinks' });
    expect(await within(grid).findByText('No status')).toBeVisible();
    expect(within(grid).getByText('Not fetched')).toBeVisible();
    expect(within(grid).queryByText('No response')).toBeNull();
  });

  it('says so when a URL has no inlinks', async () => {
    const { inspector, user } = await open(`${URLS}?inspect=3`);
    await user.click(await within(inspector).findByRole('tab', { name: /^Inlinks/ }));
    expect(await within(inspector).findByText('No inlinks')).toBeVisible();
    expect(within(inspector).getByText('Nothing the crawl read links to this URL.')).toBeVisible();
  });
});

describe('loading, failing and not there', () => {
  it('shows the summary’s skeleton only once a load has taken a moment', async () => {
    const { inspector } = await open(`${URLS}?inspect=2`, { urls: { 2: 'pending' } });
    expect(within(inspector).queryByRole('status', { name: 'Loading the summary' })).toBeNull();
    expect(await within(inspector).findByRole('status', { name: 'Loading the summary' })).toBeVisible();
    // The frame is there meanwhile: the actions, and the tabs.
    expect(within(inspector).getByRole('button', { name: 'Close the inspector' })).toBeEnabled();
    expect(within(inspector).getByRole('button', { name: 'Copy the URL' })).toBeDisabled();
    expect(within(inspector).getAllByRole('tab')).toHaveLength(9);
  });

  it('says a stale id isn’t in the crawl, and offers to close', async () => {
    const { inspector, router, user } = await open(`${URLS}?inspect=999`);
    expect(await within(inspector).findByText('This crawl doesn’t have that URL')).toBeVisible();
    expect(within(inspector).queryByRole('tablist')).toBeNull();
    expect(within(inspector).queryByRole('button', { name: 'Try again' })).toBeNull();
    // The head's own Close, and the one the message offers.
    const close = within(inspector).getAllByRole('button', { name: 'Close the inspector' });
    expect(close).toHaveLength(2);
    await user.click(close[1]!);
    await waitFor(() => expect(inspected(router)).toBeUndefined());
  });

  it('keeps the other sections working when the URL itself can’t be read, and tries again', async () => {
    let fail = true;
    const { inspector, user } = await open(`${URLS}?inspect=2`, {
      ...SITE,
      urls: (id) => (id === 2 ? (fail ? 'error' : PAGE) : id === 1 ? HOME : undefined),
    });
    expect(await within(inspector).findByText('This URL couldn’t be read.')).toBeVisible();
    // Its links are their own read.
    await user.click(within(inspector).getByRole('tab', { name: /^Inlinks/ }));
    expect(
      await within(await within(inspector).findByRole('grid', { name: 'Inlinks' })).findByText('Patio set'),
    ).toBeVisible();
    await user.click(within(inspector).getByRole('tab', { name: /^Summary/ }));
    fail = false;
    await user.click(await within(inspector).findByRole('button', { name: 'Try again' }));
    expect(await within(inspector).findByRole('heading', { name: 'Indexable' })).toBeVisible();
    // "Try again" went with the error it was in: its section's panel has the focus, not the page.
    await waitFor(() => expect(within(inspector).getByRole('tabpanel', { name: /^Summary/ })).toHaveFocus());
  });

  it('says the links couldn’t be read in their own section, the summary untouched', async () => {
    const { inspector, user } = await open(`${URLS}?inspect=2`, { urls: { 2: PAGE }, inlinks: { 2: 'error' } });
    await within(inspector).findByRole('heading', { name: 'Indexable' });
    await user.click(within(inspector).getByRole('tab', { name: /^Inlinks/ }));
    expect(await within(inspector).findByText('The inlinks couldn’t be read.')).toBeVisible();
    expect(within(inspector).getByRole('button', { name: 'Try again' })).toBeVisible();
  });

  it('refreshes in place while a crawl runs: no skeleton, and what changed shows', async () => {
    const queued = testUrlDetail({
      response: null,
      content: null,
      discovery: { ...testUrlDetail().discovery, state: 'queued' },
    });
    let fetched = false;
    // While it's queued one page links to it; by the time it's fetched, two do.
    const linking = [testLinkRow({ id: 1, url: HOME.url }), testLinkRow({ id: 3, url: OTHER.url })];
    sidecar = fakeSidecar([
      ...inspectorRoutes({
        urls: () => (fetched ? PAGE : queued),
        get inlinks() {
          return { 2: fetched ? linking : linking.slice(0, 1) };
        },
      }),
      ...issuesRoutes({ item: { status: 'running' } }),
    ]);
    setTransport(sidecar);
    const queryClient = createQueryClient();
    const router = createAppRouter(queryClient, createMemoryHistory({ initialEntries: [`${URLS}?inspect=2`] }));
    render(<App router={router} queryClient={queryClient} />);
    await act(() => router.load());
    const inspector = await screen.findByRole('complementary', { name: 'Inspector' });
    expect(await within(inspector).findByText('Not fetched yet')).toBeVisible();
    expect(within(inspector).getByText(/It’ll show here once the crawl has fetched it\./)).toBeVisible();
    await waitFor(() => expect(within(inspector).getAllByText('Live').length).toBeGreaterThan(0));
    expect(await within(inspector).findByRole('tab', { name: 'Inlinks 1' })).toBeVisible();
    fetched = true;
    // What the live bridge does on a crawl's tick: everything read about the URLs inspected.
    await act(() => queryClient.invalidateQueries({ queryKey: queryKeys.urlDetails(TEST_CRAWL_ID) }));
    expect(await within(inspector).findByRole('heading', { name: 'Indexable' })).toBeVisible();
    // The tabs' link counts are live with it: the same tick refreshes them.
    expect(await within(inspector).findByRole('tab', { name: 'Inlinks 2' })).toBeVisible();
    expect(within(inspector).queryByRole('status', { name: 'Loading the summary' })).toBeNull();
    expect(within(inspector).queryByRole('progressbar', { name: 'Reading this URL again' })).toBeNull();
  });
});

describe('the sheet', () => {
  it('resizes from the keyboard, within its range, and remembers the size', async () => {
    const { inspector } = await open(`${URLS}?inspect=2`);
    const splitter = within(inspector).getByRole('separator', { name: 'Resize the inspector' });
    expect(splitter).toHaveAttribute('aria-orientation', 'vertical');
    act(() => splitter.focus());
    fireEvent.keyDown(splitter, { key: 'ArrowLeft' });
    await waitFor(() => expect(getSettings().inspectorWidth).toBe(336));
    fireEvent.keyDown(splitter, { key: 'ArrowRight' });
    fireEvent.keyDown(splitter, { key: 'ArrowRight' });
    await waitFor(() => expect(getSettings().inspectorWidth).toBe(320));
    fireEvent.keyDown(splitter, { key: 'End' });
    await waitFor(() => expect(getSettings().inspectorWidth).toBe(960));
    expect(splitter).toHaveAttribute('aria-valuenow', '960');
  });

  it('docks at the bottom and back, and remembers it', async () => {
    const { inspector, user } = await open(`${URLS}?inspect=2`);
    await user.click(within(inspector).getByRole('button', { name: 'Dock at the bottom' }));
    expect(getSettings().inspectorDock).toBe('bottom');
    expect(within(inspector).getByRole('separator', { name: 'Resize the inspector' })).toHaveAttribute(
      'aria-orientation',
      'horizontal',
    );
    await user.click(within(inspector).getByRole('button', { name: 'Dock on the right' }));
    expect(getSettings().inspectorDock).toBe('right');
  });

  it('copies the URL, saying so', async () => {
    const { inspector } = await open(`${URLS}?inspect=2`);
    const writeText = vi.spyOn(navigator.clipboard, 'writeText').mockResolvedValue();
    await within(inspector).findByRole('heading', { level: 2, name: PAGE.url });
    fireEvent.click(within(inspector).getByRole('button', { name: 'Copy the URL' }));
    await waitFor(() => expect(writeText).toHaveBeenCalledWith(PAGE.url));
  });

  it('folds a very long URL in the head until it’s asked for', async () => {
    const long = `https://example.com/${'a-very-long-path/'.repeat(40)}`;
    const { inspector, user } = await open(`${URLS}?inspect=2`, { urls: { 2: testUrlDetail({ url: long }) } });
    const heading = await within(inspector).findByRole('heading', { level: 2, name: long });
    expect(heading).toHaveClass('line-clamp-3');
    await user.click(within(inspector).getByRole('button', { name: 'Show all of the URL' }));
    expect(heading).not.toHaveClass('line-clamp-3');
  });
});
