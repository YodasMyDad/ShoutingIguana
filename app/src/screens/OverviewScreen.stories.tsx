import type { Meta, StoryObj } from '@storybook/react-vite';
import type { QueryClient } from '@tanstack/react-query';
import { createMemoryHistory } from '@tanstack/react-router';
import { useEffect, useState } from 'react';
import { api } from '../api/client';
import { queryKeys } from '../api/queries';
import { setTransport } from '../api/transport';
import type { Analysis } from '../api/types';
import { App } from '../app/App';
import { createQueryClient } from '../app/queryClient';
import { createAppRouter } from '../app/router';
import { liveStore } from '../live/liveStore';
import { updateSettings } from '../settings/settingsStore';
import { setDirectConnection } from '../shell/connection';
import { TEST_CRAWL_ID, fakeSidecar, json } from '../test/fakeSidecar';
import { ISSUES_ANALYSIS, ISSUES_CHECKS } from '../test/issuesSidecar';
import {
  DEAD_TWIN,
  DEEP_SITE_PAGES,
  EMPTY_FACETS,
  OVERVIEW_STATS,
  alert,
  historyUntil,
  message,
  overviewRoutes,
  progressAt,
  progressUntil,
  type CrawlScript,
  type OverviewSidecarOptions,
} from '../test/overviewSidecar';

const meta = { title: 'Screens/Overview' } satisfies Meta;
export default meta;
type Story = StoryObj<typeof meta>;

/** The crawls in the design fixtures, as the mock sidecar serves them. */
const GARDEN = '/crawls/62cb879ef60639d5/overview';
const SYNTHETIC = '/crawls/5d66706ffdaef5e0/overview';
/** The crawl of the stories' own sidecars. */
const OVERVIEW = `/crawls/${TEST_CRAWL_ID}/overview`;

const connect = () => setDirectConnection({ baseUrl: 'mock://sidecar', token: 'mock', generation: 1, version: 'mock' });

/** Every story starts clean: nothing hidden, nothing live. */
function fresh() {
  updateSettings({ hiddenChecks: [], hiddenChecksByCrawl: {} });
  liveStore.reset();
}

/** A sidecar of the story's own, for the states the design fixtures can't show. */
function own(options: OverviewSidecarOptions = {}) {
  fresh();
  setTransport(fakeSidecar(overviewRoutes(options)));
  connect();
}

interface Stream {
  script: CrawlScript;
  /** Where in the crawl the stream carries on from, in seconds. */
  from: number;
}

/** A crawl running here: its item, its history as far as `seconds`, and every progress event up to then. */
function running(seconds: number, script: CrawlScript, options: OverviewSidecarOptions = {}) {
  const now = progressAt(seconds, script);
  own({
    analysis: null,
    history: historyUntil(seconds, script),
    ...options,
    item: {
      status: script.status ?? 'running',
      finishedAt: null,
      analyzedAt: null,
      issues: null,
      fetchedUrlCount: now.fetched,
      pageCount: now.htmlPages,
      ...options.item,
    },
  });
  // It ran until now, whatever its status is now: a crawl paused or stopping was running until it was.
  progressUntil(seconds - 0.5, { ...script, status: 'running' }).forEach((event) => liveStore.ingest(event));
  liveStore.ingest(message('progress', now));
}

function OverviewApp({
  path = OVERVIEW,
  stream,
  refreshAfterMs,
}: {
  path?: string;
  /** Progress events from here on, one every half second, as the engine sends them. */
  stream?: Stream;
  refreshAfterMs?: number;
}) {
  const [app] = useState(() => {
    const queryClient: QueryClient = createQueryClient();
    return { queryClient, router: createAppRouter(queryClient, createMemoryHistory({ initialEntries: [path] })) };
  });
  useEffect(() => {
    if (!stream) return undefined;
    let seconds = stream.from;
    const timer = setInterval(() => {
      seconds += 0.5;
      liveStore.ingest(message('progress', progressAt(seconds, stream.script)));
    }, 500);
    return () => clearInterval(timer);
  }, [stream]);
  useEffect(() => {
    if (refreshAfterMs === undefined) return undefined;
    const timer = setTimeout(
      () => void app.queryClient.invalidateQueries({ queryKey: queryKeys.crawl(TEST_CRAWL_ID) }),
      refreshAfterMs,
    );
    return () => clearTimeout(timer);
  }, [app, refreshAfterMs]);
  return (
    <div className="h-screen">
      <App router={app.router} queryClient={app.queryClient} />
    </div>
  );
}

