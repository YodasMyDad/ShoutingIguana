import type { Meta, StoryObj } from '@storybook/react-vite';
import type { GridFilter, UrlRow } from '../api/grids';
import { updateSettings } from '../settings/settingsStore';
import { TEST_CRAWL_ID } from '../test/fakeSidecar';
import { GARDEN_CRAWL, ScreenApp, freshStory, ownSidecar } from '../test/ScreenStory';
import { testUrlRows, urlsRoutes, type UrlsSidecarOptions, type UrlsSidecarState } from '../test/urlsSidecar';
import { URL_PRESETS } from './urls/urlColumns';

const meta = { title: 'Screens/URLs' } satisfies Meta;
export default meta;
type Story = StoryObj<typeof meta>;

/** The garden crawl in the design fixtures, as the mock sidecar serves it. */
const GARDEN = `/crawls/${GARDEN_CRAWL}/urls`;
/** The crawl of the stories' own sidecars: one URL of each thing the grid has words for. */
const URLS = `/crawls/${TEST_CRAWL_ID}/urls`;

const param = (value: unknown) => encodeURIComponent(JSON.stringify(value));
const withFilters = (path: string, filters: GridFilter[], rest = '') => `${path}?filter=${param(filters)}${rest}`;
const preset = (id: string) => URL_PRESETS.find((candidate) => candidate.id === id)?.columns ?? [];

/** Every story starts with no saved view and the site's URLs as paths. */
function clean() {
  freshStory();
  updateSettings({ savedViews: [], showFullUrls: false });
}

let current: UrlsSidecarState | null = null;

/** A sidecar of the story's own, for the states the design fixtures can't show. */
function own(options: UrlsSidecarOptions = {}) {
  const { routes, state } = urlsRoutes(options);
  current = state;
  ownSidecar(routes);
  updateSettings({ savedViews: [], showFullUrls: false });
  return state;
}

const never = new Promise<void>(() => undefined);

/** The garden crawl: the Overview preset, the host named once in the URL column's header. */
export const Garden: Story = {
  loaders: [clean],
  render: () => <ScreenApp path={GARDEN} crawlId={GARDEN_CRAWL} />,
};

/** Each preset on the garden crawl: the columns an audit of that kind reads, scrolling sideways where they don't fit. */
export const GardenIndexability: Story = {
  loaders: [clean],
  render: () => <ScreenApp path={`${GARDEN}?cols=${param(preset('indexability'))}`} crawlId={GARDEN_CRAWL} />,
};

export const GardenContent: Story = {
  loaders: [clean],
  render: () => <ScreenApp path={`${GARDEN}?cols=${param(preset('content'))}`} crawlId={GARDEN_CRAWL} />,
};

export const GardenPerformance: Story = {
  loaders: [clean],
  render: () => <ScreenApp path={`${GARDEN}?cols=${param(preset('performance'))}`} crawlId={GARDEN_CRAWL} />,
};

export const GardenLinks: Story = {
  loaders: [clean],
  render: () => <ScreenApp path={`${GARDEN}?cols=${param(preset('links'))}`} crawlId={GARDEN_CRAWL} />,
};

/** Sorted by two keys from the headers: each sorted header shows its arrow and its place. */
export const SortedByTwoColumns: Story = {
  loaders: [() => own()],
  render: () => (
    <ScreenApp
      path={`${URLS}?sort=${param([
        { column: 'depth', desc: false },
        { column: 'inlinks', desc: true },
      ])}`}
    />
  ),
};

/**
 * One of everything the grid has words for: a 404, a 500, a redirect, an image, a URL robots.txt refused ("Blocked
 * by robots.txt"), one not checked, one never crawled, another site's, and a title too long for its column.
 */
export const EveryKindOfUrl: Story = {
  loaders: [() => own()],
  render: () => <ScreenApp path={URLS} />,
};

/** "Show full URLs": every URL whole, the header naming no host. */
export const FullUrls: Story = {
  loaders: [
    () => {
      own();
      updateSettings({ showFullUrls: true });
    },
  ],
  render: () => <ScreenApp path={URLS} />,
};

/** Filters as chips that read as sentences, each editable, with Clear filters. */
export const Filtered: Story = {
  loaders: [() => own()],
  render: () => (
    <ScreenApp
      path={withFilters(URLS, [
        { column: 'statusClass', op: 'in', values: ['2xx'] },
        { column: 'depth', op: 'between', min: 1, max: 3 },
        { column: 'title', op: 'contains', value: 'garden' },
      ])}
    />
  ),
};

/** A view that is a saved one: its name on the menu's button. */
export const OnASavedView: Story = {
  loaders: [
    () => {
      own();
      updateSettings({
        savedViews: [
          {
            id: 'v-broken',
            grid: 'urls',
            name: 'Broken pages',
            search: { filter: [{ column: 'statusClass', op: 'in', values: ['4xx', '5xx'] }] },
          },
        ],
      });
    },
  ],
  render: () => <ScreenApp path={withFilters(URLS, [{ column: 'statusClass', op: 'in', values: ['4xx', '5xx'] }])} />,
};

/** The first load: the frame from the start, then the skeleton once it has taken 150 ms. */
export const FirstLoad: Story = {
  loaders: [
    () => {
      own().hold = never;
    },
  ],
  render: () => <ScreenApp path={URLS} />,
};

