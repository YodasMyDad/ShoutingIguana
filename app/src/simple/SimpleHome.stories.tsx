import type { Meta, StoryObj } from '@storybook/react-vite';
import type { CrawlListItem } from '../api/types';
import { ENGINE_DEFAULTS } from '../crawl/options/engineDefaults';
import { liveStore } from '../live/liveStore';
import { json, problem, testCrawlItem, type FakeRoute } from '../test/fakeSidecar';
import { message, progressAt } from '../test/overviewSidecar';
import { AT_200_PERCENT, ownSidecar, ScreenApp } from '../test/ScreenStory';

const meta = { title: 'Screens/Simple/Home' } satisfies Meta;
export default meta;
type Story = StoryObj<typeof meta>;

const PRESETS = [
  { id: 'polite', concurrency: 2, maxRequestsPerSecond: 2 },
  { id: 'standard', concurrency: 5, maxRequestsPerSecond: 0 },
  { id: 'fast', concurrency: 16, maxRequestsPerSecond: 0 },
];

const RUNNING_ID = 'aaaaaaaaaaaaaaaa';

/**
 * Your sites as `home.html` draws them: a check running now, the garden crawl (85, Good), and invented sites on the
 * reserved `.example` domain in every state a row can be in.
 */
const SITES: CrawlListItem[] = [
  testCrawlItem({
    id: RUNNING_ID,
    site: 'kestrel-legal.example',
    status: 'running',
    startedAt: '2026-11-01T17:20:00Z',
    finishedAt: null,
    analyzedAt: null,
    issues: null,
    score: null,
  }),
  testCrawlItem({
    id: '62cb879ef60639d5',
    site: 'gardenfurnituresets.co.uk',
    fileName: 'gardenfurnituresets.co.uk.sicrawl',
    startedAt: '2026-10-31T14:13:00Z',
    finishedAt: '2026-10-31T14:40:00Z',
    fetchedUrlCount: 4521,
    crawledUrlCount: 4521,
    pageCount: 265,
    issues: { error: 2, warning: 4, notice: 13 },
    score: { value: 85, band: 'good', partial: false },
  }),
  ...['2026-10-12T09:00:00Z', '2026-10-20T09:00:00Z', '2026-10-28T09:00:00Z'].map((startedAt, index) =>
    testCrawlItem({
      id: `bbbbbbbbbbbbbbb${index}`,
      site: 'oakmere-cycles.example',
      startedAt,
      finishedAt: startedAt,
      pageCount: 1208,
      issues: { error: 0, warning: index === 2 ? 0 : 1, notice: 3 },
      score:
        index === 2
          ? { value: 100, band: 'veryGood', partial: false }
          : { value: 98, band: 'veryGood', partial: false },
    }),
  ),
  testCrawlItem({
    id: 'cccccccccccccccc',
    site: 'fernleigh-pottery.example',
    startedAt: '2026-10-24T09:00:00Z',
    finishedAt: '2026-10-24T09:10:00Z',
    pageCount: 48,
    issues: { error: 0, warning: 1, notice: 6 },
    score: { value: 98, band: 'veryGood', partial: false },
  }),
  testCrawlItem({
    id: 'dddddddddddddddd',
    site: 'tidewell-travel.example',
    startedAt: '2026-10-19T09:00:00Z',
    finishedAt: '2026-10-19T10:00:00Z',
    pageCount: 1240,
    issues: { error: 7, warning: 6, notice: 9 },
    score: { value: 36, band: 'poor', partial: false },
  }),
  testCrawlItem({
    id: 'eeeeeeeeeeeeeeee',
    site: 'brackenfield-books.example',
    status: 'stopped',
    startedAt: '2026-10-15T09:00:00Z',
    finishedAt: '2026-10-15T09:30:00Z',
    pageCount: 312,
    issues: { error: 2, warning: 3, notice: 5 },
    score: { value: 79, band: 'good', partial: true },
  }),
  testCrawlItem({
    id: 'ffffffffffffffff',
    site: 'larkspur-dental.example',
    startedAt: '2026-10-12T09:00:00Z',
    finishedAt: '2026-10-12T09:02:00Z',
    pageCount: 5,
    issues: { error: 1, warning: 0, notice: 1 },
    score: { value: 89, band: 'good', partial: false },
  }),
  testCrawlItem({
    id: '1111111111111111',
    site: 'willowbrook-farm.example',
    startedAt: '2026-10-10T09:00:00Z',
    finishedAt: '2026-10-10T09:20:00Z',
    pageCount: 140,
    issues: null,
    analyzedAt: null,
    score: null,
  }),
  testCrawlItem({
    id: '2222222222222222',
    site: 'saltmarsh-supplies.example',
    startedAt: '2026-10-08T09:00:00Z',
    finishedAt: '2026-10-08T09:20:00Z',
    missing: true,
    score: null,
    issues: null,
    analyzedAt: null,
  }),
  testCrawlItem({
    id: '3333333333333333',
    site: 'quillon-press-and-bindery-of-the-very-long-name.example',
    startedAt: '2026-10-06T09:00:00Z',
    finishedAt: '2026-10-06T09:20:00Z',
    pageCount: 123_456,
    issues: { error: 12, warning: 1_204, notice: 9 },
    score: { value: 0, band: 'poor', partial: false },
  }),
];