// ------------------------------------------------------------------------------------------------------ The answer

/** The garden crawl over the mock: the answer, Fix first, the categories, what was skipped, and the crawl itself. */
export const Garden: Story = {
  loaders: [fresh],
  render: () => <OverviewApp path={GARDEN} />,
};

/** The 100k synthetic crawl over the mock: every status class, every reason a page isn't indexable. */
export const Synthetic: Story = {
  loaders: [fresh],
  render: () => <OverviewApp path={SYNTHETIC} />,
};

/** The answer on a small crawl, from the stories' own sidecar: a partial analysis, with its note. */
export const Done: Story = {
  loaders: [() => own()],
  render: () => <OverviewApp />,
};

/** First load: the answer's own layout in skeletons, after 150 ms. */
export const FirstLoad: Story = {
  loaders: [() => own({ analysis: 'pending' })],
  render: () => <OverviewApp />,
};

/** The answer is there; the crawl's breakdowns are still on their way, each card in its own skeleton. */
export const BreakdownsLoading: Story = {
  loaders: [() => own({ facets: 'pending' })],
  render: () => <OverviewApp />,
};

/** Refreshing: everything is read again behind what's shown; a thin line, nothing blanks. */
export const Refreshing: Story = {
  loaders: [
    () => {
      let calls = 0;
      fresh();
      setTransport(
        fakeSidecar([
          [
            'GET',
            /^\/api\/crawls\/[0-9a-f]{16}\/analysis$/,
            () => (calls++ === 0 ? json(ISSUES_ANALYSIS) : new Promise<Response>(() => undefined)),
          ],
          ...overviewRoutes(),
        ]),
      );
      connect();
    },
  ],
  render: () => <OverviewApp refreshAfterMs={400} />,
};

const ALL_PASSED: Analysis = {
  ...ISSUES_ANALYSIS,
  isPartial: false,
  notes: [],
  counts: ISSUES_CHECKS.checks.map((check) => ({ checkId: check.id, count: 0 })),
  skipped: [],
};

/** No issues at all: every check passed, said plainly, and the crawl's breakdowns below. */
/** The crawl's line says how long it spent crawling (E3c's `crawlingMs`), pauses and the time between runs left out. */
export const DoneCrawlTime: Story = {
  loaders: [() => own({ item: { crawlingMs: 2_475_000 } })],
  render: () => <OverviewApp />,
};

/** A crawl an earlier version made, which didn't keep its time: the line says it isn't known, never a guess. */
export const DoneCrawlTimeUnknown: Story = {
  loaders: [() => own({ item: { crawlingMs: null } })],
  render: () => <OverviewApp />,
};

/**
 * Done, with the probes of the site's other versions read from their rows in the URL grid: the same status codes as
 * live, the site's own, and the line saying the probes apart.
 */
export const DoneWithProbes: Story = {
  loaders: [
    () =>
      own({
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
      }),
  ],
  render: () => <OverviewApp />,
};

/**
 * The answer as the step 3b sidecar counts it: the things to change lead, with those in the template, and each
 * severity card says its own.
 */
export const DoneWithThingsToChange: Story = {
  loaders: [
    () =>
      own({
        analysis: {
          ...ISSUES_ANALYSIS,
          counts: ISSUES_ANALYSIS.counts.map((count) => ({
            ...count,
            causes: count.count === 0 ? null : count.checkId === 'empty-anchor-text' ? 1 : count.count,
            templateCauses: count.count === 0 ? null : count.checkId === 'empty-anchor-text' ? 1 : 0,
            clears: [],
          })),
        },
      }),
  ],
  render: () => <OverviewApp />,
};

