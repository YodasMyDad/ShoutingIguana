// The fix loop in the mock sidecar (docs/v2-sidecar-api.md, "(fix loop)"): the library's `siteKey`, a comparison with
// the site's previous crawl, what a check no longer flags, and rechecks, played as the sidecar plays them (the
// `rechecking` status, `recheck` events, the analysis that settles it). The design fixtures hold one crawl a site, so a
// garden crawl from two weeks before is **made up** here, with the counts then in GARDEN_THEN: the counts now are the
// fixture's own. Stories with states the mock can't show have their own sidecar (src/test/fixLoopSidecar.ts), built
// with the same `scenarioComparison`.
import type { ProblemBody } from '../errors';
import type {
  ClearedIssue,
  Comparability,
  ComparedCrawl,
  ComparisonDifference,
  CrawlChanges,
  CrawlComparison,
  CheckChange,
  CheckChangeState,
  FixedPage,
  FixedQuery,
  Recheck,
  RecheckAsk,
  RecheckKind,
  RecheckPlan,
  RecheckResult,
  RecheckRow,
  RecheckRowsQuery,
  RowSplit,
} from '../fixLoop';
import type { IssueGroupPage, IssueRow } from '../grids';
import type { TransportRequest } from '../transport';
import type {
  Analysis,
  Check,
  CheckCount,
  CrawlListItem,
  CrawlStatus,
  OutOfDateReason,
  ScoreSummary,
  Severity,
} from '../types';

// ------------------------------------------------------------------------------------------- The sidecar's rules

/**
 * The site a crawl is, as the sidecar keys it (`PreviousCrawls.SiteKeyOf`): the start URL's host without `www.`, then
 * `:port` when it has a port of its own. The app never works this out: only a sidecar does, and this is one.
 */
export function siteKeyOf(startUrl: string | null): string | null {
  if (!startUrl) return null;
  try {
    const url = new URL(startUrl);
    const host = url.hostname.toLowerCase().replace(/^www\./, '');
    return url.port ? `${host}:${url.port}` : host;
  } catch {
    return null;
  }
}

/** What a recheck of each check reads again: every check `urls` but these (the engine's catalog, `Recheck`). */
const RECHECK_KINDS: Readonly<Record<string, RecheckKind[]>> = {
  'orphan-page': [],
  'non-indexable-inlinks-only': [],
  'deep-page': [],
  'canonical-unlinked': [],
  'no-https': [],
  'robots-txt-unreachable': ['robotsTxt'],
  'robots-txt-unreachable-other-origin': ['robotsTxt'],
  'robots-txt-missing': ['robotsTxt'],
  'sitemap-non-indexable': ['urls', 'sitemaps'],
  'not-in-sitemap': ['urls', 'sitemaps'],
  'sitemap-url-other-host': ['urls', 'sitemaps'],
  'sitemap-error': ['sitemaps'],
  'sitemap-missing': ['sitemaps'],
  'duplicate-site-version': ['siteVersions'],
};

export function recheckKindsOf(checkId: string): RecheckKind[] {
  return RECHECK_KINDS[checkId] ?? ['urls'];
}

// ------------------------------------------------------------------------------------------- A comparison, made up

/** A comparison to make: the crawl now (its analysis), the crawl then (the counts that differ), and how they compare. */
export interface ComparisonScenario {
  crawlId: string;
  with: ComparedCrawl;
  /** The analysis now: its counts, its skipped checks, its fix-first order and its score. */
  now: Pick<Analysis, 'counts' | 'skipped' | 'fixFirst' | 'score'>;
  /** The counts then that differ from now's (null: the check didn't run then); any other check counted as now. */
  then: Readonly<Record<string, number | null>>;
  thenScore: ScoreSummary | null;
  /** The points each check took then, where they differ from now's. */
  thenPoints?: Readonly<Record<string, number>>;
  /** The catalog, for each check's severity and order. */
  catalog: readonly Pick<Check, 'id' | 'severity'>[];
  comparable?: Comparability;
  differences?: ComparisonDifference[];
  previousNeedsAnalysis?: OutOfDateReason[];
  /** A row split where the default (the count's difference) isn't the story. */
  splits?: Readonly<Record<string, Partial<RowSplit>>>;
  /** Score versions differ: no score change. */
  scoredApart?: boolean;
}

