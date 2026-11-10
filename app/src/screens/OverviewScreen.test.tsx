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
import { updateSettings } from '../settings/settingsStore';
import { TEST_CRAWL_ID, fakeSidecar, testCrawlItem } from '../test/fakeSidecar';
import { ISSUES_ANALYSIS } from '../test/issuesSidecar';
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

async function open(path = OVERVIEW) {
  const queryClient = createQueryClient();
  const router = createAppRouter(queryClient, createMemoryHistory({ initialEntries: [path] }));
  render(<App router={router} queryClient={queryClient} />);
  await act(() => router.load());
  // The first load's skeleton has gone: the screen is in the state the test is about.
  await waitFor(() => expect(document.querySelector('[aria-label="Loading the overview"]')).toBeNull());
  return { router, queryClient };
}

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
];

let uninstall: () => void = () => undefined;
beforeEach(() => {
  uninstall = installKeyboard();
});
afterEach(() => {
  uninstall();
  updateSettings({ hiddenChecks: [] });
  liveStore.reset();
  setTransport(fakeSidecar([]));
});

describe('the Overview of an analysed crawl', () => {
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

  it('lists what to fix first, the most severe then the most pages, each opening its check', async () => {
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
  });

  it('leaves a hidden check out of the figures and Fix first, as the sidebar does', async () => {
    updateSettings({ hiddenChecks: ['internal-client-error'] });
    sidecar();
    await open();
    expect(await region('Issues by severity').findByRole('link', { name: /Errors/ })).toHaveTextContent('0 checks');
    expect(region('Fix first').queryByRole('link', { name: /Internal URL returns 4xx/ })).toBeNull();
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
    expect(skipped.getByRole('link', { name: 'Orphan page' })).toBeVisible();
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
      '5 URLs weren’t checked because their sites had stopped answering.',
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
    expect(alerts.getByText(/37 responses blocked so far/)).toBeVisible();
    expect(pathOf(alerts.getByRole('link', { name: 'Open the crawl’s settings' }))).toBe(
      `/crawls/${TEST_CRAWL_ID}/settings`,
    );
  });
});

describe('the Overview when there’s no answer yet, or something’s off', () => {
  it('says a crawl hasn’t been analysed, analyses it from here, and still shows the crawl', async () => {
    const { analyse } = sidecar({ analysis: null, item: NEVER });
    await open();
    expect(await main().findByRole('heading', { name: 'This crawl hasn’t been analysed yet' })).toBeVisible();
    expect(await region('Status codes').findByText('4,492')).toBeVisible();
    fireEvent.click(main().getByRole('button', { name: /Analyse it/ }));
    await waitFor(() => expect(analyse).toHaveBeenCalledTimes(1));
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
    await open();
    expect(await main().findByText('The analysis couldn’t be read.')).toBeVisible();
    expect(main().getByRole('button', { name: 'Try again' })).toBeVisible();
    expect(await region('Status codes').findByText('4,492')).toBeVisible();
  });

  it('says so in each breakdown that can’t be read, and still shows the answer', async () => {
    sidecar({ facets: 'error' });
    await open();
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
      region('Found and crawled').getByRole('img', { name: /^Found and crawled URLs of the site over 1 min: / }),
    ).toBeVisible();
    expect(region('Status codes').getByText('120 URLs so far')).toBeVisible();
    expect(region('Alerts').getByText('None so far')).toBeVisible();
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
      '5 URLs weren’t checked because their sites had stopped answering.',
    );
    expect(filtersOf(notChecked)).toEqual([{ column: 'outcome', op: 'in', values: ['notChecked'] }]);
    // What was found still adds up: 6,909 + 140 + 5.
    expect(
      region('Found and crawled').getByRole('img', {
        name: 'Of 7,054 URLs found: 6,909 crawled, 0 queued, 140 not followed, 5 not checked',
      }),
    ).toBeVisible();
  });

  it('carries Resume when the crawl is paused', async () => {
    const { resume } = sidecar({ analysis: null, item: LIVE_ITEM });
    await open();
    await heard(message('progress', progressAt(100, { ...POLITE, status: 'paused' })));
    expect(main().getByText(/The crawl is paused\./)).toBeVisible();
    fireEvent.click(main().getByRole('button', { name: /Resume/ }));
    await waitFor(() => expect(resume).toHaveBeenCalledTimes(1));
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

    // The crawl ends and its analysis starts in the same breath, as the sidecar reports them.
    setStatus(queryClient, 'analyzing');
    await heard(
      message('crawl', { crawlId: TEST_CRAWL_ID, status: 'completed' }),
      message('crawl', { crawlId: TEST_CRAWL_ID, status: 'analyzing' }),
      message('analysis', { crawlId: TEST_CRAWL_ID, phase: 'reading', done: 0, total: 4_521 }),
    );
    expect(main().queryByText('Live')).toBeNull();
    expect(await main().findByRole('progressbar', { name: 'Reading the crawl' })).toBeVisible();

    // The analysis ends: the live layer refreshes the crawl's reads, and the answer is there.
    analysed = true;
    setStatus(queryClient, 'completed', { analyzedAt: '2026-11-01T10:21:00Z' });
    await heard(message('crawl', { crawlId: TEST_CRAWL_ID, status: 'completed' }));
    await act(() => queryClient.invalidateQueries({ queryKey: queryKeys.crawl(TEST_CRAWL_ID) }));
    expect(
      await main().findByText('3 checks found 11 issues on this crawl. 1 passed and 1 was skipped.'),
    ).toBeVisible();
    expect(region('Fix first').getByRole('link', { name: /Error:\s?Internal URL returns 4xx/ })).toBeVisible();
  });
});
