import type { Meta, StoryObj } from '@storybook/react-vite';
import type { QueryClient } from '@tanstack/react-query';
import { createMemoryHistory } from '@tanstack/react-router';
import { useEffect, useState } from 'react';
import type { GridQuery, IssueGroup, IssueRow } from '../api/grids';
import { queryKeys } from '../api/queries';
import { setTransport } from '../api/transport';
import type { Analysis, Checks } from '../api/types';
import { App } from '../app/App';
import { createQueryClient } from '../app/queryClient';
import { createAppRouter } from '../app/router';
import { liveStore } from '../live/liveStore';
import { updateSettings } from '../settings/settingsStore';
import { setDirectConnection } from '../shell/connection';
import { TEST_CRAWL_ID, fakeSidecar, json, problem, type FakeRoute } from '../test/fakeSidecar';
import {
  ISSUES_ANALYSIS,
  ISSUES_CHECKS,
  ISSUES_CHECKS_WITHOUT_SUBJECTS,
  ISSUE_GROUPS,
  ISSUE_ROWS,
  issueRowsPage,
  issuesRoutes,
  type IssuesSidecarOptions,
} from '../test/issuesSidecar';

const meta = { title: 'Screens/Issues' } satisfies Meta;
export default meta;
type Story = StoryObj<typeof meta>;

/** The garden crawl in the design fixtures, as the mock sidecar serves it. */
const GARDEN = '/crawls/62cb879ef60639d5/issues';
/** The crawl of the stories' own sidecars. */
const ISSUES = `/crawls/${TEST_CRAWL_ID}/issues`;

const connect = () => setDirectConnection({ baseUrl: 'mock://sidecar', token: 'mock', generation: 1, version: 'mock' });

/** Every story starts clean: nothing hidden, nothing live. */
function fresh() {
  updateSettings({ hiddenChecks: [] });
  liveStore.reset();
}

function IssuesApp({ path, refreshAfterMs }: { path: string; refreshAfterMs?: number }) {
  const [app] = useState(() => {
    const queryClient: QueryClient = createQueryClient();
    return { queryClient, router: createAppRouter(queryClient, createMemoryHistory({ initialEntries: [path] })) };
  });
  useEffect(() => {
    if (refreshAfterMs === undefined) return undefined;
    const timer = setTimeout(
      () => void app.queryClient.invalidateQueries({ queryKey: queryKeys.analysis(TEST_CRAWL_ID) }),
      refreshAfterMs,
    );
    return () => clearTimeout(timer);
  }, [app, refreshAfterMs]);
  return (
    <div className="h-screen">
      <App router={app.router} queryClient={app.queryClient} />
    </div>
  );
}

/** A sidecar of the story's own, for the states the design fixtures can't show. */
function own(options: IssuesSidecarOptions = {}, first: FakeRoute[] = []) {
  fresh();
  setTransport(fakeSidecar([...first, ...issuesRoutes(options)]));
  connect();
}

/** The garden crawl over the mock: 19 checks with issues, the first to fix selected. */
export const Garden: Story = {
  loaders: [fresh],
  render: () => <IssuesApp path={GARDEN} />,
};

/** A template fault: one group holds 264 of the 265 pages, open, with the callout and Show all in URLs. */
export const GardenGroupOpen: Story = {
  loaders: [fresh],
  render: () => (
    <IssuesApp
      path={`${GARDEN}/empty-anchor-text?group=${encodeURIComponent(JSON.stringify({ detail: null, relatedUrlId: 1 }))}`}
    />
  ),
};

/** A check whose detail is a measurement: its URLs flat, with the detail, sortable from the headers. */
export const GardenFlatCheck: Story = {
  loaders: [fresh],
  render: () => <IssuesApp path={`${GARDEN}/title-too-long?sort=-detail`} />,
};

/** The group a story opens: the pages that link to the home page without text. */
const HOME_GROUP = `?group=${encodeURIComponent(JSON.stringify({ detail: null, relatedUrlId: 1 }))}`;
const NOT_FOUND_GROUP = `?group=${encodeURIComponent(JSON.stringify({ detail: '404 Not Found', relatedUrlId: null }))}`;

