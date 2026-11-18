// The fix loop in Advanced (docs/v2-fix-loop.md, section 4, Advanced): the library's Score column and Compare with…,
// the switcher's score, and the Compare with… picker in each of its states. Over a sidecar of the story's own: the
// garden crawl compared with an earlier check of the site (src/test/fixLoopSidecar.ts).
import type { Meta, StoryObj } from '@storybook/react-vite';
import { QueryClientProvider } from '@tanstack/react-query';
import { useState } from 'react';
import type { CrawlListItem } from '../../api/types';
import { createQueryClient } from '../../app/queryClient';
import { drawAsTheAppForTests } from '../../crawl/fileOpening';
import { updateSettings } from '../../settings/settingsStore';
import { json, testCrawlItem, type FakeRoute } from '../../test/fakeSidecar';
import {
  EARLIER_ID,
  changesRoute,
  comparedGardenItem,
  earlierGardenItem,
  gardenComparison,
} from '../../test/fixLoopSidecar';
import { overviewRoutes } from '../../test/overviewSidecar';
import { ownSidecar, ScreenApp } from '../../test/ScreenStory';
import { GARDEN_ANALYSIS, simpleRoutes } from '../../test/simpleSidecar';
import { StoryFrame, StorySection } from '../../test/StoryFrame';
import type { ChangesAnswer } from '../../api/fixLoop';
import { ComparisonCard } from './ComparisonCard';
import { CompareWithMenu } from './CompareWithMenu';
import { useComparison } from './useComparison';

const meta = { title: 'Fix loop/Advanced' } satisfies Meta;
export default meta;
type Story = StoryObj<typeof meta>;

const GARDEN = comparedGardenItem();
const EARLIER = earlierGardenItem();
/** A later check of the garden site: the one "until" reads against. */
const LATER = comparedGardenItem({
  id: 'b1b2c3d4e5f60718',
  fileName: 'gardenfurnituresets.co.uk-20261114.sicrawl',
  startedAt: '2026-11-14T08:30:00Z',
  score: { value: 91, band: 'veryGood', partial: false },
  previous: {
    id: GARDEN.id,
    startedAt: GARDEN.startedAt,
    score: GARDEN.score ?? null,
    comparable: 'full',
    needsAnalysis: false,
  },
  changes: { score: 5, partial: false, fixed: [], new: [], fewer: [], more: [], newCheck: [], notCheckedNow: [] },
});
/** An older check of the site whose file has gone: listed, disabled, with why. */
const GONE = earlierGardenItem({
  id: 'c1b2c3d4e5f60718',
  startedAt: '2026-10-03T10:00:00Z',
  missing: true,
  score: null,
});
/** One stopped part way, never analysed: listed, disabled. */
const UNANALYSED = earlierGardenItem({
  id: 'd1b2c3d4e5f60718',
  startedAt: '2026-10-10T10:00:00Z',
  status: 'stopped',
  analyzedAt: null,
  issues: null,
  score: null,
});
const OAKMERE = testCrawlItem({
  id: 'e1e1e1e1e1e1e1e1',
  site: 'oakmere-cycles.example',
  startUrl: 'https://www.oakmere-cycles.example/',
  fileName: 'oakmere-cycles.example.sicrawl',
  status: 'stopped',
  startedAt: '2026-11-02T09:00:00Z',
  crawledUrlCount: 3_102,
  fetchedUrlCount: 3_102,
  pageCount: 812,
  issues: { error: 3, warning: 7, notice: 9 },
  score: { value: 64, band: 'needsWork', partial: true },
});
const FERNLEIGH = testCrawlItem({
  id: 'f1f1f1f1f1f1f1f1',
  site: 'fernleigh-pottery.example',
  startUrl: 'https://fernleigh-pottery.example/',
  fileName: 'fernleigh-pottery.example.sicrawl',
  startedAt: '2026-10-24T09:00:00Z',
  issues: null,
  analyzedAt: null,
  score: null,
});

function routes(library: CrawlListItem[]): FakeRoute[] {
  return [
    ['GET', /^\/api\/crawls$/, () => json(library)],
    [
      'GET',
      /^\/api\/crawls\/([0-9a-f]{16})$/,
      (_, match) => json(library.find((item) => item.id === match[1]) ?? library[0]),
    ],
    changesRoute(gardenComparison()),
    // The garden crawl's analysis and the Overview's reads, so the screen behind the switcher is as it should be.
    ...simpleRoutes(GARDEN, GARDEN_ANALYSIS),
    ...overviewRoutes(),
  ];
}