const NO_SPLIT: RowSplit = { fixed: 0, gone: 0, notChecked: 0, still: 0, new: 0, newUrl: 0, notComparedThen: 0 };

function stateOf(then: number | null, now: number | null): CheckChangeState {
  if (then === null) return 'newCheck';
  if (now === null) return 'notCheckedNow';
  if (then > 0 && now === 0) return 'fixed';
  if (then === 0 && now > 0) return 'new';
  if (now < then) return 'fewer';
  if (now > then) return 'more';
  return 'same';
}

/** How the rows moved, when the scenario doesn't say: the difference in counts, the rest still. */
function defaultSplit(state: CheckChangeState, then: number | null, now: number | null): RowSplit {
  const before = then ?? 0;
  const after = now ?? 0;
  switch (state) {
    case 'fixed':
      return { ...NO_SPLIT, fixed: before };
    case 'new':
      return { ...NO_SPLIT, new: after };
    case 'fewer':
      return { ...NO_SPLIT, fixed: before - after, still: after };
    case 'more':
      return { ...NO_SPLIT, still: before, new: after - before };
    case 'newCheck':
      return { ...NO_SPLIT, notComparedThen: after };
    case 'notCheckedNow':
      return { ...NO_SPLIT, notChecked: before };
    case 'same':
      return { ...NO_SPLIT, still: after };
  }
}

/** `GET /changes` for a scenario: every check that ran on either side, fix-first order then the catalog's. */
export function scenarioComparison(scenario: ComparisonScenario): CrawlComparison {
  const comparable = scenario.comparable ?? 'full';
  const countsOnly = comparable === 'countsOnly';
  const skippedNow = new Set(scenario.now.skipped.map((skip) => skip.checkId));
  const nowCounts = new Map(scenario.now.counts.map((count: CheckCount) => [count.checkId, count.count]));
  const severity = new Map<string, Severity>(scenario.catalog.map((check) => [check.id, check.severity]));
  const fixFirst = scenario.now.fixFirst ?? [];
  const order = [...fixFirst, ...scenario.catalog.map((check) => check.id).filter((id) => !fixFirst.includes(id))];
  const checks: CheckChange[] = [];
  for (const checkId of order) {
    const now = skippedNow.has(checkId) ? null : (nowCounts.get(checkId) ?? 0);
    const then = checkId in scenario.then ? (scenario.then[checkId] ?? null) : now;
    if (then === null && now === null) continue;
    const state = stateOf(then, now);
    const split = { ...defaultSplit(state, then, now), ...scenario.splits?.[checkId] };
    const known = severity.get(checkId) ?? null;
    checks.push({
      checkId,
      severity: now === null ? null : known,
      severityThen: then === null ? null : known,
      state,
      then,
      now,
      ...(countsOnly
        ? { fixed: null, gone: null, notChecked: null, still: null, new: null, newUrl: null, notComparedThen: null }
        : split),
    });
  }
  const byState = (state: CheckChangeState) => checks.filter((check) => check.state === state).length;
  const issues = countsOnly
    ? null
    : checks.reduce<RowSplit>(
        (sum, check) => ({
          fixed: sum.fixed + (check.fixed ?? 0),
          gone: sum.gone + (check.gone ?? 0),
          notChecked: sum.notChecked + (check.notChecked ?? 0),
          still: sum.still + (check.still ?? 0),
          new: sum.new + (check.new ?? 0),
          newUrl: sum.newUrl + (check.newUrl ?? 0),
          notComparedThen: sum.notComparedThen + (check.notComparedThen ?? 0),
        }),
        NO_SPLIT,
      );
  const scoreNow = scenario.now.score;
  const pointsNow = new Map((scoreNow?.lines ?? []).map((line) => [line.checkId, line.points]));
  const lineIds = [...new Set([...pointsNow.keys(), ...Object.keys(scenario.thenPoints ?? {})])];
  return {
    crawlId: scenario.crawlId,
    with: scenario.with,
    comparable,
    differences: scenario.differences ?? [],
    previousNeedsAnalysis: scenario.previousNeedsAnalysis ?? [],
    score:
      scoreNow && scenario.thenScore
        ? {
            then: scenario.thenScore,
            now: { value: scoreNow.value, band: scoreNow.band, partial: scoreNow.partial },
            change: scenario.scoredApart ? null : scoreNow.value - scenario.thenScore.value,
            lines: lineIds
              .map((checkId) => ({
                checkId,
                then: scenario.thenPoints?.[checkId] ?? pointsNow.get(checkId) ?? 0,
                now: pointsNow.get(checkId) ?? 0,
              }))
              .filter((line) => line.then > 0 || line.now > 0),
          }
        : null,
    totals: {
      checks: {
        fixed: byState('fixed'),
        new: byState('new'),
        fewer: byState('fewer'),
        more: byState('more'),
        same: byState('same'),
        newCheck: byState('newCheck'),
        notCheckedNow: byState('notCheckedNow'),
      },
      issues,
    },
    checks,
  };
}

