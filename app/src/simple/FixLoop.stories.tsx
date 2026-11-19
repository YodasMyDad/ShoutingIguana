import type { Meta, StoryObj } from '@storybook/react-vite';
import type { CrawlComparison } from '../api/fixLoop';
import type { Analysis, CrawlListItem } from '../api/types';
import { liveStore } from '../live/liveStore';
import { TEST_CRAWL_ID, json, type FakeRoute } from '../test/fakeSidecar';
import {
  RECHECK_ROWS,
  changesRoute,
  compared,
  comparedGardenItem,
  fixedRoute,
  gardenComparison,
  recheckPlan,
  recheckRecord,
  recheckResult,
  recheckRoutes,
} from '../test/fixLoopSidecar';
import { ScreenApp, ownSidecar } from '../test/ScreenStory';
import { GARDEN_ANALYSIS, SIMPLE_CHECKS, gardenItem, problemRoutes, simpleRoutes } from '../test/simpleSidecar';

// The fix loop in Simple (docs/v2-fix-loop.md, section 4): the garden crawl compared with its site's earlier check
// (17 Oct, 72), in every state the design lists. Over a sidecar of the story's own (src/test/fixLoopSidecar.ts).
const meta = { title: 'Screens/Simple/Fix loop' } satisfies Meta;
export default meta;
type Story = StoryObj<typeof meta>;

const CRAWL = `/crawls/${TEST_CRAWL_ID}`;
const SUMMARY = `${CRAWL}/overview`;

function own(item: CrawlListItem, extra: FakeRoute[] = [], analysis: Analysis = GARDEN_ANALYSIS) {
  // A check's groups and rows compared when asked, as the sidecar answers them.
  ownSidecar([...extra, ...compared(problemRoutes()), ...simpleRoutes(item, analysis)]);
}

const summary = (item: CrawlListItem, extra: FakeRoute[] = [], before?: () => void): Story => ({
  loaders: [
    () => {
      own(item, extra);
      before?.();
    },
  ],
  render: () => <ScreenApp path={SUMMARY} mode="simple" />,
});

const comparedSummary = (comparison: CrawlComparison) =>
  summary(comparedGardenItem({}, comparison), [changesRoute(comparison)]);

// ---------------------------------------------------------------------------------------------------- The Summary

/** Since the last check: "score 72 → 87, 3 problems fixed, 1 new", each a link, and See what changed. */
export const SummarySinceLastCheck: Story = comparedSummary(gardenComparison());

/** A site's first check: nothing at all, no line and no zeros. */
export const SummaryFirstCheck: Story = summary(gardenItem({ previous: null, changes: null }), [
  changesRoute({ crawlId: TEST_CRAWL_ID, with: null }),
]);

/** The comparison on its way: the Summary without the line, which appears without moving what's above. */
export const SummaryComparisonLoading: Story = summary(comparedGardenItem(), [changesRoute('never')]);

/** Partial: the last check read far more of the site; one caution line. */
export const SummaryPartial: Story = comparedSummary(
  gardenComparison({ comparable: 'partial', differences: [{ code: 'coverage', then: 265, now: 50 }] }),
);

/** Counts only: the start address moved to HTTPS, so only the totals are compared. */
export const SummaryCountsOnly: Story = comparedSummary(
  gardenComparison({
    comparable: 'countsOnly',
    differences: [
      {
        code: 'startOrigin',
        then: 'http://www.gardenfurnituresets.co.uk/',
        now: 'https://www.gardenfurnituresets.co.uk/',
      },
    ],
  }),
);

/**
 * The last check was judged by other rules: Simple works it out again by itself (its analysis is asked for, and
 * never answers here), so the line waits.
 */
export const SummaryWorkingOutTheLastCheck: Story = summary(
  comparedGardenItem({}, gardenComparison({ comparable: 'countsOnly', previousNeedsAnalysis: ['engineChanged'] })),
  [
    changesRoute(gardenComparison({ comparable: 'countsOnly', previousNeedsAnalysis: ['engineChanged'] })),
    ['POST', /^\/api\/crawls\/[0-9a-f]{16}\/analyze$/, () => new Response(null, { status: 202 })],
  ],
);