export const NoIssues: Story = {
  loaders: [() => own({ analysis: ALL_PASSED, item: { issues: { error: 0, warning: 0, notice: 0 } } })],
  render: () => <OverviewApp />,
};

/** Out of date: the crawl was continued after its analysis. A banner offers to analyse again. */
export const OutOfDate: Story = {
  loaders: [
    () =>
      own({
        analysis: { ...ISSUES_ANALYSIS, outOfDate: true, outOfDateReasons: ['crawlChanged'] },
        item: { outOfDate: true },
      }),
  ],
  render: () => <OverviewApp />,
};

/** Alerts from while it crawled, kept with the crawl: they explain what's missing. */
export const DoneWithAlerts: Story = {
  loaders: [
    () =>
      own({
        alerts: [
          alert({
            id: 1,
            code: 'sitemaps-not-read',
            data: { url: 'https://example.com/sitemap-news.xml', reason: '503 Service Unavailable' },
          }),
          alert({
            id: 2,
            code: 'blocked',
            at: '2026-11-01T10:17:30.000Z',
            data: { host: 'example.com', reason: 'Cloudflare challenge', count: 37 },
          }),
        ],
      }),
  ],
  render: () => <OverviewApp />,
};

// ------------------------------------------------------------------------------------------------- While it crawls

/** The garden crawl's pace: 2 URLs a second, towards a 10,000-URL limit. */
const POLITE: CrawlScript = { rate: 2, size: 6_286, maxUrls: 10_000 };
/** A local site at the engine's full speed: about 2,900 URLs a second. */
const FULL_SPEED: CrawlScript = { rate: 2_900, size: 2_000_000 };

const POLITE_STREAM: Stream = { script: POLITE, from: 1_346 };
const FULL_SPEED_STREAM: Stream = { script: FULL_SPEED, from: 120 };

/** A crawl started in the mock sidecar, replaying the garden crawl's real progress: live, analysed, then the answer. */
export const MockCrawl: Story = {
  loaders: [fresh],
  render: () => <StartedCrawl />,
};

function StartedCrawl() {
  const [path, setPath] = useState<string | null>(null);
  useEffect(() => {
    let wanted = true;
    void api.startCrawl({ options: { startUrl: 'https://www.gardenfurnituresets.co.uk/' } }).then((item) => {
      if (wanted) setPath(`/crawls/${item.id}/overview`);
    });
    return () => {
      wanted = false;
    };
  }, []);
  return path ? <OverviewApp path={path} /> : <div className="h-screen bg-canvas" />;
}

/** Live, 22 minutes in at 2 URLs a second, with a URL limit: so there's a time left, as the longest it can take. */
export const Live: Story = {
  loaders: [() => running(1_346, POLITE)],
  render: () => <OverviewApp stream={POLITE_STREAM} />,
};

/**
 * Live at the engine's full speed, about 2,900 URLs a second, two minutes in: a progress event every half second, the
 * figures drawn at most 4 times a second, in place. No URL limit, so no time left.
 */
export const LiveFullSpeed: Story = {
  loaders: [() => running(120, FULL_SPEED)],
  render: () => <OverviewApp stream={FULL_SPEED_STREAM} />,
};

/** Just started: the crawl's item says running, and no progress has arrived yet. Every figure is a dash, never 0. */
export const LiveJustStarted: Story = {
  loaders: [
    () =>
      own({
        analysis: null,
        item: { status: 'running', finishedAt: null, analyzedAt: null, issues: null, fetchedUrlCount: 0, pageCount: 0 },
      }),
  ],
  render: () => <OverviewApp />,
};