function routes(library: CrawlListItem[] | 'pending' | 'error', defaults: object = ENGINE_DEFAULTS): FakeRoute[] {
  return [
    [
      'GET',
      /^\/api\/crawls$/,
      () =>
        library === 'pending'
          ? new Promise<Response>(() => undefined)
          : library === 'error'
            ? problem({ type: 'internal', title: 'Something went wrong in the engine.', status: 500 })
            : json(library),
    ],
    [
      'GET',
      /^\/api\/crawls\/([0-9a-f]{16})$/,
      (_, match) => json((Array.isArray(library) ? library : []).find((item) => item.id === match[1]) ?? SITES[1]),
    ],
    ['GET', /^\/api\/crawl-options\/defaults$/, () => json({ options: defaults, speedPresets: PRESETS })],
    ['GET', /^\/api\/checks$/, () => json({ categories: [], checks: [] })],
  ];
}

/** The running check, 60% of the way: live, with its progress. */
function withRunning() {
  liveStore.ingest(message('hello', { live: [RUNNING_ID] }));
  liveStore.ingest(
    message('progress', progressAt(1_194, { rate: 2, size: 3_980, maxUrls: null, crawlId: RUNNING_ID })),
  );
}

/** A check just stopped, its results being worked out (round 5): its row says so, with Cancel. */
export const WorkingOutAfterStop: Story = {
  loaders: [
    () => {
      const stopped = { ...SITES.find((item) => item.status === 'completed')!, status: 'stopped' as const };
      ownSidecar(routes([stopped, ...SITES.filter((item) => item.id !== stopped.id && item.id !== RUNNING_ID)]));
      liveStore.ingest(message('hello', { live: [], analyzing: [stopped.id] }));
    },
  ],
  render: () => <ScreenApp path="/" mode="simple" />,
};

/** Every state a row can be in, the running check pinned first. */
export const YourSites: Story = {
  loaders: [
    () => {
      ownSidecar(routes(SITES));
      withRunning();
    },
  ],
  render: () => <ScreenApp path="/" mode="simple" />,
};

// ------------------------------------------------------------------------------------------- The fix loop

/** The catalog's severities for the changes the garden row counts: three problems fixed, one new. */
const FIX_LOOP_CHECKS = {
  categories: [],
  checks: [
    { id: 'internal-server-error', severity: 'error' },
    { id: 'title-missing', severity: 'error' },
    { id: 'redirect-chain', severity: 'warning' },
    { id: 'title-duplicate', severity: 'warning' },
    { id: 'h1-multiple', severity: 'notice' },
  ],
};

/**
 * The garden crawl compared with its site's earlier check (17 Oct, 72): ▲15, three problems fixed and one new, and a
 * recheck of five pages since. Its earlier check is folded under it, with nothing to compare with: no line, no zeros.
 */
const COMPARED_SITES: CrawlListItem[] = SITES.flatMap((item) =>
  item.id === '62cb879ef60639d5'
    ? [
        {
          ...item,
          score: { value: 87, band: 'good', partial: false },
          previous: {
            id: '1717171717171717',
            startedAt: '2026-10-17T14:13:00Z',
            score: { value: 72, band: 'good', partial: false },
            comparable: 'full',
            needsAnalysis: false,
          },
          changes: {
            score: 15,
            partial: false,
            fixed: ['internal-server-error', 'title-missing', 'redirect-chain', 'h1-multiple'],
            new: ['title-duplicate'],
            fewer: [],
            more: [],
            newCheck: [],
            notCheckedNow: [],
          },
          rechecks: {
            count: 1,
            last: { id: 1, endedAt: '2026-10-31T16:02:00Z', checkId: 'broken-internal-links', judged: 5, fixed: 4 },
          },
        },
        testCrawlItem({
          ...item,
          id: '1717171717171717',
          fileName: 'gardenfurnituresets.co.uk-earlier.sicrawl',
          startedAt: '2026-10-17T14:13:00Z',
          finishedAt: '2026-10-17T14:40:00Z',
          score: { value: 72, band: 'good', partial: false },
          issues: { error: 4, warning: 4, notice: 13 },
          previous: null,
          changes: null,
        }),
      ]
    : item.id === RUNNING_ID
      ? []
      : [item],
);

function fixLoopRoutes(library: CrawlListItem[]): FakeRoute[] {
  return [['GET', /^\/api\/checks$/, () => json(FIX_LOOP_CHECKS)], ...routes(library)];
}

/** What changed since each site's last check: "▲15 since 17 Oct" by the ring, "3 fixed · 1 new" under To fix. */
export const SinceTheLastCheck: Story = {
  loaders: [() => ownSidecar(fixLoopRoutes(COMPARED_SITES))],
  render: () => <ScreenApp path="/" mode="simple" />,
};