/** The check was stopped early: its changes are "so far". */
export const SummarySoFar: Story = summary(comparedGardenItem({ status: 'stopped' }), [
  changesRoute(
    gardenComparison({
      comparable: 'partial',
      differences: [{ code: 'stoppedEarly', side: 'now' }],
      now: {
        ...GARDEN_ANALYSIS,
        score: { ...GARDEN_ANALYSIS.score!, partial: true },
      },
    }),
  ),
]);

/** A recheck runs: the Summary stays, the top bar says Rechecking; its line waits for the results. */
export const SummaryWhileRechecking: Story = summary(
  comparedGardenItem({ status: 'rechecking' }),
  [changesRoute()],
  () => {
    liveStore.ingest({ event: 'hello', id: '', data: JSON.stringify({ live: [TEST_CRAWL_ID] }) });
    liveStore.ingest({
      event: 'crawl',
      id: '',
      data: JSON.stringify({ crawlId: TEST_CRAWL_ID, status: 'rechecking' }),
    });
    liveStore.ingest({
      event: 'recheck',
      id: '',
      data: JSON.stringify({
        crawlId: TEST_CRAWL_ID,
        recheckId: 1,
        status: 'running',
        phase: 1,
        done: 3,
        total: 8,
        result: null,
      }),
    });
  },
);

// ---------------------------------------------------------------------------------------------------- The breakdown

/** How the score is worked out, since the last check: each line's points then, and the lines fixed since, folded. */
export const BreakdownSinceLastCheck: Story = {
  loaders: [() => own(comparedGardenItem(), [changesRoute()])],
  render: () => <ScreenApp path={`${SUMMARY}?score=1`} mode="simple" />,
};

// ---------------------------------------------------------------------------------------------------- What to fix

const FIX = `${CRAWL}/issues`;

/** What to fix since the last check: each row's change under its count; the Fixed group folded below the severities. */
export const WhatToFixSinceLastCheck: Story = {
  loaders: [() => own(comparedGardenItem(), [changesRoute()])],
  render: () => <ScreenApp path={FIX} mode="simple" />,
};

/** Landed on from the Summary's "3 problems fixed": the Fixed group open, its heading in view. */
export const WhatToFixFixedGroupOpen: Story = {
  loaders: [() => own(comparedGardenItem(), [changesRoute()])],
  render: () => <ScreenApp path={`${FIX}#fixed-since`} mode="simple" />,
};

/** A site's first check with a recheck that cleared a problem: its Fixed group is what the recheck cleared. */
export const WhatToFixFirstCheckRechecked: Story = {
  loaders: [
    () =>
      own(gardenItem({ previous: null, changes: null }), [
        [
          'GET',
          /^\/api\/crawls\/[0-9a-f]{16}\/rechecks$/,
          () =>
            json({
              rechecks: [
                recheckRecord({
                  checkId: 'title-missing',
                  countBefore: 3,
                  judged: 3,
                  result: recheckResult({ fixed: 3, still: 0 }),
                }),
              ],
            }),
        ],
      ]),
  ],
  render: () => <ScreenApp path={`${FIX}#fixed-since`} mode="simple" />,
};

// ---------------------------------------------------------------------------------------------------- One problem

const BROKEN = `${CRAWL}/issues/broken-internal-links`;

const problem = (
  routes: Parameters<typeof recheckRoutes>[0] = {},
  path = BROKEN,
  live?: () => void,
  item: CrawlListItem = comparedGardenItem(),
): Story => ({
  loaders: [
    () => {
      own(item, [changesRoute(), fixedRoute(), ...recheckRoutes(routes)]);
      live?.();
    },
  ],
  render: () => <ScreenApp path={path} mode="simple" />,
});

const send = (event: string, data: object) => liveStore.ingest({ event, id: '', data: JSON.stringify(data) });

/** Fixed it?: what a recheck reads and how long, "was 9 on 17 Oct" in the head. */
export const ProblemRecheckReady: Story = problem();

/** Past the cap: the 1,000 most important, and checking the whole site instead suggested. */
export const ProblemRecheckCapped: Story = problem({
  plan: recheckPlan({
    judged: 4_000,
    capped: true,
    notRechecked: 3_000,
    estimateMs: 9 * 60_000,
    suggestFullCheck: true,
  }),
});