/**
 * A check that flags only pages (the catalog's `subject` is `page`): "Pages affected", as a share of the pages
 * analysed, and pages in its list, its grid's name and its row in the checks. The pages analysed are the analysis's
 * own count (6 here), which the check's count is a part of: not the 7 the crawl's file has.
 */
export const PageCheck: Story = {
  loaders: [() => own()],
  render: () => <IssuesApp path={`${ISSUES}/title-too-long`} />,
};

/** The same of a page check that groups: the column, the link to a group's pages and the template's share. */
export const PageCheckGroupOpen: Story = {
  loaders: [() => own()],
  render: () => <IssuesApp path={`${ISSUES}/empty-anchor-text${HOME_GROUP}`} />,
};

/**
 * A check that can flag any URL (`subject` is `url`: a missing image, a redirect, a URL on another of the site's
 * versions): "URLs affected", as a share of the URLs crawled when the analysis ran (its own count, 40 here), and
 * nothing of it is called pages.
 */
export const UrlCheck: Story = {
  loaders: [() => own()],
  render: () => <IssuesApp path={`${ISSUES}/internal-client-error${NOT_FOUND_GROUP}`} />,
};

/**
 * A catalog that doesn't say what its checks flag (an engine from before E3b, as the design fixtures are): every
 * count is of URLs, as a share of the URLs crawled, which is true whatever a check flags.
 */
export const CheckWithoutSubject: Story = {
  loaders: [() => own({ checks: ISSUES_CHECKS_WITHOUT_SUBJECTS })],
  render: () => <IssuesApp path={`${ISSUES}/empty-anchor-text${HOME_GROUP}`} />,
};

/**
 * An analysis that kept no count of the URLs crawled (an engine from before the score ran it): a URL check says how
 * many it flags and no share, as there is no total its count is known to be a part of.
 */
export const UrlCheckOfAnEarlierEnginesAnalysis: Story = {
  loaders: [
    () =>
      own({
        analysis: { ...ISSUES_ANALYSIS, crawledUrlCount: null, outOfDate: true, outOfDateReasons: ['engineChanged'] },
        item: { outOfDate: true },
      }),
  ],
  render: () => <IssuesApp path={`${ISSUES}/internal-client-error`} />,
};

const BIG_ANALYSIS: Analysis = {
  ...ISSUES_ANALYSIS,
  pagesAnalyzed: 987_653,
  crawledUrlCount: 1_234_567,
  counts: [
    { checkId: 'internal-client-error', count: 312_876 },
    { checkId: 'internal-server-error', count: 0 },
    { checkId: 'empty-anchor-text', count: 987_650 },
    { checkId: 'title-too-long', count: 654_321 },
  ],
};

const BIG_GROUPS: Record<string, IssueGroup[]> = {
  'empty-anchor-text': [
    {
      detail: null,
      relatedUrlId: 1,
      relatedUrl: 'https://example.com/',
      pages: 987_649,
      share: 987_649 / 987_650,
      likelyTemplate: true,
    },
    {
      detail: null,
      relatedUrlId: 9,
      relatedUrl: 'https://example.com/garden/',
      pages: 1,
      share: 1 / 987_650,
      likelyTemplate: false,
    },
  ],
  'internal-client-error': [
    {
      detail: '404 Not Found',
      relatedUrlId: null,
      relatedUrl: null,
      pages: 300_000,
      share: 0.9588,
      likelyTemplate: false,
    },
    { detail: '410 Gone', relatedUrlId: null, relatedUrl: null, pages: 12_876, share: 0.0412, likelyTemplate: false },
  ],
};

const big = () =>
  own({
    analysis: BIG_ANALYSIS,
    groups: BIG_GROUPS,
    item: { pageCount: 987_654, fetchedUrlCount: 1_234_567, crawledUrlCount: 1_234_567 },
  });

