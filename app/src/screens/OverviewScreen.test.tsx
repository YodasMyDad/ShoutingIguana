import type { QueryClient } from '@tanstack/react-query';
import { createMemoryHistory } from '@tanstack/react-router';
import { act, fireEvent, render, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { GridFilter } from '../api/grids';
import { queryKeys } from '../api/queries';
import { setTransport } from '../api/transport';
import type { CrawlListItem, CrawlStatus } from '../api/types';
import { App } from '../app/App';
import { createQueryClient } from '../app/queryClient';
import { createAppRouter } from '../app/router';
import { installKeyboard } from '../keyboard/registry';
import { PUBLISH_INTERVAL_MS, liveStore } from '../live/liveStore';
import { formatCount } from '../lib/format';
import { updateSettings } from '../settings/settingsStore';
import { TEST_CRAWL_ID, fakeSidecar, testCrawlItem } from '../test/fakeSidecar';
import { ISSUES_ANALYSIS, ISSUES_CHECKS_WITHOUT_SUBJECTS } from '../test/issuesSidecar';
import { createQueryClientWithoutBackoff } from '../test/queryClient';
import {
  DEAD_TWIN,
  EMPTY_FACETS,
  OVERVIEW_STATS,
  alert,
  historyUntil,
  message,
  overviewRoutes,
  progressAt,
  type CrawlScript,
  type OverviewSidecarOptions,
} from '../test/overviewSidecar';

const OVERVIEW = `/crawls/${TEST_CRAWL_ID}/overview`;
const NEVER = { analyzedAt: null, issues: null } as const;

type Heard = 'onAnalyse' | 'onCancelAnalysis' | 'onContinue' | 'onPause' | 'onResume' | 'onStop';

function sidecar(options: Omit<OverviewSidecarOptions, Heard> = {}) {
  const heard = { analyse: vi.fn(), cancel: vi.fn(), proceed: vi.fn(), resume: vi.fn() };
  const transport = fakeSidecar(
    overviewRoutes({
      ...options,
      onAnalyse: heard.analyse,
      onCancelAnalysis: heard.cancel,
      onContinue: heard.proceed,
      onResume: heard.resume,
    }),
  );
  setTransport(transport);
  return { requests: transport.requests, ...heard };
}

async function openAndKeep(path = OVERVIEW, queryClient = createQueryClient()) {
  const router = createAppRouter(queryClient, createMemoryHistory({ initialEntries: [path] }));
  const { unmount } = render(<App router={router} queryClient={queryClient} />);
  await act(() => router.load());
  // The first load's skeleton has gone: the screen is in the state the test is about.
  await waitFor(() => expect(document.querySelector('[aria-label="Loading the overview"]')).toBeNull());
  return { router, queryClient, unmount };
}

const open = (path = OVERVIEW) => openAndKeep(path);
/**
 * `open`, for a read that fails for good: its tries come at once, without the app's back-off between them (750 ms
 * in real time, of the second a test waits for what the screen says then).
 */
const openFailing = () => openAndKeep(OVERVIEW, createQueryClientWithoutBackoff());

/** What the live layer does when the crawl's status changes. */
function setStatus(queryClient: QueryClient, status: CrawlStatus, extra: Partial<CrawlListItem> = {}) {
  act(() => {
    queryClient.setQueryData<CrawlListItem>(queryKeys.crawlItem(TEST_CRAWL_ID), (item) =>
      item ? { ...item, ...extra, status } : item,
    );
  });
}

/** Stream events, published once the live store's interval has passed. */
async function heard(...events: ReturnType<typeof message>[]) {
  await act(async () => {
    events.forEach((event) => liveStore.ingest(event));
    await new Promise((resolve) => setTimeout(resolve, PUBLISH_INTERVAL_MS + 50));
  });
}

/** The screen, without the top bar's own crawl controls. */
const main = () => within(document.getElementById('main') ?? document.body);
const region = (name: string | RegExp) => within(main().getByRole('region', { name }));
const findRegion = async (name: string | RegExp) => within(await main().findByRole('region', { name }));
const filtersOf = (link: HTMLElement): GridFilter[] =>
  JSON.parse(new URL((link as HTMLAnchorElement).href).searchParams.get('filter') ?? '[]') as GridFilter[];
const searchOf = (link: HTMLElement) => new URL((link as HTMLAnchorElement).href).searchParams;
const pathOf = (link: HTMLElement) => new URL((link as HTMLAnchorElement).href).pathname;

const NOT_BLOCKED: GridFilter = { column: 'outcome', op: 'notIn', values: ['blocked'] };
// The site's page URLs, without the crawler's probes of its other versions.
const SITE_PAGES: GridFilter[] = [
  { column: 'isInternal', op: 'is', value: true },
  { column: 'type', op: 'in', values: ['page'] },
  { column: 'source', op: 'notIn', values: ['siteVersion'] },
];
// A page is HTML the crawl read (it has a word count): a URL whose status alone was checked isn't one.
const ANSWERED: GridFilter[] = [
  ...SITE_PAGES,
  { column: 'statusClass', op: 'in', values: ['2xx'] },
  { column: 'wordCount', op: 'isNotEmpty' },
  { column: 'url', op: 'notContains', value: '/cdn-cgi/' },
];

let uninstall: () => void = () => undefined;
beforeEach(() => {
  uninstall = installKeyboard();
});
afterEach(() => {
  uninstall();
  updateSettings({ hiddenChecks: [], hiddenChecksByCrawl: {} });
  liveStore.reset();
  setTransport(fakeSidecar([]));
});

describe('the Overview of an analysed crawl', () => {
  it('says when the crawl started and how long it spent crawling, by that clock, not from start to finish', async () => {
    // Started 10:15, finished 10:20, but crawled for 41 s: it was paused, and stopped and continued.
    sidecar({ item: { crawlingMs: 41_250 } });
    await open();
    const facts = (await main().findByRole('link', { name: '4,521 URLs' })).closest('p');
    expect(facts).toHaveTextContent(
      /^4,521 URLs crawled, 265 pages read, started .+ 2026.*\. It spent 41 s crawling, not counting pauses or the time between runs\.$/,
    );
    expect(facts).not.toHaveTextContent(/5 min/);
  });

  it('says it doesn’t know how long a crawl an earlier version made spent crawling, and doesn’t guess', async () => {
    sidecar({ item: { crawlingMs: null } });
    await open();
    const facts = (await main().findByRole('link', { name: '4,521 URLs' })).closest('p');
    expect(facts).toHaveTextContent(
      'How long it spent crawling isn’t known: an earlier version of Shouting Iguana crawled it, or part of it, and didn’t keep the time.',
    );
    expect(facts).not.toHaveTextContent(/\d+ (s|min) crawling|5 min/);
  });

  it('answers first, in one sentence built from the counts', async () => {
    sidecar();
    await open();
    expect(
      await main().findByText('3 checks found 11 issues on this crawl. 1 passed and 1 was skipped.'),
    ).toBeVisible();
    expect(main().getByRole('heading', { level: 1, name: 'Overview' })).toBeVisible();
    // Done, not running: no "Live".
    expect(main().queryByText('Live')).toBeNull();
  });

  it('counts issues by severity, each figure opening Issues at that severity', async () => {
    sidecar();
    await open();
    const figures = region('Issues by severity');
    const errors = await figures.findByRole('link', { name: /Errors/ });
    expect(errors).toHaveTextContent('1 check');
    expect(errors).toHaveTextContent('2 issues between them');
    expect(pathOf(errors)).toBe(`/crawls/${TEST_CRAWL_ID}/issues`);
    expect(searchOf(errors).get('severity')).toBe('["error"]');
    expect(figures.getByRole('link', { name: /Notices/ })).toHaveTextContent('2 checks');
    expect(figures.getByRole('link', { name: /Warnings/ })).toHaveTextContent('None found an issue');
    // What passed and what was skipped is said, never left to read as clean.
    expect(figures.getByText('1 was skipped, with why below')).toBeVisible();
  });

  it('leads with the things to change when the analysis counts them, per severity too', async () => {
    sidecar({
      analysis: {
        ...ISSUES_ANALYSIS,
        counts: ISSUES_ANALYSIS.counts.map((count) => ({
          ...count,
          causes: count.count === 0 ? null : count.checkId === 'empty-anchor-text' ? 1 : count.count,
          templateCauses: count.count === 0 ? null : count.checkId === 'empty-anchor-text' ? 1 : 0,
          clears: [],
        })),
      },
    });
    await open();
    expect(
      await main().findByText(
        'About 6 things to change, 1 of them in the site’s template: 3 checks found 11 issues on this crawl. 1 passed and 1 was skipped.',
      ),
    ).toBeVisible();
    const figures = region('Issues by severity');
    expect(await figures.findByRole('link', { name: /Errors/ })).toHaveTextContent(
      'About 2 things to change, from 2 issues',
    );
    expect(figures.getByRole('link', { name: /Notices/ })).toHaveTextContent(
      'About 4 things to change, 1 of them in the site’s template, from 9 issues',
    );
  });

  it('lists what to fix first, the most severe then the most pages or URLs, each opening its check', async () => {
    sidecar();
    await open();
    const fix = region('Fix first');
    const rows = await fix.findAllByRole('listitem');
    expect(rows.map((row) => within(row).getByRole('link').textContent)).toEqual([
      expect.stringContaining('Error: Internal URL returns 4xx'),
      expect.stringContaining('Notice: Internal links without anchor text'),
      expect.stringContaining('Notice: Title too long'),
    ]);
    const first = within(rows[0]!).getByRole('link');
    expect(first).toHaveTextContent('2 URLs');
    expect(pathOf(first)).toBe(`/crawls/${TEST_CRAWL_ID}/issues/internal-client-error`);
    // Each count is called what it is: URLs for a check that can flag any URL, pages for one that flags only pages.
    expect(within(rows[1]!).getByRole('link')).toHaveTextContent('6 pages');
    expect(within(rows[2]!).getByRole('link')).toHaveTextContent('3 pages');
    // So the order isn't said to be by pages alone.
    expect(fix.getByText('The most severe first, then the most pages or URLs affected')).toBeVisible();
    expect(fix.queryByText(/most pages affected/)).toBeNull();
  });

  it('lists what to fix first in the engine’s order when the analysis sends one (fixFirst), hidden checks left out', async () => {
    // The engine leads with the title check (as a fault of the whole site would), then the error, then the anchors.
    updateSettings({ hiddenChecks: ['empty-anchor-text'] });
    sidecar({
      analysis: { ...ISSUES_ANALYSIS, fixFirst: ['title-too-long', 'internal-client-error', 'empty-anchor-text'] },
    });
    await open();
    const fix = region('Fix first');
    const rows = await fix.findAllByRole('listitem');
    expect(rows.map((row) => within(row).getByRole('link').textContent)).toEqual([
      expect.stringContaining('Notice: Title too long'),
      expect.stringContaining('Error: Internal URL returns 4xx'),
    ]);
    expect(fix.getByText('Most severe first, site-wide faults ahead, then most URLs')).toBeVisible();
  });

  it('counts URLs in Fix first for checks that don’t say what they flag', async () => {
    sidecar({ checks: ISSUES_CHECKS_WITHOUT_SUBJECTS });
    await open();
    const rows = await region('Fix first').findAllByRole('listitem');
    expect(rows.map((row) => /[\d,]+ (?:URLs?|pages?)/.exec(within(row).getByRole('link').textContent)?.[0])).toEqual([
      '2 URLs',
      '6 URLs',
      '3 URLs',
    ]);
  });

  it('leaves a hidden check out of the figures and Fix first, as the sidebar does', async () => {
    updateSettings({ hiddenChecks: ['internal-client-error'] });
    sidecar();
    await open();
    expect(await region('Issues by severity').findByRole('link', { name: /Errors/ })).toHaveTextContent('0 checks');
    expect(region('Fix first').queryByRole('link', { name: /Internal URL returns 4xx/ })).toBeNull();
  });

  it('answers of the checks that aren’t hidden, sentence and figures alike, and says what the hidden ones found', async () => {
    // The error with its 2 issues, hidden: the sentence said "3 checks found 11 issues" above figures of 2 and 9.
    updateSettings({ hiddenChecks: ['internal-client-error'] });
    sidecar();
    await open();
    const sentence = (await main().findByText(/checks found/)).closest('p');
    expect(sentence).toHaveTextContent(
      '2 checks found 9 issues on this crawl. 1 passed and 1 was skipped. 1 hidden check isn’t counted: it found 2 issues.',
    );
    const figures = region('Issues by severity');
    expect(figures.getByRole('link', { name: /Notices/ })).toHaveTextContent('2 checks');
    expect(figures.getByRole('link', { name: /Notices/ })).toHaveTextContent('9 issues between them');
    // The count opens Issues, where hidden checks can be shown.
    const hidden = within(sentence!).getByRole('link', { name: '1 hidden check' });
    expect(pathOf(hidden)).toBe(`/crawls/${TEST_CRAWL_ID}/issues`);
  });

  it('says the only checks with issues are hidden, rather than list them beside figures that say none found any', async () => {
    updateSettings({ hiddenChecks: ['internal-client-error', 'empty-anchor-text', 'title-too-long'] });
    sidecar();
    await open();
    expect((await main().findByText(/No check found an issue/)).closest('p')).toHaveTextContent(
      'No check found an issue on this crawl. 1 passed and 1 was skipped. 3 hidden checks aren’t counted: 3 of them found 11 issues.',
    );
    const fix = region('Fix first');
    expect(fix.queryAllByRole('link')).toHaveLength(0);
    expect(
      fix.getByText('Nothing to fix among the checks shown: the only checks that found issues are hidden.'),
    ).toBeVisible();
    expect(region('Issues by severity').getByRole('link', { name: /Errors/ })).toHaveTextContent('None found an issue');
  });

  it('lists the categories with issues, and the skipped checks with why', async () => {
    sidecar();
    await open();
    const links = await region('Issues by category').findByRole('link', { name: /Links/ });
    expect(links).toHaveTextContent('6 issues');
    // By the category's id, never its title as text, which other categories' checks have in their own titles.
    expect(searchOf(links).get('category')).toBe('links');
    expect(searchOf(links).get('q')).toBeNull();
    const skipped = region('Not checked on this crawl');
    expect(skipped.getByRole('link', { name: 'Orphan page in a sitemap' })).toBeVisible();
    expect(skipped.getByText('No sitemap could be read.')).toBeVisible();
  });

  it('shows the status codes, every class a link to exactly its URLs, the blocked apart from their status', async () => {
    sidecar();
    await open();
    const status = region('Status codes');
    const rows = within(await status.findByRole('list', { name: 'Status codes by class' }));
    const row = (name: RegExp) => rows.getByRole('link', { name });
    expect(row(/^2xx/)).toHaveTextContent('4,492');
    expect(row(/^2xx/)).toHaveTextContent('99.4%');
    expect(row(/^4xx/)).toHaveTextContent('404 × 9');
    expect(filtersOf(row(/^4xx/))).toEqual([{ column: 'statusClass', op: 'in', values: ['4xx'] }, NOT_BLOCKED]);
    // What the "not blocked" filter left out: the firewall's answers, whatever their status.
    expect(row(/^Blocked/)).toHaveTextContent('2');
    expect(filtersOf(row(/^Blocked/))).toEqual([{ column: 'outcome', op: 'in', values: ['blocked'] }]);
    // A class with nothing in it is listed, and opens nothing.
    expect(rows.getByText('5xx').closest('a')).toBeNull();
    // The bar's parts are the same links, with their numbers in words.
    expect(filtersOf(status.getByRole('link', { name: '3xx: 16 URLs' }))).toEqual([
      { column: 'statusClass', op: 'in', values: ['3xx'] },
      NOT_BLOCKED,
    ]);
    expect(status.getByText(/not 2xx, magnified/)).toHaveTextContent('The 29 not 2xx, magnified');
    expect(filtersOf(status.getByRole('link', { name: '1,765 more URLs' }))).toEqual([
      { column: 'statusClass', op: 'isEmpty' },
    ]);
    // The crawl's stats say why there was no response.
    expect(await rows.findByText('Refused by robots.txt × 2')).toBeVisible();
  });

  it('leaves the probes of the site’s other versions out of its status codes once done, as it did live', async () => {
    // The garden crawl's two URLs with no response are its probes: refused by robots.txt (the twin's own file).
    sidecar({
      probes: [
        {
          id: 9_001,
          url: 'http://example.com/',
          outcome: 'robotsDisallowed',
          statusClass: 'noResponse',
          statusCode: null,
        },
        {
          id: 9_002,
          url: 'http://www.example.com/',
          outcome: 'robotsDisallowed',
          statusClass: 'noResponse',
          statusCode: null,
        },
      ],
    });
    await open();
    const status = region('Status codes');
    const rows = within(await status.findByRole('list', { name: 'Status codes by class' }));
    expect(rows.getByText('No response').closest('li')).toHaveTextContent('None');
    expect(filtersOf(rows.getByRole('link', { name: /^3xx/ }))).toEqual([
      { column: 'statusClass', op: 'in', values: ['3xx'] },
      NOT_BLOCKED,
      { column: 'source', op: 'notIn', values: ['siteVersion'] },
    ]);
    expect(status.getByText('4,519 URLs')).toBeVisible();
    const probes = status.getByRole('link', { name: '2 URLs' });
    expect(probes.closest('p')).toHaveTextContent(/^2 URLs the crawler tried for the site’s other versions/);
    // The stats' reasons are the site's own too: the probes' refusals come out.
    await waitFor(() => expect(status.queryByText(/Refused by robots.txt/)).toBeNull());
    // "N URLs crawled" is every URL crawled, as the grid's `fetched` lists them, the probes among them.
    expect(main().getByRole('link', { name: '4,521 URLs' })).toBeVisible();
  });

  it('doesn’t wait for the crawl’s stats, which are slow on a big crawl', async () => {
    sidecar({ stats: 'pending' });
    await open();
    const rows = within(await region('Status codes').findByRole('list', { name: 'Status codes by class' }));
    expect(rows.getByRole('link', { name: /^No response/ })).toHaveTextContent('No HTTP answer');
  });

  it('counts the URLs that weren’t checked as the URL grid does: not crawled, not errors, and said apart', async () => {
    const deadTwin = {
      item: { ...DEAD_TWIN.item, status: 'stopped', finishedAt: null },
      facets: { status: DEAD_TWIN.status },
      notChecked: DEAD_TWIN.notChecked,
    } as const;
    // Without the stats, which are slow: every figure is already what the grid counts.
    sidecar({ ...deadTwin, stats: 'pending' });
    await open();
    const status = await findRegion('Status codes');
    const rows = within(await status.findByRole('list', { name: 'Status codes by class' }));
    expect(status.getByText('6,909 URLs')).toBeVisible();
    expect(rows.getByRole('link', { name: /^No response/ })).toHaveTextContent('13');
    const notChecked = status.getByRole('link', { name: '5 URLs' });
    expect(notChecked.closest('p')).toHaveTextContent(
      '5 URLs weren’t checked: their sites had stopped answering, asked the crawler to slow down, or are on a private network.',
    );
    expect(filtersOf(notChecked)).toEqual([{ column: 'outcome', op: 'in', values: ['notChecked'] }]);
    // The URLs only found, without those 5.
    expect(filtersOf(status.getByRole('link', { name: '140 more URLs' }))).toEqual([
      { column: 'statusClass', op: 'isEmpty' },
      { column: 'outcome', op: 'isEmpty' },
    ]);
    // The crawl's own line and its banner say what the grid opens, not the library's count with the 5 in it.
    const crawled = main().getByRole('link', { name: '6,909 URLs' });
    expect(filtersOf(crawled)).toEqual([{ column: 'fetched', op: 'is', value: true }]);
    expect(main().getByText(/This crawl was stopped after 6,909 URLs/)).toBeVisible();
    expect(main().queryByText(/6,914/)).toBeNull();
  });

  it('takes the URLs that weren’t checked out of the network errors the crawl’s stats count', async () => {
    sidecar({
      item: DEAD_TWIN.item,
      facets: { status: DEAD_TWIN.status },
      notChecked: DEAD_TWIN.notChecked,
      stats: { ...OVERVIEW_STATS, ...DEAD_TWIN.stats },
    });
    await open();
    const rows = within(await region('Status codes').findByRole('list', { name: 'Status codes by class' }));
    // 10 and 3 beside the 13: `/stats` says 8 network errors, 5 of them never asked.
    expect(await rows.findByText('Refused by robots.txt × 10, Network error × 3')).toBeVisible();
  });

  it('shows indexability and why not, depth and response times, each number opening its pages', async () => {
    sidecar();
    await open();
    const indexability = region('Indexability');
    const indexable = await indexability.findByRole('link', { name: /^Indexable/ });
    expect(indexable).toHaveTextContent('264');
    expect(filtersOf(indexable)).toEqual([
      ...SITE_PAGES,
      { column: 'fetched', op: 'is', value: true },
      { column: 'indexability', op: 'in', values: ['none'] },
    ]);
    expect(indexability.getByRole('link', { name: /^Not indexable/ })).toHaveTextContent('10');
    expect(indexability.getByRole('link', { name: /^Answers 4xx/ })).toHaveTextContent('9');

    const depth = region('Pages by depth');
    const two = await depth.findByRole('link', { name: 'Depth 2: 180 pages' });
    expect(filtersOf(two)).toEqual([...ANSWERED, { column: 'depth', op: 'eq', value: 2 }]);

    const response = region('Response times');
    expect(filtersOf(await response.findByRole('link', { name: '250 to 499 ms: 13 pages' }))).toEqual([
      ...ANSWERED,
      { column: 'responseMs', op: 'between', min: 250, max: 499 },
    ]);
    // A band with nothing in it keeps its place and opens nothing.
    expect(response.getByText('500 to 999 ms: 0 pages')).toBeInTheDocument();
    expect(filtersOf(response.getByRole('link', { name: '3 pages' }))).toEqual([
      ...ANSWERED,
      { column: 'responseMs', op: 'gte', value: 1000 },
    ]);
  });

  it('offers to analyse again when the analysis is out of date', async () => {
    const { analyse } = sidecar({
      analysis: { ...ISSUES_ANALYSIS, outOfDate: true, outOfDateReasons: ['crawlChanged'] },
      item: { outOfDate: true },
    });
    await open();
    expect(await main().findByText(/These issues are out of date/)).toBeVisible();
    fireEvent.click(main().getByRole('button', { name: /Analyse again/ }));
    await waitFor(() => expect(analyse).toHaveBeenCalledTimes(1));
  });

  it('shows the alerts kept with the crawl, each with what to do', async () => {
    sidecar({
      alerts: [
        alert({ id: 2, code: 'blocked', data: { host: 'example.com', reason: 'Cloudflare challenge', count: 37 } }),
      ],
    });
    await open();
    const alerts = await findRegion('Alerts');
    expect(await alerts.findByText('The site is blocking the crawl')).toBeVisible();
    // The crawl is over: what it came to, not "so far".
    expect(alerts.getByText('example.com answered with a Cloudflare challenge. 37 responses blocked.')).toBeVisible();
    // Each way on opens where it's done, on its setting.
    expect(alerts.getByRole('link', { name: 'Continue more slowly' })).toHaveAttribute(
      'href',
      `/crawls/${TEST_CRAWL_ID}/settings?focus=speed`,
    );
    expect(alerts.getByRole('link', { name: 'Add headers' })).toHaveAttribute(
      'href',
      `/crawls/${TEST_CRAWL_ID}/settings?focus=headers`,
    );
    expect(alerts.getByRole('link', { name: 'Crawl as another user agent' })).toHaveAttribute(
      'href',
      `/new?from=${TEST_CRAWL_ID}&focus=userAgent`,
    );
  });
});

/** Counts every time an element matching the selector is added to the page, however briefly. */
function watchFor(selector: string) {
  let seen = 0;
  const observer = new MutationObserver((records) => {
    for (const record of records) {
      for (const node of record.addedNodes) {
        if (node instanceof Element && (node.matches(selector) || node.querySelector(selector))) seen++;
      }
    }
  });
  observer.observe(document.body, { childList: true, subtree: true });
  return {
    seen: () => {
      for (const record of observer.takeRecords()) {
        for (const node of record.addedNodes) {
          if (node instanceof Element && (node.matches(selector) || node.querySelector(selector))) seen++;
        }
      }
      return seen;
    },
    stop: () => observer.disconnect(),
  };
}

/** Whether the element with these words is in a live region: what a screen reader reads out as it changes. */
const LIVE_REGION = '[role="status"], [role="alert"], [role="log"], [aria-live="polite"], [aria-live="assertive"]';
const liveRegionOf = (element: HTMLElement) => element.closest(LIVE_REGION);

describe('the Overview when there’s no answer yet, or something’s off', () => {
  it('states the crawl’s status without announcing it: the live bridge does that, once', async () => {
    // Each of these comes with a status change the bridge announces (and toasts, for a stop or a failure). A banner
    // that was a live region too had the news read out two or three times.
    const states: [CrawlStatus, RegExp][] = [
      ['stopped', /This crawl was stopped after/],
      ['interrupted', /This crawl was interrupted after/],
      ['failed', /This crawl failed after/],
      ['inUse', /This crawl is running in another Shouting Iguana window/],
    ];
    for (const [status, words] of states) {
      sidecar({ analysis: null, item: { ...NEVER, status, finishedAt: null } });
      const { unmount } = await openAndKeep();
      const banner = await main().findByText(words);
      expect([status, liveRegionOf(banner)]).toEqual([status, null]);
      unmount();
    }
  });

  it('shows the analysis’s progress without reading out every phase and count as it changes', async () => {
    sidecar({ analysis: null, item: { ...NEVER, status: 'analyzing' } });
    await open();
    await heard(message('analysis', { crawlId: TEST_CRAWL_ID, phase: 'pageRules', done: 152, total: 265 }));
    const progress = await main().findByRole('progressbar', { name: 'Checking each page' });
    expect(liveRegionOf(progress)).toBeNull();
    expect(liveRegionOf(main().getByText(/The crawl is being analysed\./))).toBeNull();
  });

  it('says a crawl hasn’t been analysed, analyses it from here, and still shows the crawl', async () => {
    const { analyse } = sidecar({ analysis: null, item: NEVER });
    await open();
    expect(await main().findByRole('heading', { name: 'This crawl hasn’t been analysed yet' })).toBeVisible();
    expect(await region('Status codes').findByText('4,492')).toBeVisible();
    fireEvent.click(main().getByRole('button', { name: /Analyse it/ }));
    await waitFor(() => expect(analyse).toHaveBeenCalledTimes(1));
  });

  it('keeps the crawl on screen while a never-analysed crawl’s analysis is read again', async () => {
    // Not analysed is an answer that comes as an error, which the query forgets when it's asked again (a stream that
    // reconnected asks for every read again): the whole screen was swapped for its loading frame while it was.
    let slow: ((response: Response) => void) | null = null;
    let asked = 0;
    const notAnalysed = () =>
      new Response(
        JSON.stringify({ type: 'not-analyzed', title: 'This crawl hasn’t been analysed yet.', status: 404 }),
        {
          status: 404,
          headers: { 'Content-Type': 'application/problem+json' },
        },
      );
    setTransport(
      fakeSidecar([
        [
          'GET',
          /^\/api\/crawls\/[0-9a-f]{16}\/analysis$/,
          () => (asked++ === 0 ? notAnalysed() : new Promise<Response>((resolve) => (slow = resolve))),
        ],
        ...overviewRoutes({ analysis: null, item: NEVER }),
      ]),
    );
    const { queryClient } = await open();
    expect(await main().findByRole('heading', { name: 'This crawl hasn’t been analysed yet' })).toBeVisible();
    const codes = await region('Status codes').findByText('4,492');
    const frames = watchFor('[aria-label="Loading the overview"]');

    act(() => void queryClient.invalidateQueries({ queryKey: queryKeys.analysis(TEST_CRAWL_ID) }));
    await waitFor(() => expect(slow).not.toBeNull());
    // Still here, the same elements, with the line that says it's being read again: no frame in their place.
    await act(() => new Promise((resolve) => setTimeout(resolve, 200)));
    expect(codes.isConnected).toBe(true);
    expect(main().getByRole('heading', { name: 'This crawl hasn’t been analysed yet' })).toBeVisible();
    expect(main().getByRole('progressbar', { name: 'Refreshing the overview' })).toBeVisible();
    expect(frames.seen()).toBe(0);

    // The read says the same again: nothing changes but the line going.
    await act(async () => {
      slow?.(notAnalysed());
      await Promise.resolve();
    });
    await waitFor(() => expect(main().queryByRole('progressbar', { name: 'Refreshing the overview' })).toBeNull());
    expect(codes.isConnected).toBe(true);
    expect(frames.seen()).toBe(0);
    frames.stop();
  });

  it('shows the analysis’s phase and progress while it runs, and cancels it', async () => {
    const { cancel } = sidecar({ analysis: null, item: { ...NEVER, status: 'analyzing' } });
    await open();
    await heard(message('analysis', { crawlId: TEST_CRAWL_ID, phase: 'pageRules', done: 152, total: 265 }));
    const progress = await main().findByRole('progressbar', { name: 'Checking each page' });
    expect(progress).toHaveAttribute('aria-valuetext', '152 of 265 pages');
    fireEvent.click(main().getByRole('button', { name: /Cancel/ }));
    await waitFor(() => expect(cancel).toHaveBeenCalledTimes(1));
  });

  it('says how far a stopped crawl got and what was still queued, with Continue', async () => {
    const { proceed } = sidecar({
      analysis: null,
      item: { ...NEVER, status: 'stopped', finishedAt: null },
      stats: { ...OVERVIEW_STATS, queuedCount: 1_417 },
    });
    await open();
    expect(await main().findByText(/This crawl was stopped after 4,521 URLs, with 1,417 still queued\./)).toBeVisible();
    fireEvent.click(main().getAllByRole('button', { name: /Continue/ })[0]!);
    await waitFor(() => expect(proceed).toHaveBeenCalledTimes(1));
  });

  it('takes focus to its title when Continue has worked and its banner goes, rather than drop it', async () => {
    const { proceed } = sidecar({ analysis: null, item: { ...NEVER, status: 'stopped', finishedAt: null } });
    const { queryClient } = await open();
    const resume = (await main().findAllByRole('button', { name: /Continue/ }))[0]!;
    act(() => resume.focus());
    fireEvent.click(resume);
    await waitFor(() => expect(proceed).toHaveBeenCalledTimes(1));
    expect(document.activeElement).toBe(resume);
    // The crawl runs again: the screen is the live crawl, and the banner with the button is gone.
    setStatus(queryClient, 'running');
    await heard(message('progress', progressAt(100, POLITE)));
    expect(main().getByText('Live')).toBeVisible();
    expect(resume.isConnected).toBe(false);
    expect(document.activeElement).toBe(main().getByRole('heading', { level: 1, name: /Overview/ }));
  });

  it('says an interrupted crawl was interrupted, with Continue', async () => {
    sidecar({ analysis: null, item: { ...NEVER, status: 'interrupted', finishedAt: null } });
    await open();
    expect(await main().findByText(/This crawl was interrupted after 4,521 URLs/)).toBeVisible();
    expect(main().getAllByRole('button', { name: /Continue/ }).length).toBeGreaterThan(0);
  });

  it('says why a crawl read no pages, from how its start URL answered, with the next action', async () => {
    sidecar({
      analysis: null,
      facets: EMPTY_FACETS,
      item: { ...NEVER, fetchedUrlCount: 1, pageCount: 0 },
      startUrl: { id: 1, url: 'https://example.com/', statusCode: 403, outcome: 'clientError' },
    });
    await open();
    expect(await main().findByRole('heading', { name: 'This crawl didn’t read any pages' })).toBeVisible();
    expect(await main().findByText(/The start URL answered 403 Forbidden/)).toBeVisible();
    const again = main().getByRole('link', { name: 'Crawl it again' });
    expect(pathOf(again)).toBe('/new');
    expect(searchOf(again).get('url')).toBe('https://example.com/');
    // Never an answer made of nothing: no "no check found an issue", no breakdown of no pages.
    expect(main().queryByText(/hasn’t been analysed/)).toBeNull();
    expect(main().queryByRole('region', { name: 'Pages by depth' })).toBeNull();
    expect(await region('Status codes').findByText('403 × 1')).toBeVisible();
  });

  it('says so when the analysis can’t be read, and still shows the crawl', async () => {
    sidecar({ analysis: 'error' });
    await openFailing();
    expect(await main().findByText('The analysis couldn’t be read.')).toBeVisible();
    expect(main().getByRole('button', { name: 'Try again' })).toBeVisible();
    expect(await region('Status codes').findByText('4,492')).toBeVisible();
  });

  it('says so in each breakdown that can’t be read, and still shows the answer', async () => {
    sidecar({ facets: 'error' });
    await openFailing();
    expect(await region('Status codes').findByText('Status codes couldn’t be read.')).toBeVisible();
    expect(await region('Indexability').findByText('Indexability couldn’t be read.')).toBeVisible();
    expect(region('Fix first').getByRole('link', { name: /Error:\s?Internal URL returns 4xx/ })).toBeVisible();
  });

  it('says a file that can’t be read can’t be read, in the engine’s plain words', async () => {
    sidecar({
      analysis: null,
      item: { ...NEVER, status: 'unreadable', error: 'Another program has this crawl file open.' },
    });
    await open();
    expect(await main().findByRole('heading', { name: 'This crawl’s file can’t be read' })).toBeVisible();
    expect(main().getByText(/Another program has this crawl file open\./)).toBeVisible();
  });
});

const POLITE: CrawlScript = { rate: 2, size: 6_286, maxUrls: 10_000 };
const LIVE_ITEM = { ...NEVER, status: 'running', finishedAt: null } as const;

describe('the Overview while the crawl runs', () => {
  const progress = (seconds: number, script: CrawlScript = POLITE) => message('progress', progressAt(seconds, script));
  const cell = (label: string) => region('Progress').getByText(label).closest<HTMLElement>('.si-stat')!;
  const figure = (label: string) =>
    within(cell(label)).getByText(/^[\d,.–]+$/, { selector: '.si-stat__value > .figures > span:first-child' });

  it('is live: crawled, found, queued and pages, the rate, the time so far and the time left', async () => {
    sidecar({ analysis: null, item: LIVE_ITEM, history: historyUntil(60, POLITE) });
    await open();
    await heard(...Array.from({ length: 121 }, (_, index) => progress(index / 2)));
    expect(main().getByText('Live')).toBeVisible();
    expect(figure('Crawled')).toHaveTextContent('120');
    expect(figure('Found')).toHaveTextContent('481');
    expect(cell('Elapsed')).toHaveTextContent('1 min');
    // 9,880 of the limit to go at 2 a second.
    expect(cell('Elapsed')).toHaveTextContent('At this pace, done within 1 h 22 min');
    expect(region('Progress').getByRole('img', { name: /^URLs a second over the last 1 min: / })).toBeVisible();
    expect(
      region('Found and crawled').getByRole('img', {
        name: /^Found and crawled URLs of the site over 1 min of crawling: /,
      }),
    ).toBeVisible();
    expect(region('Status codes').getByText('120 URLs so far')).toBeVisible();
    expect(region('Alerts').getByText('None so far')).toBeVisible();
  });

  // Round 5's polish: the card said Queued 92,111 ("5 on their way") and the bar under it Queued 92,116.
  it('says one Queued: the card and the bar under the chart count the URLs on their way alike', async () => {
    sidecar({ analysis: null, item: LIVE_ITEM, history: historyUntil(60, POLITE) });
    await open();
    await heard(...Array.from({ length: 121 }, (_, index) => progress(index / 2)));
    const last = progressAt(60, POLITE);
    const queued = (last.queued + last.inFlight).toLocaleString('en-GB');
    expect(last.inFlight).toBeGreaterThan(0);
    expect(figure('Queued')).toHaveTextContent(queued);
    expect(cell('Queued')).toHaveTextContent(`${last.inFlight} on their way`);
    const bar =
      main()
        .getByLabelText(/^Of [\d,]+ URLs found: /)
        .getAttribute('aria-label') ?? '';
    expect(/, ([\d,]+) queued,/.exec(bar)?.[1]).toBe(queued);
  });

  it('draws at most 4 times a second, and updates its figures in place: nothing is added, removed or moved', async () => {
    sidecar({ analysis: null, item: LIVE_ITEM });
    await open();
    await heard(progress(99.5), progress(100));
    const card = main().getByRole('region', { name: 'Progress' });
    const crawled = figure('Crawled');
    expect(crawled).toHaveTextContent('200');
    // Every figure's cell and its label, value and context line: the elements themselves, to tell them moved or remade.
    const parts = () => [...card.querySelectorAll('.si-stat, .si-stat > *')];
    const before = parts();
    expect(before).toHaveLength(6 * 4);

    // A burst of events between two draws: none of them shows until the store publishes, then only the last does.
    act(() => {
      for (let seconds = 100.5; seconds <= 120; seconds += 0.5) liveStore.ingest(progress(seconds));
    });
    expect(crawled).toHaveTextContent('200');
    await heard();
    // The same element, with the new number: updated in place.
    expect(crawled).toHaveTextContent('240');
    expect(crawled.isConnected).toBe(true);
    const after = parts();
    expect(after).toHaveLength(before.length);
    expect(after.every((element, index) => element === before[index])).toBe(true);
    // Each row of figures has a context line with room for two lines, so a line that wraps moves nothing.
    expect(card.querySelectorAll('.si-stat__context .min-h-8').length).toBeGreaterThanOrEqual(3);
  });

  it('shows a dash, never 0, for a figure the stream hasn’t given', async () => {
    sidecar({ analysis: null, item: LIVE_ITEM });
    await open();
    expect(await region('Progress').findByText('Crawled')).toBeVisible();
    expect(figure('Crawled')).toHaveTextContent('–');
    await heard(message('progress', { crawlId: TEST_CRAWL_ID, status: 'running', fetched: 12, queued: 'lots' }));
    expect(figure('Crawled')).toHaveTextContent('12');
    expect(figure('Queued')).toHaveTextContent('–');
  });

  it('is its full size before the crawl’s first figures, each a dash, so nothing moves when they come', async () => {
    sidecar({ analysis: null, item: LIVE_ITEM });
    await open();
    expect(await region('Progress').findByText('Crawled')).toBeVisible();
    // The status codes: every row it will have, not a line of words in their place (the cards under it moved 270 px
    // when the rows came).
    const status = region('Status codes');
    const rows = () => within(status.getByRole('list', { name: 'Status codes by class' })).getAllByRole('listitem');
    const before = rows();
    expect(before.map((row) => row.textContent.replace(/\s+/g, ' ').trim())).toEqual([
      '2xx –',
      '3xx –',
      '4xx –',
      '5xx –',
      'No response –',
      'Blocked –',
    ]);
    expect(status.getByText('Waiting for the crawl’s first figures')).toBeVisible();
    // The chart's box, its key and the bar of what was found are there too.
    const progressCard = region('Found and crawled');
    const chart = progressCard.getByRole('img', { name: /^Found and crawled URLs of the site: waiting/ });
    const found = progressCard.getByRole('img', { name: 'What was found: waiting for the crawl’s first figures' });
    const key = progressCard.getByText('On the site').closest('.si-legend');
    expect(key).toHaveTextContent('Found –');
    expect(key).toHaveTextContent('Crawled –');
    const card = main().getByRole('region', { name: 'Progress' });
    const cells = () => [...card.querySelectorAll('.si-stat, .si-stat > *')];
    const cellsBefore = cells();

    await heard(progress(0.5), progress(1));
    // The same elements, filled in.
    const after = rows();
    expect(after).toHaveLength(before.length);
    expect(after.every((row, index) => row === before[index])).toBe(true);
    expect(after[0]).toHaveTextContent('2xx');
    expect(after[0]).not.toHaveTextContent('–');
    expect(chart.isConnected).toBe(true);
    expect(chart).toHaveAccessibleName(/^Found and crawled URLs of the site over 1 s of crawling: /);
    expect(found.isConnected).toBe(true);
    expect(found).toHaveAccessibleName('Of 9 URLs found: 2 crawled, 8 queued, 0 not followed');
    expect(key?.isConnected).toBe(true);
    expect(key).not.toHaveTextContent('–');
    expect(cells().every((element, index) => element === cellsBefore[index])).toBe(true);
  });

  it('says nothing of a URL limit until the crawl has said whether it has one', async () => {
    sidecar({ analysis: null, item: LIVE_ITEM });
    await open();
    expect(await region('Progress').findByText('Elapsed')).toBeVisible();
    // Before the first progress, and from an engine that doesn't say: no claim either way.
    expect(cell('Elapsed')).not.toHaveTextContent(/limit/i);
    const { maxUrls: _limit, ...unsaid } = progressAt(1, POLITE);
    await heard(message('progress', unsaid));
    expect(figure('Crawled')).toHaveTextContent('2');
    expect(cell('Elapsed')).not.toHaveTextContent(/limit/i);
    // It has none: said, and only then.
    await heard(message('progress', { ...progressAt(1.5, POLITE), maxUrls: null }));
    expect(cell('Elapsed')).toHaveTextContent('No URL limit, so no end time');
    // It has one: the pace is being worked out.
    await heard(message('progress', progressAt(2, POLITE)));
    expect(cell('Elapsed')).toHaveTextContent('Working out the pace');
  });

  it('links its figures and its status codes to the URLs, which work while it crawls', async () => {
    sidecar({ analysis: null, item: LIVE_ITEM });
    await open();
    await heard(progress(600));
    expect(filtersOf(region('Progress').getByRole('link', { name: /Crawled/ }))).toEqual([
      { column: 'fetched', op: 'is', value: true },
    ]);
    expect(filtersOf(region('Progress').getByRole('link', { name: /Pages/ }))).toEqual(ANSWERED);
    const classes = within(region('Status codes').getByRole('list', { name: 'Status codes by class' }));
    expect(filtersOf(classes.getByRole('link', { name: /^4xx/ }))).toEqual([
      { column: 'statusClass', op: 'in', values: ['4xx'] },
      NOT_BLOCKED,
    ]);
  });

  it('counts the URLs that weren’t checked as the URL grid does while it runs: not crawled, and said apart', async () => {
    sidecar({ analysis: null, item: LIVE_ITEM });
    await open();
    await heard(message('progress', { ...progressAt(60, POLITE), ...DEAD_TWIN.progress }));
    // 6,914 outcomes stored, 5 of them never asked: what "fetched is true" opens is 6,909.
    expect(figure('Crawled')).toHaveTextContent('6,909');
    const status = region('Status codes');
    expect(status.getByText('6,909 URLs so far')).toBeVisible();
    const classes = within(status.getByRole('list', { name: 'Status codes by class' }));
    const noResponse = classes.getByRole('link', { name: /^No response/ });
    expect(noResponse).toHaveTextContent('13');
    expect(noResponse).toHaveTextContent('Refused by robots.txt × 10, Network error × 3');
    const notChecked = status.getByRole('link', { name: '5 URLs' });
    expect(notChecked.closest('p')).toHaveTextContent(
      '5 URLs weren’t checked: their sites had stopped answering, asked the crawler to slow down, or are on a private network.',
    );
    expect(filtersOf(notChecked)).toEqual([{ column: 'outcome', op: 'in', values: ['notChecked'] }]);
    // What was found still adds up: 6,909 + 140 + 5.
    expect(
      region('Found and crawled').getByRole('img', {
        name: 'Of 7,054 URLs found: 6,909 crawled, 0 queued, 140 not followed, 5 not checked',
      }),
    ).toBeVisible();
  });

  it('leaves the probes of the site’s other versions out of its status codes: an http-only site has no network error', async () => {
    sidecar({ analysis: null, item: LIVE_ITEM });
    await open();
    // Three seconds into a crawl of an http-only site: the https probe got no answer, the www probe redirected.
    const event = progressAt(3, POLITE);
    const classes = event.statusClasses!;
    await heard(
      message('progress', {
        ...event,
        fetched: event.fetched + 2,
        crawled: event.fetched + 2,
        statusClasses: { ...classes, '3xx': classes['3xx'] + 1, noResponse: classes.noResponse + 1 },
        statusCodes: { ...event.statusCodes, '301': (event.statusCodes?.['301'] ?? 0) + 1 },
        noResponse: { ...event.noResponse!, networkError: 1 },
        siteVersions: {
          fetched: 2,
          statusClasses: { '2xx': 0, '3xx': 1, '4xx': 0, '5xx': 0, other: 0, noResponse: 1, blocked: 0 },
          statusCodes: { '301': 1 },
          noResponse: { robotsDisallowed: 0, timeout: 0, networkError: 1, notChecked: 0, processingError: 0 },
        },
      }),
    );
    // Crawled is every URL the grid has as fetched, the probes among them.
    expect(figure('Crawled')).toHaveTextContent(formatCount(event.fetched + 2));
    const status = region('Status codes');
    expect(status.getByText(`${formatCount(event.fetched)} URLs so far`)).toBeVisible();
    expect(status.queryByText(/Network error/)).toBeNull();
    const list = within(status.getByRole('list', { name: 'Status codes by class' }));
    expect(list.getByText('No response').closest('li')).toHaveTextContent('None so far');
    const probes = status.getByRole('link', { name: '2 URLs' });
    expect(probes.closest('p')).toHaveTextContent(
      '2 URLs the crawler tried for the site’s other versions (http or https, www or not) aren’t counted here: nothing on the site leads to them.',
    );
    expect(filtersOf(probes)).toEqual([
      { column: 'source', op: 'in', values: ['siteVersion'] },
      { column: 'outcome', op: 'isNotEmpty' },
    ]);
    expect(filtersOf(list.getByRole('link', { name: /^2xx/ }))).toEqual([
      { column: 'statusClass', op: 'in', values: ['2xx'] },
      NOT_BLOCKED,
      { column: 'source', op: 'notIn', values: ['siteVersion'] },
    ]);
  });

  it('keeps the probes’ line’s room from the start, so nothing below the status codes moves when they answer', async () => {
    sidecar({ analysis: null, item: LIVE_ITEM });
    await open();
    await heard(progress(1));
    const room = region('Status codes').getByText(/the crawler tried for the site’s other versions/);
    expect(room).toHaveAttribute('aria-hidden', 'true');
    expect(room).toHaveClass('invisible');
  });

  // A progress event `wall` seconds after the run began, with the counts the crawl has `crawling` seconds in.
  const at = (wall: number, crawling: number, extra: Record<string, unknown> = {}) => {
    const event = progressAt(crawling, POLITE);
    return message('progress', {
      ...event,
      elapsedMs: wall * 1_000,
      at: new Date(Date.parse(progressAt(0, POLITE).at ?? '') + wall * 1_000).toISOString(),
      ...extra,
    });
  };

  it('keeps one clock: Elapsed and the time left go by the time spent crawling, which a pause is no part of', async () => {
    sidecar({ analysis: null, item: LIVE_ITEM, history: historyUntil(60, POLITE) });
    await open();
    await heard(...Array.from({ length: 121 }, (_, index) => progress(index / 2)));
    expect(cell('Elapsed')).toHaveTextContent('1 min');
    expect(cell('Elapsed')).toHaveTextContent('At this pace, done within 1 h 22 min');
    // Paused for 45 s: progress keeps arriving, and the engine's own count of the run goes on.
    await heard(...Array.from({ length: 90 }, (_, index) => at(60.5 + index / 2, 60, { status: 'paused' })));
    expect(cell('Elapsed')).toHaveTextContent('Paused');
    expect(cell('Elapsed')).not.toHaveTextContent('1 min 4');
    // Resumed: no 45 s of nothing in the pace, so the time left is as it was, at once, and Elapsed goes on from 1 min.
    await heard(at(105.5, 60), at(106, 60.5), at(106.5, 61), at(107, 61.5), at(107.5, 62));
    expect(cell('Elapsed')).toHaveTextContent(/1 min 2 s|1 min 3 s/);
    expect(cell('Elapsed')).not.toHaveTextContent('1 min 4');
    expect(cell('Elapsed')).toHaveTextContent('At this pace, done within 1 h 22 min');
    // And the chart's time is the same clock.
    expect(
      region('Found and crawled').getByRole('img', {
        name: /^Found and crawled URLs of the site over 1 min [23] s of crawling: /,
      }),
    ).toBeVisible();
  });

  it('is of the run being watched when the crawl was continued: its time, its chart, and the pace', async () => {
    // Ten minutes of crawling three days ago, counted back from the file, then this run, 4 s old.
    const first = Date.parse('2026-10-29T10:15:00.000Z');
    const runStart = Date.parse(progressAt(0, POLITE).at ?? '');
    const filePoint = (ms: number, fetched: number) => ({
      at: new Date(ms).toISOString(),
      fetched,
      htmlPages: fetched,
      internalFetched: fetched,
      internalFound: 3_000,
      errors: 0,
      blocked: 0,
    });
    sidecar({
      analysis: null,
      item: LIVE_ITEM,
      history: {
        crawlId: TEST_CRAWL_ID,
        startedAt: new Date(first).toISOString(),
        liveFrom: new Date(runStart).toISOString(),
        points: [
          ...Array.from({ length: 11 }, (_, index) => filePoint(first + index * 60_000, index * 120)),
          { ...filePoint(runStart, 1_200), elapsedMs: 0 },
        ],
      },
    });
    await open();
    await heard(
      ...Array.from({ length: 9 }, (_, index) =>
        at(index / 2, 0, { fetched: 1_200 + index, internalFetched: 1_200 + index, internalFound: 3_000 }),
      ),
    );
    expect(cell('Elapsed')).toHaveTextContent('4 s');
    const chart = region('Found and crawled');
    expect(chart.getByText('The site’s URLs over the 4 s since it was continued')).toBeVisible();
    expect(
      chart.getByRole('img', { name: /^Found and crawled URLs of the site over the 4 s since it was continued: / }),
    ).toBeVisible();
    // Not the three days and ten minutes since it first started.
    expect(main().queryByText(/72 h|3 days|10 min/)).toBeNull();
  });

  it('says the time the crawl has spent crawling, every run’s, from the engine’s own count: never jumps for the history', async () => {
    // A crawl continued 4 s ago after ten minutes of crawling; its history never answers.
    sidecar({ analysis: null, item: LIVE_ITEM, history: 'pending' });
    await open();
    await heard(
      ...Array.from({ length: 9 }, (_, index) =>
        at(index / 2, 0, { fetched: 1_200 + index, crawlingMs: 600_000 + index * 500 }),
      ),
    );
    expect(cell('Elapsed')).toHaveTextContent('10 min 4 s');
  });

  it('draws a continued crawl whole on the time it spent crawling, the days between runs closed, each start marked', async () => {
    // Ten minutes of crawling three days ago, with a minute's pause in it, counted back from the file; then this run.
    const first = Date.parse('2026-10-29T10:15:00.000Z');
    const runStart = Date.parse(progressAt(0, POLITE).at ?? '');
    const filePoint = (ms: number, fetched: number) => ({
      at: new Date(ms).toISOString(),
      fetched,
      htmlPages: fetched,
      internalFetched: fetched,
      internalFound: 3_000,
      errors: 0,
      blocked: 0,
    });
    const iso = (ms: number) => new Date(ms).toISOString();
    sidecar({
      analysis: null,
      item: LIVE_ITEM,
      history: {
        crawlId: TEST_CRAWL_ID,
        startedAt: iso(first),
        liveFrom: iso(runStart),
        points: [
          // 120 a minute while it crawled, nothing in the paused minute (4 to 5 min in).
          ...Array.from({ length: 12 }, (_, index) =>
            filePoint(first + index * 60_000, Math.min(index, 4) * 120 + Math.max(0, index - 5) * 120),
          ),
          { ...filePoint(runStart, 1_200), elapsedMs: 0 },
        ],
        crawlingMs: 600_000,
        runs: [
          {
            startedAt: iso(first),
            endedAt: iso(first + 660_000),
            crawlingMs: 600_000,
            crawling: [
              { from: iso(first), to: iso(first + 240_000) },
              { from: iso(first + 300_000), to: iso(first + 660_000) },
            ],
          },
          { startedAt: iso(runStart), endedAt: null, crawlingMs: 0, crawling: [] },
        ],
      },
    });
    await open();
    await heard(
      ...Array.from({ length: 9 }, (_, index) =>
        at(index / 2, 0, { fetched: 1_200 + index, internalFetched: 1_200 + index, internalFound: 3_000 }),
      ),
    );
    // One clock: Elapsed and the chart's axis are the crawl's time spent crawling, its earlier run's ten minutes first.
    expect(cell('Elapsed')).toHaveTextContent('10 min 4 s');
    const chart = region('Found and crawled');
    expect(chart.getByText('The site’s URLs over 10 min 4 s of crawling, in 2 runs')).toBeVisible();
    expect(
      chart.getByRole('img', {
        name: 'Found and crawled URLs of the site over 10 min 4 s of crawling, in 2 runs, continued at 10 min of it: 3,000 found, 1,208 crawled',
      }),
    ).toBeVisible();
    // This run's start is marked, once.
    expect(
      main().getByRole('region', { name: 'Found and crawled' }).querySelectorAll('.si-chart__marker'),
    ).toHaveLength(1);
    // Not the three days since it first started, nor the minute it was paused.
    expect(main().queryByText(/72 h|3 days|11 min/)).toBeNull();
  });

  it('says the limit is reached, not that there’s no time left, while the last requests finish', async () => {
    sidecar({ analysis: null, item: LIVE_ITEM });
    await open();
    const limited: CrawlScript = { rate: 100, size: 40_000, maxUrls: 3_000 };
    await heard(
      ...Array.from({ length: 61 }, (_, index) => message('progress', progressAt(index / 2, limited))),
      message('progress', { ...progressAt(30.5, limited), phase: 'crawling' }),
    );
    expect(figure('Crawled')).toHaveTextContent('3,000');
    expect(cell('Elapsed')).toHaveTextContent('At its URL limit: the last requests are finishing');
    expect(cell('Elapsed')).not.toHaveTextContent(/About 0 s left|Working out the pace/);
  });

  it('carries Resume when the crawl is paused', async () => {
    const { resume } = sidecar({ analysis: null, item: LIVE_ITEM });
    await open();
    await heard(message('progress', progressAt(100, { ...POLITE, status: 'paused' })));
    expect(main().getByText(/The crawl is paused\./)).toBeVisible();
    fireEvent.click(main().getByRole('button', { name: /Resume/ }));
    await waitFor(() => expect(resume).toHaveBeenCalledTimes(1));
  });

  it('states that it’s paused or stopping without announcing it: the live bridge does that, once', async () => {
    sidecar({ analysis: null, item: LIVE_ITEM });
    await open();
    await heard(message('progress', progressAt(100, { ...POLITE, status: 'paused' })));
    expect(liveRegionOf(main().getByText(/The crawl is paused\./))).toBeNull();
    await heard(message('progress', progressAt(100.5, { ...POLITE, status: 'stopping' })));
    expect(liveRegionOf(main().getByText(/Stopping: the requests on their way are finishing\./))).toBeNull();
    // Nothing in the screen reads the status out: no live region in it has those words.
    const regions = [...(document.getElementById('main')?.querySelectorAll(LIVE_REGION) ?? [])];
    expect(regions.filter((region) => /paused|stopping/i.test(region.textContent)).length).toBe(0);
  });

  it('takes focus to its title when Resume has worked and its banner goes, rather than drop it', async () => {
    sidecar({ analysis: null, item: LIVE_ITEM });
    await open();
    await heard(message('progress', progressAt(100, { ...POLITE, status: 'paused' })));
    const button = main().getByRole('button', { name: /Resume/ });
    act(() => button.focus());
    fireEvent.click(button);
    // It runs again: the banner, and the button in it, are gone.
    await heard(message('progress', progressAt(100.5, POLITE)));
    expect(main().queryByText(/The crawl is paused\./)).toBeNull();
    expect(button.isConnected).toBe(false);
    expect(document.activeElement).toBe(main().getByRole('heading', { level: 1, name: /Overview/ }));
  });

  it('keeps focus in the top bar’s controls: Pause hands it to Resume, and Stop, with nothing left, to the view', async () => {
    sidecar({ analysis: null, item: LIVE_ITEM });
    const { queryClient } = await open();
    await heard(message('progress', progressAt(100, POLITE)));
    const bar = within(document.querySelector('header')!);
    // By its own name: More (the bar at its narrowest) names Pause and Stop too.
    const pause = bar.getByRole('button', { name: /^Pause/ });
    act(() => pause.focus());
    fireEvent.click(pause);
    await heard(message('progress', progressAt(100.5, { ...POLITE, status: 'paused' })));
    expect(pause.isConnected).toBe(false);
    expect(document.activeElement).toBe(bar.getByRole('button', { name: /^Resume/ }));
    // Stop: while the crawl is stopping the bar has no control at all.
    const stop = bar.getByRole('button', { name: /^Stop/ });
    act(() => stop.focus());
    fireEvent.click(stop);
    setStatus(queryClient, 'stopping');
    await heard(message('progress', progressAt(101, { ...POLITE, status: 'stopping' })));
    expect(stop.isConnected).toBe(false);
    expect(document.activeElement).toBe(document.getElementById('main'));
  });

  it('leaves focus alone when what goes didn’t have it', async () => {
    sidecar({ analysis: null, item: LIVE_ITEM });
    await open();
    await heard(message('progress', progressAt(100, { ...POLITE, status: 'paused' })));
    // On a figure that stays, and then nowhere at all (a click on empty space): neither is a loss to make good.
    const crawled = region('Progress').getByRole('link', { name: /Crawled/ });
    act(() => crawled.focus());
    await heard(message('progress', progressAt(100.5, POLITE)));
    expect(document.activeElement).toBe(crawled);
    await heard(message('progress', progressAt(101, { ...POLITE, status: 'paused' })));
    act(() => crawled.blur());
    await heard(message('progress', progressAt(101.5, POLITE)));
    expect(document.activeElement).toBe(document.body);
  });

  it('shows an alert as it’s raised, with what to do, and what holds the crawl up now', async () => {
    sidecar({ analysis: null, item: LIVE_ITEM });
    await open();
    await heard(
      progress(100),
      message(
        'alert',
        alert({ id: 3, code: 'blocked', data: { host: 'example.com', reason: 'Cloudflare challenge', count: 1 } }),
      ),
      message('progress', {
        ...progressAt(100.5, POLITE),
        waits: [{ reason: 'crawlDelay', host: 'example.com', durationMs: 5_000 }],
      }),
    );
    const alerts = region('Alerts');
    expect(within(alerts.getByRole('log')).getByText('The site is blocking the crawl')).toBeVisible();
    expect(alerts.getByText(/Stop the crawl and continue it more slowly/)).toBeVisible();
    expect(
      region('Holding it up now').getByText('example.com asks for 5 s between requests in its robots.txt.'),
    ).toBeVisible();
  });

  it('makes no claim of how many responses a host has blocked “so far” while the crawl runs', async () => {
    // The count is the file's, read once when the screen opens (`GET /alerts`): 37 then. The crawl goes on being
    // blocked, the alert isn't raised again for the host, and nothing asks again, so "37 so far" stood for as long
    // as the screen was open, beside a Blocked class that had long passed it.
    const blocked = alert({
      id: 2,
      code: 'blocked',
      data: { host: 'example.com', reason: 'Cloudflare challenge', count: 37 },
    });
    const { requests } = sidecar({ analysis: null, item: LIVE_ITEM, alerts: [blocked] });
    await open();
    await heard(progress(100));
    const alerts = region('Alerts');
    expect(await alerts.findByText('The site is blocking the crawl')).toBeVisible();
    // Ten minutes on, with 600 blocked responses by the crawl's own count.
    const later = progressAt(700, POLITE);
    await heard(
      message('progress', { ...later, blocked: 600, statusClasses: { ...later.statusClasses, blocked: 600 } }),
    );
    expect(requests.filter((request) => request.path.endsWith('/alerts'))).toHaveLength(1);
    expect(
      within(region('Status codes').getByRole('list', { name: 'Status codes by class' })).getByRole('link', {
        name: /^Blocked/,
      }),
    ).toHaveTextContent('600');
    expect(alerts.queryByText(/so far/)).toBeNull();
    expect(alerts.queryByText(/37 responses/)).toBeNull();
    expect(alerts.getByText('example.com answered with a Cloudflare challenge.')).toBeVisible();
  });

  it('reads out an alert as it’s raised, the first included, and not its age again every minute', async () => {
    sidecar({ analysis: null, item: LIVE_ITEM });
    await open();
    await heard(progress(100));
    const alerts = region('Alerts');
    // The log is there before any alert, empty: an alert that arrived together with its log wouldn't be read out.
    const log = alerts.getByRole('log');
    expect(log).toBeEmptyDOMElement();
    expect(alerts.getByText('None so far')).toBeVisible();
    expect(log.contains(alerts.getByText('None so far'))).toBe(false);
    await heard(
      message('alert', alert({ id: 1, code: 'sitemaps', level: 'info', data: { count: 4, urlCount: 6_146 } })),
    );
    // The same log, with the alert added to it.
    expect(alerts.getByRole('log')).toBe(log);
    expect(within(log).getByText('Sitemaps read')).toBeVisible();
    expect(alerts.queryByText('None so far')).toBeNull();
    // Its age is rewritten by the clock every minute ("1 minute ago", "2 minutes ago"): no part of what the log reads.
    const age = log.querySelector('time');
    expect(age).not.toBeNull();
    expect(age?.closest('[aria-live]')).toHaveAttribute('aria-live', 'off');
    expect(log.contains(age?.closest('[aria-live]') ?? null)).toBe(true);
  });

  it('becomes the analysis’s progress, then the answer, without a reload', async () => {
    let analysed = false;
    const transport = fakeSidecar([
      [
        'GET',
        /^\/api\/crawls\/[0-9a-f]{16}$/,
        () =>
          Response.json(
            testCrawlItem(
              analysed ? { fetchedUrlCount: 4_521, pageCount: 265 } : { ...LIVE_ITEM, fetchedUrlCount: 200 },
            ),
          ),
      ],
      [
        'GET',
        /^\/api\/crawls\/[0-9a-f]{16}\/analysis$/,
        () =>
          analysed
            ? Response.json(ISSUES_ANALYSIS)
            : new Response(
                JSON.stringify({ type: 'not-analyzed', title: 'This crawl hasn’t been analysed yet.', status: 404 }),
                {
                  status: 404,
                  headers: { 'Content-Type': 'application/problem+json' },
                },
              ),
      ],
      ...overviewRoutes({ analysis: null, item: LIVE_ITEM }),
    ]);
    setTransport(transport);
    const { queryClient } = await open();
    await heard(progress(100));
    expect(main().getByText('Live')).toBeVisible();
    // From here to the answer the screen is never swapped for its loading frame: not as the crawl ends, when its
    // analysis is asked for the first time, nor as the analysis ends, when it's asked again.
    const frames = watchFor('[aria-label="Loading the overview"]');

    // Focus is on one of the live crawl's figures, which go with it.
    const crawled = region('Progress').getByRole('link', { name: /Crawled/ });
    act(() => crawled.focus());

    // The crawl ends and its analysis starts in the same breath, as the sidecar reports them.
    setStatus(queryClient, 'analyzing');
    await heard(
      message('crawl', { crawlId: TEST_CRAWL_ID, status: 'completed' }),
      message('crawl', { crawlId: TEST_CRAWL_ID, status: 'analyzing' }),
      message('analysis', { crawlId: TEST_CRAWL_ID, phase: 'reading', done: 0, total: 4_521 }),
    );
    expect(main().queryByText('Live')).toBeNull();
    expect(await main().findByRole('progressbar', { name: 'Reading the crawl' })).toBeVisible();
    // The figure that had focus went with the live crawl: focus is on the title, not dropped, and not on Cancel,
    // which nobody asked for.
    expect(crawled.isConnected).toBe(false);
    expect(document.activeElement).toBe(main().getByRole('heading', { level: 1, name: /Overview/ }));

    // The analysis ends: the live layer refreshes the crawl's reads, and the answer is there.
    analysed = true;
    setStatus(queryClient, 'completed', { analyzedAt: '2026-11-01T10:21:00Z' });
    await heard(message('crawl', { crawlId: TEST_CRAWL_ID, status: 'completed' }));
    await act(() => queryClient.invalidateQueries({ queryKey: queryKeys.crawl(TEST_CRAWL_ID) }));
    expect(
      await main().findByText('3 checks found 11 issues on this crawl. 1 passed and 1 was skipped.'),
    ).toBeVisible();
    expect(region('Fix first').getByRole('link', { name: /Error:\s?Internal URL returns 4xx/ })).toBeVisible();
    expect(frames.seen()).toBe(0);
    frames.stop();
  });
});