const LIBRARY = [GARDEN, EARLIER, UNANALYSED, GONE, OAKMERE, FERNLEIGH];

/** Waits for an element the app draws, then gives it to `act`. */
async function whenThere<T extends HTMLElement>(find: () => T | null | undefined, act: (element: T) => void) {
  for (let tries = 0; tries < 60; tries++) {
    const element = find();
    if (element) {
      act(element);
      return;
    }
    await new Promise((resolve) => setTimeout(resolve, 50));
  }
}

/** Opens a menu from its button as the keyboard does: Radix opens on a press, not a click. */
const openMenu = (button: HTMLElement) => {
  button.focus();
  button.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
};

/** Unfolds the garden site's older crawls. */
const unfoldGarden = () =>
  whenThere(
    () =>
      [...document.querySelectorAll<HTMLButtonElement>('button[aria-expanded]')].find((button) =>
        button.textContent.includes('older crawls'),
      ),
    (button) => button.click(),
  );

/**
 * The library's Score column: "86 ▲14" against each crawl's previous crawl (the glyph neutral, the change in words for a
 * screen reader), "so far" under a stopped crawl's, "–" with no score; the garden site's older crawls unfolded, each
 * with its own.
 */
export const LibraryScores: Story = {
  loaders: [
    () => {
      ownSidecar(routes(LIBRARY));
      updateSettings({ compareWithByCrawl: {} });
      drawAsTheAppForTests(null);
    },
  ],
  render: () => <ScreenApp path="/" />,
  play: unfoldGarden,
};

/** An older crawl's menu: Compare with…, which opens the latest crawl's Overview with this one picked. */
export const LibraryCompareWith: Story = {
  loaders: [
    () => {
      ownSidecar(routes(LIBRARY));
      updateSettings({ compareWithByCrawl: {} });
      drawAsTheAppForTests(null);
    },
  ],
  render: () => <ScreenApp path="/" />,
  play: async () => {
    await unfoldGarden();
    await whenThere(
      () =>
        document.querySelector<HTMLButtonElement>(
          'button[aria-label^="Actions for gardenfurnituresets.co.uk, 17 Oct"]',
        ),
      openMenu,
    );
  },
};

/** The sidebar's switcher in Advanced: each crawl with its score, "so far" for one stopped early. */
export const SwitcherScores: Story = {
  loaders: [
    () => {
      ownSidecar(routes(LIBRARY));
      updateSettings({ compareWithByCrawl: {} });
    },
  ],
  render: () => <ScreenApp path={`/crawls/${GARDEN.id}/overview`} crawlId={GARDEN.id} />,
  play: () => whenThere(() => document.querySelector<HTMLButtonElement>('.si-switcher'), openMenu),
};

function Picker({ crawlId }: { crawlId: string }) {
  const comparison = useComparison(crawlId);
  const [open, setOpen] = useState(true);
  return (
    <div className="pr-80 flex justify-end">
      <CompareWithMenu crawlId={crawlId} comparison={comparison} open={open} onOpenChange={setOpen} />
    </div>
  );
}

function PickerStory({ crawlId }: { crawlId: string }) {
  const [queryClient] = useState(() => createQueryClient());
  return (
    <QueryClientProvider client={queryClient}>
      <StoryFrame>
        <StorySection title="Compare with…">
          <Picker crawlId={crawlId} />
        </StorySection>
      </StoryFrame>
    </QueryClientProvider>
  );
}

/**
 * The picker, open: the site's other crawls newest first, each with when, its status, URLs and score; the previous one
 * (the default) ticked; a later one marked; one whose file has gone and one never analysed disabled with why; then
 * Don't compare.
 */
export const PickerOpen: Story = {
  loaders: [
    () => {
      ownSidecar(routes([LATER, ...LIBRARY]));
      updateSettings({ compareWithByCrawl: {} });
    },
  ],
  render: () => <PickerStory crawlId={GARDEN.id} />,
};

/** A later crawl picked: the control says so, and the comparison reads "until". */
export const PickerLaterPicked: Story = {
  loaders: [
    () => {
      ownSidecar(routes([LATER, ...LIBRARY]));
      updateSettings({ compareWithByCrawl: { [GARDEN.id]: LATER.id } });
    },
  ],
  render: () => <PickerStory crawlId={GARDEN.id} />,
};

