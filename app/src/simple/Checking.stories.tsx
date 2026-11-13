import type { Meta, StoryObj } from '@storybook/react-vite';
import { useEffect } from 'react';
import { liveStore } from '../live/liveStore';
import { TEST_CRAWL_ID } from '../test/fakeSidecar';
import {
  alert,
  historyUntil,
  message,
  overviewRoutes,
  progressAt,
  progressUntil,
  type CrawlScript,
  type OverviewSidecarOptions,
} from '../test/overviewSidecar';
import { AT_200_PERCENT, ownSidecar, ScreenApp } from '../test/ScreenStory';

const meta = { title: 'Screens/Simple/Checking a site' } satisfies Meta;
export default meta;
type Story = StoryObj<typeof meta>;

const OVERVIEW = `/crawls/${TEST_CRAWL_ID}/overview`;
/** The garden crawl's pace: about 2 addresses a second, no limit. */
const POLITE: CrawlScript = { rate: 2, size: 6_286, maxUrls: null };
const NEVER = { analyzedAt: null, issues: null } as const;

/** A check running here: its item, its history as far as `seconds`, and every progress event up to then. */
function running(seconds: number, script: CrawlScript, options: OverviewSidecarOptions = {}) {
  const now = progressAt(seconds, script);
  ownSidecar(
    overviewRoutes({
      analysis: null,
      history: historyUntil(seconds, script),
      ...options,
      item: {
        ...NEVER,
        status: script.status ?? 'running',
        finishedAt: null,
        fetchedUrlCount: now.fetched,
        pageCount: now.htmlPages,
        ...options.item,
      },
    }),
  );
  progressUntil(seconds - 0.5, { ...script, status: 'running' }).forEach((event) => liveStore.ingest(event));
  liveStore.ingest(message('progress', now));
}

/** Progress from here on, one event every half second, as the engine sends them. */
function Stream({ from, script, children }: { from: number; script: CrawlScript; children: React.ReactNode }) {
  useEffect(() => {
    let seconds = from;
    const timer = setInterval(() => {
      seconds += 0.5;
      liveStore.ingest(message('progress', progressAt(seconds, script)));
    }, 500);
    return () => clearInterval(timer);
  }, [from, script]);
  return children;
}

/** The garden crawl 22 minutes in: the first step, three figures, one bar and the floor on time left. */
export const Checking: Story = {
  loaders: [() => running(1_346, POLITE)],
  render: () => (
    <Stream from={1_346} script={POLITE}>
      <ScreenApp path={OVERVIEW} mode="simple" />
    </Stream>
  ),
};

/** Just started: no progress yet. Every figure is a dash, never 0, and the time left says it's being worked out. */
export const JustStarted: Story = {
  loaders: [
    () =>
      ownSidecar(
        overviewRoutes({
          analysis: null,
          item: { ...NEVER, status: 'running', finishedAt: null, fetchedUrlCount: 0, pageCount: 0 },
        }),
      ),
  ],
  render: () => <ScreenApp path={OVERVIEW} mode="simple" />,
};

/** Paused: the first step says so, and a banner carries Resume. */
export const Paused: Story = {
  loaders: [() => running(1_346, { ...POLITE, status: 'paused' })],
  render: () => <ScreenApp path={OVERVIEW} mode="simple" />,
};

/** Stopping: the pages on their way finish first. */
export const Stopping: Story = {
  loaders: [() => running(1_346, { ...POLITE, status: 'stopping' })],
  render: () => <ScreenApp path={OVERVIEW} mode="simple" />,
};

/**
 * Something in the way, in plain words with what to do: the site turning the check away, its rules file not
 * answering, the start address forwarding to another site, and the site answering slowly (a wait, from the progress).
 */
export const InTheWay: Story = {
  loaders: [
    () => {
      running(1_346, POLITE);
      liveStore.ingest(
        message('progress', {
          ...progressAt(1_346.5, POLITE),
          waits: [{ reason: 'backoff', host: 'example.com', durationMs: 4_000 }],
        }),
      );
      for (const heard of [
        alert({
          id: 1,
          code: 'start-redirects-away',
          at: '2026-11-01T10:15:02.000Z',
          data: { url: 'https://example.org/' },
        }),
        alert({
          id: 2,
          code: 'robots-unreachable',
          at: '2026-11-01T10:21:10.000Z',
          data: { origin: 'https://example.com' },
        }),
        alert({
          id: 3,
          code: 'blocked',
          level: 'error',
          at: '2026-11-01T10:30:42.000Z',
          data: { host: 'example.com', reason: 'Cloudflare challenge', count: 1 },
        }),
      ]) {
        liveStore.ingest(message('alert', heard));
      }
    },
  ],
  render: () => <ScreenApp path={OVERVIEW} mode="simple" />,
};

/** Working out the results: the second step, with the analysis's phase, its progress and Cancel; the figures stay. */
export const WorkingOutTheResults: Story = {
  loaders: [
    () => {
      running(1_346, { ...POLITE, status: 'analyzing' });
      liveStore.ingest(message('crawl', { crawlId: TEST_CRAWL_ID, status: 'analyzing' }));
      liveStore.ingest(message('analysis', { crawlId: TEST_CRAWL_ID, phase: 'pageRules', done: 152, total: 265 }));
    },
  ],
  render: () => <ScreenApp path={OVERVIEW} mode="simple" />,
};

/** A 1280x720 window at 200% zoom: the steps wrap, the figures stand one under another, the two cards stack. */
export const At200Percent: Story = {
  loaders: [() => running(1_346, POLITE)],
  render: () => <ScreenApp path={OVERVIEW} mode="simple" size={AT_200_PERCENT} />,
};
