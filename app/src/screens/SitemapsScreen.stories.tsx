import type { Meta, StoryObj } from '@storybook/react-vite';
import type { Sitemap, SitemapList, SummaryState } from '../api/grids';
import { TEST_CRAWL_ID, json, problem } from '../test/fakeSidecar';
import { inspectorRoutes, testUrlDetail } from '../test/inspectorSidecar';
import { GARDEN_CRAWL, SYNTHETIC_CRAWL, ScreenApp, freshStory, ownSidecar } from '../test/ScreenStory';
import {
  COVERAGE_PAGE,
  SITE,
  TEST_SITEMAPS,
  TEST_SITEMAP_LIST,
  sitemapsRobotsRoutes,
  testSitemap,
  withoutNewCounts,
  type SitemapsRobotsOptions,
} from '../test/sitemapsRobotsSidecar';

const meta = { title: 'Screens/Sitemaps' } satisfies Meta;
export default meta;
type Story = StoryObj<typeof meta>;

const LIST = `/crawls/${TEST_CRAWL_ID}/sitemaps`;
const FAILED = () => problem({ type: 'internal', title: 'Something went wrong in the engine.', status: 500 });
const never = () => new Promise<Response>(() => undefined);

const own = (options: SitemapsRobotsOptions = {}) => ownSidecar(sitemapsRobotsRoutes(options));
const without = (reason: SummaryState['reason']): SummaryState => ({ available: false, reason });
const uncounted = (reason: SummaryState['reason']) => ({
  ...COVERAGE_PAGE,
  summary: without(reason),
  unavailable: ['inSitemap' as const],
  facets: {},
});

// ------------------------------------------------------------------------------------------------------- The list

/** The garden crawl over the mock: no sitemap could be read, and the screen says where the crawl looked. */
export const Garden: Story = {
  loaders: [freshStory],
  render: () => <ScreenApp path={`/crawls/${GARDEN_CRAWL}/sitemaps`} crawlId={GARDEN_CRAWL} />,
};

/** The 100k synthetic crawl over the mock: an index with twelve sitemaps under it, one of which couldn't be read. */
export const Synthetic: Story = {
  loaders: [freshStory],
  render: () => <ScreenApp path={`/crawls/${SYNTHETIC_CRAWL}/sitemaps`} crawlId={SYNTHETIC_CRAWL} />,
};

/**
 * The list: an index and its sitemaps, one given in the settings, one the crawl can't say how it found. A sitemap's
 * name is a link and a click anywhere on its row opens it; one an index lists says "In /sitemap.xml".
 */
export const List: Story = {
  loaders: [() => own()],
  render: () => <ScreenApp path={LIST} />,
};

/** First load: the list's frame from the start, its rows as skeletons after 150 ms. */
export const FirstLoad: Story = {
  loaders: [() => ownSidecar([['GET', /\/sitemaps$/, never], ...sitemapsRobotsRoutes()])],
  render: () => <ScreenApp path={LIST} />,
};

/** Refreshing: the list is read again behind what's shown; a thin line, nothing blanks. */
export const Refreshing: Story = {
  loaders: [
    () => {
      let calls = 0;
      ownSidecar([
        ['GET', /\/sitemaps$/, () => (calls++ === 0 ? json(TEST_SITEMAP_LIST) : never())],
        ...sitemapsRobotsRoutes(),
      ]);
    },
  ],
  render: () => <ScreenApp path={LIST} refreshAfterMs={400} />,
};