/** Paused: the figures stand still, and the banner carries Resume (F7). */
export const LivePaused: Story = {
  loaders: [() => running(1_346, { ...POLITE, status: 'paused' })],
  render: () => <OverviewApp />,
};

/** Stopping: the requests on their way finish first. */
export const LiveStopping: Story = {
  loaders: [() => running(1_346, { ...POLITE, status: 'stopping' })],
  render: () => <OverviewApp />,
};

/** The limit is in sight: the crawl has found as many URLs as it may fetch, so the time left is what's left. */
export const LiveLimitReached: Story = {
  loaders: [() => running(600, { rate: 12, size: 40_000, maxUrls: 10_000 })],
  render: () => <OverviewApp />,
};

/** At the limit: every URL it may fetch is fetched, and it says so, rather than "about 0 s left" or a pace. */
export const LiveAtLimit: Story = {
  loaders: [() => running(840, { rate: 12, size: 40_000, maxUrls: 10_000 })],
  render: () => <OverviewApp />,
};

/** A crawl continued three days after it was stopped, four minutes into this run. */
const CONTINUED_AFTER = 600;
const CONTINUED_FOR = 240;

/**
 * Continued: the screen's times are this run's. Elapsed is the four minutes it has spent crawling since, the chart
 * starts where this run did, at the counts it started with (the ten minutes of crawling three days ago aren't on its
 * time axis, nor the days between), and the pace is this run's.
 */
export const LiveContinued: Story = {
  loaders: [
    () => {
      const runStart = Date.parse(progressAt(CONTINUED_AFTER, POLITE).at ?? '');
      const firstStart = runStart - 3 * 86_400_000;
      // This run's events: the counts carry on from the first run's, the engine's `elapsedMs` starts again.
      const event = (seconds: number) => ({
        ...progressAt(CONTINUED_AFTER + seconds, POLITE),
        elapsedMs: seconds * 1_000,
      });
      const earlier = historyUntil(CONTINUED_AFTER, POLITE).points.map(({ elapsedMs: _run, ...point }, index) => ({
        ...point,
        // Counted back from the file: when each was fetched, three days ago, and nothing of the run it was in.
        at: new Date(firstStart + index * 2_000).toISOString(),
      }));
      const now = event(CONTINUED_FOR);
      own({
        analysis: null,
        item: {
          status: 'running',
          finishedAt: null,
          analyzedAt: null,
          issues: null,
          fetchedUrlCount: now.fetched,
          pageCount: now.htmlPages,
        },
        history: {
          crawlId: TEST_CRAWL_ID,
          startedAt: new Date(firstStart).toISOString(),
          liveFrom: new Date(runStart).toISOString(),
          points: [
            ...earlier,
            {
              at: new Date(runStart).toISOString(),
              fetched: event(0).fetched,
              htmlPages: event(0).htmlPages,
              internalFetched: event(0).internalFetched ?? 0,
              internalFound: event(0).internalFound ?? 0,
              errors: 0,
              blocked: 0,
              elapsedMs: 0,
            },
          ],
        },
      });
      for (let seconds = 0; seconds <= CONTINUED_FOR; seconds += 0.5) {
        liveStore.ingest(message('progress', event(seconds)));
      }
    },
  ],
  render: () => <OverviewApp stream={{ script: POLITE, from: CONTINUED_AFTER + CONTINUED_FOR }} />,
};

/** The probes of an http-only site's other versions: in no class, said apart under the status codes, opening them. */
const PROBES = {
  fetched: 2,
  statusClasses: { '2xx': 0, '3xx': 1, '4xx': 0, '5xx': 0, other: 0, noResponse: 1, blocked: 0 },
  statusCodes: { '301': 1 },
  noResponse: { robotsDisallowed: 0, timeout: 0, networkError: 1, notChecked: 0, processingError: 0 },
};

/**
 * An http-only site: its https probe got no answer and its www probe redirected. They're the crawler's own questions,
 * not the site's URLs: the status codes are the site's own, with no "Network error", and a line says the two apart.
 */
