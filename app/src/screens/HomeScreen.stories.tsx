import type { Meta, StoryObj } from '@storybook/react-vite';
import type { CrawlListItem } from '../api/types';
import { drawAsTheAppForTests } from '../crawl/fileOpening';
import { ENGINE_DEFAULTS } from '../crawl/options/engineDefaults';
import { liveStore } from '../live/liveStore';
import { installFakeDrops } from '../test/fakeDrops';
import { json, problem, testCrawlItem, type FakeRoute } from '../test/fakeSidecar';
import { message, progressAt } from '../test/overviewSidecar';
import { AT_200_PERCENT, ownSidecar, ScreenApp } from '../test/ScreenStory';

const meta = { title: 'Screens/Home' } satisfies Meta;
export default meta;
type Story = StoryObj<typeof meta>;

const PRESETS = [
  { id: 'polite', concurrency: 2, maxRequestsPerSecond: 2 },
  { id: 'standard', concurrency: 5, maxRequestsPerSecond: 0 },
  { id: 'fast', concurrency: 16, maxRequestsPerSecond: 0 },
];

const RUNNING_ID = 'aaaaaaaaaaaaaaaa';
const PAUSED_ID = 'abababababababab';

const crawl = (overrides: Partial<CrawlListItem> & Pick<CrawlListItem, 'id' | 'site'>): CrawlListItem =>
  testCrawlItem({
    startUrl: `https://www.${overrides.site ?? 'example.com'}/`,
    fileName: `${overrides.site ?? 'crawl'}-${overrides.id.slice(0, 4)}.sicrawl`,
    path: `C:\\Users\\me\\Documents\\Shouting Iguana\\${overrides.site ?? 'crawl'}.sicrawl`,
    ...overrides,
  });

/**
 * The library as `clarity/home.html` draws it: a crawl running now, the garden crawl, and invented sites on the reserved
 * `.example` domain in every state a row can be in, one with older crawls folded under it.
 */