/** A crawl's sitemaps as it runs: read one after another, their counts growing as the crawl gets to their URLs. */
function liveList(tick: number): SitemapList {
  const grow = (sitemap: Sitemap, at: number): Sitemap => {
    if (tick < at)
      return {
        ...sitemap,
        state: 'pending',
        statusCode: null,
        entries: 0,
        urls: { listed: 0, fetched: 0, indexable: 0, nonIndexable: 0, robotsDisallowed: 0, redirectingOrBroken: 0 },
      };
    const listed = sitemap.urls.listed * 1_000;
    const fetched = Math.min(listed, (tick - at) * 137);
    return {
      ...sitemap,
      entries: listed,
      urls: {
        listed,
        fetched,
        indexable: Math.round(fetched * 0.9),
        nonIndexable: Math.round(fetched * 0.1),
        robotsDisallowed: Math.round(fetched * 0.02),
        redirectingOrBroken: Math.round(fetched * 0.05),
      },
    };
  };
  const children = TEST_SITEMAPS.filter((sitemap) => sitemap.parentId === 1 && sitemap.state === 'read').map(
    (sitemap, index) => grow(sitemap, index * 3),
  );
  const sum = (key: keyof Sitemap['urls']) => children.reduce((total, child) => total + (child.urls[key] ?? 0), 0);
  const index: Sitemap = {
    ...TEST_SITEMAPS[0]!,
    sitemaps: children.length,
    urls: {
      listed: sum('listed'),
      fetched: sum('fetched'),
      indexable: sum('indexable'),
      nonIndexable: sum('nonIndexable'),
      robotsDisallowed: sum('robotsDisallowed'),
      redirectingOrBroken: sum('redirectingOrBroken'),
    },
  };
  return { discovery: 'running', summary: without('crawling'), sitemaps: [index, ...children] };
}

let liveTick = 0;

/** Live: a crawl running. The counts move in place once a second, and nothing jumps as they grow. */
export const Live: Story = {
  loaders: [
    () => {
      liveTick = 0;
      ownSidecar([
        ['GET', /\/sitemaps$/, () => json(liveList(liveTick))],
        ...sitemapsRobotsRoutes({
          item: { status: 'running', finishedAt: null, analyzedAt: null, issues: null },
          coverage: uncounted('crawling'),
        }),
      ]);
    },
  ],
  render: () => <ScreenApp path={LIST} live={{ onTick: () => liveTick++ }} />,
};

/** Never analysed: every count shows but one, and the screen says which are the crawl's own judgement. */
export const NeverAnalysed: Story = {
  loaders: [
    () =>
      own({
        sitemaps: { ...TEST_SITEMAP_LIST, summary: without('notAnalyzed') },
        coverage: uncounted('notAnalyzed'),
        item: { analyzedAt: null, issues: null },
      }),
  ],
  render: () => <ScreenApp path={LIST} />,
};

/** Out of date: the crawl was continued after its last analysis. */
export const OutOfDate: Story = {
  loaders: [
    () =>
      own({
        sitemaps: { ...TEST_SITEMAP_LIST, summary: without('crawlChanged') },
        coverage: uncounted('crawlChanged'),
        item: { outOfDate: true },
      }),
  ],
  render: () => <ScreenApp path={LIST} />,
};

/** An earlier engine's crawl: another version of Shouting Iguana analysed it. */
export const EarlierEngine: Story = {
  loaders: [
    () =>
      own({
        sitemaps: { ...TEST_SITEMAP_LIST, summary: without('engineChanged') },
        coverage: uncounted('engineChanged'),
        item: { outOfDate: true },
      }),
  ],
  render: () => <ScreenApp path={LIST} />,
};

/** Empty: the crawl found no sitemap, and the screen says where it looked. */
export const NoSitemaps: Story = {
  loaders: [() => own({ sitemaps: { ...TEST_SITEMAP_LIST, sitemaps: [] } })],
  render: () => <ScreenApp path={LIST} />,
};

/** The crawl was told not to read sitemaps: said, with the way to its settings. */
export const SitemapsOff: Story = {
  loaders: [() => own({ sitemaps: { ...TEST_SITEMAP_LIST, discovery: 'off', sitemaps: [] } })],
  render: () => <ScreenApp path={LIST} />,
};

