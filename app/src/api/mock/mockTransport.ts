// A sidecar in the page, for Storybook, the clickable prototype and the grids the engine doesn't serve yet. It
// answers the contract's routes from the real-crawl fixtures, keeps the library's changes in memory (remove, restore,
// delete, start), replays the garden crawl's real progress for a crawl started here, and streams events like the
// sidecar does. Nothing is made up beyond what a running crawl must show: its progress is the garden crawl's own.
import type { ProblemBody } from '../errors';
import type {
  CheckGrouping,
  FacetsQuery,
  GridPage,
  GridQuery,
  IssueGroup,
  IssueGroupPage,
  IssueRow,
  LinkFacetsQuery,
  SummaryState,
  LinkEnds,
  UrlGridQuery,
  UrlLookup,
} from '../grids';
import { LINK_KINDS, type LinkKind, type LinkPage, type LinksQuery } from '../inspector';
import type { Transport, TransportRequest } from '../transport';
import { SYNTHETIC_FAVICON, e6Routes, linkEndIds } from './e6';
import { sitemapsRobotsRoutes } from './sitemapsRobots';
import {
  isCrawling,
  type Analysis,
  type CrawlHistory,
  type CrawlListItem,
  type CrawlOptionDefaults,
  type CrawlOptions,
  type CrawlStatus,
  type HistoryPoint,
  type IssuePage,
  type ProgressEvent,
  type StartCrawlRequest,
} from '../types';
import {
  MissingFixtureError,
  adaptAnalysis,
  adaptChecks,
  adaptCrawlItem,
  adaptInspector,
  adaptLinks,
  adaptSnapshot,
  adaptSource,
  adaptStats,
  fixture,
  inspectorFixtures,
  linkFacts,
  optionalFixture,
  urlRows,
  type FixtureCrawl,
  type LiveSnapshot,
  type SavedChecks,
} from './fixtures';
import {
  answerFacets,
  answerGridQuery,
  decodePageCursor,
  encodePageCursor,
  pagingErrors,
  queryErrors,
  withPagingDefaults,
} from './gridQuery';
import { LINK_COLUMNS, answerLinkFacets, answerLinkQuery, linkRows } from './linkGrid';
import { URL_COLUMNS } from './urlColumns';

/** Every answer waits this long, so loading states behave as they will against the sidecar. */
const LATENCY_MS = 30;
/** The replay: a progress event every 500 ms, as the engine sends them, stepping this many seconds of the real crawl. */
const PROGRESS_INTERVAL_MS = 500;
const REPLAY_STEP = 15;
const KEEP_ALIVE_MS = 15_000;
/** A history has at most this many points, evenly spread, and the latest (docs/v2-sidecar-api.md, History). */
const HISTORY_POINTS = 600;
/** Each phase of an analysis here takes this long, so its progress can be seen. */
const ANALYSIS_PHASE_MS = 300;
const ANALYSIS_PHASES = ['reading', 'pageRules', 'siteRules', 'urlSummary', 'writing'] as const;

type Json = unknown;
type Handler = (match: RegExpMatchArray, request: TransportRequest) => Promise<Response>;

interface Simulation {
  crawlId: string;
  status: CrawlStatus;
  index: number;
  timer: ReturnType<typeof setInterval> | null;
  /** Crawled here, so its completion is a "Crawl finished". */
  startedAt: string;
}

const encoder = new TextEncoder();

function json(body: Json, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
}

function empty(status: 202 | 204): Response {
  return new Response(null, { status });
}

function problem(body: ProblemBody): Response {
  return new Response(JSON.stringify(body), {
    status: body.status,
    headers: { 'Content-Type': 'application/problem+json' },
  });
}

const missingFixtures = (path: string): ProblemBody => ({
  type: 'not-found',
  title: 'The sample data is missing. Regenerate the design fixtures to use the mock.',
  status: 404,
  detail: `${path} is not in app/design/data/fixtures. Run: node app/design/data/extract/run-all.mjs`,
});

const crawlNotFound = (id: string): ProblemBody => ({
  type: 'crawl-not-found',
  title: 'This crawl isn’t in the library any more.',
  status: 404,
  detail: `No crawl has the id ${id}.`,
});

const busy = (): ProblemBody => ({ type: 'crawl-busy', title: 'This crawl is running. Stop it first.', status: 409 });

const urlNotFound = (url: string): ProblemBody => ({
  type: 'url-not-found',
  title: 'This crawl didn’t find that URL.',
  status: 404,
  detail: url,
});

const invalidQuery = (errors: Record<string, string[]>): ProblemBody => {
  const detail = Object.values(errors)[0]?.[0];
  return {
    type: 'invalid-query',
    title: 'This view’s query can’t be run.',
    status: 422,
    ...(detail !== undefined ? { detail } : {}),
    errors,
  };
};

const badRequest = (title: string): ProblemBody => ({ type: 'bad-request', title, status: 400 });

/** A URL's id from a body: none is `invalid-query`; one no URL could have is `url-not-found`. */
function readUrlId(body: unknown): number | ProblemBody {
  if (typeof body !== 'object' || body === null) return badRequest('This call needs a JSON body.');
  const urlId = (body as { urlId?: unknown }).urlId;
  if (urlId === undefined || urlId === null) return invalidQuery({ urlId: ['Give the URL’s id.'] });
  if (typeof urlId !== 'number' || !Number.isInteger(urlId)) {
    return badRequest('The request body isn’t the JSON this call takes.');
  }
  if (urlId < 1 || urlId > 2_147_483_647) return urlNotFound(`URL ${urlId}`);
  return urlId;
}