export const LiveHttpOnly: Story = {
  loaders: [
    () => {
      running(1_346, POLITE);
      const event = progressAt(1_346, POLITE);
      const classes = event.statusClasses ?? {
        '2xx': 0,
        '3xx': 0,
        '4xx': 0,
        '5xx': 0,
        other: 0,
        noResponse: 0,
        blocked: 0,
      };
      liveStore.ingest(
        message('progress', {
          ...event,
          fetched: event.fetched + 2,
          crawled: event.fetched + 2,
          statusClasses: { ...classes, '3xx': classes['3xx'] + 1, noResponse: classes.noResponse + 1 },
          statusCodes: { ...event.statusCodes, '301': (event.statusCodes?.['301'] ?? 0) + 1 },
          noResponse: { ...PROBES.noResponse, robotsDisallowed: event.noResponse?.robotsDisallowed ?? 0 },
          siteVersions: PROBES,
        }),
      );
    },
  ],
  render: () => <OverviewApp />,
};

/**
 * Continued twice, with the runs' times (E3c's `/history.runs`): the chart is of the whole crawl on the time it spent
 * crawling, the days between runs and a pause closed up, each later run's start a dashed rule; Elapsed is the
 * engine's own time spent crawling, every run's.
 */
export const LiveContinuedWithRuns: Story = {
  loaders: [
    () => {
      const runStart = Date.parse(progressAt(CONTINUED_AFTER, POLITE).at ?? '');
      const firstStart = runStart - 3 * 86_400_000;
      const secondStart = runStart - 86_400_000;
      const iso = (ms: number) => new Date(ms).toISOString();
      // The first run crawled 300 s, the second 300 s with a minute's pause in it; this run carries on from both.
      const points = historyUntil(CONTINUED_AFTER, POLITE).points;
      const half = points.length / 2;
      const earlier = points.map(({ elapsedMs: _run, ...point }, index) => ({
        ...point,
        at: iso(index < half ? firstStart + (index / half) * 300_000 : secondStart + ((index - half) / half) * 360_000),
      }));
      const event = (seconds: number) => ({
        ...progressAt(CONTINUED_AFTER + seconds, POLITE),
        elapsedMs: seconds * 1_000,
        crawlingMs: (CONTINUED_AFTER + seconds) * 1_000,
      });
      const now = event(CONTINUED_FOR);
      own({
        analysis: null,
        item: {
          status: 'running',
          finishedAt: null,
          analyzedAt: null,
          issues: null,
          fetchedUrlCount: now.fetched,
          pageCount: now.htmlPages,
        },
        history: {
          crawlId: TEST_CRAWL_ID,
          startedAt: iso(firstStart),
          liveFrom: iso(runStart),
          points: earlier,
          crawlingMs: CONTINUED_AFTER * 1_000,
          runs: [
            {
              startedAt: iso(firstStart),
              endedAt: iso(firstStart + 300_000),
              crawlingMs: 300_000,
              crawling: [{ from: iso(firstStart), to: iso(firstStart + 300_000) }],
            },
            {
              startedAt: iso(secondStart),
              endedAt: iso(secondStart + 360_000),
              crawlingMs: 300_000,
              crawling: [
                { from: iso(secondStart), to: iso(secondStart + 120_000) },
                { from: iso(secondStart + 180_000), to: iso(secondStart + 360_000) },
              ],
            },
            { startedAt: iso(runStart + 50), endedAt: null, crawlingMs: 0, crawling: [] },
          ],
        },
      });
      for (let seconds = 0; seconds <= CONTINUED_FOR; seconds += 0.5) {
        liveStore.ingest(message('progress', event(seconds)));
      }
    },
  ],
  render: () => <OverviewApp />,
};

/**
 * A host stopped answering while it ran, so some URLs weren't checked: they aren't crawled and aren't errors. Crawled
 * and the status codes leave them out, as the URL grid does, and a line under the classes says how many, opening them.
 */
