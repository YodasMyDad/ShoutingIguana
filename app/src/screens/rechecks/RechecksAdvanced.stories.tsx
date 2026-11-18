// Rechecks in Advanced (docs/v2-fix-loop.md, section 4, Advanced): the head's Recheck in each of its states, the
// banner over a check's detail from the analysis first to the result, Overview's Rechecks card and a recheck's URLs.
// Over a sidecar of the story's own; a recheck's run is played on the live store, as the stream would send it.
import type { Meta, StoryObj } from '@storybook/react-vite';
import { QueryClientProvider } from '@tanstack/react-query';
import { RouterContextProvider, createMemoryHistory, createRootRoute, createRouter } from '@tanstack/react-router';
import { useState, type ReactNode } from 'react';
import type { Recheck, RecheckPlan } from '../../api/fixLoop';
import type { Check } from '../../api/types';
import { createQueryClient } from '../../app/queryClient';
import { resetPendingRechecks, startRecheck } from '../../crawl/recheck';
import { liveStore } from '../../live/liveStore';
import { TEST_CRAWL_ID, type FakeRoute } from '../../test/fakeSidecar';
import { recheckEvent, recheckPlan, recheckRecord, recheckResult, recheckRoutes } from '../../test/fixLoopSidecar';
import { message } from '../../test/overviewSidecar';
import { ownSidecar } from '../../test/ScreenStory';
import { StateRow, StoryFrame, StorySection } from '../../test/StoryFrame';
import { RecheckBanner } from './RecheckBanner';
import { RecheckControl } from './RecheckControl';
import { RechecksCard } from './RechecksCard';

const meta = { title: 'Fix loop/Advanced rechecks' } satisfies Meta;
export default meta;
type Story = StoryObj<typeof meta>;

const CHECK = {
  id: 'broken-internal-links',
  title: 'Broken internal links',
  subject: 'page',
  references: 'targets',
  recheck: ['urls'],
} as Check;
const titleOf = (id: string) =>
  ({ 'broken-internal-links': 'Broken internal links', 'title-too-long': 'Titles over 60 characters' })[id] ?? null;

function Frame({ children }: { children: ReactNode }) {
  const [queryClient] = useState(() => createQueryClient());
  const [router] = useState(() => createRouter({ routeTree: createRootRoute(), history: createMemoryHistory() }));
  return (
    <QueryClientProvider client={queryClient}>
      <RouterContextProvider router={router}>
        <StoryFrame>{children}</StoryFrame>
      </RouterContextProvider>
    </QueryClientProvider>
  );
}

const sidecar = (routes: FakeRoute[]) => {
  ownSidecar(routes);
  resetPendingRechecks();
};

// ---------------------------------------------------------------------------------------------- The head's Recheck

const controlStory = (plan: RecheckPlan, check: Check = CHECK, selection?: number[]): Story => ({
  loaders: [() => sidecar(recheckRoutes({ plan }))],
  render: () => (
    <Frame>
      <StorySection title="Recheck in a check’s head">
        <div className="flex justify-end">
          <RecheckControl
            crawlId={TEST_CRAWL_ID}
            check={check}
            group={{ detail: null, relatedUrlId: 812, urls: 3 }}
            selection={selection}
          />
        </div>
      </StorySection>
    </Frame>
  ),
});

/** Ready: "Recheck 5 URLs", what it fetches and about how long under it. */
export const ControlReady: Story = controlStory(recheckPlan(), CHECK, [31, 40]);

/** Past the cap: the 1,000 most important, the rest staying as crawled. */
export const ControlCapped: Story = controlStory(
  recheckPlan({ judged: 4_000, capped: true, notRechecked: 3_000, phase1: 1_000, estimateMs: 540_000 }),
);

/** A sources check: up to N more with the pages linking to them. */
export const ControlSources: Story = controlStory(recheckPlan({ phase1: 5, phase2Max: 14, estimateMs: 30_000 }));

/** Refused now: disabled, with the sidecar's reason. */
export const ControlRefused: Story = controlStory(
  recheckPlan({
    refused: {
      type: 'site-busy',
      title: 'A check of this site is running. Its results will show what’s fixed.',
    },
  }),
);

/** A check only a full crawl can settle: Crawl again… in its place. */
export const ControlFullCrawlOnly: Story = controlStory(recheckPlan(), { ...CHECK, id: 'orphan-page', recheck: [] });

// ---------------------------------------------------------------------------------------------- The banner

const RECHECKS: Recheck[] = [recheckRecord({ endedAt: '2026-11-17T14:02:31Z' })];

