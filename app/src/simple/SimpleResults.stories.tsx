import type { Meta, StoryObj } from '@storybook/react-vite';
import type { Analysis, CrawlListItem, Score } from '../api/types';
import { liveStore } from '../live/liveStore';
import { updateSettings } from '../settings/settingsStore';
import { TEST_CRAWL_ID, json, type FakeRoute } from '../test/fakeSidecar';
import { AT_200_PERCENT, ScreenApp, ownSidecar } from '../test/ScreenStory';
import {
  GARDEN_ANALYSIS,
  NOTICES_ANALYSIS,
  NO_PAGES_ANALYSIS,
  PERFECT_ANALYSIS,
  SITE_WIDE_ANALYSIS,
  STOPPED_ANALYSIS,
  gardenItem,
  problemRoutes,
  simpleRoutes,
} from '../test/simpleSidecar';

const meta = { title: 'Screens/Simple results' } satisfies Meta;
export default meta;
type Story = StoryObj<typeof meta>;

const CRAWL = `/crawls/${TEST_CRAWL_ID}`;
const SUMMARY = `${CRAWL}/overview`;
const FIX = `${CRAWL}/issues`;
const never = () => new Promise<Response>(() => undefined);

/** A sidecar of the story's own: the garden crawl (or the item given) and an analysis, with its problems' pages. */
function own(analysis: Analysis | 'not-analyzed', item: CrawlListItem = gardenItem(), extra: FakeRoute[] = []) {
  ownSidecar([...extra, ...problemRoutes(), ...simpleRoutes(item, analysis)]);
}

const story = (
  path: string,
  analysis: Analysis | 'not-analyzed',
  item?: CrawlListItem,
  extra?: FakeRoute[],
  size?: { width: number; height: number },
): Story => ({
  loaders: [() => own(analysis, item, extra)],
  render: () => <ScreenApp path={path} mode="simple" size={size} />,
});

const scored = (changes: Partial<Score>, analysis: Analysis = GARDEN_ANALYSIS): Analysis => ({
  ...analysis,
  score: { ...analysis.score!, ...changes },
});

// ---------------------------------------------------------------------------------------------------- The Summary

/** The garden crawl, as today's engine scores it: 85, Good; 2 serious, 4 worth fixing, 13 worth a look. */
export const Summary: Story = story(SUMMARY, GARDEN_ANALYSIS);

/** First load: the skeleton in the screen's shape (the ring's outline, three lines, five rows) after 150 ms. */
export const SummaryFirstLoad: Story = story(SUMMARY, GARDEN_ANALYSIS, gardenItem(), [['GET', /\/analysis$/, never]]);

/** Nothing found at all: 100, Very good, and when to check again. */
export const SummaryPerfect: Story = story(
  SUMMARY,
  PERFECT_ANALYSIS,
  gardenItem({ issues: { error: 0, warning: 0, notice: 0 } }),
);

/** Only things worth a look: nothing needs fixing, and they don't change the score. */
export const SummaryNothingButNotices: Story = story(SUMMARY, NOTICES_ANALYSIS);

/** Stopped early: the banner says what it read and what was left, with Continue checking; the score is "so far". */
export const SummaryStoppedEarly: Story = story(SUMMARY, STOPPED_ANALYSIS, gardenItem({ status: 'stopped' }));

/** Stopped at its limit of addresses. */
export const SummaryAtItsLimit: Story = story(
  SUMMARY,
  scored({
    partial: true,
    covers: { ...GARDEN_ANALYSIS.score!.covers, complete: false, reason: 'urlLimit' },
  }),
  gardenItem(),
  [['GET', /\/options$/, () => json({ maxUrls: 5_000 })]],
);

/** Turned away part way: the site's security stopped the check. */
export const SummaryBlockedPartWay: Story = story(SUMMARY, STOPPED_ANALYSIS, gardenItem({ status: 'stopped' }), [
  [
    'GET',
    /\/alerts$/,
    () =>
      json([
        {
          crawlId: TEST_CRAWL_ID,
          id: 1,
          level: 'error',
          code: 'blocked',
          message: 'The site is blocking the crawler.',
          at: '2026-10-31T14:40:00Z',
        },
      ]),
  ],
]);

/** Failed: it couldn't finish; what it read is shown as a check stopped early. */
export const SummaryFailed: Story = story(
  SUMMARY,
  STOPPED_ANALYSIS,
  gardenItem({ status: 'failed', error: 'The disk is full.' }),
);

/** A serious fault of the whole site leads the sentence and the list: no HTTPS, 36, Poor. */
export const SummarySiteWideFault: Story = story(SUMMARY, SITE_WIDE_ANALYSIS);

/** Out of date: the banner says why, with Update the results; the score and lists stay. */
export const SummaryOutOfDate: Story = story(SUMMARY, {
  ...GARDEN_ANALYSIS,
  outOfDate: true,
  outOfDateReasons: ['crawlChanged', 'settingsChanged'],
});

/** Never analysed: no score yet, and Work out the results. The line of facts still shows. */
export const SummaryNeverAnalysed: Story = story(
  SUMMARY,
  'not-analyzed',
  gardenItem({ analyzedAt: null, issues: null, score: null }),
);