/** A stopped crawl that hadn't read every sitemap: the list isn't all there is, with Continue. */
export const Unfinished: Story = {
  loaders: [
    () =>
      own({
        sitemaps: {
          discovery: 'unfinished',
          summary: without('notAnalyzed'),
          sitemaps: [
            ...TEST_SITEMAPS.slice(0, 3),
            testSitemap({
              id: 7,
              url: `${SITE}/sitemaps/later.xml`,
              parentId: 1,
              foundVia: 'index',
              state: 'pending',
              statusCode: null,
            }),
          ],
        },
        coverage: uncounted('notAnalyzed'),
        item: { status: 'stopped', analyzedAt: null, issues: null },
      }),
  ],
  render: () => <ScreenApp path={LIST} />,
};

/** The only sitemap couldn't be read: the site has none the crawl could use, and it says where it looked. */
export const NoneRead: Story = {
  loaders: [
    () =>
      own({
        sitemaps: {
          ...TEST_SITEMAP_LIST,
          sitemaps: [
            testSitemap({
              id: 1,
              url: `${SITE}/sitemap.xml`,
              foundVia: 'defaultLocation',
              state: 'failed',
              statusCode: 404,
              error: '404 Not Found',
            }),
          ],
        },
      }),
  ],
  render: () => <ScreenApp path={LIST} />,
};

/** An error: the list couldn't be read. A plain cause, the detail behind Details, and Try again. */
export const ListError: Story = {
  loaders: [() => own({ sitemaps: FAILED() })],
  render: () => <ScreenApp path={LIST} />,
};

/** A failed part doesn't blank the rest: the pages no sitemap lists couldn't be counted, and the list is all there. */
export const CoverageError: Story = {
  loaders: [() => own({ coverage: FAILED() })],
  render: () => <ScreenApp path={LIST} />,
};

const LONG = `${SITE}/sitemaps/${'category-with-a-very-long-name-'.repeat(8)}products.xml?page=12&session=${'a1b2c3'.repeat(20)}`;

/** Sizes at the edges: seven-digit counts, very long URLs and errors, a thousand sitemaps, two levels of index. */
export const LongAndMany: Story = {
  loaders: [
    () => {
      const big = { listed: 1_234_567, fetched: 1_198_765, indexable: 1_100_000, nonIndexable: 98_765 };
      const many = Array.from({ length: 1_000 }, (_, index) =>
        testSitemap({
          id: index + 10,
          url: index % 7 === 0 ? `${LONG}&n=${index}` : `${SITE}/sitemaps/products-${index}.xml`,
          parentId: index < 500 ? 2 : 3,
          foundVia: 'index',
          entries: 50_000,
          urls: { listed: 48_211, fetched: 47_903, indexable: 44_000, nonIndexable: 3_903 },
          ...(index % 11 === 0
            ? {
                state: 'failed' as const,
                error:
                  'The XML couldn’t be read: an unexpected end of file at line 48,211, position 17, inside the element urlset',
                statusCode: 200,
              }
            : {}),
        }),
      );
      own({
        sitemaps: {
          ...TEST_SITEMAP_LIST,
          sitemaps: [
            testSitemap({ id: 1, url: LONG, isIndex: true, sitemaps: 2, urls: big }),
            testSitemap({
              id: 2,
              url: `${SITE}/sitemaps/index-a.xml`,
              parentId: 1,
              foundVia: 'index',
              isIndex: true,
              sitemaps: 500,
              urls: big,
            }),
            testSitemap({
              id: 3,
              url: `${SITE}/sitemaps/index-b.xml`,
              parentId: 1,
              foundVia: 'index',
              isIndex: true,
              sitemaps: 500,
              urls: big,
            }),
            ...many,
            testSitemap({
              id: 5_000,
              url: `${SITE}/sitemaps/over-the-limit.xml`,
              foundVia: 'index',
              parentId: 3,
              state: 'notRead',
              statusCode: null,
              // As the engine words it (SitemapDiscoverer).
              error: 'Not read: the crawler reads at most 1,000 sitemaps',
            }),
          ],
        },
        coverage: {
          ...COVERAGE_PAGE,
          matching: 1_500_000,
          facets: {
            inSitemap: [{ value: false, count: 265_433, filter: { column: 'inSitemap', op: 'is', value: false } }],
          },
        },
      });
    },
  ],
  render: () => <ScreenApp path={LIST} />,
};