/**
 * A library item's `changes` from a comparison, as the sidecar works them out from cached summaries: every check by
 * state, hidden or not; the score's change; "so far" when either side's score is partial.
 */
export function itemChangesOf(comparison: CrawlComparison): CrawlChanges {
  const of = (state: CheckChangeState) =>
    comparison.checks.filter((check) => check.state === state).map((check) => check.checkId);
  return {
    score: comparison.score?.change ?? null,
    partial: (comparison.score?.then.partial ?? false) || (comparison.score?.now.partial ?? false),
    fixed: of('fixed'),
    new: of('new'),
    fewer: of('fewer'),
    more: of('more'),
    newCheck: of('newCheck'),
    notCheckedNow: of('notCheckedNow'),
  };
}

/** `/fixed` for a scenario: the rows given, narrowed and paged as the sidecar does. */
export function scenarioFixed(
  checkId: string,
  comparison: CrawlComparison | null,
  rows: readonly ClearedIssue[],
  query: FixedQuery,
): FixedPage {
  if (!comparison) return { checkId, compared: null, level: 'rows', total: null, offset: 0, rows: [] };
  const compared = {
    with: comparison.with.id,
    startedAt: comparison.with.startedAt,
    comparable: comparison.comparable,
  };
  const offset = query.offset ?? 0;
  const limit = query.limit ?? 100;
  const states = query.states ?? ['fixed', 'gone', 'notChecked'];
  if (comparison.comparable === 'countsOnly') {
    return { checkId, compared, level: query.level ?? 'rows', total: null, offset, rows: [] };
  }
  const matching = rows.filter(
    (row) =>
      states.includes(row.state) &&
      (!query.group ||
        (row.relatedUrlThen === query.group.relatedUrl &&
          (query.group.detail === null || row.detailThen === query.group.detail))),
  );
  if (query.level === 'groups') {
    const causes = new Map<string, { detail: string | null; relatedUrl: string | null; rows: ClearedIssue[] }>();
    for (const row of matching) {
      const key = row.relatedUrlThen ?? '';
      const cause = causes.get(key) ?? { detail: null, relatedUrl: row.relatedUrlThen, rows: [] };
      cause.rows.push(row);
      causes.set(key, cause);
    }
    const groups = [...causes.values()].map((cause) => ({
      detail: cause.detail,
      relatedUrl: cause.relatedUrl,
      sharedText: null,
      then: cause.rows.length,
      fixed: cause.rows.filter((row) => row.state === 'fixed').length,
      gone: cause.rows.filter((row) => row.state === 'gone').length,
      notChecked: cause.rows.filter((row) => row.state === 'notChecked').length,
    }));
    return {
      checkId,
      compared,
      level: 'groups',
      total: groups.length,
      offset,
      groups: groups.slice(offset, offset + limit),
    };
  }
  return {
    checkId,
    compared,
    level: 'rows',
    total: matching.length,
    offset,
    rows: matching.slice(offset, offset + limit),
  };
}