const LIBRARY: CrawlListItem[] = [
  crawl({
    id: RUNNING_ID,
    site: 'kestrel-legal.example',
    status: 'running',
    startedAt: '2026-11-01T17:20:00Z',
    finishedAt: null,
    analyzedAt: null,
    issues: null,
  }),
  crawl({
    id: '62cb879ef60639d5',
    site: 'gardenfurnituresets.co.uk',
    startedAt: '2026-10-31T14:13:00Z',
    finishedAt: '2026-10-31T14:40:00Z',
    fetchedUrlCount: 4521,
    crawledUrlCount: 4521,
    pageCount: 265,
    issues: { error: 2, warning: 6, notice: 11 },
  }),
  ...['2026-10-12T09:00:00Z', '2026-10-20T09:00:00Z', '2026-10-28T09:00:00Z'].map((startedAt, index) =>
    crawl({
      id: `bbbbbbbbbbbbbbb${index}`,
      site: 'oakmere-cycles.example',
      startedAt,
      finishedAt: startedAt,
      crawledUrlCount: 9860,
      fetchedUrlCount: 9860,
      pageCount: 3214,
      issues: { error: 4, warning: 9, notice: 13 },
      outOfDate: index === 2,
    }),
  ),
  crawl({
    id: 'cccccccccccccccc',
    site: 'kestrel-legal.example',
    startedAt: '2026-10-12T09:20:00Z',
    finishedAt: '2026-10-12T09:50:00Z',
    crawledUrlCount: 5870,
    fetchedUrlCount: 5870,
    pageCount: 1946,
    issues: { error: 3, warning: 7, notice: 12 },
  }),
  crawl({
    id: 'eeeeeeeeeeeeeeee',
    site: 'brightwater-dental.example',
    status: 'stopped',
    startedAt: '2026-10-27T10:12:00Z',
    finishedAt: '2026-10-27T10:30:00Z',
    crawledUrlCount: 1206,
    fetchedUrlCount: 1206,
    pageCount: 188,
    issues: { error: 1, warning: 4, notice: 6 },
  }),
  crawl({
    id: '1111111111111111',
    site: 'willowbrook-farm.example',
    status: 'interrupted',
    startedAt: '2026-10-25T09:00:00Z',
    finishedAt: null,
    pageCount: 140,
    issues: null,
    analyzedAt: null,
  }),
  crawl({
    id: '1212121212121212',
    site: 'fernleigh-pottery.example',
    startedAt: '2026-10-24T09:00:00Z',
    finishedAt: '2026-10-24T09:10:00Z',
    crawledUrlCount: 1388,
    fetchedUrlCount: 1388,
    pageCount: 412,
    issues: null,
    analyzedAt: null,
  }),
  crawl({
    id: '1313131313131313',
    site: 'larkspur-surveys.example',
    status: 'failed',
    startedAt: '2026-10-22T09:00:00Z',
    finishedAt: '2026-10-22T09:00:05Z',
    crawledUrlCount: 1,
    fetchedUrlCount: 1,
    pageCount: 0,
    issues: null,
    analyzedAt: null,
  }),
  crawl({
    id: '2222222222222222',
    site: 'tidewell-travel.example',
    startedAt: '2026-10-14T13:30:00Z',
    finishedAt: '2026-10-14T13:50:00Z',
    missing: true,
    crawledUrlCount: 8412,
    fetchedUrlCount: 8412,
    pageCount: 2957,
    issues: { error: 2, warning: 8, notice: 11 },
  }),
  crawl({
    id: '2323232323232323',
    site: 'saltmarsh-supplies.example',
    status: 'unreadable',
    startedAt: null,
    error: 'Another program has the file open, so it couldn’t be read: close it, then try again.',
    issues: null,
    analyzedAt: null,
  }),
  crawl({
    id: '2424242424242424',
    site: 'harbourside-cafe.example',
    status: 'inUse',
    startedAt: '2026-10-09T09:00:00Z',
    pageCount: 72,
    issues: null,
    analyzedAt: null,
  }),
  crawl({
    id: '3333333333333333',
    site: 'quillon-press-and-bindery-of-the-very-long-name.example',
    startedAt: '2026-10-06T09:00:00Z',
    finishedAt: '2026-10-06T09:20:00Z',
    crawledUrlCount: 1_234_567,
    fetchedUrlCount: 1_234_567,
    pageCount: 987_654,
    issues: { error: 12, warning: 1_204, notice: 9 },
  }),
];

function routes(library: CrawlListItem[] | 'pending' | 'error'): FakeRoute[] {
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
      (_, match) => json((Array.isArray(library) ? library : []).find((item) => item.id === match[1]) ?? LIBRARY[1]),
    ],
    ['GET', /^\/api\/crawl-options\/defaults$/, () => json({ options: ENGINE_DEFAULTS, speedPresets: PRESETS })],
    ['GET', /^\/api\/checks$/, () => json({ categories: [], checks: [] })],
  ];
}

/** The running crawl, a minute in; with `paused`, a second one paused. */
function withRunning(paused = false) {
  liveStore.ingest(message('hello', { live: paused ? [RUNNING_ID, PAUSED_ID] : [RUNNING_ID] }));
  liveStore.ingest(
    message('progress', progressAt(240, { rate: 9.6, size: 6_140, maxUrls: null, crawlId: RUNNING_ID })),
  );
  if (paused) {
    liveStore.ingest(
      message(
        'progress',
        progressAt(90, { rate: 2, size: 1_400, maxUrls: 1_000, crawlId: PAUSED_ID, status: 'paused' }),
      ),
    );
  }
}

const PAUSED = crawl({
  id: PAUSED_ID,
  site: 'brackenfield-books.example',
  status: 'paused',
  startedAt: '2026-11-01T17:30:00Z',
  finishedAt: null,
  analyzedAt: null,
  issues: null,
});

/** Every state a row can be in, the running crawl pinned above as a card; as it shows outside the app. */
export const Library: Story = {
  loaders: [
    () => {
      ownSidecar(routes(LIBRARY));
      drawAsTheAppForTests(null);
      withRunning();
    },
  ],
  render: () => <ScreenApp path="/" />,
};

/** In the app: Open… (Ctrl+O) under the URL field, and Show in folder in each row's menu. */
export const InTheApp: Story = {
  loaders: [
    () => {
      ownSidecar(routes(LIBRARY.slice(1, 6)));
      drawAsTheAppForTests(true);
    },
  ],
  render: () => <ScreenApp path="/" />,
};