/** Six-digit counts of a page check: the figures, the list's counts and the group's link keep their room. */
export const SixDigitPageCounts: Story = {
  loaders: [big],
  render: () => <IssuesApp path={`${ISSUES}/empty-anchor-text${HOME_GROUP}`} />,
};

/** The same of a URL check, whose share is of over a million URLs crawled. */
export const SixDigitUrlCounts: Story = {
  loaders: [big],
  render: () => <IssuesApp path={`${ISSUES}/internal-client-error${NOT_FOUND_GROUP}`} />,
};

/**
 * One category's checks, as Overview's "Issues by category" links here: the garden's URLs category, whose title as
 * text would list other categories' checks too. The category is a chip above the list, which removes it.
 */
export const OneCategory: Story = {
  loaders: [fresh],
  render: () => <IssuesApp path={`${GARDEN}?category=urls`} />,
};

/** First load: the screen's own layout in skeletons, after 150 ms. */
export const FirstLoad: Story = {
  loaders: [() => own({ analysis: 'pending' })],
  render: () => <IssuesApp path={`${ISSUES}/internal-client-error`} />,
};

/** Refreshing: the analysis is read again behind what's shown; a thin line, nothing blanks. */
export const Refreshing: Story = {
  loaders: [
    () => {
      let calls = 0;
      own({}, [
        [
          'GET',
          /^\/api\/crawls\/[0-9a-f]{16}\/analysis$/,
          () => (calls++ === 0 ? json(ISSUES_ANALYSIS) : new Promise<Response>(() => undefined)),
        ],
      ]);
    },
  ],
  render: () => <IssuesApp path={`${ISSUES}/empty-anchor-text`} refreshAfterMs={400} />,
};

/** A crawl running (continued after its analysis): the last analysis's issues stay, and say so. */
export const WhileCrawling: Story = {
  loaders: [() => own({ item: { status: 'running' } })],
  render: () => <IssuesApp path={`${ISSUES}/empty-anchor-text`} />,
};

const ALL_PASSED: Analysis = {
  ...ISSUES_ANALYSIS,
  isPartial: false,
  notes: [],
  counts: ISSUES_CHECKS.checks.map((check) => ({ checkId: check.id, count: 0 })),
  skipped: [],
};

/** No issues at all: every check passed, said plainly. */
export const NoIssues: Story = {
  loaders: [() => own({ analysis: ALL_PASSED, item: { issues: { error: 0, warning: 0, notice: 0 } } })],
  render: () => <IssuesApp path={ISSUES} />,
};

/** Filtered to nothing: the filters shown, cleared in one click. */
export const FilteredToNothing: Story = {
  loaders: [() => own()],
  render: () => (
    <IssuesApp path={`${ISSUES}/internal-client-error?q=hreflang&severity=${encodeURIComponent('["error"]')}`} />
  ),
};

/** A skipped check, with its reason, and a partial analysis's notes, in plain words. Never 0. */
export const SkippedAndPartial: Story = {
  loaders: [() => own()],
  render: () => <IssuesApp path={`${ISSUES}/orphan-page`} />,
};

/** A check that passed, selected (the switch shows them): it says so. */
export const PassedCheck: Story = {
  loaders: [() => own()],
  render: () => <IssuesApp path={`${ISSUES}/internal-server-error`} />,
};

/**
 * The crawl changed after its analysis: a banner offers to analyse again. A check's share stands, of the analysis's
 * own total: here 2 of the 40 URLs crawled when it ran, whatever the crawl has crawled since (390).
 */
export const OutOfDate: Story = {
  loaders: [
    () =>
      own({
        analysis: { ...ISSUES_ANALYSIS, outOfDate: true, outOfDateReasons: ['crawlChanged'] },
        item: { outOfDate: true, pageCount: 30, fetchedUrlCount: 390, crawledUrlCount: 390 },
      }),
  ],
  render: () => <IssuesApp path={`${ISSUES}/internal-client-error`} />,
};