/** A check's groups compared: every cause then as many as now, but the first, which had two more. */
export function comparedGroups(page: IssueGroupPage, comparison: CrawlComparison): IssueGroupPage {
  const change = comparison.checks.find((check) => check.checkId === page.checkId);
  const compared = {
    with: comparison.with.id,
    startedAt: comparison.with.startedAt,
    comparable: comparison.comparable,
    fixedGroups: change && change.state !== 'newCheck' ? Math.min(2, change.fixed ?? 0) : null,
  };
  if (comparison.comparable === 'countsOnly') return { ...page, compared };
  return {
    ...page,
    compared,
    groups: page.groups.map((group, index) => {
      const urls = group.urls ?? group.pages;
      if (change?.state === 'new' || change?.state === 'newCheck')
        return { ...group, change: { state: 'new', then: 0 } };
      return { ...group, change: index === 0 ? { state: 'fewer', then: urls + 2 } : { state: 'same', then: urls } };
    }),
  };
}

/** A check's rows compared: still, but for the check's new rows (its last ones), which are new. */
export function comparedRows(rows: readonly IssueRow[], comparison: CrawlComparison, checkId: string): IssueRow[] {
  const change = comparison.checks.find((check) => check.checkId === checkId);
  if (comparison.comparable === 'countsOnly') return rows.map((row) => ({ ...row, change: null }));
  const added = (change?.new ?? 0) + (change?.newUrl ?? 0);
  return rows.map((row, index) => ({
    ...row,
    change:
      change?.state === 'newCheck'
        ? { state: 'notComparedThen', detailThen: null }
        : index >= rows.length - added
          ? { state: 'new', detailThen: null }
          : { state: 'still', detailThen: row.detail ?? null },
  }));
}

// ------------------------------------------------------------------------------------------- Never an empty list

/**
 * Stand-ins for what a check flagged then and not now, where a story's sidecar has no rows of its own: as many as the
 * comparison counts, split as it splits them (fixed, no longer found, not checked this time), on the site, each its own
 * cause. Never a count over an empty list (round 4).
 */
export function standInCleared(change: CheckChange, site: string): ClearedIssue[] {
  const rows: ClearedIssue[] = [];
  const add = (state: ClearedIssue['state'], count: number) => {
    for (let index = 0; index < count; index++) {
      const n = rows.length + 1;
      rows.push({
        url: `${site}/example-page-${n}/`,
        urlId: state === 'gone' ? null : 20_000 + n,
        thenUrlId: 30_000 + n,
        state,
        reason: state === 'notChecked' ? { code: 'notRead', text: 'Not read this time' } : null,
        detailThen: null,
        relatedUrlThen: `${site}/example-cause-${n}/`,
      });
    }
  };
  add('fixed', change.fixed ?? 0);
  add('gone', change.gone ?? 0);
  add('notChecked', change.notChecked ?? 0);
  return rows;
}

/**
 * Stand-ins for a settled recheck's URLs, where a story's sidecar has none of its own: the URLs it judged, as many with
 * each outcome as its result counts, and those that have it now and didn't. Never a count over an empty list.
 */
export function standInRecheckRows(recheck: Recheck, site: string): RecheckRow[] {
  const result = recheck.result;
  if (!result) return [];
  const rows: RecheckRow[] = [];
  const add = (outcome: RecheckRow['outcome'], count: number, flaggedBefore: boolean) => {
    for (let index = 0; index < count; index++) {
      const n = rows.length + 1;
      rows.push({
        urlId: 40_000 + n,
        url: `${site}/example-page-${n}/`,
        role: 'judged',
        outcome,
        flaggedBefore,
        detailBefore: flaggedBefore ? 'As the check found it' : null,
        detailAfter: outcome === 'still' || outcome === 'new' ? 'As the recheck found it' : null,
        statusCode: outcome === 'notChecked' ? null : 200,
      });
    }
  };
  add('still', result.still, true);
  add('fixed', result.fixed, true);
  add('notChecked', result.notChecked, true);
  add('notRechecked', result.notRechecked, true);
  add('new', result.new, false);
  return rows;
}