/** A refresh the user asked for: the rows stay, under the thin progress line. */
export const Refreshing: Story = {
  loaders: [() => own()],
  render: () => <ScreenApp path={URLS} refreshAfterMs={500} />,
  play: async () => {
    await new Promise((resolve) => setTimeout(resolve, 400));
    if (current) current.hold = never;
  },
};

/** A URL the running crawl finds, sorting in among the others. */
const arriving = (count: number): UrlRow => ({
  ...testUrlRows(1)[0]!,
  id: 100 + count,
  url: `https://example.com/found-${String(count).padStart(3, '0')}/`,
  depth: 1,
  issues: null,
});

/**
 * A crawl running, sorted by URL: URLs arrive among the rows once a second, the count moves in place, the cursor
 * stays on its URL, and the analysis's columns say they fill in once it's done. No skeleton, no progress line.
 */
export const Live: Story = {
  loaders: [
    () =>
      own({
        item: { status: 'running', analyzedAt: null, finishedAt: null },
        summary: { available: false, reason: 'crawling' },
      }),
  ],
  render: () => (
    <ScreenApp
      path={`${URLS}?sort=url`}
      live={{
        onTick: () => {
          if (current) current.rows = [...current.rows, arriving(current.rows.length)];
        },
      }}
    />
  ),
};

/** Never analysed: the crawl's own columns are here; the analysis's say why they're empty, with Analyse it. */
export const NeverAnalysed: Story = {
  loaders: [() => own({ item: { analyzedAt: null }, summary: { available: false, reason: 'notAnalyzed' } })],
  render: () => <ScreenApp path={URLS} />,
};

/** Continued after its analysis: Analyse again, never "Analyse it". */
export const ContinuedSinceItsAnalysis: Story = {
  loaders: [() => own({ summary: { available: false, reason: 'crawlChanged' } })],
  render: () => <ScreenApp path={URLS} />,
};

/** Being analysed: the columns fill in when it's done, with nothing to press. */
export const BeingAnalysed: Story = {
  loaders: [
    () =>
      own({ item: { status: 'analyzing', analyzedAt: null }, summary: { available: false, reason: 'notAnalyzed' } }),
  ],
  render: () => <ScreenApp path={URLS} />,
};

/** A filter on a column the analysis fills, on a crawl never analysed: the state says so, with Analyse it. */
export const FilterThatNeedsAnAnalysis: Story = {
  loaders: [() => own({ item: { analyzedAt: null }, summary: { available: false, reason: 'notAnalyzed' } })],
  render: () => <ScreenApp path={withFilters(URLS, [{ column: 'inlinks', op: 'eq', value: 0 }])} />,
};

const REFUSED: GridFilter[] = [
  { column: 'statusClass', op: 'in', values: ['2xx'] },
  { column: 'title', op: 'regex', value: '(?=sale)garden' },
];

/** A pattern the engine won't run: its own sentence, with Change the filter and Remove the filter. */
export const FilterTheEngineRefuses: Story = {
  loaders: [() => own()],
  render: () => <ScreenApp path={withFilters(URLS, REFUSED)} />,
};

/** A query refused for something that isn't a filter (a sort the engine doesn't take): its sentence, and a way out. */
export const QueryTheEngineRefuses: Story = {
  loaders: [() => own()],
  render: () => <ScreenApp path={`${URLS}?cols=${param(['url', 'outcome'])}&sort=-outcome`} />,
};

/** A view with the 20 filters a query takes: "Add filter" says why it takes no more. */
export const AsManyFiltersAsAViewTakes: Story = {
  loaders: [() => own()],
  render: () => (
    <ScreenApp
      path={withFilters(
        URLS,
        Array.from({ length: 20 }, (_, index): GridFilter => ({ column: 'depth', op: 'ne', value: index + 10 })),
      )}
    />
  ),
};

/** Nothing matches: the filters, and one press to clear them. */
export const NoUrlsMatch: Story = {
  loaders: [() => own()],
  render: () => <ScreenApp path={withFilters(URLS, [{ column: 'title', op: 'contains', value: 'no such title' }])} />,
};

/** A crawl that has found no URL yet. */
export const NoUrlsYet: Story = {
  loaders: [
    () =>
      own({
        rows: [],
        item: { status: 'running', analyzedAt: null, finishedAt: null },
        summary: { available: false, reason: 'crawling' },
      }),
  ],
  render: () => <ScreenApp path={URLS} />,
};

/** The grid can't be read: the cause in plain words, with Try again. */
export const ReadError: Story = {
  loaders: [
    () => own({ urlsProblem: { type: 'internal', title: 'Something went wrong in the engine.', status: 500 } }),
  ],
  render: () => <ScreenApp path={URLS} />,
};

/** The engine's list of columns can't be read: the URLs are here, and what waits for the list says so. */
export const ColumnsListUnreadable: Story = {
  loaders: [() => own({ columnsFail: true })],
  render: () => <ScreenApp path={URLS} />,
};

/** A hundred thousand URLs: six-digit counts, scrolled anywhere without blank rows. */
export const AHundredThousandUrls: Story = {
  loaders: [() => own({ rows: testUrlRows(100_000) })],
  render: () => <ScreenApp path={URLS} />,
};