// ---------------------------------------------------------------------------------------------------- One sitemap

/** One sitemap: its facts, the quick filters, and its URLs as the URL grid, each with what the crawl did with it. */
export const OneSitemap: Story = {
  loaders: [() => own()],
  render: () => <ScreenApp path={`${LIST}/1`} />,
};

/** The 100k crawl's index over the mock: the URL grid narrowed to it. */
export const SyntheticSitemap: Story = {
  loaders: [freshStory],
  render: () => <ScreenApp path={`/crawls/${SYNTHETIC_CRAWL}/sitemaps/1`} crawlId={SYNTHETIC_CRAWL} />,
};

/** A quick filter: listed, but redirecting or broken. */
export const OneSitemapBroken: Story = {
  loaders: [() => own()],
  render: () => (
    <ScreenApp
      path={`${LIST}/1?filter=${encodeURIComponent(
        JSON.stringify([
          {
            column: 'outcome',
            op: 'in',
            values: ['redirect', 'clientError', 'serverError', 'timeout', 'networkError'],
          },
        ]),
      )}`}
    />
  ),
};

/** A count's own filter, from the list: a chip, and a segment of its own beside the quick filters. */
export const OneSitemapOtherFilter: Story = {
  loaders: [() => own()],
  render: () => (
    <ScreenApp
      path={`${LIST}/1?filter=${encodeURIComponent(JSON.stringify([{ column: 'fetched', op: 'is', value: true }]))}`}
    />
  ),
};

/** Filtered to nothing: says so, shows the filter, and clears it in one click. */
export const OneSitemapNothingMatches: Story = {
  loaders: [() => own()],
  render: () => <ScreenApp path={`${LIST}/5?q=nothing-like-this`} />,
};

/** A sitemap that couldn't be read: no URLs to show, and why. */
export const OneSitemapFailed: Story = {
  loaders: [() => own()],
  render: () => <ScreenApp path={`${LIST}/4`} />,
};

/** A sitemap the crawl can't say how it found: said plainly, under its facts. */
export const OneSitemapUnknownSource: Story = {
  loaders: [() => own()],
  render: () => <ScreenApp path={`${LIST}/6`} />,
};

/** Not found: a stale id says so, and offers the list. */
export const OneSitemapNotFound: Story = {
  loaders: [() => own()],
  render: () => <ScreenApp path={`${LIST}/999`} />,
};

/** Never analysed: the analysis's columns say why they're empty, and whose judgement "indexable" is. */
export const OneSitemapNeverAnalysed: Story = {
  loaders: [
    () =>
      own({
        sitemaps: { ...TEST_SITEMAP_LIST, summary: without('notAnalyzed') },
        item: { analyzedAt: null, issues: null },
      }),
  ],
  render: () => <ScreenApp path={`${LIST}/1`} />,
};

/** Live: a crawl running, the grid refreshing in place. */
export const OneSitemapLive: Story = {
  loaders: [
    () =>
      own({
        sitemaps: { ...TEST_SITEMAP_LIST, discovery: 'running', summary: without('crawling') },
        item: { status: 'running', finishedAt: null, analyzedAt: null, issues: null },
      }),
  ],
  render: () => <ScreenApp path={`${LIST}/3`} live={{}} />,
};