// ------------------------------------------------------------------------------------------- The garden's made-up past

/** The garden crawl of two weeks before, made up for the mock: the site's previous crawl. */
export const EARLIER_GARDEN_ID = 'a1b2c3d4e5f60718';

/**
 * The garden crawl's counts then, where they differ from the fixture's (now): three problems fixed, one new, fewer
 * broken links and 4xx answers, more large images, and a check that didn't run then. Every other check as now.
 */
export const GARDEN_THEN: Readonly<Record<string, number | null>> = {
  'internal-client-error': 12,
  'broken-internal-links': 9,
  'internal-server-error': 2,
  'title-missing': 3,
  'redirect-chain': 4,
  'canonical-to-first-page': 0,
  'image-too-large': 58,
  'title-too-long': 86,
  'h1-multiple': 2,
  'slow-response': null,
};

/** The points each check took then, where they differ from now. */
export const GARDEN_THEN_POINTS: Readonly<Record<string, number>> = {
  'internal-client-error': 3,
  'broken-internal-links': 7,
  'internal-server-error': 6,
  'title-missing': 5,
  'redirect-chain': 3,
  'canonical-to-first-page': 0,
  'slow-response': 0,
};

/** The rows the broken-links check had then and not now: three fixed, two no longer found. */
export function gardenClearedRows(site: string): ClearedIssue[] {
  const page = (path: string) => `${site}${path}`;
  return [
    {
      url: page('/garden-furniture/'),
      urlId: 40,
      thenUrlId: 38,
      state: 'fixed',
      reason: null,
      detailThen: 'Links to 1 broken URL',
      relatedUrlThen: page('/old-range/'),
    },
    {
      url: page('/rattan-sofa-sets/'),
      urlId: 52,
      thenUrlId: 51,
      state: 'fixed',
      reason: null,
      detailThen: 'Links to 1 broken URL',
      relatedUrlThen: page('/old-range/'),
    },
    {
      url: page('/dining-sets/'),
      urlId: 61,
      thenUrlId: 60,
      state: 'fixed',
      reason: null,
      detailThen: 'Links to 2 broken URLs',
      relatedUrlThen: page('/summer-sale/'),
    },
    {
      url: page('/winter-sale/'),
      urlId: null,
      thenUrlId: 977,
      state: 'gone',
      reason: null,
      detailThen: 'Links to 1 broken URL',
      relatedUrlThen: page('/old-range/'),
    },
    {
      url: page('/clearance/'),
      urlId: null,
      thenUrlId: 981,
      state: 'gone',
      reason: null,
      detailThen: 'Links to 1 broken URL',
      relatedUrlThen: page('/summer-sale/'),
    },
  ];
}

// ------------------------------------------------------------------------------------------- Rechecks, played

type Handler = (match: RegExpMatchArray, request: TransportRequest) => Promise<Response>;
export type FixLoopRoute = [method: TransportRequest['method'], pattern: RegExp, handler: Handler];

/** What the routes need of the mock they're part of. */
export interface FixLoopMock {
  json: (body: unknown, status?: number) => Response;
  problem: (body: ProblemBody) => Response;
  /** Every crawl of the library, as listed. */
  items: () => Promise<CrawlListItem[]>;
  item: (id: string) => Promise<CrawlListItem | null>;
  update: (id: string, patch: Partial<CrawlListItem>) => void;
  setStatus: (id: string, status: CrawlStatus) => void;
  emit: (event: string, data: unknown) => void;
  /** Analyses the crawl as the mock does: its phases as events, then `done`. */
  analyse: (id: string, done: () => void) => void;
  /** Crawling here (not a recheck). */
  crawling: (id: string) => boolean;
  analysis: (id: string) => Promise<Analysis | null>;
  checks: () => Promise<Check[]>;
  /** A check's flagged rows in the crawl, for a recheck's rows. */
  issueRows: (id: string, checkId: string) => Promise<IssueRow[]>;
}