const bannerStory = (play: () => Promise<void> | void, checkId = CHECK.id): Story => ({
  loaders: [
    async () => {
      sidecar([
        ['POST', /\/stop$/, () => new Response(null, { status: 202 })],
        ...recheckRoutes({ rechecks: RECHECKS }),
      ]);
      await play();
    },
  ],
  render: () => (
    <Frame>
      <StorySection title="A recheck over a check’s detail">
        <RecheckBanner crawlId={TEST_CRAWL_ID} checkId={checkId} titleOf={titleOf} />
      </StorySection>
    </Frame>
  ),
});

const hear = (event: string, data: unknown) => liveStore.ingest(message(event, data));

/** Asked while the crawl's analysis was out of date: it's brought up to date first. */
export const BannerAnalysingFirst: Story = bannerStory(async () => {
  await startRecheck(TEST_CRAWL_ID, { checkId: CHECK.id });
  hear('crawl', { crawlId: TEST_CRAWL_ID, status: 'analyzing' });
});

/** Fetching: its count of the phase, the bar and Stop. */
export const BannerFetching: Story = bannerStory(() => {
  hear('crawl', { crawlId: TEST_CRAWL_ID, status: 'rechecking' });
  hear('recheck', recheckEvent({ crawlId: TEST_CRAWL_ID, status: 'running', phase: 1, done: 3, total: 8 }));
});

/** Phase 2: the pages linking to what still fails. */
export const BannerFetchingPhase2: Story = bannerStory(() => {
  hear('crawl', { crawlId: TEST_CRAWL_ID, status: 'rechecking' });
  hear('recheck', recheckEvent({ crawlId: TEST_CRAWL_ID, status: 'running', phase: 2, done: 6, total: 14 }));
});

/** The run is over: the analysis after it works out what it found. */
export const BannerAnalysing: Story = bannerStory(() => {
  hear('crawl', { crawlId: TEST_CRAWL_ID, status: 'analyzing' });
  hear('recheck', recheckEvent({ crawlId: TEST_CRAWL_ID, status: 'completed', result: null }));
});

/** Settled: "Rechecked 17 Nov 14:02: 4 of 5 fixed, 1 still (/sofas/), 0 new. Score 86 → 88", its URLs a click away. */
export const BannerSettled: Story = bannerStory(() => {
  hear('recheck', recheckEvent({ crawlId: TEST_CRAWL_ID, status: 'completed', result: recheckResult() }));
});

/** A recheck of another check of the crawl, over this one's detail: named. */
export const BannerOtherCheck: Story = bannerStory(() => {
  hear('recheck', recheckEvent({ crawlId: TEST_CRAWL_ID, status: 'completed', result: recheckResult() }));
}, 'title-too-long');

/** Refused once the analysis first was done: the sidecar's reason. */
export const BannerEnded: Story = bannerStory(async () => {
  await startRecheck(TEST_CRAWL_ID, { checkId: CHECK.id });
  hear('crawl', { crawlId: TEST_CRAWL_ID, status: 'analyzing' });
  hear('crawl', { crawlId: TEST_CRAWL_ID, status: 'completed', error: 'Recheck works on the site’s latest check.' });
});

// ---------------------------------------------------------------------------------------------- Overview's card

/** Overview's Rechecks card: each recheck with when, what, how long and what it found. */
export const RechecksCardList: Story = {
  loaders: [
    () =>
      sidecar(
        recheckRoutes({
          rechecks: [
            recheckRecord({
              id: 3,
              status: 'running',
              startedAt: '2026-11-17T15:40:00Z',
              endedAt: null,
              settledAt: null,
              crawlingMs: null,
              result: null,
            }),
            recheckRecord({
              id: 2,
              startedAt: '2026-11-17T15:10:00Z',
              endedAt: '2026-11-17T15:10:40Z',
              urlList: true,
              judged: 6,
              crawlingMs: 40_000,
              status: 'stopped',
              result: recheckResult({ fixed: 2, still: 1, notRechecked: 3, scoreAfter: 86 }),
            }),
            recheckRecord({
              group: { detail: null, relatedUrlId: 812 },
              judged: 3,
              result: recheckResult({ fixed: 2, still: 1, scoreAfter: 87 }),
            }),
          ],
        }),
      ),
  ],
  render: () => (
    <Frame>
      <StateRow label="Rechecks card">
        <div className="w-full">
          <RechecksCard crawlId={TEST_CRAWL_ID} titleOf={titleOf} />
        </div>
      </StateRow>
    </Frame>
  ),
};

/** A recheck's URLs, opened from the card: judged first, each with its outcome, before and now. */
export const RecheckUrls: Story = {
  ...RechecksCardList,
  play: async () => {
    for (let tries = 0; tries < 60; tries++) {
      const button = document.querySelector<HTMLButtonElement>('button[aria-label="URLs of recheck 1"]');
      if (button) {
        button.click();
        return;
      }
      await new Promise((resolve) => setTimeout(resolve, 50));
    }
  },
};