export const LiveNotChecked: Story = {
  loaders: [
    () => {
      running(60, POLITE);
      liveStore.ingest(message('progress', { ...progressAt(60, POLITE), ...DEAD_TWIN.progress }));
    },
  ],
  render: () => <OverviewApp />,
};

/** Trouble: the site blocks the crawl, its robots.txt can't be read, and requests are held back. Each says what to do. */
export const LiveWithAlerts: Story = {
  loaders: [
    () => {
      running(1_346, POLITE, {
        alerts: [
          alert({
            id: 1,
            level: 'info',
            code: 'sitemaps',
            at: '2026-11-01T10:15:04.000Z',
            data: { count: 3, urlCount: 4_180 },
          }),
        ],
      });
      for (const heard of [
        alert({
          id: 2,
          code: 'robots-unreachable',
          at: '2026-11-01T10:21:10.000Z',
          data: { origin: 'https://shop.example.com', reason: '503 Service Unavailable', durationMs: 120_000 },
        }),
        alert({
          id: 3,
          code: 'blocked',
          at: '2026-11-01T10:30:42.000Z',
          data: { host: 'example.com', reason: 'Cloudflare challenge', count: 1 },
        }),
        alert({
          id: 4,
          code: 'site-unreachable',
          at: '2026-11-01T10:36:02.000Z',
          data: { origin: 'https://example.com', reason: 'The connection timed out', durationMs: 60_000 },
        }),
      ]) {
        liveStore.ingest(message('alert', heard));
      }
      liveStore.ingest(
        message('progress', {
          ...progressAt(1_346.5, POLITE),
          waits: [
            { reason: 'siteDown', host: 'https://example.com', durationMs: 95_000 },
            { reason: 'robotsTxtCoolDown', urls: 212, durationMs: 48_000 },
            { reason: 'crawlDelay', host: 'shop.example.com', durationMs: 5_000 },
          ],
          moreWaits: { crawlDelay: 14 },
        }),
      );
    },
  ],
  render: () => <OverviewApp />,
};

/**
 * A site of many hosts, each blocking the crawl: an alert for each. The newest twenty are drawn, with "Show all 150
 * alerts" beneath, and the list isn't drawn again as the figures above it change twice a second.
 */
export const LiveManyAlerts: Story = {
  loaders: [
    () => {
      running(1_346, POLITE);
      for (let host = 1; host <= 150; host++) {
        liveStore.ingest(
          message(
            'alert',
            alert({
              id: host,
              code: 'blocked',
              at: new Date(Date.parse('2026-11-01T10:16:00.000Z') + host * 7_000).toISOString(),
              data: { host: `shop${host}.example.com`, reason: 'Cloudflare challenge', count: 1 },
            }),
          ),
        );
      }
    },
  ],
  render: () => <OverviewApp stream={POLITE_STREAM} />,
};

// --------------------------------------------------------------------------------------- Between the two, and around

const NEVER = { analyzedAt: null, issues: null } as const;

/** The crawl has just ended and its analysis runs on its own: the phase, its progress, and Cancel. */
export const AnalysingAfterCrawl: Story = {
  loaders: [
    () => {
      own({ analysis: null, item: { ...NEVER, status: 'analyzing' } });
      liveStore.ingest(message('analysis', { crawlId: TEST_CRAWL_ID, phase: 'pageRules', done: 152, total: 265 }));
    },
  ],
  render: () => <OverviewApp />,
};

/** Analysing again: the last answer stays, under the analysis's progress. */
export const AnalysingAgain: Story = {
  loaders: [
    () => {
      own({ item: { status: 'analyzing' } });
      liveStore.ingest(message('analysis', { crawlId: TEST_CRAWL_ID, phase: 'siteRules', done: 3, total: 9 }));
    },
  ],
  render: () => <OverviewApp />,
};

/** Never analysed: why there's no answer yet, with Analyse it. The crawl's own breakdowns don't need the analysis. */
export const NeverAnalysed: Story = {
  loaders: [() => own({ analysis: null, item: NEVER })],
  render: () => <OverviewApp />,
};

