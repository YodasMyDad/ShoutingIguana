import type { Meta, StoryObj } from '@storybook/react-vite';
import gardenFull from '../../design/report/garden-full.html?raw';
import gardenSummary from '../../design/report/garden-summary.html?raw';
import type { Analysis, CrawlListItem } from '../api/types';
import { liveStore } from '../live/liveStore';
import { setTransport } from '../api/transport';
import { AT_200_PERCENT, ScreenApp, freshStory } from '../test/ScreenStory';
import { TEST_CRAWL_ID, fakeSidecar, problem, type FakeRoute } from '../test/fakeSidecar';
import { ISSUES_ANALYSIS } from '../test/issuesSidecar';
import { message, overviewRoutes } from '../test/overviewSidecar';
import { setDirectConnection } from '../shell/connection';

const meta = { title: 'Screens/Report' } satisfies Meta;
export default meta;
type Story = StoryObj<typeof meta>;

const REPORT = `/crawls/${TEST_CRAWL_ID}/report`;

/** The garden crawl as the engine's report has it: what the toolbar says beside the document. */
const GARDEN_ITEM: Partial<CrawlListItem> = {
  site: 'gardenfurnituresets.co.uk',
  startUrl: 'https://www.gardenfurnituresets.co.uk/',
  startedAt: '2026-10-31T14:13:00Z',
  pageCount: 265,
};
const GARDEN_ANALYSIS: Analysis = { ...ISSUES_ANALYSIS, pagesAnalyzed: 265, crawledUrlCount: 4_521 };

interface ReportSidecar {
  item?: Partial<CrawlListItem>;
  /** The analysis, or null for a crawl never analysed. */
  analysis?: Analysis | null;
  /** The report: the engine's document by cut, held back (`pending`) or failing (`error`). */
  report?: 'pending' | 'error';
}

/** The report's own route, then the Overview's sidecar (the item, the analysis, the catalog). */
function sidecar({ item, analysis = GARDEN_ANALYSIS, report }: ReportSidecar = {}) {
  freshStory();
  const reportRoute: FakeRoute = [
    'GET',
    /^\/api\/crawls\/[0-9a-f]{16}\/report(\?.*)?$/,
    (_request, match) => {
      if (report === 'pending') return new Promise<Response>(() => undefined);
      if (report === 'error') {
        return problem({
          type: 'internal',
          title: 'Something went wrong in the engine.',
          status: 500,
          detail: 'SqliteException: database is locked',
        });
      }
      const summary = (match[1] ?? '').includes('cut=summary');
      return new Response(summary ? gardenSummary : gardenFull, {
        headers: { 'Content-Type': 'text/html; charset=utf-8' },
      });
    },
  ];
  setTransport(fakeSidecar([reportRoute, ...overviewRoutes({ analysis, item: { ...GARDEN_ITEM, ...item } })]));
  setDirectConnection({ baseUrl: 'mock://sidecar', token: 'mock', generation: 1, version: 'mock' });
}

/** The full report, in Simple: the toolbar, then the engine's document as a sheet on the canvas. */
export const Simple: Story = {
  loaders: [() => sidecar()],
  render: () => <ScreenApp path={REPORT} mode="simple" />,
};

/** The same screen in Advanced: the toolbar says "crawled", and Report is the sidebar's eighth section. */
export const Advanced: Story = {
  loaders: [() => sidecar()],
  render: () => <ScreenApp path={REPORT} />,
};

/** The summary page: the report's first page alone, the same as the full report's opening. */
export const SummaryPage: Story = {
  loaders: [() => sidecar()],
  render: () => <ScreenApp path={`${REPORT}?cut=summary`} mode="simple" />,
};

/** A 1280x720 window at 200% zoom: the toolbar's controls go under its title, and the sheet keeps its least height. */
export const At200Percent: Story = {
  loaders: [() => sidecar()],
  render: () => <ScreenApp path={REPORT} mode="simple" size={AT_200_PERCENT} />,
};