/** The garden crawl stopped early: its changes are "so far". */
export const SinceTheLastCheckSoFar: Story = {
  loaders: [
    () =>
      ownSidecar(
        fixLoopRoutes(
          COMPARED_SITES.map((item) =>
            item.id === '62cb879ef60639d5'
              ? { ...item, status: 'stopped' as const, score: { value: 87, band: 'good' as const, partial: true } }
              : item,
          ),
        ),
      ),
  ],
  render: () => <ScreenApp path="/" mode="simple" />,
};

/** The garden crawl being rechecked: pinned as checking now, with the recheck's own count. */
export const Rechecking: Story = {
  loaders: [
    () => {
      ownSidecar(
        fixLoopRoutes(
          COMPARED_SITES.map((item) =>
            item.id === '62cb879ef60639d5' ? { ...item, status: 'rechecking' as const, outOfDate: true } : item,
          ),
        ),
      );
      liveStore.ingest(message('hello', { live: ['62cb879ef60639d5'] }));
      liveStore.ingest(message('crawl', { crawlId: '62cb879ef60639d5', status: 'rechecking' }));
      liveStore.ingest(
        message('recheck', {
          crawlId: '62cb879ef60639d5',
          recheckId: 2,
          status: 'running',
          phase: 1,
          done: 3,
          total: 8,
          result: null,
        }),
      );
    },
  ],
  render: () => <ScreenApp path="/" mode="simple" />,
};

/** A first start, with no checks: three steps, then where Advanced and a crawl file are. */
export const FirstStart: Story = {
  loaders: [() => ownSidecar(routes([]))],
  render: () => <ScreenApp path="/" mode="simple" />,
};

/** Saved defaults that differ from the engine's: the line says so, in Simple's words. */
export const SavedSettings: Story = {
  loaders: [
    () =>
      ownSidecar(
        routes(SITES.slice(1, 3), {
          ...ENGINE_DEFAULTS,
          concurrency: 2,
          maxRequestsPerSecond: 2,
          maxUrls: 5000,
          maxDepth: 4,
          storeHtml: true,
          maxRetries: 0,
        }),
      ),
  ],
  render: () => <ScreenApp path="/" mode="simple" />,
};

/** Types an address into Home's field and presses Check site, once the field is there. */
async function checkAddress(root: HTMLElement, address: string) {
  for (let tries = 0; tries < 50; tries++) {
    const field = root.querySelector<HTMLInputElement>('input[type="url"]');
    if (field) {
      // A controlled field: set its value as the browser would, then tell React.
      Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set?.call(field, address);
      field.dispatchEvent(new Event('input', { bubbles: true }));
      field.form?.requestSubmit();
      return;
    }
    await new Promise((resolve) => setTimeout(resolve, 100));
  }
}

/** The engine refusing the check's options, with each field's problem. */
const refused = (errors: Record<string, string[]>): FakeRoute => [
  'POST',
  /^\/api\/crawls$/,
  () => problem({ type: 'invalid-options', title: 'The crawl options are invalid.', status: 400, errors }),
];

/** An address that isn't one: said under the field in Simple's words, never the engine's. */
export const AddressRefused: Story = {
  loaders: [
    () =>
      ownSidecar([
        refused({ startUrl: ['Start URL must be an absolute http or https URL.'] }),
        ...routes(SITES.slice(1, 3)),
      ]),
  ],
  render: () => <ScreenApp path="/" mode="simple" />,
  play: async ({ canvasElement }) => {
    await checkAddress(canvasElement, 'my shop');
  },
};

/** A saved setting the engine refuses: said under the form, with Options… where to fix it. */
export const SavedSettingRefused: Story = {
  loaders: [
    () =>
      ownSidecar([refused({ concurrency: ['Concurrency must be between 1 and 100.'] }), ...routes(SITES.slice(1, 3))]),
  ],
  render: () => <ScreenApp path="/" mode="simple" />,
  play: async ({ canvasElement }) => {
    await checkAddress(canvasElement, 'www.example.co.uk');
  },
};

/** Your sites being read: a skeleton in the list's shape, after 150 ms. */
export const Loading: Story = {
  loaders: [() => ownSidecar(routes('pending'))],
  render: () => <ScreenApp path="/" mode="simple" />,
};

/** Your sites couldn't be read: the plain cause, and Try again. */
export const LibraryError: Story = {
  loaders: [() => ownSidecar(routes('error'))],
  render: () => <ScreenApp path="/" mode="simple" />,
};

/** A 1280x720 window at 200% zoom: the score and the fixes go under the site. */
export const At200Percent: Story = {
  loaders: [
    () => {
      ownSidecar(routes(SITES));
      withRunning();
    },
  ],
  render: () => <ScreenApp path="/" mode="simple" size={AT_200_PERCENT} />,
};

/** The first start at 200% zoom: the three steps stand one under another. */
export const FirstStartAt200Percent: Story = {
  loaders: [() => ownSidecar(routes([]))],
  render: () => <ScreenApp path="/" mode="simple" size={AT_200_PERCENT} />,
};