/** Two crawls at once, one paused: two cards, the heading saying one isn't crawling. */
export const RunningAndPaused: Story = {
  loaders: [
    () => {
      ownSidecar(routes([LIBRARY[0]!, PAUSED, LIBRARY[1]!]));
      drawAsTheAppForTests(null);
      withRunning(true);
    },
  ],
  render: () => <ScreenApp path="/" />,
};

/** A crawl just stopped, analysed now (round 5): its card says so, with Cancel the analysis and nothing to stop. */
export const AnalysingAfterStop: Story = {
  loaders: [
    () => {
      const stopped = LIBRARY.find((item) => item.status === 'stopped')!;
      ownSidecar(routes([stopped, ...LIBRARY.filter((item) => item.status === 'completed').slice(0, 3)]));
      drawAsTheAppForTests(null);
      liveStore.ingest(message('hello', { live: [], analyzing: [stopped.id] }));
    },
  ],
  render: () => <ScreenApp path="/" />,
};

/** Nothing crawled yet. */
export const Empty: Story = {
  loaders: [
    () => {
      ownSidecar(routes([]));
      drawAsTheAppForTests(null);
    },
  ],
  render: () => <ScreenApp path="/" />,
};

/** A search that matches nothing: Clear the search. */
export const FilteredToNothing: Story = {
  loaders: [
    () => {
      ownSidecar(routes(LIBRARY.slice(1)));
      drawAsTheAppForTests(null);
    },
  ],
  render: () => <ScreenApp path="/?q=no-such-site" />,
};

/** The library being read: a skeleton in the table's shape, after 150 ms. */
export const Loading: Story = {
  loaders: [
    () => {
      ownSidecar(routes('pending'));
      drawAsTheAppForTests(null);
    },
  ],
  render: () => <ScreenApp path="/" />,
};

/** The library couldn't be read: the plain cause, and Try again. */
export const LibraryError: Story = {
  loaders: [
    () => {
      ownSidecar(routes('error'));
      drawAsTheAppForTests(null);
    },
  ],
  render: () => <ScreenApp path="/" />,
};

/** A 1280x720 window at 200% zoom: Pages and Started fold into second lines, and nothing scrolls sideways. */
export const At200Percent: Story = {
  loaders: [
    () => {
      ownSidecar(routes(LIBRARY));
      drawAsTheAppForTests(true);
      withRunning();
    },
  ],
  render: () => <ScreenApp path="/" size={AT_200_PERCENT} />,
};

/** A crawl file dragged over the window: what a drop will do, over any screen. */
export const DraggingACrawlFile: Story = {
  loaders: [
    () => {
      ownSidecar(routes(LIBRARY.slice(1, 4)));
      drawAsTheAppForTests(true);
      const drops = installFakeDrops();
      // Once the window listens: the story's app mounts after its loaders.
      setTimeout(() => drops.emit({ type: 'enter', paths: ['C:\\Crawls\\gardenfurnituresets.co.uk.sicrawl'] }), 300);
    },
  ],
  render: () => <ScreenApp path="/" />,
};

/** Something that isn't a crawl file dragged over the window: it says so before the drop. */
export const DraggingAnotherFile: Story = {
  loaders: [
    () => {
      ownSidecar(routes(LIBRARY.slice(1, 4)));
      drawAsTheAppForTests(true);
      const drops = installFakeDrops();
      setTimeout(() => drops.emit({ type: 'enter', paths: ['C:\\Documents\\notes.txt'] }), 300);
    },
  ],
  render: () => <ScreenApp path="/" />,
};

/** The same over Simple's Home, in Simple's words. */
export const DraggingInSimple: Story = {
  loaders: [
    () => {
      ownSidecar(routes(LIBRARY.slice(1, 4)));
      const drops = installFakeDrops();
      setTimeout(() => drops.emit({ type: 'enter', paths: ['C:\\Crawls\\a.sicrawl', 'C:\\Crawls\\b.sicrawl'] }), 300);
    },
  ],
  render: () => <ScreenApp path="/" mode="simple" />,
};