/**
 * The same crawl, on a check that flags only pages: its share stands, of the pages the analysis ran on (6), whatever
 * the crawl has read since (30 here).
 */
export const OutOfDatePageCheck: Story = {
  loaders: [
    () =>
      own({
        analysis: { ...ISSUES_ANALYSIS, outOfDate: true, outOfDateReasons: ['crawlChanged'] },
        item: { outOfDate: true, pageCount: 30, fetchedUrlCount: 390, crawledUrlCount: 390 },
      }),
  ],
  render: () => <IssuesApp path={`${ISSUES}/title-too-long`} />,
};

/** Never analysed, and stopped: offers to analyse it. */
export const NeverAnalysed: Story = {
  loaders: [() => own({ analysis: null, item: { status: 'stopped', analyzedAt: null, issues: null } })],
  render: () => <IssuesApp path={ISSUES} />,
};

/** Never analysed, and crawling: the issues come when it finishes. */
export const NeverAnalysedWhileCrawling: Story = {
  loaders: [() => own({ analysis: null, item: { status: 'running', analyzedAt: null, issues: null } })],
  render: () => <IssuesApp path={ISSUES} />,
};

/** Analysing now: the phase and its progress from the analysis events, with Cancel; the last issues stay. */
export const Analysing: Story = {
  loaders: [
    () => {
      own({ item: { status: 'analyzing' } });
      liveStore.ingest({
        event: 'analysis',
        id: '',
        data: JSON.stringify({ crawlId: TEST_CRAWL_ID, phase: 'pageRules', done: 1_204, total: 4_521 }),
      });
    },
  ],
  render: () => <IssuesApp path={`${ISSUES}/empty-anchor-text`} />,
};

/** Analysing a crawl for the first time: the progress is the screen. */
export const AnalysingFirstTime: Story = {
  loaders: [
    () => {
      own({ analysis: null, item: { status: 'analyzing', analyzedAt: null, issues: null } });
      liveStore.ingest({
        event: 'analysis',
        id: '',
        data: JSON.stringify({ crawlId: TEST_CRAWL_ID, phase: 'siteRules', done: 3, total: 9 }),
      });
    },
  ],
  render: () => <IssuesApp path={ISSUES} />,
};

/** The analysis can't be read: the plain cause, Try again, and the detail behind Details. */
export const ReadError: Story = {
  loaders: [() => own({ analysis: 'error' })],
  render: () => <IssuesApp path={ISSUES} />,
};

/** Hidden checks: left out of the list, which says how many and shows them on request. */
export const HiddenChecks: Story = {
  loaders: [
    () => {
      own();
      updateSettings({ hiddenChecks: ['title-too-long', 'internal-client-error'] });
    },
  ],
  render: () => <IssuesApp path={`${ISSUES}/empty-anchor-text`} />,
};

const ENGINE_FAILED = () =>
  problem({
    type: 'internal',
    title: 'Something went wrong in the engine.',
    status: 500,
    detail: 'SqliteException: database disk image is malformed',
  });

/** A check's groups can't be read: the compact error in the check, with Try again. */
export const GroupsReadError: Story = {
  loaders: [() => own({}, [['POST', /\/issues\/empty-anchor-text\/groups$/, ENGINE_FAILED]])],
  render: () => <IssuesApp path={`${ISSUES}/empty-anchor-text`} />,
};

/** An open group's first pages can't be read: it stays open with its link to all of them, and says so above. */
export const GroupPagesReadError: Story = {
  loaders: [
    () =>
      own({}, [
        [
          'POST',
          /\/issues\/empty-anchor-text\/rows$/,
          (request) =>
            (request.body as GridQuery).group
              ? ENGINE_FAILED()
              : json(issueRowsPage('empty-anchor-text', request.body as GridQuery)),
        ],
      ]),
  ],
  render: () => (
    <IssuesApp
      path={`${ISSUES}/empty-anchor-text?group=${encodeURIComponent(JSON.stringify({ detail: null, relatedUrlId: 1 }))}`}
    />
  ),
};

