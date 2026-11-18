// The fix loop over a sidecar of the test's own (docs/v2-fix-loop.md; docs/v2-sidecar-api.md, "(fix loop)"): the
// garden crawl (src/test/simple, its counts now) compared with an earlier check of the site made up for it (its counts
// then are the mock's GARDEN_THEN: three problems fixed, one new, fewer broken links, more large images, a check that
// didn't run then), what a check no longer flags, and rechecks in each of their states. For Simple's and Advanced's
// stories and tests alike.
import type {
  ChangesAnswer,
  ClearedIssue,
  CrawlComparison,
  PreviousCrawl,
  Recheck,
  RecheckEvent,
  RecheckPlan,
  RecheckResult,
  RecheckRow,
  RecheckRowsQuery,
} from '../api/fixLoop';
import {
  GARDEN_THEN,
  GARDEN_THEN_POINTS,
  comparedGroups,
  comparedRows,
  itemChangesOf,
  scenarioComparison,
  scenarioFixed,
  standInCleared,
  standInRecheckRows,
  type ComparisonScenario,
} from '../api/mock/fixLoop';
import type { ProblemBody } from '../api/errors';
import type { GridPage, IssueGroupPage, IssueRow } from '../api/grids';
import type { CrawlListItem } from '../api/types';
import { json, problem, type FakeRoute } from './fakeSidecar';
import { GARDEN_ANALYSIS, SIMPLE_CHECKS, gardenItem } from './simpleSidecar';

/** The earlier check of the garden site: two weeks before the garden crawl (31 Oct), so "since 17 Oct". */
export const EARLIER_ID = 'a1b2c3d4e5f60718';
export const EARLIER_STARTED = '2026-10-17T14:13:30Z';
const SITE = 'https://www.gardenfurnituresets.co.uk';

/** Its score then: 72, Good; the garden crawl's now is 86, so "▲14 since 17 Oct". */
export const EARLIER_SCORE = { value: 72, band: 'good', partial: false } as const;

export const GARDEN_PREVIOUS: PreviousCrawl = {
  id: EARLIER_ID,
  startedAt: EARLIER_STARTED,
  score: EARLIER_SCORE,
  comparable: 'full',
  needsAnalysis: false,
};

/** The garden crawl compared with the earlier check: full by default; a story changes what it needs. */
export function gardenComparison(patch: Partial<ComparisonScenario> = {}): CrawlComparison {
  return scenarioComparison({
    crawlId: '0123456789abcdef',
    with: {
      id: EARLIER_ID,
      startedAt: EARLIER_STARTED,
      finishedAt: '2026-10-17T14:51:00Z',
      status: 'completed',
      startUrl: `${SITE}/`,
      analyzedAt: '2026-10-17T14:52:00Z',
      pagesAnalyzed: 265,
      crawledUrlCount: 4_480,
      chosen: 'previous',
    },
    now: GARDEN_ANALYSIS,
    then: GARDEN_THEN,
    thenScore: EARLIER_SCORE,
    thenPoints: GARDEN_THEN_POINTS,
    catalog: SIMPLE_CHECKS.checks,
    splits: { 'broken-internal-links': { fixed: 3, gone: 2, still: 4, new: 1 } },
    ...patch,
  });
}

/** The garden crawl's item compared with the earlier check: `previous` and `changes` as the sidecar lists them. */
export function comparedGardenItem(
  overrides: Partial<CrawlListItem> = {},
  comparison: CrawlComparison = gardenComparison(),
): CrawlListItem {
  return gardenItem({ previous: GARDEN_PREVIOUS, changes: itemChangesOf(comparison), ...overrides });
}

