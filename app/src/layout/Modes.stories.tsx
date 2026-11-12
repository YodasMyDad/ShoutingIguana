// The shell in each mode (docs/v2-ui-simple.md, The shell in Simple and Top bar): the switch, Simple's sidebar and
// words, and the top bar at its fullest (a crawl running) and at 200% zoom, where its parts give way in order.
import type { Meta, StoryObj } from '@storybook/react-vite';
import { setOverlay } from '../app/overlays';
import { liveStore } from '../live/liveStore';
import type { Mode } from '../mode/mode';
import { AT_200_PERCENT, GARDEN_CRAWL, ScreenApp, freshStory, ownSidecar } from '../test/ScreenStory';
import { TEST_CRAWL_ID } from '../test/fakeSidecar';
import { historyUntil, message, overviewRoutes, progressAt, progressUntil } from '../test/overviewSidecar';

const meta = { title: 'Shell/Modes' } satisfies Meta;
export default meta;
type Story = StoryObj<typeof meta>;

const GARDEN = `/crawls/${GARDEN_CRAWL}/overview`;
const RUNNING = `/crawls/${TEST_CRAWL_ID}/overview`;
/** A long crawl at full speed: six-digit counts in the status pill. */
const SCRIPT = { rate: 900, size: 400_000 };
const SECONDS = 120;

/** A crawl of the story's own sidecar, running here, 120 s in: 108,000 URLs. */
function running() {
  const now = progressAt(SECONDS, SCRIPT);
  ownSidecar(
    overviewRoutes({
      analysis: null,
      history: historyUntil(SECONDS, SCRIPT),
      item: {
        status: 'running',
        finishedAt: null,
        analyzedAt: null,
        issues: null,
        fetchedUrlCount: now.fetched,
        pageCount: now.htmlPages,
      },
    }),
  );
  progressUntil(SECONDS - 0.5, SCRIPT).forEach((event) => liveStore.ingest(event));
  liveStore.ingest(message('progress', now));
}

const shell = (path: string, mode: Mode, at200 = false): Story => ({
  loaders: [() => freshStory()],
  render: () => <ScreenApp path={path} mode={mode} size={at200 ? AT_200_PERCENT : undefined} />,
});

const live = (mode: Mode, at200 = false): Story => ({
  loaders: [() => running()],
  render: () => <ScreenApp path={RUNNING} mode={mode} size={at200 ? AT_200_PERCENT : undefined} />,
});

/** Simple, a first start's mode: Home, with search as its icon and the switch at the end of the top bar. */
export const SimpleHome = shell('/', 'simple');
/** A crawl in Simple: Summary and What to fix (its serious problems and those worth fixing) in the sidebar. */
export const SimpleCrawl = shell(GARDEN, 'simple');
/** The same crawl in Advanced: the sidebar as built. */
export const AdvancedCrawl = shell(GARDEN, 'advanced');
/** The top bar at its fullest in Simple: Checking, its count, Pause and Stop with their keys, search, the switch. */
export const SimpleRunning = live('simple');
/** And in Advanced: Running, the count, Pause and Stop, the search field, the switch. */
export const AdvancedRunning = live('advanced');
/** 200% zoom, a crawl running: Back, the section, the status word, the controls as icons, search, the switch. */
export const SimpleRunningAt200 = live('simple', true);
export const AdvancedRunningAt200 = live('advanced', true);
/** 200% zoom, idle: the site shortened or gone, the date gone, search as its icon. */
export const SimpleCrawlAt200 = shell(GARDEN, 'simple', true);
export const AdvancedCrawlAt200 = shell(GARDEN, 'advanced', true);
/** Home at 200% zoom, in each mode. */
export const SimpleHomeAt200 = shell('/', 'simple', true);
export const AdvancedHomeAt200 = shell('/', 'advanced', true);

/** An Advanced-only screen reached in Simple (a link, Back, a reopened session): Advanced is on, with Back to Simple. */
export const AdvancedIsOn = shell(`/crawls/${GARDEN_CRAWL}/urls`, 'simple');

/** The palette in Simple: Simple's commands, in its words, with Switch to Advanced and nothing else of Advanced's. */
export const SimplePalette: Story = {
  ...shell(GARDEN, 'simple'),
  play: () => setOverlay('palette'),
};

/** The shortcut sheet in Simple: only the keys that work there. */
export const SimpleShortcuts: Story = {
  ...shell(GARDEN, 'simple'),
  play: () => setOverlay('shortcuts'),
};