/** How long each URL of a played recheck takes to fetch. */
const RECHECK_STEP_MS = 250;

interface PlayedRecheck {
  record: Recheck;
  status: CrawlStatus;
  timer: ReturnType<typeof setInterval> | null;
  rows: RecheckRow[];
  stop: () => void;
}

export function fixLoopRoutes(mock: FixLoopMock): { routes: FixLoopRoute[]; stop: (id: string) => boolean } {
  const crawl = String.raw`^\/api\/crawls\/([0-9a-f]{16})`;
  const rechecks = new Map<string, Recheck[]>();
  const playing = new Map<string, PlayedRecheck>();
  /** Each settled recheck's URLs, by `<crawl id>/<recheck id>`, for `/rechecks/{n}/rows`. */
  const playedRows = new Map<string, RecheckRow[]>();

  const notFound = (id: string): ProblemBody => ({
    type: 'crawl-not-found',
    title: 'There’s no crawl with this id.',
    status: 404,
    detail: id,
  });

  /** The site's latest crawl, in Home's order: the newest start, and of two at once the smaller id. */
  const latestOfSite = async (item: CrawlListItem) => {
    const all = (await mock.items()).filter((other) => other.siteKey === item.siteKey && !other.missing);
    all.sort((a, b) => (b.startedAt ?? '').localeCompare(a.startedAt ?? '') || a.id.localeCompare(b.id));
    return all[0]?.id ?? item.id;
  };

  const plan = async (item: CrawlListItem, ask: RecheckAsk): Promise<RecheckPlan | ProblemBody> => {
    const checkId = ask.checkId ?? null;
    const analysis = await mock.analysis(item.id);
    const count = checkId ? (analysis?.counts.find((entry) => entry.checkId === checkId)?.count ?? 0) : null;
    const rows = checkId ? await mock.issueRows(item.id, checkId) : [];
    const inGroup = ask.group ? rows.filter((row) => row.relatedUrlId === ask.group?.relatedUrlId) : rows;
    const judged = ask.urlIds ? ask.urlIds.length : ask.group ? inGroup.length : (count ?? 0);
    const kinds = checkId ? recheckKindsOf(checkId) : (['urls'] as RecheckKind[]);
    const live = item.status === 'rechecking' || item.status === 'analyzing' || mock.crawling(item.id);
    const siteBusy = (await mock.items()).some(
      (other) =>
        other.id !== item.id &&
        other.siteKey === item.siteKey &&
        (other.status === 'running' || other.status === 'rechecking'),
    );
    const refused = live
      ? { type: 'crawl-busy', title: 'This crawl is running. Stop it first.' }
      : !item.analyzedAt
        ? { type: 'not-analyzed', title: 'This check hasn’t been analysed yet.' }
        : checkId && kinds.length === 0
          ? { type: 'recheck-unsupported', title: 'Only checking the whole site again can show whether this is fixed.' }
          : siteBusy
            ? { type: 'site-busy', title: 'A check of this site is running. Its results will show what’s fixed.' }
            : (await latestOfSite(item)) !== item.id
              ? { type: 'recheck-not-latest', title: 'Recheck works on the site’s latest check.' }
              : null;
    const capped = judged > 1_000;
    return {
      checkId,
      group: ask.group ?? null,
      urlList: ask.urlIds !== undefined,
      kinds,
      judged,
      phase1: Math.min(judged, 1_000) + (checkId === 'broken-internal-links' ? 3 : 0),
      phase2Max: checkId === 'internal-client-error' ? Math.min(judged, 1_000) * 3 : 0,
      limit: 1_000,
      capped,
      notRechecked: capped ? judged - 1_000 : 0,
      estimateMs: 4_000 + Math.min(judged, 1_000) * 500,
      suggestFullCheck: judged > item.fetchedUrlCount / 2,
      analysisFirst: item.outOfDate,
      countBefore: count,
      secrets: { missingHeaders: [], proxyCredentials: false },
      refused,
    };
  };

  /** Plays a recheck: `rechecking`, its progress, its end, the analysis, then the result once settled. */
  const play = (item: CrawlListItem, planned: RecheckPlan, record: Recheck, rows: IssueRow[]) => {
    const before = item.status;
    const judged = Math.min(planned.judged, 1_000);
    // Of what it judges, the first still has it; the rest are fixed.
    const still = judged > 1 ? 1 : 0;
    const total = planned.phase1;
    let done = 0;
    const played: PlayedRecheck = { record, status: before, timer: null, rows: [], stop: () => undefined };
    const event = (status: Recheck['status'], result: RecheckResult | null) =>
      mock.emit('recheck', {
        crawlId: item.id,
        recheckId: record.id,
        status,
        phase: done > 0 ? 1 : null,
        done,
        total,
        result,
      });
    const settle = (status: Recheck['status']) => {
      const rechecked = status === 'stopped' ? Math.min(done, judged) : judged;
      const result: RecheckResult = {
        fixed: Math.max(0, rechecked - still),
        still: Math.min(still, rechecked),
        notChecked: 0,
        notRechecked: judged - rechecked + (planned.judged - judged),
        new: 0,
        scoreBefore: item.score?.value ?? null,
        scoreAfter: item.score ? Math.min(100, item.score.value + (rechecked > still ? 2 : 0)) : null,
      };
      played.rows = rows.slice(0, judged).map((row, index) => ({
        urlId: row.id,
        url: row.url ?? '',
        role: 'judged',
        outcome: index >= rechecked ? 'notRechecked' : index < still ? 'still' : 'fixed',
        flaggedBefore: true,
        detailBefore: row.detail ?? null,
        detailAfter: index < still ? (row.detail ?? null) : null,
        statusCode: 200,
      }));
      playedRows.set(`${item.id}/${record.id}`, played.rows);
      const ended = new Date().toISOString();
      const settled: Recheck = {
        ...record,
        status,
        endedAt: ended,
        settledAt: ended,
        fetched: done,
        crawlingMs: done * RECHECK_STEP_MS,
        result,
      };
      rechecks.set(item.id, [settled, ...(rechecks.get(item.id) ?? []).filter((each) => each.id !== record.id)]);
      mock.analyse(item.id, () => {
        playing.delete(item.id);
        mock.update(item.id, {
          rechecks: {
            count: rechecks.get(item.id)?.length ?? 1,
            last: {
              id: record.id,
              endedAt: ended,
              checkId: record.checkId,
              judged: record.judged,
              fixed: result.fixed,
            },
          },
          ...(item.score && result.scoreAfter !== null ? { score: { ...item.score, value: result.scoreAfter } } : {}),
          analyzedAt: new Date().toISOString(),
          outOfDate: false,
        });
        event(status, result);
        mock.setStatus(item.id, before);
        mock.emit('library', {});
      });
    };
    const finish = (status: Recheck['status']) => {
      if (played.timer) clearInterval(played.timer);
      played.timer = null;
      event(status, null);
      settle(status);
    };
    played.stop = () => {
      mock.setStatus(item.id, 'stopping');
      setTimeout(() => finish('stopped'), 300);
    };
    playing.set(item.id, played);
    mock.setStatus(item.id, 'rechecking');
    event('running', null);
    played.timer = setInterval(() => {
      done = Math.min(total, done + 1);
      event('running', null);
      if (done >= total) finish('completed');
    }, RECHECK_STEP_MS);
  };

  const asked = (body: unknown): RecheckAsk | ProblemBody => {
    const ask = (body ?? {}) as RecheckAsk;
    if (!ask.checkId && !ask.urlIds) {
      return {
        type: 'invalid-query',
        title: 'Name a check or some URLs.',
        status: 422,
        errors: { checkId: ['Name a check or some URLs.'] },
      };
    }
    if (ask.urlIds && (ask.urlIds.length === 0 || ask.urlIds.length > 1_000)) {
      return {
        type: 'invalid-query',
        title: 'Give 1 to 1,000 URLs.',
        status: 422,
        errors: { urlIds: ['Give 1 to 1,000 URLs.'] },
      };
    }
    return ask;
  };

  const isProblem = (value: unknown): value is ProblemBody =>
    typeof value === 'object' && value !== null && 'type' in value && 'status' in value;

  const routes: FixLoopRoute[] = [
    [
      'GET',
      new RegExp(`${crawl}\\/rechecks$`),
      async ([, id = '']) => {
        if (!(await mock.item(id))) return mock.problem(notFound(id));
        return mock.json({ rechecks: rechecks.get(id) ?? [] });
      },
    ],
    [
      'GET',
      new RegExp(`${crawl}\\/rechecks\\/(\\d+)$`),
      async ([, id = '', number = '']) => {
        if (!(await mock.item(id))) return mock.problem(notFound(id));
        const record = (rechecks.get(id) ?? []).find((each) => each.id === Number(number));
        return record
          ? mock.json(record)
          : mock.problem({
              type: 'recheck-not-found',
              title: 'This crawl has no recheck with this number.',
              status: 404,
            });
      },
    ],
    [
      'POST',
      new RegExp(`${crawl}\\/rechecks\\/(\\d+)\\/rows$`),
      ([, id = '', number = ''], request) => {
        const record = (rechecks.get(id) ?? []).find((each) => each.id === Number(number));
        if (!record) {
          return Promise.resolve(
            mock.problem({
              type: 'recheck-not-found',
              title: 'This crawl has no recheck with this number.',
              status: 404,
            }),
          );
        }
        const query = (request.body ?? {}) as RecheckRowsQuery;
        const rows = playedRows.get(`${id}/${record.id}`) ?? [];
        const matching = rows.filter(
          (row) =>
            (!query.outcomes || (row.outcome !== null && query.outcomes.includes(row.outcome))) &&
            (!query.roles || query.roles.includes(row.role)),
        );
        const offset = query.offset ?? 0;
        return Promise.resolve(
          mock.json({ total: matching.length, offset, rows: matching.slice(offset, offset + (query.limit ?? 100)) }),
        );
      },
    ],
    [
      'POST',
      new RegExp(`${crawl}\\/rechecks\\/plan$`),
      async ([, id = ''], request) => {
        const item = await mock.item(id);
        if (!item) return mock.problem(notFound(id));
        const ask = asked(request.body);
        if (isProblem(ask)) return mock.problem(ask);
        const answer = await plan(item, ask);
        return isProblem(answer) ? mock.problem(answer) : mock.json(answer);
      },
    ],
    [
      'POST',
      new RegExp(`${crawl}\\/rechecks$`),
      async ([, id = ''], request) => {
        const item = await mock.item(id);
        if (!item) return mock.problem(notFound(id));
        const ask = asked(request.body);
        if (isProblem(ask)) return mock.problem(ask);
        const planned = await plan(item, ask);
        if (isProblem(planned)) return mock.problem(planned);
        if (planned.refused) {
          return mock.problem({ type: planned.refused.type, title: planned.refused.title, status: 409 });
        }
        const recheckId = Math.max(0, ...(rechecks.get(id) ?? []).map((each) => each.id)) + 1;
        const record: Recheck = {
          id: recheckId,
          status: 'running',
          startedAt: new Date().toISOString(),
          endedAt: null,
          settledAt: null,
          crawlingMs: null,
          checkId: planned.checkId,
          group: planned.group,
          urlList: planned.urlList,
          kinds: planned.kinds,
          judged: planned.judged,
          fetched: 0,
          countBefore: planned.countBefore,
          result: null,
        };
        rechecks.set(id, [record, ...(rechecks.get(id) ?? [])]);
        const rows = planned.checkId ? await mock.issueRows(id, planned.checkId) : [];
        play(item, planned, record, rows);
        return mock.json({ recheckId, ...planned }, 202);
      },
    ],
  ];

  return {
    routes,
    stop: (id) => {
      const played = playing.get(id);
      if (!played) return false;
      played.stop();
      return true;
    },
  };
}