/** Stopped early before its results were worked out: the stopped banner with Continue checking, above No score yet. */
export const SummaryStoppedNeverAnalysed: Story = story(
  SUMMARY,
  'not-analyzed',
  gardenItem({ status: 'stopped', analyzedAt: null, issues: null, score: null }),
);

/** Failed before its results were worked out: couldn't finish, its cause and Try again, above No score yet. */
export const SummaryFailedNeverAnalysed: Story = story(
  SUMMARY,
  'not-analyzed',
  gardenItem({ status: 'failed', error: 'The disk is full.', analyzedAt: null, issues: null, score: null }),
);

/** The results couldn't be worked out: the analysis failed, with Try again and its details. */
export const SummaryResultsFailed: Story = {
  loaders: [
    () => {
      own('not-analyzed', gardenItem({ analyzedAt: null, issues: null, score: null }));
      liveStore.ingest({
        event: 'crawl',
        id: '1',
        data: JSON.stringify({ crawlId: TEST_CRAWL_ID, status: 'completed', error: 'A rule failed: out of memory.' }),
      });
    },
  ],
  render: () => <ScreenApp path={SUMMARY} mode="simple" />,
};

/** An analysis by an engine from before the score: no score, and Update the results. */
export const SummaryNoScoreOldEngine: Story = story(SUMMARY, {
  ...GARDEN_ANALYSIS,
  score: null,
  noScoreReason: 'notScored',
  outOfDate: true,
  outOfDateReasons: ['engineChanged'],
});

/** Nothing could be read: the start address answered "not found". */
export const SummaryNothingRead: Story = story(
  SUMMARY,
  NO_PAGES_ANALYSIS,
  gardenItem({ pageCount: 0, score: null, issues: null }),
  [
    [
      'POST',
      /\/urls$/,
      () =>
        json({
          columns: ['id', 'url', 'statusCode', 'outcome'],
          rows: [{ id: 1, url: 'https://www.gardenfurnituresets.co.uk/', statusCode: 404, outcome: 'clientError' }],
          offset: 0,
          matching: 1,
          total: 1,
          next: null,
          summary: { available: true, reason: null },
          unavailable: [],
        }),
    ],
  ],
);

/** The start address as robots.txt kept the check from it, and how that robots.txt answered: one sentence each. */
function robotsKeptOut(state: 'read' | 'unreachable', statusCode: number | null): Story {
  return story(SUMMARY, NO_PAGES_ANALYSIS, gardenItem({ pageCount: 0, score: null, issues: null }), [
    [
      'POST',
      /\/urls$/,
      () =>
        json({
          columns: ['id', 'url', 'statusCode', 'outcome'],
          rows: [
            { id: 1, url: 'https://www.gardenfurnituresets.co.uk/', statusCode: null, outcome: 'robotsDisallowed' },
          ],
          offset: 0,
          matching: 1,
          total: 1,
          next: null,
          summary: { available: true, reason: null },
          unavailable: [],
        }),
    ],
    [
      'POST',
      /\/robots\/file$/,
      () =>
        json({
          origin: 'https://www.gardenfurnituresets.co.uk',
          isInternal: true,
          state,
          statusCode,
          fetchedAt: '2026-10-31T14:13:00Z',
          error: statusCode === null && state === 'unreachable' ? 'Connection refused' : null,
          bytes: state === 'read' ? 24 : null,
          blocked: 1,
          respected: true,
          userAgent: 'ShoutingIguana',
          lines: [],
          unmatched: { unreachable: 0, unknown: 0 },
        }),
    ],
  ]);
}

/** Nothing read: robots.txt's rules tell checkers to stay out. */
export const SummaryNothingReadRobotsRules: Story = robotsKeptOut('read', 200);

/** Nothing read: robots.txt answered a server error, so the check stayed out until it could be read (gap 1). */
export const SummaryNothingReadRobotsFailing: Story = robotsKeptOut('unreachable', 503);

/** Nothing read: the site didn't answer when asked for robots.txt: never "stay out" (the closing audit's gap 1). */
export const SummaryNothingReadRobotsNoAnswer: Story = robotsKeptOut('unreachable', null);

/** A tiny site: 5 pages, and the coverage says it may have more that need JavaScript to reach. */
export const SummaryTinySite: Story = story(
  SUMMARY,
  scored(
    { covers: { ...GARDEN_ANALYSIS.score!.covers, pages: 5, urls: 12 } },
    { ...GARDEN_ANALYSIS, pagesAnalyzed: 5 },
  ),
  gardenItem({ pageCount: 5 }),
);

/** A huge site: six-digit counts take separators, and the layout holds. */
export const SummaryHugeSite: Story = story(
  SUMMARY,
  scored(
    { covers: { ...GARDEN_ANALYSIS.score!.covers, pages: 101_107, urls: 110_251 } },
    {
      ...GARDEN_ANALYSIS,
      pagesAnalyzed: 101_107,
      crawledUrlCount: 110_251,
      counts: GARDEN_ANALYSIS.counts.map((count) => ({ ...count, count: count.count * 381 })),
    },
  ),
  gardenItem({ pageCount: 101_107, crawledUrlCount: 110_251, fetchedUrlCount: 110_251 }),
);