/** A kind named twice, a null, or a name that isn't one, keyed by `kinds[n]`. A list that isn't one is `bad-request`. */
function kindErrors(kinds: unknown): { errors: Record<string, string[]> } | { problem: ProblemBody } | null {
  if (kinds === undefined) return null;
  if (!Array.isArray(kinds)) return { problem: badRequest('The request body isn’t the JSON this call takes.') };
  const errors: Record<string, string[]> = {};
  const seen = new Set<string>();
  kinds.forEach((name, index) => {
    if (typeof name !== 'string' || !LINK_KINDS.includes(name as LinkKind)) {
      const called = name == null ? '' : String(name);
      errors[`kinds[${index}]`] = [`There’s no kind of link called ‘${called}’: there are ${LINK_KINDS.join(', ')}.`];
    } else if (seen.has(name)) {
      errors[`kinds[${index}]`] = [`‘${name}’ is named twice: name each kind of link once.`];
    } else {
      seen.add(name);
    }
  });
  return Object.keys(errors).length > 0 ? { errors } : null;
}

const notAnalyzed = (): ProblemBody => ({
  type: 'not-analyzed',
  title: 'This crawl hasn’t been analysed yet.',
  status: 404,
});

const checkNotFound = (checkId: string): ProblemBody => ({
  type: 'check-not-found',
  title: `There’s no check called ‘${checkId}’.`,
  status: 404,
});

/** The design fixtures hold some things for a few URLs only: said plainly, never made up. */
const sampleLacks = (what: string): ProblemBody => ({
  type: 'not-found',
  title: `The sample data doesn’t have ${what}.`,
  status: 404,
  detail: 'The mock serves the design fixtures, which hold the inspector for a few URLs of the garden crawl.',
});

const siteHost = (hostname: string) => hostname.toLowerCase().replace(/^www\./, '');

/**
 * The URL the text stands for, then the same path on the site's other versions (www or not, http or https). Null when
 * the text isn't a URL. A path starting with `/` is taken on the start URL's origin.
 */
function lookupCandidates(text: string, startUrl: string | null): { requested: string; candidates: string[] } | null {
  const trimmed = text.trim();
  if (trimmed === '') return null;
  let origin: URL | null = null;
  if (startUrl) {
    try {
      origin = new URL(startUrl);
    } catch {
      origin = null;
    }
  }
  const scheme = origin?.protocol === 'http:' ? 'http:' : 'https:';
  let candidate = trimmed;
  if (candidate.startsWith('/') && !candidate.startsWith('//')) {
    if (!origin) return null;
    try {
      const resolved = new URL(candidate, origin);
      resolved.hash = '';
      if (resolved.protocol !== 'http:' && resolved.protocol !== 'https:') return null;
      return { requested: resolved.href, candidates: siteVersions(resolved, origin) };
    } catch {
      return null;
    }
  }
  if (candidate.startsWith('//')) candidate = `${scheme}${candidate}`;
  else if (!/^[A-Za-z][A-Za-z0-9+.-]*:(?![0-9])/.test(candidate)) candidate = `${scheme}//${candidate}`;
  let asked: URL;
  try {
    asked = new URL(candidate);
  } catch {
    return null;
  }
  if (asked.protocol !== 'http:' && asked.protocol !== 'https:') return null;
  asked.hash = '';
  const requested = asked.href;
  const versions = origin && siteHost(asked.hostname) === siteHost(origin.hostname) ? siteVersions(asked, origin) : [];
  return { requested, candidates: [requested, ...versions.filter((url) => url !== requested)] };
}

/** The same path and query on the start URL's origin and its other versions, the origin itself first. */
function siteVersions(asked: URL, start: URL): string[] {
  const root = siteHost(start.hostname);
  const hosts = root.includes('.') && !/^\d/.test(root) ? [start.hostname, root, `www.${root}`] : [start.hostname];
  const schemes = [start.protocol, start.protocol === 'https:' ? 'http:' : 'https:'];
  const urls: string[] = [];
  for (const protocol of schemes) {
    for (const hostname of hosts) {
      const copy = new URL(asked.href);
      copy.protocol = protocol;
      copy.hostname = hostname;
      copy.hash = '';
      urls.push(copy.href);
    }
  }
  return [...new Set(urls)];
}

/** A share of a check's URLs, with at least this many, is a likely template fault: one fix clears it. */
const TEMPLATE_SHARE = 0.5;
const TEMPLATE_MIN_PAGES = 10;

/** A check's issues grouped by their cause (E3a, Issue groups), largest first. */
export function issueGroups(
  checkId: string,
  grouping: CheckGrouping,
  rows: readonly IssueRow[] | null,
  offset: number,
  limit: number,
): IssueGroupPage {
  if (rows === null) return { checkId, grouping, total: null, groupCount: null, offset, groups: [] };
  const total = rows.length;
  if (grouping === 'none') return { checkId, grouping, total, groupCount: 0, offset, groups: [] };
  const groups = new Map<string, IssueGroup>();
  for (const row of rows) {
    const detail = grouping === 'relatedUrl' ? null : (row.detail ?? null);
    const key = JSON.stringify([detail, row.relatedUrlId ?? null]);
    const group = groups.get(key) ?? {
      detail,
      relatedUrlId: row.relatedUrlId ?? null,
      relatedUrl: row.relatedUrl ?? null,
      pages: 0,
      share: 0,
      likelyTemplate: false,
    };
    group.pages++;
    groups.set(key, group);
  }
  const all = [...groups.values()]
    .map((group) => {
      const share = total > 0 ? group.pages / total : 0;
      // A detail that says how the URL answered is no evidence of a template: "404 Not Found", or "HTTP 404" as an
      // analysis from before the score wrote it (and as a code with no standard name still reads).
      const evidence = group.relatedUrlId !== null || !/^(HTTP )?\d{3}\b/.test(group.detail ?? '');
      return {
        ...group,
        share,
        likelyTemplate: evidence && share >= TEMPLATE_SHARE && group.pages >= TEMPLATE_MIN_PAGES,
      };
    })
    .sort(
      (a, b) =>
        b.pages - a.pages ||
        (a.detail ?? '').localeCompare(b.detail ?? '') ||
        (a.relatedUrlId ?? 0) - (b.relatedUrlId ?? 0),
    );
  return { checkId, grouping, total, groupCount: all.length, offset, groups: all.slice(offset, offset + limit) };
}

function hexId(): string {
  return Array.from(crypto.getRandomValues(new Uint8Array(8)), (byte) => byte.toString(16).padStart(2, '0')).join('');
}