/**
 * A deep, big site: eleven depth columns in half the window, with counts in the hundreds of thousands. A number wider
 * than its column is written shorter ("313k") rather than run into its neighbour's; the whole number is the column's
 * name and its tooltip.
 */
export const DeepBigSite: Story = {
  loaders: [() => own({ facets: { pages: DEEP_SITE_PAGES } })],
  render: () => <OverviewApp />,
};

/**
 * A check with issues is hidden (a preference, in every crawl): the answer is of the checks that aren't, sentence and
 * figures alike, and the sentence ends with what the hidden one found, its count opening Issues.
 */
export const HiddenChecks: Story = {
  loaders: [
    () => {
      own();
      updateSettings({ hiddenChecks: ['internal-client-error'] });
    },
  ],
  render: () => <OverviewApp />,
};

/** Every check that found issues is hidden: no figure counts them, Fix first lists none, and both say why. */
export const EveryIssueHidden: Story = {
  loaders: [
    () => {
      own();
      updateSettings({ hiddenChecks: ['internal-client-error', 'empty-anchor-text', 'title-too-long'] });
    },
  ],
  render: () => <OverviewApp />,
};

/** Stopped: how far it got and what was still queued, with Continue. A stopped crawl isn't analysed on its own. */
export const Stopped: Story = {
  loaders: [
    () =>
      own({
        analysis: null,
        item: { ...NEVER, status: 'stopped', finishedAt: null, fetchedUrlCount: 2_713, pageCount: 241 },
        stats: { ...OVERVIEW_STATS, queuedCount: 1_417 },
      }),
  ],
  render: () => <OverviewApp />,
};

/**
 * A crawl with URLs that weren't checked, done: 6,909 URLs crawled though the library counts 6,914 outcomes, No
 * response without them (and its reasons adding up to it), the 5 said apart in the engine's words, and the URLs only
 * found counted without them. Every count opens exactly its URLs.
 */
export const NotChecked: Story = {
  loaders: [
    () =>
      own({
        item: DEAD_TWIN.item,
        facets: { status: DEAD_TWIN.status },
        notChecked: DEAD_TWIN.notChecked,
        stats: { ...OVERVIEW_STATS, ...DEAD_TWIN.stats },
      }),
  ],
  render: () => <OverviewApp />,
};

/** Interrupted: the app closed, or its engine stopped, while it ran. It says so, with Continue. */
export const Interrupted: Story = {
  loaders: [
    () =>
      own({
        analysis: null,
        item: { ...NEVER, status: 'interrupted', finishedAt: null, fetchedUrlCount: 2_713, pageCount: 241 },
      }),
  ],
  render: () => <OverviewApp />,
};

/** Failed: the engine's reason, and what it had fetched by then. */
export const Failed: Story = {
  loaders: [
    () => {
      own({ analysis: null, item: { ...NEVER, status: 'failed', fetchedUrlCount: 311, pageCount: 40 } });
      liveStore.ingest(
        message('crawl', {
          crawlId: TEST_CRAWL_ID,
          status: 'failed',
          error: 'The disk is full: the crawl file couldn’t be written.',
        }),
      );
    },
  ],
  render: () => <OverviewApp />,
};

/** Running in another window: watched and controlled there, readable here. */
export const InUse: Story = {
  loaders: [() => own({ analysis: null, item: { ...NEVER, status: 'inUse', finishedAt: null } })],
  render: () => <OverviewApp />,
};

/** Nothing crawled: the start URL answered 403, so it says why, with the next action. Never an empty answer. */
export const NothingCrawled: Story = {
  loaders: [
    () =>
      own({
        analysis: null,
        facets: EMPTY_FACETS,
        item: { ...NEVER, fetchedUrlCount: 1, pageCount: 0 },
        startUrl: { id: 1, url: 'https://example.com/', statusCode: 403, outcome: 'clientError' },
      }),
  ],
  render: () => <OverviewApp />,
};

