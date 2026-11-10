// The Overview's alerts list with a great many alerts (a site with hundreds of hosts, each blocking the crawl, or a
// sitemap index of hundreds of sitemaps that can't be read, raises one each): how much of it is drawn, and how often.
// Apart from OverviewScreen.test.tsx because it counts the callouts' renders, which takes a mock of the component.
import { createMemoryHistory } from '@tanstack/react-router';
import { act, fireEvent, render, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { setTransport } from '../api/transport';
import { App } from '../app/App';
import { createQueryClient } from '../app/queryClient';
import { createAppRouter } from '../app/router';
import { installKeyboard } from '../keyboard/registry';
import { PUBLISH_INTERVAL_MS, liveStore } from '../live/liveStore';
import { TEST_CRAWL_ID, fakeSidecar } from '../test/fakeSidecar';
import { alert, message, overviewRoutes, progressAt, type CrawlScript } from '../test/overviewSidecar';
import type * as CalloutModule from '../ui/Callout';

const calloutRenders = vi.hoisted(() => ({ count: 0 }));
vi.mock('../ui/Callout', async (importOriginal) => {
  const actual = await importOriginal<typeof CalloutModule>();
  return {
    ...actual,
    Callout: (props: Parameters<typeof actual.Callout>[0]) => {
      calloutRenders.count++;
      return actual.Callout(props);
    },
  };
});

const OVERVIEW = `/crawls/${TEST_CRAWL_ID}/overview`;
const POLITE: CrawlScript = { rate: 2, size: 6_286, maxUrls: 10_000 };
const LIVE_ITEM = { analyzedAt: null, issues: null, status: 'running', finishedAt: null } as const;
const MANY = 150;

/** One alert per host, a second apart: the newest is the last. */
const blockedBy = (count: number) =>
  Array.from({ length: count }, (_, index) =>
    alert({
      id: index + 1,
      code: 'blocked',
      at: new Date(Date.parse('2026-11-01T10:16:00.000Z') + index * 1_000).toISOString(),
      data: { host: `shop${index + 1}.example.com`, reason: 'Cloudflare challenge', count: 1 },
    }),
  );

async function open() {
  const queryClient = createQueryClient();
  const router = createAppRouter(queryClient, createMemoryHistory({ initialEntries: [OVERVIEW] }));
  render(<App router={router} queryClient={queryClient} />);
  await act(() => router.load());
  await waitFor(() => expect(document.querySelector('[aria-label="Loading the overview"]')).toBeNull());
}

async function heard(...events: ReturnType<typeof message>[]) {
  await act(async () => {
    events.forEach((event) => liveStore.ingest(event));
    await new Promise((resolve) => setTimeout(resolve, PUBLISH_INTERVAL_MS + 50));
  });
}

const alerts = () =>
  within(within(document.getElementById('main') ?? document.body).getByRole('region', { name: 'Alerts' }));

let uninstall: () => void = () => undefined;
beforeEach(() => {
  uninstall = installKeyboard();
  calloutRenders.count = 0;
});
afterEach(() => {
  uninstall();
  liveStore.reset();
  setTransport(fakeSidecar([]));
});

describe('the Overview’s alerts, when there are a great many', () => {
  it('draws the newest twenty, the rest a press away, and reads out only what’s new', async () => {
    setTransport(fakeSidecar(overviewRoutes({ analysis: null, item: LIVE_ITEM })));
    await open();
    await heard(
      message('progress', progressAt(100, POLITE)),
      ...blockedBy(MANY).map((raised) => message('alert', raised)),
    );
    const list = alerts();
    expect(list.getAllByText('The site is blocking the crawl')).toHaveLength(20);
    // The newest first.
    expect(list.getAllByRole('listitem')[0]).toHaveTextContent(`shop${MANY}.example.com`);
    const all = list.getByRole('button', { name: `Show all ${MANY} alerts` });
    expect(all).toHaveAttribute('aria-expanded', 'false');
    act(() => all.focus());
    fireEvent.click(all);
    expect(list.getAllByText('The site is blocking the crawl')).toHaveLength(MANY);
    // The same button, still focused, now folds them away again.
    expect(document.activeElement).toBe(all);
    expect(all).toHaveAttribute('aria-expanded', 'true');
    expect(all).toHaveAccessibleName('Show the newest 20 only');
    // The log holds the newest twenty only: showing the earlier ones isn't 130 alerts read out.
    expect(within(list.getByRole('log')).getAllByText('The site is blocking the crawl')).toHaveLength(20);
    fireEvent.click(all);
    expect(list.getAllByText('The site is blocking the crawl')).toHaveLength(20);
  });

  it('isn’t drawn again as the crawl’s figures change, only when an alert is raised', async () => {
    setTransport(fakeSidecar(overviewRoutes({ analysis: null, item: LIVE_ITEM })));
    await open();
    await heard(
      message('progress', progressAt(100, POLITE)),
      ...blockedBy(MANY).map((raised) => message('alert', raised)),
    );
    expect(alerts().getAllByText('The site is blocking the crawl').length).toBeGreaterThan(0);
    calloutRenders.count = 0;
    // Four ticks of progress, two seconds of crawling, and no new alert.
    for (let tick = 1; tick <= 4; tick++) await heard(message('progress', progressAt(100 + tick / 2, POLITE)));
    expect(calloutRenders.count).toBe(0);
    // A new alert draws what changed.
    await heard(message('alert', alert({ id: MANY + 1, code: 'sitemaps-failed', at: '2026-11-01T11:00:00.000Z' })));
    expect(alerts().getByText('The sitemaps couldn’t be read')).toBeVisible();
    expect(calloutRenders.count).toBeGreaterThan(0);
    expect(calloutRenders.count).toBeLessThanOrEqual(21);
  });

  it('draws the newest twenty of a finished crawl’s alerts too, with the rest a press away', async () => {
    setTransport(fakeSidecar(overviewRoutes({ alerts: blockedBy(MANY) })));
    await open();
    await waitFor(() => expect(alerts().getAllByText('The site is blocking the crawl')).toHaveLength(20));
    const list = alerts();
    fireEvent.click(list.getByRole('button', { name: `Show all ${MANY} alerts` }));
    expect(list.getAllByText('The site is blocking the crawl')).toHaveLength(MANY);
  });
});