/** The earlier check's own item: the site's first, so it has nothing to compare with. */
export function earlierGardenItem(overrides: Partial<CrawlListItem> = {}): CrawlListItem {
  return gardenItem({
    id: EARLIER_ID,
    path: 'C:\\crawls\\gardenfurnituresets.co.uk-20261017.sicrawl',
    fileName: 'gardenfurnituresets.co.uk-20261017.sicrawl',
    startedAt: EARLIER_STARTED,
    finishedAt: '2026-10-17T14:51:00Z',
    analyzedAt: '2026-10-17T14:52:00Z',
    score: EARLIER_SCORE,
    issues: { error: 4, warning: 4, notice: 14 },
    previous: null,
    changes: null,
    ...overrides,
  });
}

/** The broken-links check's rows then that it doesn't flag now: three fixed, two no longer found. */
export const BROKEN_LINKS_CLEARED: readonly ClearedIssue[] = [
  {
    url: `${SITE}/garden-furniture/`,
    urlId: 40,
    thenUrlId: 38,
    state: 'fixed',
    reason: null,
    detailThen: 'Links to 1 broken URL',
    relatedUrlThen: `${SITE}/old-range/`,
  },
  {
    url: `${SITE}/rattan-sofa-sets/`,
    urlId: 52,
    thenUrlId: 51,
    state: 'fixed',
    reason: null,
    detailThen: 'Links to 1 broken URL',
    relatedUrlThen: `${SITE}/old-range/`,
  },
  {
    url: `${SITE}/dining-sets/`,
    urlId: 61,
    thenUrlId: 60,
    state: 'fixed',
    reason: null,
    detailThen: 'Links to 2 broken URLs',
    relatedUrlThen: `${SITE}/summer-sale/`,
  },
  {
    url: `${SITE}/winter-sale/`,
    urlId: null,
    thenUrlId: 977,
    state: 'gone',
    reason: null,
    detailThen: 'Links to 1 broken URL',
    relatedUrlThen: `${SITE}/old-range/`,
  },
  {
    url: `${SITE}/clearance/`,
    urlId: null,
    thenUrlId: 981,
    state: 'gone',
    reason: null,
    detailThen: 'Links to 1 broken URL',
    relatedUrlThen: `${SITE}/summer-sale/`,
  },
];

/** A fixed check's rows then (each fixed): `internal-server-error`'s two 500s, which answer 200 now. */
export const SERVER_ERROR_CLEARED: readonly ClearedIssue[] = [
  {
    url: `${SITE}/checkout/`,
    urlId: 88,
    thenUrlId: 86,
    state: 'fixed',
    reason: null,
    detailThen: '500 Internal Server Error',
    relatedUrlThen: null,
  },
  {
    url: `${SITE}/basket/`,
    urlId: 89,
    thenUrlId: 87,
    state: 'notChecked',
    reason: { code: 'failed', text: 'No answer within 30 s' },
    detailThen: '500 Internal Server Error',
    relatedUrlThen: null,
  },
];

/** `/changes` answering with the comparison given, or a first crawl's `{ crawlId, with: null }`. */
export function changesRoute(answer: ChangesAnswer | 'never' = gardenComparison()): FakeRoute {
  return [
    'GET',
    /^\/api\/crawls\/[0-9a-f]{16}\/changes/,
    () => (answer === 'never' ? new Promise<Response>(() => undefined) : json(answer)),
  ];
}

/** `/fixed` from the rows each check had then and not now, narrowed and paged as the sidecar does. */
export function fixedRoute(
  rowsByCheck: Readonly<Record<string, readonly ClearedIssue[]>> = {
    'broken-internal-links': BROKEN_LINKS_CLEARED,
    'internal-server-error': SERVER_ERROR_CLEARED,
  },
  comparison: CrawlComparison | null = gardenComparison(),
): FakeRoute {
  return [
    'POST',
    /^\/api\/crawls\/[0-9a-f]{16}\/issues\/([^/]+)\/fixed$/,
    (request, match) => {
      const checkId = match[1] ?? '';
      const change = comparison?.checks.find((each) => each.checkId === checkId);
      // A check without rows of its own gets stand-ins, as many as the comparison counts: never a count over an empty list.
      const rows = rowsByCheck[checkId] ?? (change ? standInCleared(change, SITE) : []);
      return json(scenarioFixed(checkId, comparison, rows, request.body ?? {}));
    },
  ];
}

