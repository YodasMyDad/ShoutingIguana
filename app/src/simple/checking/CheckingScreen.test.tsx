import type { QueryClient } from '@tanstack/react-query';
import { createMemoryHistory } from '@tanstack/react-router';
import { act, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { queryKeys } from '../../api/queries';
import { setTransport } from '../../api/transport';
import type { CrawlListItem, CrawlStatus } from '../../api/types';
import { App } from '../../app/App';
import { createQueryClient } from '../../app/queryClient';
import { createAppRouter } from '../../app/router';
import { liveStore, PUBLISH_INTERVAL_MS } from '../../live/liveStore';
import { replaceSettings } from '../../settings/settingsStore';
import { fakeSidecar, TEST_CRAWL_ID } from '../../test/fakeSidecar';
import {
  alert,
  historyUntil,
  message,
  overviewRoutes,
  progressAt,
  type CrawlScript,
  type OverviewSidecarOptions,
} from '../../test/overviewSidecar';
import { TABLE_STUBBED } from '../tableStubbed';

const OVERVIEW = `/crawls/${TEST_CRAWL_ID}/overview`;
const LIVE_ITEM = { analyzedAt: null, issues: null, status: 'running', finishedAt: null } as const;
const POLITE: CrawlScript = { rate: 2, size: 6_286, maxUrls: null };

async function open(options: OverviewSidecarOptions = {}) {
  replaceSettings({ mode: 'simple' });
  setTransport(fakeSidecar(overviewRoutes({ analysis: null, item: LIVE_ITEM, ...options })));
  const queryClient = createQueryClient();
  const router = createAppRouter(queryClient, createMemoryHistory({ initialEntries: [OVERVIEW] }));
  render(<App router={router} queryClient={queryClient} />);
  await act(() => router.load());
  return { queryClient };
}

async function heard(...events: ReturnType<typeof message>[]) {
  await act(async () => {
    events.forEach((event) => liveStore.ingest(event));
    await new Promise((resolve) => setTimeout(resolve, PUBLISH_INTERVAL_MS + 50));
  });
}

function setStatus(queryClient: QueryClient, status: CrawlStatus, extra: Partial<CrawlListItem> = {}) {
  act(() => {
    queryClient.setQueryData<CrawlListItem>(queryKeys.crawlItem(TEST_CRAWL_ID), (item) =>
      item ? { ...item, ...extra, status } : item,
    );
  });
}

const main = () => within(document.getElementById('main') ?? document.body);
const progress = (seconds: number, extra: object = {}) =>
  message('progress', { ...progressAt(seconds, POLITE), ...extra });
const figure = (label: string) =>
  main()
    .getByText(label, { selector: '.si-stat__label' })
    .closest<HTMLElement>('.si-stat')!
    .querySelector('.si-stat__value');

afterEach(() => {
  replaceSettings({ mode: 'advanced' });
  liveStore.reset();
});

describe.skipIf(TABLE_STUBBED)('checking a site, in Simple', () => {
  it('marks the first step, and takes its three figures from one progress event', async () => {
    await open({ history: historyUntil(60, POLITE) });
    await heard(progress(100));
    const event = progressAt(100, POLITE);
    expect(await main().findByRole('heading', { level: 1, name: 'Checking example.com' })).toBeVisible();
    const steps = main().getByRole('list', { name: 'Steps' });
    expect(
      within(steps)
        .getAllByRole('listitem')
        .map((step) => step.getAttribute('aria-current')),
    ).toEqual(['step', null, null]);
    // Addresses checked is what the URL grid counts as crawled: the URLs that weren't checked aren't in it.
    expect(figure('Addresses checked')).toHaveTextContent(String(event.fetched - (event.noResponse?.notChecked ?? 0)));
    expect(figure('Pages read')).toHaveTextContent(String(event.htmlPages));
    // Waiting their turn and on their way, as Advanced's Queued counts them (liveFigures.queuedSoFar).
    expect(figure('Still to check')).toHaveTextContent(String(event.queued + event.inFlight));
    expect(main().getByText(/^At least \d+ minutes to go\.$/)).toBeVisible();
    expect(main().getByText(/Nothing so far\./)).toBeVisible();
    // No rate chart and no status codes: they're Advanced's.
    expect(main().queryByRole('region', { name: 'Status codes' })).toBeNull();
  });

  it('says what’s in the way in plain words, with what to do, landing where Advanced’s alert does', async () => {
    await open();
    await heard(
      progress(100),
      message(
        'alert',
        alert({
          id: 1,
          code: 'blocked',
          level: 'error',
          at: '2026-11-01T10:17:00.000Z',
          data: { host: 'example.com' },
        }),
      ),
    );
    expect(
      await main().findByText(/The site is turning the check away: its security takes it for a robot\./),
    ).toBeVisible();
    const gently = main().getByRole('link', { name: 'Continue more gently (Polite), in Advanced' });
    expect(new URL((gently as HTMLAnchorElement).href).pathname).toBe(`/crawls/${TEST_CRAWL_ID}/settings`);
    expect(new URL((gently as HTMLAnchorElement).href).searchParams.get('focus')).toBe('speed');
    expect(main().getByRole('button', { name: 'What to tell whoever runs the site' })).toBeVisible();
    expect(main().queryByText(/Nothing so far\./)).toBeNull();
  });

  it('says the pages read before the site turned the check away as they were then, not as they grow', async () => {
    await open();
    await heard(
      progress(100, { htmlPages: 312 }),
      message(
        'alert',
        alert({
          id: 1,
          code: 'blocked',
          level: 'error',
          at: '2026-11-01T10:17:00.000Z',
          data: { host: 'example.com' },
        }),
      ),
    );
    expect(await main().findByText(/312 pages were read first\./)).toBeVisible();
    await heard(progress(110, { htmlPages: 340 }));
    // Another alert redraws what's in the way, with the pages read by now.
    await heard(
      progress(120, { htmlPages: 365 }),
      message('alert', alert({ id: 2, code: 'sitemaps-failed', at: '2026-11-01T10:19:00.000Z' })),
    );
    expect(main().getByText(/312 pages were read first\./)).toBeVisible();
    expect(main().queryByText(/365 pages were read first/)).toBeNull();
  });

  // The closing audit's gap 25: a Polite check said the site was slow in its first seconds, its https probe failing.
  it('says no slowdown before the site has answered enough to judge it by', async () => {
    await open();
    await heard(
      progress(2, {
        fetched: 3,
        siteVersions: { fetched: 2, statusClasses: {}, statusCodes: {}, noResponse: {} },
        waits: [{ reason: 'backoff', host: 'example.com', durationMs: 500 }],
      }),
    );
    expect(main().queryByText(/The site asked the check to slow down/)).toBeNull();
    await heard(progress(8, { fetched: 14, waits: [{ reason: 'backoff', host: 'example.com', durationMs: 500 }] }));
    expect(await main().findByText(/The site asked the check to slow down/)).toBeVisible();
  });

  it('says a slowdown from the progress’s waits', async () => {
    await open();
    await heard(progress(100, { waits: [{ reason: 'backoff', host: 'example.com', durationMs: 4000 }] }));
    expect(
      await main().findByText('The site asked the check to slow down, so it has. It will take longer.'),
    ).toBeVisible();
  });

  it('tells the person a browser window is waiting for them when the site wants to see a person', async () => {
    await open();
    await heard(progress(40, { waits: [{ reason: 'humanCheck', host: 'example.com', durationMs: 240_000 }] }));
    expect(
      await main().findByText(
        'The site wants to see that you’re a person. A browser window has opened on its page, in front of this one: do what it asks there, and reading the site carries on by itself. If you can’t see it, look for the browser on the taskbar or the Dock. Closing the window leaves the site’s pages unread.',
      ),
    ).toBeVisible();
  });

  // Views 06 and 31 of gallery 7: Still to check 0 and "Nearly done" while Advanced said 1,041 /p/ URLs were left.
  it('counts what a section the site limits still holds, and never says "Nearly done" while it does', async () => {
    await open();
    await heard(
      progress(100, {
        queued: 0,
        inFlight: 1041,
        urlsPerSecond: 0.2,
        waits: [{ reason: 'sectionLimited', host: 'example.com', urls: 1041, durationMs: 4000, section: '/p/' }],
      }),
      message(
        'alert',
        alert({
          id: 1,
          code: 'section-rate-limited',
          level: 'info',
          at: '2026-11-01T10:17:00.000Z',
          message: 'example.com limits its /p/ pages (429 Too Many Requests), not the rest of the site.',
          data: { host: 'example.com', url: '/p/' },
        }),
      ),
    );
    expect(figure('Still to check')).toHaveTextContent('1,041');
    expect(main().queryByText('Nearly done.')).toBeNull();
    expect(main().getByText('Reading the last pages slowly.')).toBeVisible();
    // Said once, though the engine's notice and the wait both say it.
    expect(await main().findAllByText(/The site only lets some of its pages be read slowly/)).toHaveLength(1);
    expect(main().queryByText(/429/)).toBeNull();
  });

  it('says Paused on its first step, with a banner and Resume', async () => {
    const { queryClient } = await open();
    await heard(progress(100));
    setStatus(queryClient, 'paused');
    await heard(message('crawl', { crawlId: TEST_CRAWL_ID, status: 'paused' }));
    expect(await main().findByText('Paused. Nothing is lost.')).toBeVisible();
    expect(main().getByRole('button', { name: 'Resume' })).toBeVisible();
    const steps = main().getByRole('list', { name: 'Steps' });
    expect(within(steps).getAllByRole('listitem')[0]).toHaveTextContent('Paused');
  });

  it('works out the results on its second step, with the phase, its progress and Cancel, the figures kept', async () => {
    const { queryClient } = await open();
    await heard(progress(100));
    setStatus(queryClient, 'analyzing');
    await heard(
      message('crawl', { crawlId: TEST_CRAWL_ID, status: 'completed' }),
      message('crawl', { crawlId: TEST_CRAWL_ID, status: 'analyzing' }),
      message('analysis', { crawlId: TEST_CRAWL_ID, phase: 'pageRules', done: 120, total: 265 }),
    );
    expect(
      await main().findByRole('heading', { level: 1, name: 'Working out the results for example.com' }),
    ).toBeVisible();
    const steps = within(main().getByRole('list', { name: 'Steps' })).getAllByRole('listitem');
    expect(steps[1]).toHaveAttribute('aria-current', 'step');
    expect(steps[0]).toHaveTextContent('Read your site, done');
    const bar = main().getByRole('progressbar', { name: 'Checking each page' });
    expect(bar).toHaveAttribute('aria-valuetext', '120 of 265 pages');
    expect(main().getByRole('button', { name: 'Cancel' })).toBeVisible();
    expect(figure('Pages read')).toHaveTextContent(String(progressAt(100, POLITE).htmlPages));
  });

  it('becomes the Summary in place when it ends, focus on its title, never dropped', async () => {
    const { queryClient } = await open();
    await heard(progress(100));
    setStatus(queryClient, 'analyzing');
    await heard(
      message('crawl', { crawlId: TEST_CRAWL_ID, status: 'completed' }),
      message('crawl', { crawlId: TEST_CRAWL_ID, status: 'analyzing' }),
    );
    const cancel = await main().findByRole('button', { name: 'Cancel' });
    act(() => cancel.focus());
    setStatus(queryClient, 'completed', {
      analyzedAt: '2026-11-01T10:21:00Z',
      issues: { error: 1, warning: 1, notice: 1 },
    });
    await heard(message('crawl', { crawlId: TEST_CRAWL_ID, status: 'completed' }));
    await waitFor(() => expect(main().queryByRole('list', { name: 'Steps' })).toBeNull());
    await waitFor(() => expect(document.activeElement?.tagName).toBe('H1'));
    expect(screen.queryByRole('heading', { level: 1, name: /Checking|Working out/ })).toBeNull();
    // The title is the Summary's (part B's), not Advanced's Overview, which stood in before the merge.
    expect(document.activeElement).toHaveClass('si-simple-title');
    expect(screen.queryByRole('heading', { level: 1, name: 'Overview' })).toBeNull();
  });
});