/** Don't compare: "Not compared", and the choice ticked. */
export const PickerNotCompared: Story = {
  loaders: [
    () => {
      ownSidecar(routes(LIBRARY));
      updateSettings({ compareWithByCrawl: { [GARDEN.id]: 'none' } });
    },
  ],
  render: () => <PickerStory crawlId={GARDEN.id} />,
};

/** A site's only crawl: "No other crawl of this site". */
export const PickerOnlyCrawl: Story = {
  loaders: [
    () => {
      ownSidecar(routes([{ ...EARLIER, id: EARLIER_ID }]));
      updateSettings({ compareWithByCrawl: {} });
    },
  ],
  render: () => <PickerStory crawlId={EARLIER_ID} />,
};

// ---------------------------------------------------------------------------------------------- The comparison card

function CardOf({ crawlId }: { crawlId: string }) {
  return <ComparisonCard crawlId={crawlId} comparison={useComparison(crawlId)} />;
}

function CardStory({ crawlId = GARDEN.id }: { crawlId?: string }) {
  const [queryClient] = useState(() => createQueryClient());
  return (
    <QueryClientProvider client={queryClient}>
      <StoryFrame>
        <CardOf crawlId={crawlId} />
      </StoryFrame>
    </QueryClientProvider>
  );
}

/** The card's sidecar: the library, the catalog, and `/changes` answering as given (or failing, or never). */
function cardRoutes(answer: ChangesAnswer | 'never' | Response, library: CrawlListItem[] = LIBRARY): FakeRoute[] {
  return [
    ['GET', /^\/api\/crawls$/, () => json(library)],
    [
      'GET',
      /^\/api\/crawls\/([0-9a-f]{16})$/,
      (_, match) => json(library.find((item) => item.id === match[1]) ?? GARDEN),
    ],
    answer instanceof Response ? ['GET', /\/changes/, () => answer.clone()] : changesRoute(answer),
    ...simpleRoutes(GARDEN, GARDEN_ANALYSIS),
  ];
}

const cardStory = (answer: ChangesAnswer | 'never' | Response, compareWith: Record<string, string> = {}): Story => ({
  loaders: [
    () => {
      ownSidecar(cardRoutes(answer, [LATER, ...LIBRARY]));
      updateSettings({ compareWithByCrawl: compareWith });
    },
  ],
  render: () => <CardStory />,
});

/** Like for like: the score and each severity then and now with their moves, the checks and the issues as they moved. */
export const CardFull: Story = cardStory(gardenComparison());

/** Partial: the crawls covered different things; the differences folded under the figures (opened by a click). */
export const CardPartial: Story = cardStory(
  gardenComparison({
    comparable: 'partial',
    differences: [
      { code: 'stoppedEarly', side: 'now' },
      { code: 'limits', field: 'maxUrls', then: null, now: 5_000 },
      { code: 'client', field: 'userAgent', then: 'chrome', now: 'googlebot' },
    ],
  }),
);

/** Counts only: another start origin, so no URL can be matched: no issues' line, the differences open. */
export const CardCountsOnly: Story = cardStory(
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

/** The other crawl was analysed by an earlier version: counts only, with Analyse it again. */
export const CardNeedsAnalysis: Story = cardStory(
  gardenComparison({ comparable: 'countsOnly', previousNeedsAnalysis: ['engineChanged'] }),
);

/** The comparison on its way: the card's skeleton under its head. */
export const CardLoading: Story = cardStory('never');

/** A crawl picked that can't be compared now: the sidecar's words, and the way back to the previous crawl. */
export const CardFailed: Story = cardStory(
  new Response(
    JSON.stringify({ type: 'crawl-not-comparable', title: 'That crawl can’t be compared with this one.', status: 409 }),
    { status: 409, headers: { 'Content-Type': 'application/problem+json' } },
  ),
  { [GARDEN.id]: EARLIER_ID },
);

/** A later crawl picked: "Until 14 Nov", the comparison read forward from this crawl to that one. */
export const CardUntil: Story = cardStory(
  gardenComparison({
    with: {
      id: LATER.id,
      startedAt: LATER.startedAt,
      finishedAt: '2026-11-14T09:02:00Z',
      status: 'completed',
      startUrl: LATER.startUrl,
      analyzedAt: '2026-11-14T09:03:00Z',
      pagesAnalyzed: 265,
      crawledUrlCount: 4_530,
      chosen: 'picked',
    },
  }),
  { [GARDEN.id]: LATER.id },
);
