import type { Meta, StoryObj } from '@storybook/react-vite';
import type { CrawlComparison } from '../api/fixLoop';
import type { Analysis, CrawlListItem } from '../api/types';
import { liveStore } from '../live/liveStore';
import { TEST_CRAWL_ID, type FakeRoute } from '../test/fakeSidecar';
import { changesRoute, comparedGardenItem, gardenComparison } from '../test/fixLoopSidecar';
import { ScreenApp, ownSidecar } from '../test/ScreenStory';
import { GARDEN_ANALYSIS, gardenItem, problemRoutes, simpleRoutes } from '../test/simpleSidecar';

// The fix loop in Simple (docs/v2-fix-loop.md, section 4): the garden crawl compared with its site's earlier check
// (17 Oct, 72), in every state the design lists. Over a sidecar of the story's own (src/test/fixLoopSidecar.ts).
const meta = { title: 'Screens/Simple/Fix loop' } satisfies Meta;
export default meta;
type Story = StoryObj<typeof meta>;

const CRAWL = `/crawls/${TEST_CRAWL_ID}`;
const SUMMARY = `${CRAWL}/overview`;

function own(item: CrawlListItem, extra: FakeRoute[] = [], analysis: Analysis = GARDEN_ANALYSIS) {
  ownSidecar([...extra, ...problemRoutes(), ...simpleRoutes(item, analysis)]);
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

const compared = (comparison: CrawlComparison) =>
  summary(comparedGardenItem({}, comparison), [changesRoute(comparison)]);

// ---------------------------------------------------------------------------------------------------- The Summary

/** Since the last check: "score 72 → 86, 3 problems fixed, 1 new", each a link, and See what changed. */
export const SummarySinceLastCheck: Story = compared(gardenComparison());

/** A site's first check: nothing at all, no line and no zeros. */
export const SummaryFirstCheck: Story = summary(gardenItem({ previous: null, changes: null }), [
  changesRoute({ crawlId: TEST_CRAWL_ID, with: null }),
]);

/** The comparison on its way: the Summary without the line, which appears without moving what's above. */
export const SummaryComparisonLoading: Story = summary(comparedGardenItem(), [changesRoute('never')]);

/** Partial: the last check read far more of the site; one caution line. */
export const SummaryPartial: Story = compared(
  gardenComparison({ comparable: 'partial', differences: [{ code: 'coverage', then: 265, now: 50 }] }),
);

/** Counts only: the start address moved to HTTPS, so only the totals are compared. */
export const SummaryCountsOnly: Story = compared(
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