function siteOf(url: string): string {
  try {
    return new URL(url).hostname.replace(/^www\./, '');
  } catch {
    return url;
  }
}

export function createMockTransport(): Transport {
  const items = new Map<string, CrawlListItem>();
  const removed = new Map<string, CrawlListItem>();
  const folders = new Map<string, FixtureCrawl>();
  const simulations = new Map<string, Simulation>();
  const subscribers = new Set<(event: string, data: Json) => void>();
  let snapshots: LiveSnapshot[] | null = null;
  let replayMaxUrls: number | null = null;
  let loaded: Promise<void> | null = null;
  /** The analyses running, each with what cancels it. */
  const analyses = new Map<string, () => void>();

  const emit = (event: string, data: Json) => subscribers.forEach((send) => send(event, data));

  const load = () =>
    (loaded ??= (async () => {
      const library = await fixture<Record<string, unknown>[]>('api/crawls.json');
      for (const raw of library) {
        const item = adaptCrawlItem(raw);
        items.set(item.id, item);
      }
      for (const folder of ['garden', 'synthetic'] as const) {
        const crawl = await optionalFixture<Record<string, unknown>>(`api/${folder}/crawl.json`);
        if (crawl && typeof crawl.id === 'string') folders.set(crawl.id, folder);
      }

      // A crawl of the synthetic site keeps its icon (E6): the one that site serves at /favicon.ico.
      for (const [id, folder] of folders) {
        const item = items.get(id);
        if (item) items.set(id, { ...item, hasFavicon: folder === 'synthetic' });
      }
    })());

  const folderOf = (id: string): FixtureCrawl => folders.get(id) ?? 'garden';

  const liveSnapshots = async () => {
    if (!snapshots) {
      const replay = await fixture<{ snapshots: Record<string, unknown>[]; options?: { maxUrls?: unknown } }>(
        'garden/live.json',
      );
      snapshots = replay.snapshots.map(adaptSnapshot);
      replayMaxUrls = typeof replay.options?.maxUrls === 'number' ? replay.options.maxUrls : null;
    }
    return snapshots;
  };

  const setStatus = (id: string, status: CrawlStatus, extra: Partial<CrawlListItem> = {}) => {
    const item = items.get(id);
    if (!item) return;
    items.set(id, { ...item, ...extra, status, updatedAt: new Date().toISOString() });
    emit('crawl', { crawlId: id, status });
  };

  /**
   * A progress event from the replay, with E4's part. The fixture doesn't tell the site's own URLs from the rest (40
   * of the garden crawl's 4,521 are other sites'), so every URL the crawl means to fetch counts as the site's.
   */
  const progressOf = (simulation: Simulation, snapshot: LiveSnapshot, last: boolean): ProgressEvent => {
    const { pages: _pages, ...counts } = snapshot;
    return {
      crawlId: simulation.crawlId,
      status: simulation.status,
      ...counts,
      at: new Date(Date.parse(simulation.startedAt) + snapshot.elapsedMs).toISOString(),
      phase: last ? 'finishing' : snapshot.fetched === 0 ? 'starting' : 'crawling',
      internalFound: snapshot.discovered - snapshot.skipped,
      internalFetched: snapshot.fetched,
      maxUrls: replayMaxUrls,
      waits: [],
      moreWaits: {},
    };
  };

  const historyPoint = (startedAt: string, snapshot: LiveSnapshot): HistoryPoint => ({
    at: new Date(Date.parse(startedAt) + snapshot.elapsedMs).toISOString(),
    fetched: snapshot.fetched,
    htmlPages: snapshot.htmlPages,
    internalFetched: snapshot.fetched,
    internalFound: snapshot.discovered - snapshot.skipped,
    errors: snapshot.errors,
    blocked: snapshot.blocked,
    // As the sidecar's own live points: the time spent in the run they belong to.
    elapsedMs: snapshot.elapsedMs,
  });

  /** The replay as far as `end`, as a history: at most HISTORY_POINTS evenly spread, and the latest. */
  const historyOf = (startedAt: string, all: readonly LiveSnapshot[], end: number): HistoryPoint[] => {
    const stride = Math.max(1, Math.ceil((end + 1) / HISTORY_POINTS));
    const points: HistoryPoint[] = [];
    for (let index = 0; index <= end; index += stride) {
      const snapshot = all[index];
      if (snapshot) points.push(historyPoint(startedAt, snapshot));
    }
    const latest = all[end];
    if (latest && end % stride !== 0) points.push(historyPoint(startedAt, latest));
    return points;
  };

  /**
   * An analysis: its phases as E4's `analysis` events, then `done`. Cancelled, nothing it did is kept and the crawl is
   * what it was before.
   */
  const analyse = (id: string, done: () => void) => {
    const before = items.get(id);
    const pages = Math.max(1, before?.pageCount ?? 1);
    let step = 0;
    let timer: ReturnType<typeof setTimeout>;
    setStatus(id, 'analyzing');
    const next = () => {
      const phase = ANALYSIS_PHASES[Math.floor(step / 2)];
      if (!phase) {
        analyses.delete(id);
        done();
        emit('library', {});
        return;
      }
      const total = phase === 'writing' ? 1 : phase === 'siteRules' ? 9 : pages;
      const starting = step % 2 === 0;
      emit('analysis', { crawlId: id, phase, done: starting ? 0 : total, total });
      step++;
      timer = setTimeout(next, starting ? ANALYSIS_PHASE_MS : 0);
    };
    timer = setTimeout(next, 0);
    analyses.set(id, () => {
      clearTimeout(timer);
      analyses.delete(id);
      if (before) setStatus(id, before.status);
      emit('library', {});
    });
  };

  const finish = (simulation: Simulation) => {
    if (simulation.timer) clearInterval(simulation.timer);
    simulation.timer = null;
    simulations.delete(simulation.crawlId);
    setStatus(simulation.crawlId, 'completed', { finishedAt: new Date().toISOString() });
    analyse(simulation.crawlId, () =>
      setStatus(simulation.crawlId, 'completed', {
        analyzedAt: new Date().toISOString(),
        issues: { error: 2, warning: 6, notice: 11 },
      }),
    );
  };

  const tick = async (simulation: Simulation) => {
    const all = await liveSnapshots();
    if (simulation.status === 'running') simulation.index = Math.min(all.length - 1, simulation.index + REPLAY_STEP);
    const snapshot = all[simulation.index];
    if (!snapshot) return;
    const item = items.get(simulation.crawlId);
    if (item) items.set(simulation.crawlId, { ...item, fetchedUrlCount: snapshot.fetched, pageCount: snapshot.pages });
    const last = simulation.status === 'running' && simulation.index === all.length - 1;
    emit('progress', progressOf(simulation, snapshot, last));
    if (last) finish(simulation);
  };

  const run = (simulation: Simulation) => {
    simulations.set(simulation.crawlId, simulation);
    simulation.timer ??= setInterval(() => void tick(simulation), PROGRESS_INTERVAL_MS);
  };

  const live = (id: string) => simulations.get(id);

  const routes: [method: TransportRequest['method'], pattern: RegExp, handler: Handler][] = [
    ['GET', /^\/api\/health$/, async () => json(await fixture('api/health.json'))],
    ['POST', /^\/api\/shutdown$/, () => Promise.resolve(empty(202))],
    ['GET', /^\/api\/checks$/, async () => json(adaptChecks(await fixture<SavedChecks>('api/checks.json')))],
    ['GET', /^\/api\/crawl-options\/defaults$/, async () => json(await fixture('api/crawl-options-defaults.json'))],
    [
      'PUT',
      /^\/api\/crawl-options\/defaults$/,
      async (_match, request) => {
        const defaults = await fixture<CrawlOptionDefaults>('api/crawl-options-defaults.json');
        const options = { ...defaults.options, ...(request.body as CrawlOptions), startUrl: null };
        return json({ ...defaults, options });
      },
    ],
    [
      'POST',
      /^\/api\/crawl-options\/validate$/,
      (_match, request) => Promise.resolve(json({ errors: validate(request.body as CrawlOptions) })),
    ],
    [
      'GET',
      /^\/api\/crawls$/,
      async () => {
        await load();
        return json([...items.values()].sort((a, b) => (b.startedAt ?? '').localeCompare(a.startedAt ?? '')));
      },
    ],
    [
      'POST',
      /^\/api\/crawls$/,
      async (_match, request) => {
        await load();
        const body = request.body as StartCrawlRequest | undefined;
        const startUrl = body?.options.startUrl ?? '';
        const errors = validate({ ...body?.options, startUrl });
        if (Object.keys(errors).length > 0) {
          return problem({ type: 'invalid-options', title: 'Some settings need changing.', status: 422, errors });
        }
        const id = hexId();
        const now = new Date().toISOString();
        const site = siteOf(startUrl);
        const item: CrawlListItem = {
          id,
          path: `C:\\Users\\mock\\Documents\\Shouting Iguana\\${site}-mock.sicrawl`,
          fileName: `${site}-mock.sicrawl`,
          site,
          startUrl,
          status: 'running',
          startedAt: now,
          finishedAt: null,
          updatedAt: now,
          fetchedUrlCount: 0,
          pageCount: 0,
          issues: null,
          analyzedAt: null,
          outOfDate: false,
          sizeBytes: 0,
          missing: false,
          error: null,
        };
        items.set(id, item);
        folders.set(id, 'garden');
        run({ crawlId: id, status: 'running', index: 0, timer: null, startedAt: now });
        emit('crawl', { crawlId: id, status: 'running' });
        emit('library', {});
        return json(item, 201);
      },
    ],
    [
      'POST',
      /^\/api\/crawls\/open$/,
      () =>
        Promise.resolve(
          problem({ type: 'file-error', title: 'The sample data can’t open crawl files from disk.', status: 400 }),
        ),
    ],
    [
      'GET',
      /^\/api\/crawls\/([0-9a-f]{16})$/,
      async ([, id = '']) => {
        await load();
        const item = items.get(id);
        return item ? json(item) : problem(crawlNotFound(id));
      },
    ],
    [
      'DELETE',
      /^\/api\/crawls\/([0-9a-f]{16})$/,
      async ([, id = '']) => {
        await load();
        const item = items.get(id);
        if (!item) return problem(crawlNotFound(id));
        if (live(id)) return problem(busy());
        items.delete(id);
        removed.set(id, item);
        emit('library', {});
        return empty(204);
      },
    ],
    [
      'POST',
      /^\/api\/crawls\/([0-9a-f]{16})\/restore$/,
      async ([, id = '']) => {
        await load();
        const item = removed.get(id);
        if (!item) return problem(crawlNotFound(id));
        removed.delete(id);
        items.set(id, item);
        emit('library', {});
        return json(item);
      },
    ],
    [
      'POST',
      /^\/api\/crawls\/([0-9a-f]{16})\/delete-file$/,
      async ([, id = '']) => {
        await load();
        if (!items.has(id)) return problem(crawlNotFound(id));
        if (live(id)) return problem(busy());
        items.delete(id);
        emit('library', {});
        return empty(204);
      },
    ],
    [
      'POST',
      /^\/api\/crawls\/([0-9a-f]{16})\/(pause|resume|stop)$/,
      ([, id = '', action]) => {
        const simulation = live(id);
        if (!simulation) {
          return Promise.resolve(problem({ type: 'crawl-busy', title: 'This crawl isn’t running here.', status: 409 }));
        }
        if (action === 'stop') {
          simulation.status = 'stopping';
          setStatus(id, 'stopping');
          setTimeout(() => {
            if (simulation.timer) clearInterval(simulation.timer);
            simulations.delete(id);
            setStatus(id, 'stopped');
            emit('library', {});
          }, 600);
          return Promise.resolve(empty(202));
        }
        simulation.status = action === 'pause' ? 'paused' : 'running';
        setStatus(id, simulation.status);
        return Promise.resolve(empty(204));
      },
    ],
    [
      'POST',
      /^\/api\/crawls\/([0-9a-f]{16})\/continue$/,
      async ([, id = '']) => {
        await load();
        const item = items.get(id);
        if (!item) return problem(crawlNotFound(id));
        if (live(id) || !['stopped', 'interrupted'].includes(item.status)) {
          return problem({ type: 'crawl-busy', title: 'Only a stopped crawl can be continued.', status: 409 });
        }
        const all = await liveSnapshots();
        const index = Math.max(
          0,
          all.findIndex((snapshot) => snapshot.fetched >= item.fetchedUrlCount),
        );
        run({ crawlId: id, status: 'running', index, timer: null, startedAt: new Date().toISOString() });
        setStatus(id, 'running');
        return json(items.get(id));
      },
    ],
    [
      'GET',
      /^\/api\/crawls\/([0-9a-f]{16})\/options$/,
      async ([, id = '']) => {
        await load();
        const item = items.get(id);
        if (!item) return problem(crawlNotFound(id));
        const stored = await optionalFixture<CrawlOptions>(`api/${folderOf(id)}/options.json`);
        return json({
          ...(stored ?? (await fixture<CrawlOptionDefaults>('api/crawl-options-defaults.json')).options),
          startUrl: item.startUrl,
        });
      },
    ],
    ['GET', /^\/api\/crawls\/([0-9a-f]{16})\/alerts$/, () => Promise.resolve(json([]))],
    [
      'GET',
      /^\/api\/crawls\/([0-9a-f]{16})\/history$/,
      async ([, id = '']) => {
        await load();
        const item = items.get(id);
        if (!item) return problem(crawlNotFound(id));
        const startedAt = item.startedAt ?? new Date().toISOString();
        const simulation = live(id);
        let points: HistoryPoint[];
        if (simulation) {
          points = historyOf(simulation.startedAt, await liveSnapshots(), simulation.index);
        } else if (folderOf(id) === 'garden') {
          // As far as the crawl got: one stopped here ends where its replay did.
          const all = await liveSnapshots();
          const reached = all.findIndex((snapshot) => snapshot.fetched >= item.fetchedUrlCount);
          points = historyOf(startedAt, all, reached < 0 ? all.length - 1 : reached);
        } else {
          // No replay of this crawl was kept: where it started and where it ended.
          points = [
            { at: startedAt, fetched: 0, htmlPages: 0, internalFetched: 0, internalFound: 0, errors: 0, blocked: 0 },
            {
              at: item.finishedAt ?? item.updatedAt ?? startedAt,
              fetched: item.fetchedUrlCount,
              htmlPages: item.pageCount,
              internalFetched: item.fetchedUrlCount,
              internalFound: item.fetchedUrlCount,
              errors: 0,
              blocked: 0,
            },
          ];
        }
        return json({
          crawlId: id,
          startedAt: simulation?.startedAt ?? startedAt,
          liveFrom: simulation?.startedAt ?? null,
          points,
        } satisfies CrawlHistory);
      },
    ],
    [
      'POST',
      /^\/api\/crawls\/([0-9a-f]{16})\/analyze$/,
      async ([, id = '']) => {
        await load();
        const item = items.get(id);
        if (!item) return problem(crawlNotFound(id));
        if (live(id))
          return problem({ type: 'not-analysable', title: 'A crawl can’t be analysed while it runs.', status: 409 });
        if (analyses.has(id))
          return problem({ type: 'crawl-busy', title: 'This crawl is being analysed.', status: 409 });
        const status = item.status;
        analyse(id, () => setStatus(id, status, { analyzedAt: new Date().toISOString(), outOfDate: false }));
        return empty(202);
      },
    ],
    [
      'POST',
      /^\/api\/crawls\/([0-9a-f]{16})\/analyze\/cancel$/,
      ([, id = '']) => {
        analyses.get(id)?.();
        return Promise.resolve(empty(202));
      },
    ],
    [
      'GET',
      /^\/api\/crawls\/([0-9a-f]{16})\/analysis$/,
      async ([, id = '']) => {
        await load();
        const item = items.get(id);
        if (!item) return problem(crawlNotFound(id));
        if (item.analyzedAt === null) {
          return problem({ type: 'not-analyzed', title: 'This crawl hasn’t been analysed yet.', status: 404 });
        }
        return json(adaptAnalysis(await fixture(`api/${folderOf(id)}/analysis.json`)));
      },
    ],
    [
      'GET',
      /^\/api\/crawls\/([0-9a-f]{16})\/stats$/,
      async ([, id = '']) => {
        await load();
        if (!items.has(id)) return problem(crawlNotFound(id));
        return json(adaptStats(await fixture(`api/${folderOf(id)}/stats.json`)));
      },
    ],
    [
      'GET',
      /^\/api\/crawls\/([0-9a-f]{16})\/issues\/([a-z0-9-]+)(?:\?(.*))?$/,
      async ([, id = '', checkId = '', query = '']) => {
        await load();
        if (!items.has(id)) return problem(crawlNotFound(id));
        const params = new URLSearchParams(query);
        const limit = Math.min(1_000, Math.max(1, Number(params.get('limit') ?? 200)));
        const offset = Math.max(0, Number(params.get('offset') ?? 0));
        const analysis = adaptAnalysis(await fixture<Record<string, unknown>>(`api/${folderOf(id)}/analysis.json`));
        return json(await issuesPage(analysis, folderOf(id), checkId, offset, limit));
      },
    ],
    ['GET', /^\/api\/url-columns$/, () => Promise.resolve(json(URL_COLUMNS))],
    [
      'POST',
      /^\/api\/crawls\/([0-9a-f]{16})\/urls$/,
      async ([, id = ''], request) => {
        const item = await itemOrNull(id);
        if (!item) return problem(crawlNotFound(id));
        const { linkEnds, ...body } = (request.body ?? {}) as UrlGridQuery;
        if (body.group !== undefined) {
          return problem(badRequest('Only a check’s rows take a group: filter by the check, with its group, instead.'));
        }
        const rows = await narrowed(id, item, await crawlRows(id), linkEnds);
        if (rows instanceof Response) return rows;
        const answer = answerGridQuery(rows, body, summaryOf(item));
        return answer.ok ? json(answer.page) : problem(answer.problem);
      },
    ],
    [
      'POST',
      /^\/api\/crawls\/([0-9a-f]{16})\/urls\/facets$/,
      async ([, id = ''], request) => {
        const item = await itemOrNull(id);
        if (!item) return problem(crawlNotFound(id));
        const { linkEnds, ...query }: FacetsQuery = (request.body as FacetsQuery | undefined) ?? {};
        const rows = await narrowed(id, item, await crawlRows(id), linkEnds);
        if (rows instanceof Response) return rows;
        const answer = answerFacets(rows, query, summaryOf(item));
        return answer.ok ? json(answer.page) : problem(answer.problem);
      },
    ],
    [
      'POST',
      /^\/api\/crawls\/([0-9a-f]{16})\/urls\/lookup$/,
      async ([, id = ''], request) => {
        const item = await itemOrNull(id);
        if (!item) return problem(crawlNotFound(id));
        const url = (request.body as { url?: unknown } | undefined)?.url;
        if (typeof url !== 'string' || url.trim() === '') {
          return problem(invalidQuery({ url: ['Give a URL, or a path that starts with /.'] }));
        }
        const looked = lookupCandidates(url, item.startUrl);
        if (looked === null) {
          return problem(
            invalidQuery({
              url: ['This isn’t a URL: give one, with or without http:// or https://, or a path that starts with /.'],
            }),
          );
        }
        const rows = await crawlRows(id);
        const row = looked.candidates
          .map((candidate) => rows.find((item) => item.url === candidate))
          .find((found) => found !== undefined);
        if (!row?.url) return problem(urlNotFound(looked.requested));
        return json({
          id: row.id,
          url: row.url,
          requested: row.url === looked.requested ? null : looked.requested,
        } satisfies UrlLookup);
      },
    ],
    [
      'POST',
      /^\/api\/crawls\/([0-9a-f]{16})\/urls\/inspect$/,
      async ([, id = ''], request) => {
        const urlId = readUrlId(request.body);
        if (typeof urlId !== 'number') return problem(urlId);
        const found = await inspected(id, urlId);
        if (found instanceof Response) return found;
        const rows = await crawlRows(id);
        const idOf = (url: string) => rows.find((row) => row.url === url)?.id ?? null;
        return json(adaptInspector(found.raw, idOf));
      },
    ],
    [
      'POST',
      /^\/api\/crawls\/([0-9a-f]{16})\/urls\/(inlinks|outlinks)$/,
      async ([, id = '', direction = 'inlinks'], request) => {
        const urlId = readUrlId(request.body);
        if (typeof urlId !== 'number') return problem(urlId);
        const query = (request.body ?? {}) as LinksQuery & { urlId?: number };
        const kinds = kindErrors(query.kinds);
        if (kinds && 'problem' in kinds) return problem(kinds.problem);
        if (kinds) return problem(invalidQuery(kinds.errors));
        const offset = query.offset ?? 0;
        const limit = query.limit ?? 100;
        const paging = pagingErrors(offset, limit, query.after);
        if (Object.keys(paging).length > 0) return problem(invalidQuery(paging));
        const found = await inspected(id, urlId);
        if (found instanceof Response) return found;
        const way = direction === 'outlinks' ? 'outlinks' : 'inlinks';
        const selected = [...(query.kinds ?? [])].sort();
        const all = adaptLinks(found.raw, way).filter(
          (link) => selected.length === 0 || (link.kind !== null && selected.includes(link.kind)),
        );
        const scope = `${way}|${urlId}|${selected.join(',')}`;
        let start = offset;
        if (query.after !== undefined) {
          const cursor = decodePageCursor(query.after);
          const same = decodePageCursor(encodePageCursor(scope, 0, ''));
          if (cursor === null || same === null) {
            return problem(invalidQuery({ after: ['This isn’t a cursor a page of this grid gave.'] }));
          }
          if (cursor.h !== same.h) {
            return problem(
              invalidQuery({ after: ['This cursor belongs to another query: start from an offset again.'] }),
            );
          }
          if (cursor.i < 0 || cursor.i >= all.length) {
            return problem(
              invalidQuery({
                after: ['The row this cursor stopped at isn’t in the crawl any more: start from an offset again.'],
              }),
            );
          }
          start = cursor.i + 1;
        }
        const rows = all.slice(start, start + limit);
        const more = start + rows.length < all.length;
        return json({
          rows,
          offset: query.after !== undefined ? null : offset,
          total: all.length,
          next: more && rows.length > 0 ? encodePageCursor(scope, start + rows.length - 1, '') : null,
        } satisfies LinkPage);
      },
    ],
    // ---- Links (E3b): the links grid's columns, a page of links, and the links counted by value.
    ['GET', /^\/api\/link-columns$/, () => Promise.resolve(json(LINK_COLUMNS))],
    [
      'POST',
      /^\/api\/crawls\/([0-9a-f]{16})\/links$/,
      async ([, id = ''], request) => {
        const item = await itemOrNull(id);
        if (!item) return problem(crawlNotFound(id));
        const body = (request.body ?? {}) as GridQuery;
        if (body.group !== undefined) return problem(badRequest('Only a check’s rows take a group.'));
        const links = await crawlLinks(id);
        if (!links) return problem(sampleLacks('this crawl’s links'));
        const answer = answerLinkQuery(links, body, summaryOf(item));
        return answer.ok ? json(answer.page) : problem(answer.problem);
      },
    ],
    [
      'POST',
      /^\/api\/crawls\/([0-9a-f]{16})\/links\/facets$/,
      async ([, id = ''], request) => {
        const item = await itemOrNull(id);
        if (!item) return problem(crawlNotFound(id));
        const links = await crawlLinks(id);
        if (!links) return problem(sampleLacks('this crawl’s links'));
        const answer = answerLinkFacets(links, (request.body as LinkFacetsQuery | undefined) ?? {}, summaryOf(item));
        return answer.ok ? json(answer.page) : problem(answer.problem);
      },
    ],
    [
      'POST',
      /^\/api\/crawls\/([0-9a-f]{16})\/urls\/source$/,
      async ([, id = ''], request) => {
        const urlId = readUrlId(request.body);
        if (typeof urlId !== 'number') return problem(urlId);
        const found = await inspected(id, urlId);
        if (found instanceof Response) return found;
        const source = adaptSource(found.raw);
        return source ? json(source) : problem(sampleLacks('the HTML of this URL'));
      },
    ],
    [
      'POST',
      /^\/api\/crawls\/([0-9a-f]{16})\/issues\/([a-z0-9-]+)\/(groups|rows)$/,
      async ([, id = '', checkId = '', what = 'groups'], request) => {
        const item = await itemOrNull(id);
        if (!item) return problem(crawlNotFound(id));
        if (item.analyzedAt === null) return problem(notAnalyzed());
        const analysis = adaptAnalysis(await fixture<Record<string, unknown>>(`api/${folderOf(id)}/analysis.json`));
        const catalog = adaptChecks(await fixture<SavedChecks>('api/checks.json'));
        const check = catalog.checks.find((candidate) => candidate.id === checkId);
        const skipped = analysis.skipped.some((skip) => skip.checkId === checkId);
        const counted = analysis.counts.some((count) => count.checkId === checkId);
        if (!check && !skipped && !counted) return problem(checkNotFound(checkId));
        const grouping: CheckGrouping = check?.grouping ?? 'none';
        const issueRows = skipped ? [] : await joinedIssueRows(id, checkId);
        if (what === 'groups') {
          const body = (request.body ?? {}) as { offset?: unknown; limit?: unknown };
          if (
            (body.offset != null && typeof body.offset !== 'number') ||
            (body.limit != null && typeof body.limit !== 'number')
          ) {
            return problem(badRequest('The request body isn’t the JSON this call takes.'));
          }
          const offset = typeof body.offset === 'number' ? body.offset : 0;
          const limit = typeof body.limit === 'number' ? body.limit : 100;
          const paging = pagingErrors(offset, limit);
          if (Object.keys(paging).length > 0) return problem(invalidQuery(paging));
          return json(issueGroups(checkId, grouping, skipped ? null : issueRows, offset, limit));
        }
        const { linkEnds, ...asked } = (request.body ?? {}) as UrlGridQuery;
        const query = withPagingDefaults(asked);
        if (query.columns === undefined || query.columns.length === 0) query.columns = ['url', 'detail', 'relatedUrl'];
        const errors = queryErrors(query, true);
        if (grouping === 'none' && query.group !== undefined) {
          const message = 'This check lists its issues without groups, so there’s no group to narrow them to.';
          const existing = Object.hasOwn(errors, 'group') ? errors.group : undefined;
          if (existing === undefined) errors.group = [message];
          else existing.push(message);
        }
        if (Object.keys(errors).length > 0) return problem(invalidQuery(errors));
        if (skipped) {
          return json({
            columns: ['id'],
            rows: [],
            offset: query.offset ?? 0,
            matching: null,
            total: null,
            next: null,
            summary: summaryOf(item),
            unavailable: [],
          } satisfies GridPage<IssueRow>);
        }
        const inGroup = query.group
          ? issueRows.filter(
              (row) =>
                (grouping === 'relatedUrl' || row.detail === query.group?.detail) &&
                row.relatedUrlId === query.group?.relatedUrlId,
            )
          : issueRows;
        const ends = await narrowed(id, item, inGroup, linkEnds);
        if (ends instanceof Response) return ends;
        const answer = answerGridQuery(ends, query, summaryOf(item), { issueRows: true });
        return answer.ok ? json(answer.page) : problem(answer.problem);
      },
    ],
    // Sitemaps and robots.txt (E3b): their own file.
    ...sitemapsRobotsRoutes({
      json,
      problem,
      item: itemOrNull,
      notFound: crawlNotFound,
      folder: folderOf,
      rows: crawlRows,
      summary: summaryOf,
      live: (id) => live(id) !== undefined,
      narrow: narrowed,
    }),
    // What E6 added: search, a links view's ends, the site's icon and the analysis's limits.
    ...e6Routes({
      json,
      problem,
      item: itemOrNull,
      notFound: crawlNotFound,
      folder: folderOf,
      rows: crawlRows,
      summary: summaryOf,
      live: (id) => live(id) !== undefined,
      narrow: narrowed,
      links: crawlLinks,
      icon: (id) => (folderOf(id) === 'synthetic' ? SYNTHETIC_FAVICON : null),
    }),
  ];

  /**
   * A URL grid's rows narrowed to the ends of a links view (E6's `linkEnds`), or the problem the sidecar gives for one
   * it can't run; the rows as they are without one.
   */
  async function narrowed<Row extends { id: number }>(
    id: string,
    item: CrawlListItem,
    rows: Row[],
    linkEnds: LinkEnds | undefined,
  ): Promise<Row[] | Response> {
    if (linkEnds === undefined) return rows;
    const links = await crawlLinks(id);
    if (!links) return problem(sampleLacks('this crawl’s links'));
    const ends = linkEndIds(links, linkEnds, summaryOf(item));
    if (ends === null) return rows;
    return ends.ok ? rows.filter((row) => ends.page.has(row.id)) : problem(ends.problem);
  }

  async function itemOrNull(id: string) {
    await load();
    return items.get(id) ?? null;
  }

  // ---- Links (E3b). The links grid, over the links the inspector fixtures hold: the garden crawl's alone.

  /**
   * A crawl's links as the grid's rows, each end joined to its URL as the crawl has it now (while a crawl started
   * here runs, only the links between the URLs its replay has reached), or null for a crawl the fixtures hold no
   * links of.
   */
  async function crawlLinks(id: string) {
    if (folderOf(id) !== 'garden') return null;
    return linkRows(await linkFacts(), await crawlRows(id));
  }

  /**
   * Whether the URL summary describes the crawl, as the sidecar decides it: not while it runs, not before its first
   * analysis, and not once it was continued after one.
   */
  function summaryOf(item: CrawlListItem): SummaryState {
    if (live(item.id) || isCrawling(item.status)) return { available: false, reason: 'crawling' };
    if (item.analyzedAt === null) return { available: false, reason: 'notAnalyzed' };
    if (item.status === 'interrupted') return { available: false, reason: 'crawlChanged' };
    return { available: true, reason: null };
  }

  /** The crawl's URLs: while a crawl started here runs, only those its replay has reached, so the grid grows live. */
  async function crawlRows(id: string) {
    const rows = await urlRows(folderOf(id));
    const simulation = live(id);
    if (!simulation) return rows;
    const reached = (await liveSnapshots())[simulation.index]?.fetched ?? 0;
    return rows.slice(0, reached);
  }

  /** The URL for the inspector: its design fixture, or the problem the sidecar (or the sample data) would give. */
  async function inspected(id: string, urlId: number): Promise<{ raw: Record<string, unknown> } | Response> {
    const item = await itemOrNull(id);
    if (!item) return problem(crawlNotFound(id));
    if (!(await crawlRows(id)).some((row) => row.id === urlId)) return problem(urlNotFound(`URL ${urlId}`));
    const raw = (await inspectorFixtures()).get(urlId);
    return raw ? { raw } : problem(sampleLacks(`the inspector for URL ${urlId}`));
  }

  /** The issue rows of a check, each joined to its URL's row: what `/rows` filters and sorts. */
  async function joinedIssueRows(id: string, checkId: string): Promise<IssueRow[]> {
    const rows = await crawlRows(id);
    const byUrl = new Map(rows.map((row) => [row.url, row]));
    const file = await optionalFixture<IssuePage>(`api/${folderOf(id)}/issues/${checkId}.json`);
    return (file?.rows ?? []).flatMap((issue) => {
      const row = byUrl.get(issue.url);
      if (!row) return [];
      const related = issue.relatedUrl === null ? undefined : byUrl.get(issue.relatedUrl);
      return [
        {
          ...row,
          detail: issue.detail,
          relatedUrl: issue.relatedUrl,
          relatedUrlId: related?.id ?? null,
          relatedStatusCode: related?.statusCode ?? null,
        },
      ];
    });
  }

  async function issuesPage(analysis: Analysis, folder: FixtureCrawl, checkId: string, offset: number, limit: number) {
    if (analysis.skipped.some((skip) => skip.checkId === checkId)) return { rows: [], total: null } satisfies IssuePage;
    const count = analysis.counts.find((entry) => entry.checkId === checkId);
    if (!count) throw new CheckNotFound(checkId);
    const file = await optionalFixture<IssuePage>(`api/${folder}/issues/${checkId}.json`);
    const rows = file?.rows ?? [];
    return { rows: rows.slice(offset, offset + limit), total: count.count } satisfies IssuePage;
  }

  function events(signal: AbortSignal | undefined): Response {
    let cleanup: (() => void) | null = null;
    const body = new ReadableStream<Uint8Array>({
      start(controller) {
        const send = (event: string, data: Json) =>
          controller.enqueue(encoder.encode(`event: ${event}\ndata: ${JSON.stringify(data)}\n\n`));
        send('hello', { live: [...simulations.keys()], analyzing: [...analyses.keys()] });
        subscribers.add(send);
        const keepAlive = setInterval(() => controller.enqueue(encoder.encode(': keep-alive\n\n')), KEEP_ALIVE_MS);
        cleanup = () => {
          subscribers.delete(send);
          clearInterval(keepAlive);
        };
        signal?.addEventListener('abort', () => {
          cleanup?.();
          controller.error(new DOMException('Aborted', 'AbortError'));
        });
      },
      cancel() {
        cleanup?.();
      },
    });
    return new Response(body, { status: 200, headers: { 'Content-Type': 'text/event-stream' } });
  }

  return {
    kind: 'mock',
    async send(request) {
      if (request.signal?.aborted) throw new DOMException('Aborted', 'AbortError');
      if (request.method === 'GET' && request.path === '/api/events') return events(request.signal);
      await new Promise((resolve) => setTimeout(resolve, LATENCY_MS));
      for (const [method, pattern, handler] of routes) {
        const match = request.method === method ? pattern.exec(request.path) : null;
        if (!match) continue;
        try {
          return await handler(match, request);
        } catch (error) {
          if (error instanceof MissingFixtureError)
            return problem(missingFixtures(error.message.replace('Missing fixture: ', '')));
          if (error instanceof CheckNotFound) {
            return problem({
              type: 'check-not-found',
              title: 'There’s no check with this id.',
              status: 404,
              detail: error.message,
            });
          }
          throw error;
        }
      }
      return problem({
        type: 'not-found',
        title: 'The engine doesn’t have this.',
        status: 404,
        detail: `${request.method} ${request.path}`,
      });
    },
  };
}

class CheckNotFound extends Error {}

function isHttpUrl(text: string): boolean {
  try {
    const url = new URL(text);
    return url.protocol === 'http:' || url.protocol === 'https:';
  } catch {
    return false;
  }
}

/** The engine's own rules for what the mock can check without it: a start URL, and sane numbers. */
export function validate(options: CrawlOptions): Record<string, string[]> {
  const errors: Record<string, string[]> = {};
  const startUrl = options.startUrl?.trim() ?? '';
  if ((startUrl !== '' || options.startUrl !== undefined) && !isHttpUrl(startUrl)) {
    errors.startUrl = ['Give a start URL that begins with http:// or https://.'];
  }
  if (typeof options.maxUrls === 'number' && options.maxUrls < 0) errors.maxUrls = ['Max URLs can’t be negative.'];
  if (typeof options.concurrency === 'number' && (options.concurrency < 1 || options.concurrency > 64)) {
    errors.concurrency = ['Concurrency must be from 1 to 64.'];
  }
  return errors;
}