/** Nothing crawled, because the start URL leads to another site: Crawl from there. */
export const NothingCrawledRedirect: Story = {
  loaders: [
    () =>
      own({
        analysis: null,
        facets: EMPTY_FACETS,
        item: { ...NEVER, fetchedUrlCount: 1, pageCount: 0 },
        startUrl: {
          id: 1,
          url: 'https://example.com/',
          statusCode: 301,
          outcome: 'redirect',
          redirectTarget: 'https://www.example.org/',
        },
        alerts: [alert({ id: 1, code: 'start-redirects-away', data: { url: 'https://www.example.org/' } })],
      }),
  ],
  render: () => <OverviewApp />,
};

/** The analysis can't be read: the plain cause and Try again, and the crawl's breakdowns still show. */
export const ReadError: Story = {
  loaders: [() => own({ analysis: 'error' })],
  render: () => <OverviewApp />,
};

/** The breakdowns can't be read: each card says so, with Try again, and the answer still shows. */
export const BreakdownsError: Story = {
  loaders: [() => own({ facets: 'error' })],
  render: () => <OverviewApp />,
};

/** The crawl's file can't be read: the reason in plain words, and nothing made up. */
export const Unreadable: Story = {
  loaders: [
    () =>
      own({
        analysis: null,
        item: { ...NEVER, status: 'unreadable', error: 'Another program has this crawl file open.' },
      }),
  ],
  render: () => <OverviewApp />,
};

// ---------------------------------------------------------------------------------------- The score (round 4, part C)

/** A score as the engine stores it: 85, Good, from two lines that cost points. */
const SCORE: NonNullable<Analysis['score']> = {
  version: 1,
  value: 85,
  band: 'good',
  lost: 15,
  beforeCeiling: 85,
  heldBySerious: false,
  partial: false,
  covers: { pages: 6, urls: 40, checksRun: 3, checksSkipped: 1, complete: true, reason: null, blockedByRobots: 0 },
  lines: [
    { checkId: 'internal-client-error', severity: 'error', count: 2, of: 40, unit: 'urls', extent: 'some', points: 4 },
    { checkId: 'empty-anchor-text', severity: 'warning', count: 6, of: 6, unit: 'pages', extent: 'most', points: 4 },
    { checkId: 'title-too-long', severity: 'notice', count: 3, of: 6, unit: 'pages', extent: 'most', points: 0 },
  ],
  ifFixed: { checkIds: ['internal-client-error', 'empty-anchor-text'], value: 100 },
};

/** The score beside the answer, with its word: it opens how it's worked out. */
export const Score: Story = {
  loaders: [() => own({ analysis: { ...ISSUES_ANALYSIS, score: SCORE, noScoreReason: null } })],
  render: () => <OverviewApp />,
};

/** A crawl that didn't read all of the site: the score "so far", on a dashed ring. */
export const ScoreSoFar: Story = {
  loaders: [
    () =>
      own({
        analysis: {
          ...ISSUES_ANALYSIS,
          score: { ...SCORE, partial: true, covers: { ...SCORE.covers, complete: false, reason: 'stopped' } },
          noScoreReason: null,
        },
        item: { status: 'stopped' },
      }),
  ],
  render: () => <OverviewApp />,
};

/** No score, and why: analysed by an engine from before scores. It opens nothing. */
export const NoScore: Story = {
  loaders: [() => own({ analysis: { ...ISSUES_ANALYSIS, score: null, noScoreReason: 'notScored' } })],
  render: () => <OverviewApp />,
};

/** How the score is worked out, over Overview (`?score=1`): Esc closes it. */
export const ScoreBreakdown: Story = {
  loaders: [() => own({ analysis: { ...ISSUES_ANALYSIS, score: SCORE, noScoreReason: null } })],
  render: () => <OverviewApp path={`${OVERVIEW}?score=1`} />,
};