/** First load of one sitemap: its facts' line and its grid's rows keep their room. */
export const OneSitemapFirstLoad: Story = {
  loaders: [() => own({ delayMs: 60_000 })],
  render: () => <ScreenApp path={`${LIST}/1`} />,
};

/** A failed part doesn't blank the rest: the sitemap's facts couldn't be read, and its URLs are all there. */
export const OneSitemapFactsError: Story = {
  loaders: [() => own({ sitemaps: FAILED() })],
  render: () => <ScreenApp path={`${LIST}/2`} />,
};

/** The inspector open on one of its URLs. */
export const OneSitemapInspecting: Story = {
  loaders: [
    () =>
      ownSidecar([
        ...inspectorRoutes({ urls: { 2: testUrlDetail({ id: 2, url: `${SITE}/about` }) } }),
        ...sitemapsRobotsRoutes(),
      ]),
  ],
  render: () => <ScreenApp path={`${LIST}/2?inspect=2`} />,
};

/**
 * A link landed on one of its URLs (`?inspect=`, the inspector's "In a sitemap: Yes: pages.xml"): the grid starts on
 * that URL's row, marked though the grid hasn't focus, and the inspector stays on it.
 */
export const OneSitemapLandedOnAUrl: Story = {
  loaders: [
    () =>
      ownSidecar([
        ...inspectorRoutes({ urls: { 4: testUrlDetail({ id: 4, url: `${SITE}/contact` }) } }),
        ...sitemapsRobotsRoutes(),
      ]),
  ],
  render: () => <ScreenApp path={`${LIST}/2?inspect=4`} />,
};

// ------------------------------------------------------------------------------------ What the review's fixes added

const BLOCKED = encodeURIComponent(JSON.stringify([{ column: 'outcome', op: 'in', values: ['robotsDisallowed'] }]));

/**
 * A crawl that stopped between an index and the sitemaps it lists: the index was read, the one sitemap of its the
 * crawl had taken up never was. Its counts are its sitemaps', so they're dashes, never 0; its badge can't say how
 * many it lists; and its status says how far the crawl got.
 */
export const IndexOfAStoppedCrawl: Story = {
  loaders: [
    () =>
      own({
        sitemaps: {
          discovery: 'unfinished',
          summary: { available: true, reason: null },
          sitemaps: [
            testSitemap({ id: 1, url: `${SITE}/sitemap.xml`, isIndex: true, sitemaps: 1 }),
            testSitemap({
              id: 2,
              url: `${SITE}/sitemaps/pages.xml`,
              parentId: 1,
              foundVia: 'index',
              state: 'pending',
              statusCode: null,
            }),
          ],
        },
        // The analysis skipped the comparison of pages with sitemaps, and says why.
        coverage: {
          ...COVERAGE_PAGE,
          matching: 1,
          facets: { inSitemap: [{ value: null, count: 1, filter: { column: 'inSitemap', op: 'isEmpty' } }] },
        },
        analysis: {
          skipped: [
            { checkId: 'not-in-sitemap', reason: { code: 'crawl-unfinished', text: 'The crawl didn’t finish.' } },
          ],
        },
        item: { status: 'stopped' },
      }),
  ],
  render: () => <ScreenApp path={LIST} />,
};

/** The same index, opened: no URLs to show, and why, never "This sitemap lists no URLs". */
export const OneIndexOfAStoppedCrawl: Story = {
  loaders: [
    () =>
      own({
        sitemaps: {
          discovery: 'unfinished',
          summary: { available: true, reason: null },
          sitemaps: [
            testSitemap({ id: 11, url: `${SITE}/sitemap.xml`, isIndex: true, sitemaps: 1 }),
            testSitemap({ id: 12, parentId: 11, foundVia: 'index', state: 'pending', statusCode: null }),
          ],
        },
        item: { status: 'stopped' },
      }),
  ],
  render: () => <ScreenApp path={`${LIST}/11`} />,
};