/** A link to a check this version doesn't have: the list as ever, and the detail says so. */
export const UnknownCheck: Story = {
  loaders: [() => own()],
  render: () => <IssuesApp path={`${ISSUES}/a-check-from-a-newer-version`} />,
};

const MANY_GROUPS: IssueGroup[] = Array.from({ length: 5_152 }, (_, index) => ({
  detail: `Garden furniture set ${String(index + 1)} | Example Gardens`,
  relatedUrlId: null,
  relatedUrl: null,
  pages: Math.max(1, 40 - Math.floor(index / 130)),
  share: 1 / 5_152,
  likelyTemplate: false,
}));

/** Thousands of groups (the 100k crawl's duplicate titles): paged and virtualised like any grid. */
export const ThousandsOfGroups: Story = {
  loaders: [
    () =>
      own({
        checks: {
          ...ISSUES_CHECKS,
          checks: ISSUES_CHECKS.checks.map((check) =>
            check.id === 'internal-client-error'
              ? { ...check, title: 'Duplicate title', grouping: 'detail', subject: 'page' }
              : check,
          ),
        },
        analysis: {
          ...ISSUES_ANALYSIS,
          counts: ISSUES_ANALYSIS.counts.map((count) =>
            count.checkId === 'internal-client-error'
              ? { ...count, count: MANY_GROUPS.reduce((sum, group) => sum + group.pages, 0) }
              : count,
          ),
        },
        groups: { ...ISSUE_GROUPS, 'internal-client-error': MANY_GROUPS },
      }),
  ],
  render: () => <IssuesApp path={`${ISSUES}/internal-client-error`} />,
};

/** A skipped check with no reason of its own, and no notes to point to: the list and the check say so plainly. */
export const SkippedWithoutReasonOrNotes: Story = {
  loaders: [() => own({ analysis: { ...ISSUES_ANALYSIS, skipped: [], notes: [], isPartial: true } })],
  render: () => <IssuesApp path={`${ISSUES}/orphan-page`} />,
};

const LONG_TITLE = 'Another version of the site answers with a page instead of redirecting to the main one';
const LONG_DETAIL =
  "'<' is an invalid start of a property name. Expected a '\"' after the object's opening brace, or a closing brace.";
const LONG_URL =
  'https://example.com/garden-furniture/rattan-garden-furniture/rattan-corner-sofa-sets/kensington-8-seater-rattan-corner-sofa-set-with-rising-table-and-cushions/';

const LONG_CHECKS: Checks = {
  ...ISSUES_CHECKS,
  checks: ISSUES_CHECKS.checks.map((check) =>
    check.id === 'internal-client-error'
      ? {
          ...check,
          title: LONG_TITLE,
          description:
            'The site answers on another of its versions (http, or the bare domain) with a page of its own, so search engines can index the same content twice and split its links between the two.',
        }
      : check,
  ),
};

const LONG_GROUPS: Record<string, IssueGroup[]> = {
  ...ISSUE_GROUPS,
  'internal-client-error': [
    { detail: LONG_DETAIL, relatedUrlId: 40, relatedUrl: LONG_URL, pages: 2, share: 1, likelyTemplate: true },
  ],
};

const LONG_ROWS: Record<string, (IssueRow & { group?: number })[]> = {
  ...ISSUE_ROWS,
  'internal-client-error': [
    { id: 41, url: `${LONG_URL}?colour=grey&size=large&utm_source=newsletter`, detail: LONG_DETAIL },
    { id: 42, url: LONG_URL, detail: LONG_DETAIL },
  ],
};

/** Long titles, details and URLs: they truncate where they must, with the whole of each a hover away. */
export const LongTitlesAndDetails: Story = {
  loaders: [() => own({ checks: LONG_CHECKS, groups: LONG_GROUPS, rows: LONG_ROWS })],
  render: () => (
    <IssuesApp
      path={`${ISSUES}/internal-client-error?group=${encodeURIComponent(JSON.stringify({ detail: LONG_DETAIL, relatedUrlId: 40 }))}`}
    />
  ),
};