/** At 200% zoom of a 1280x720 window: the score and its counts stack. */
export const SummaryAt200Percent: Story = story(SUMMARY, GARDEN_ANALYSIS, gardenItem(), [], AT_200_PERCENT);

/** "How the score is worked out", over the Summary: the lines, the sum, what it covers, the points, the version. */
export const ScoreBreakdown: Story = story(`${SUMMARY}?score=1`, GARDEN_ANALYSIS);

/** The breakdown when the ceiling holds the score: "Held at 89 while a serious problem stands (98 otherwise)". */
export const ScoreBreakdownHeld: Story = story(
  `${SUMMARY}?score=1`,
  scored({ value: 89, beforeCeiling: 98, lost: 2, heldBySerious: true }),
);

// ---------------------------------------------------------------------------------------------------- What to fix

/** What to fix on the garden crawl: three groups, Couldn't check and Nothing wrong folded. */
export const WhatToFix: Story = story(FIX, GARDEN_ANALYSIS);

/** Landed on from the Summary's "13 Things worth a look": all of them, the heading in view with focus. */
export const WhatToFixLandedOnNotices: Story = story(`${FIX}#worth-a-look`, GARDEN_ANALYSIS);

/** Landed on "Checks found nothing wrong": the fold is open. */
export const WhatToFixNothingWrongOpen: Story = story(`${FIX}#nothing-wrong`, GARDEN_ANALYSIS);

/** A check hidden in Advanced stays out, with a line to show it. */
export const WhatToFixWithAHiddenCheck: Story = {
  loaders: [
    () => {
      own(GARDEN_ANALYSIS);
      updateSettings({ hiddenChecks: ['image-too-large'] });
    },
  ],
  render: () => <ScreenApp path={FIX} mode="simple" />,
};

/** Every problem's check hidden in Advanced: nothing to fix among the checks shown, and how many are hidden. */
const EVERY_PROBLEM = [
  'internal-client-error',
  'broken-internal-links',
  'title-duplicate',
  'description-duplicate',
  'image-too-large',
  'slow-response',
];

export const WhatToFixEveryProblemHidden: Story = {
  loaders: [
    () => {
      own(GARDEN_ANALYSIS);
      updateSettings({ hiddenChecks: EVERY_PROBLEM });
    },
  ],
  render: () => <ScreenApp path={FIX} mode="simple" />,
};

/** The Summary with every problem's check hidden: nothing to fix among the checks shown; the score is the engine's. */
export const SummaryEveryProblemHidden: Story = {
  loaders: [
    () => {
      own(GARDEN_ANALYSIS);
      updateSettings({ hiddenChecks: EVERY_PROBLEM });
    },
  ],
  render: () => <ScreenApp path={SUMMARY} mode="simple" />,
};

/** Nothing to fix: the things worth a look, all of them. */
export const WhatToFixNothingToFix: Story = story(FIX, NOTICES_ANALYSIS);

/** Never analysed: Work out the results. */
export const WhatToFixNeverAnalysed: Story = story(
  FIX,
  'not-analyzed',
  gardenItem({ analyzedAt: null, issues: null, score: null }),
);

/** First load: rows as skeletons after 150 ms. */
export const WhatToFixFirstLoad: Story = story(FIX, GARDEN_ANALYSIS, gardenItem(), [['GET', /\/analysis$/, never]]);

/** At 200% zoom. */
export const WhatToFixAt200Percent: Story = story(FIX, GARDEN_ANALYSIS, gardenItem(), [], AT_200_PERCENT);

// ---------------------------------------------------------------------------------------------------- One problem

/** A template fault: the One fix callout leads, and the cause on 264 pages names its first three. */
export const ProblemTemplateFault: Story = story(`${FIX}/empty-anchor-text`, GARDEN_ANALYSIS);

/** Broken links: each broken address with its status, and the page it's linked from. */
export const ProblemBrokenLinks: Story = story(`${FIX}/broken-internal-links`, GARDEN_ANALYSIS);

/** More causes than a page: 25 at a time, Show 25 more. */
export const ProblemManyCauses: Story = story(`${FIX}/image-too-large`, GARDEN_ANALYSIS);

/** A check that doesn't group: its pages flat, with what was measured; a page with no title is its address. */
export const ProblemMeasured: Story = story(`${FIX}/slow-response`, GARDEN_ANALYSIS);

/** A check that couldn't run: what it looks for, why, and what would let it. */
export const ProblemSkipped: Story = story(`${FIX}/orphan-page`, GARDEN_ANALYSIS);

/** A check that found nothing: its good news. */
export const ProblemPassed: Story = story(`${FIX}/title-missing`, GARDEN_ANALYSIS);

/** At 200% zoom: the two columns stack. */
export const ProblemAt200Percent: Story = story(
  `${FIX}/broken-internal-links`,
  GARDEN_ANALYSIS,
  gardenItem(),
  [],
  AT_200_PERCENT,
);