/** An index with every sitemap under it read: its own counts, as links. */
export const IndexReadWhole: Story = {
  loaders: [
    () => own({ sitemaps: { ...TEST_SITEMAP_LIST, sitemaps: TEST_SITEMAPS.filter((sitemap) => sitemap.id !== 4) } }),
  ],
  render: () => <ScreenApp path={LIST} />,
};

/**
 * An engine from before the list counted, for each sitemap, the URLs robots.txt refused: "Crawled" and "Blocked by
 * robots.txt" are dashes that say why, never the engine's "done with" number under the name "Crawled".
 */
export const EngineWithoutTheNewCounts: Story = {
  loaders: [() => own({ sitemaps: { ...TEST_SITEMAP_LIST, sitemaps: TEST_SITEMAPS.map(withoutNewCounts) } })],
  render: () => <ScreenApp path={LIST} />,
};

/** One sitemap under that engine: its page counts the crawled and the blocked itself. */
export const OneSitemapCountsItself: Story = {
  loaders: [() => own({ sitemaps: { ...TEST_SITEMAP_LIST, sitemaps: TEST_SITEMAPS.map(withoutNewCounts) } })],
  render: () => <ScreenApp path={`${LIST}/6`} />,
};

/** What robots.txt blocked of one sitemap's URLs: the count's link from the list lands here. */
export const OneSitemapBlockedByRobots: Story = {
  loaders: [() => own()],
  render: () => <ScreenApp path={`${LIST}/6?filter=${BLOCKED}`} />,
};

/** Nothing declared a sitemap, and the default location has none: no sitemap found, not one that failed. */
export const NothingAtTheDefaultLocation: Story = {
  loaders: [
    () =>
      own({
        sitemaps: {
          ...TEST_SITEMAP_LIST,
          sitemaps: [
            testSitemap({
              id: 1,
              url: `${SITE}/sitemap.xml`,
              foundVia: 'defaultLocation',
              state: 'failed',
              statusCode: 404,
              error: '404 Not Found',
            }),
          ],
        },
      }),
  ],
  render: () => <ScreenApp path={LIST} />,
};

/** Sitemaps that failed in ways the engine records in its own text: said by their kind, the text a hover away. */
export const FailedInWords: Story = {
  loaders: [
    () =>
      own({
        sitemaps: {
          ...TEST_SITEMAP_LIST,
          sitemaps: [
            testSitemap({ id: 1, entries: 4, urls: TEST_SITEMAPS[1]?.urls ?? TEST_SITEMAPS[0]!.urls }),
            testSitemap({
              id: 2,
              state: 'failed',
              statusCode: null,
              error:
                'Unexpected end of file has occurred. The following elements are not closed: urlset. Line 1217, position 3.',
            }),
            testSitemap({
              id: 3,
              state: 'failed',
              statusCode: null,
              error: 'No such host is known. (cdn.example.net:443)',
            }),
            testSitemap({
              id: 4,
              state: 'failed',
              statusCode: null,
              error: 'The SSL connection could not be established, see inner exception.',
            }),
            testSitemap({ id: 5, state: 'failed', statusCode: null, error: 'Timed out', entries: 40 }),
            testSitemap({
              id: 6,
              state: 'notRead',
              statusCode: null,
              error: 'Not read: sitemap indexes nested more than 3 deep aren’t followed',
            }),
          ],
        },
      }),
  ],
  render: () => <ScreenApp path={LIST} />,
};

/** A sitemap a crawl that hasn't ended doesn't have: "not so far", and the page fills in if the crawl finds it. */
export const OneSitemapNotFoundYet: Story = {
  loaders: [
    () =>
      own({
        sitemaps: { ...TEST_SITEMAP_LIST, discovery: 'running', summary: without('crawling') },
        item: { status: 'running', finishedAt: null, analyzedAt: null, issues: null },
      }),
  ],
  render: () => <ScreenApp path={`${LIST}/999`} live={{}} />,
};