// ------------------------------------------------------------------------------------------- Rechecks

/** A plan for the broken-links check's five pages: 8 fetches, about 20 seconds, nothing refused. */
export function recheckPlan(patch: Partial<RecheckPlan> = {}): RecheckPlan {
  return {
    checkId: 'broken-internal-links',
    group: null,
    urlList: false,
    kinds: ['urls'],
    judged: 5,
    phase1: 8,
    phase2Max: 0,
    limit: 1_000,
    capped: false,
    notRechecked: 0,
    estimateMs: 21_000,
    suggestFullCheck: false,
    analysisFirst: false,
    countBefore: 5,
    secrets: { missingHeaders: [], proxyCredentials: false },
    refused: null,
    ...patch,
  };
}

/** What a recheck of the five found: 4 fixed, 1 still has it; the score 86 to 88. */
export function recheckResult(patch: Partial<RecheckResult> = {}): RecheckResult {
  return { fixed: 4, still: 1, notChecked: 0, notRechecked: 0, new: 0, scoreBefore: 86, scoreAfter: 88, ...patch };
}

/** A settled recheck of the broken-links check, today at 14:02. */
export function recheckRecord(patch: Partial<Recheck> = {}): Recheck {
  return {
    id: 1,
    status: 'completed',
    startedAt: '2026-11-17T14:02:12Z',
    endedAt: '2026-11-17T14:02:31Z',
    settledAt: '2026-11-17T14:02:41Z',
    crawlingMs: 19_200,
    checkId: 'broken-internal-links',
    group: null,
    urlList: false,
    kinds: ['urls'],
    judged: 5,
    fetched: 8,
    countBefore: 5,
    result: recheckResult(),
    ...patch,
  };
}

/** A `recheck` event as the stream sends it. */
export function recheckEvent(patch: Partial<RecheckEvent> = {}): RecheckEvent {
  return {
    crawlId: '0123456789abcdef',
    recheckId: 1,
    status: 'running',
    phase: 1,
    done: 3,
    total: 8,
    result: null,
    ...patch,
  };
}

/** The recheck's URLs: the one page that still has it, then the four fixed. */
export const RECHECK_ROWS: readonly RecheckRow[] = [
  {
    urlId: 31,
    url: `${SITE}/sofas/`,
    role: 'judged',
    outcome: 'still',
    flaggedBefore: true,
    detailBefore: 'Links to 2 broken URLs',
    detailAfter: 'Links to 1 broken URL',
    statusCode: 200,
  },
  ...['/garden-furniture/', '/rattan-sofa-sets/', '/dining-sets/', '/bistro-sets/'].map((path, index): RecheckRow => ({
    urlId: 40 + index,
    url: `${SITE}${path}`,
    role: 'judged',
    outcome: 'fixed',
    flaggedBefore: true,
    detailBefore: 'Links to 1 broken URL',
    detailAfter: null,
    statusCode: 200,
  })),
];

/**
 * The recheck routes: the plan (or a refusal), starting one (202 with its number, or the problem given), the crawl's
 * rechecks, one of them, and its rows, narrowed by outcome and role.
 */