/** Building: a blank sheet with the shape of its first page, so nothing moves when the document arrives. */
export const Building: Story = {
  loaders: [() => sidecar({ report: 'pending' })],
  render: () => <ScreenApp path={REPORT} mode="simple" />,
};

/** Never analysed, in Simple: there's no report until the results are worked out, with the one action that does it. */
export const NeverAnalysed: Story = {
  loaders: [() => sidecar({ analysis: null, item: { analyzedAt: null, issues: null } })],
  render: () => <ScreenApp path={REPORT} mode="simple" />,
};

/** Never analysed, in Advanced's words. */
export const NeverAnalysedAdvanced: Story = {
  loaders: [() => sidecar({ analysis: null, item: { analyzedAt: null, issues: null } })],
  render: () => <ScreenApp path={REPORT} />,
};

/** Never analysed while it runs: the report comes when it ends. */
export const NeverAnalysedRunning: Story = {
  loaders: [
    () => sidecar({ analysis: null, item: { analyzedAt: null, issues: null, status: 'running', finishedAt: null } }),
  ],
  render: () => <ScreenApp path={REPORT} mode="simple" />,
};

/** Working out the results, for a crawl that had none: how far it has got, and Cancel. */
export const Analysing: Story = {
  loaders: [
    () => {
      sidecar({ analysis: null, item: { analyzedAt: null, issues: null, status: 'analyzing' } });
      liveStore.ingest(message('analysis', { crawlId: TEST_CRAWL_ID, phase: 'pageRules', done: 152, total: 265 }));
    },
  ],
  render: () => <ScreenApp path={REPORT} mode="simple" />,
};

/** Working out the results again: the last report stays, under the progress. */
export const AnalysingAgain: Story = {
  loaders: [
    () => {
      sidecar({ item: { status: 'analyzing' } });
      liveStore.ingest(message('analysis', { crawlId: TEST_CRAWL_ID, phase: 'siteRules', done: 3, total: 9 }));
    },
  ],
  render: () => <ScreenApp path={REPORT} mode="simple" />,
};

/** Out of date: why the results no longer stand, with Work out the results again. The report gives their date. */
export const OutOfDate: Story = {
  loaders: [
    () =>
      sidecar({
        analysis: { ...GARDEN_ANALYSIS, outOfDate: true, outOfDateReasons: ['crawlChanged'] },
        item: { outOfDate: true },
      }),
  ],
  render: () => <ScreenApp path={REPORT} mode="simple" />,
};

/** Out of date, in Advanced's words. */
export const OutOfDateAdvanced: Story = {
  loaders: [
    () =>
      sidecar({
        analysis: { ...GARDEN_ANALYSIS, outOfDate: true, outOfDateReasons: ['engineChanged', 'settingsChanged'] },
        item: { outOfDate: true },
      }),
  ],
  render: () => <ScreenApp path={REPORT} />,
};

/**
 * Stopped early: the screen adds nothing, as the document says it itself (here the garden crawl's, which finished:
 * a stopped crawl's report says so under the site, and marks its score "so far").
 */
export const StoppedEarly: Story = {
  loaders: [() => sidecar({ item: { status: 'stopped' } })],
  render: () => <ScreenApp path={REPORT} mode="simple" />,
};

/** The report couldn't be made: the cause, its detail behind a button, and Try again. */
export const ReportError: Story = {
  loaders: [() => sidecar({ report: 'error' })],
  render: () => <ScreenApp path={REPORT} mode="simple" />,
};

/** A crawl file that can't be read has no report. */
export const Unreadable: Story = {
  loaders: [
    () => sidecar({ analysis: null, item: { status: 'unreadable', error: 'The file is damaged or not a crawl.' } }),
  ],
  render: () => <ScreenApp path={REPORT} mode="simple" />,
};