/** Refused: a check of the site runs; the button off, the reason its line. */
export const ProblemRecheckSiteBusy: Story = problem({
  plan: recheckPlan({
    refused: { type: 'site-busy', title: 'A check of this site is running. Its results will show what’s fixed.' },
  }),
});

/** An earlier check of the site: the same problem on its latest check. */
export const ProblemRecheckEarlierCheck: Story = problem({
  plan: recheckPlan({ refused: { type: 'recheck-not-latest', title: 'Recheck works on the site’s latest check.' } }),
});

/** A check a recheck can't settle: Check the site again in its place. */
export const ProblemRecheckUnsupported: Story = problem({}, `${CRAWL}/issues/non-indexable-inlinks-only`);

/** Running: its count, then working out the results, with Stop. */
export const ProblemRechecking: Story = problem(
  { rechecks: [recheckRecord({ status: 'running', result: null, endedAt: null, settledAt: null })] },
  BROKEN,
  () => {
    send('hello', { live: [TEST_CRAWL_ID] });
    send('crawl', { crawlId: TEST_CRAWL_ID, status: 'rechecking' });
    send('recheck', {
      crawlId: TEST_CRAWL_ID,
      recheckId: 1,
      status: 'running',
      phase: 1,
      done: 3,
      total: 8,
      result: null,
    });
  },
);

/** The analysis after it: "Working out the results…", the page as it was. */
export const ProblemRecheckWorkingOut: Story = problem(
  { rechecks: [recheckRecord({ status: 'running', result: null, endedAt: null, settledAt: null })] },
  BROKEN,
  () => {
    send('hello', { live: [TEST_CRAWL_ID], analyzing: [TEST_CRAWL_ID] });
    send('recheck', {
      crawlId: TEST_CRAWL_ID,
      recheckId: 1,
      status: 'completed',
      phase: 1,
      done: 8,
      total: 8,
      result: null,
    });
    send('crawl', { crawlId: TEST_CRAWL_ID, status: 'analyzing' });
  },
);

/** "4 of 5 fixed. 1 still has it:", the address, the score before and after, when, and Recheck again. */
export const ProblemRecheckSomeFixed: Story = problem({ rechecks: [recheckRecord()] });

/** All of them fixed. */
export const ProblemRecheckAllFixed: Story = problem({
  rechecks: [recheckRecord({ result: recheckResult({ fixed: 5, still: 0 }) })],
});

/** None fixed: maybe a saved copy (a cache). */
export const ProblemRecheckNoneFixed: Story = problem({
  rechecks: [recheckRecord({ result: recheckResult({ fixed: 0, still: 5, scoreAfter: 86 }) })],
});

/** Stopped part way: what it got through. */
export const ProblemRecheckStopped: Story = problem({
  rechecks: [recheckRecord({ status: 'stopped', result: recheckResult({ fixed: 2, still: 1, notRechecked: 2 }) })],
});

/** Cut short when the app closed: what it got through, never more. */
export const ProblemRecheckInterrupted: Story = problem({
  rechecks: [recheckRecord({ status: 'interrupted', result: recheckResult({ fixed: 1, still: 0, notRechecked: 4 }) })],
});

/** Some couldn't be rechecked, and one page has it now that didn't. */
export const ProblemRecheckNotCheckedAndNew: Story = problem({
  rechecks: [recheckRecord({ result: recheckResult({ fixed: 3, still: 0, notChecked: 2, new: 1 }) })],
  rows: [{ ...RECHECK_ROWS[0]!, outcome: 'new', urlId: 77, url: 'https://www.gardenfurnituresets.co.uk/y/' }],
});

/** A problem fixed since the last check: the good news, what was fixed, and what wasn't checked this time. */
export const ProblemFixedSince: Story = problem({}, `${CRAWL}/issues/internal-server-error`);

/** Before the fix loop (a sidecar from before it, no recheck in its catalog): Fixed it? as it was. */
export const ProblemLegacyCard: Story = {
  loaders: [
    () =>
      own(gardenItem({ previous: null, changes: null }), [
        [
          'GET',
          /^\/api\/checks/,
          () =>
            json({ ...SIMPLE_CHECKS, checks: SIMPLE_CHECKS.checks.map(({ recheck: _recheck, ...check }) => check) }),
        ],
      ]),
  ],
  render: () => <ScreenApp path={BROKEN} mode="simple" />,
};