export function recheckRoutes({
  plan = recheckPlan(),
  rechecks = [],
  rows,
  start,
}: {
  plan?: RecheckPlan | 'never';
  rechecks?: readonly Recheck[];
  rows?: readonly RecheckRow[];
  /** What starting one answers: by default 202 with the next number and the plan. */
  start?: (request: unknown) => Response;
} = {}): FakeRoute[] {
  // A recheck's URLs: those given; else the broken links' (one still has it, four fixed) for its 4-of-5 result; else as
  // many stand-ins with each outcome as its result counts. Never a count over an empty list.
  const rowsOf = (recheck: Recheck | undefined): readonly RecheckRow[] => {
    if (rows) return rows;
    if (!recheck?.result) return RECHECK_ROWS;
    const { fixed, still, notChecked, notRechecked, new: added } = recheck.result;
    if (fixed === 4 && still === 1 && notChecked + notRechecked + added === 0) return RECHECK_ROWS;
    return standInRecheckRows(recheck, SITE);
  };
  const nextId = Math.max(0, ...rechecks.map((each) => each.id)) + 1;
  return [
    [
      'POST',
      /^\/api\/crawls\/[0-9a-f]{16}\/rechecks\/plan$/,
      () => (plan === 'never' ? new Promise<Response>(() => undefined) : json(plan)),
    ],
    [
      'POST',
      /^\/api\/crawls\/[0-9a-f]{16}\/rechecks$/,
      (request) =>
        start ? start(request.body) : json({ recheckId: nextId, ...(plan === 'never' ? recheckPlan() : plan) }, 202),
    ],
    ['GET', /^\/api\/crawls\/[0-9a-f]{16}\/rechecks$/, () => json({ rechecks })],
    [
      'GET',
      /^\/api\/crawls\/[0-9a-f]{16}\/rechecks\/(\d+)$/,
      (_request, match) => {
        const found = rechecks.find((each) => each.id === Number(match[1]));
        return found
          ? json(found)
          : problem({ type: 'recheck-not-found', title: 'This crawl has no recheck with this number.', status: 404 });
      },
    ],
    [
      'POST',
      /^\/api\/crawls\/[0-9a-f]{16}\/rechecks\/(\d+)\/rows$/,
      (request, match) => {
        const query = (request.body ?? {}) as RecheckRowsQuery;
        const matching = rowsOf(rechecks.find((each) => each.id === Number(match[1]))).filter(
          (row) =>
            (!query.outcomes || (row.outcome !== null && query.outcomes.includes(row.outcome))) &&
            (!query.roles || query.roles.includes(row.role)),
        );
        const offset = query.offset ?? 0;
        return json({ total: matching.length, offset, rows: matching.slice(offset, offset + (query.limit ?? 100)) });
      },
    ],
  ];
}

/** The needs-secrets problem a recheck of a crawl that had header values answers with, as Continue does. */
export const RECHECK_NEEDS_SECRETS = {
  type: 'needs-secrets',
  title: 'Rechecking this crawl needs the values of its headers.',
  status: 409,
  missingHeaders: ['Authorization'],
  proxyCredentials: false,
} satisfies ProblemBody;

/**
 * The given routes, with a check's groups and rows compared when asked (`compareWith`), as the sidecar answers them:
 * each group and row gains its `change` and the page `compared` (the mock's `comparedGroups` and `comparedRows`).
 */
export function compared(routes: readonly FakeRoute[], comparison: CrawlComparison = gardenComparison()): FakeRoute[] {
  return routes.map(([method, pattern, answer]): FakeRoute => {
    if (method !== 'POST' || !/issues.*(groups|rows)\$$/.test(pattern.source)) return [method, pattern, answer];
    return [
      method,
      pattern,
      async (request, match) => {
        const response = await answer(request, match);
        const body = request.body as { compareWith?: string } | undefined;
        if (!body?.compareWith || !response.ok) return response;
        const page = (await response.json()) as IssueGroupPage | GridPage<IssueRow>;
        if ('groups' in page) return json(comparedGroups(page, comparison));
        const checkId = /\/issues\/([^/]+)\/rows$/.exec(request.path)?.[1] ?? '';
        return json({
          ...page,
          rows: comparedRows(page.rows, comparison, checkId),
          compared: {
            with: comparison.with.id,
            startedAt: comparison.with.startedAt,
            comparable: comparison.comparable,
          },
        });
      },
    ];
  });
}
