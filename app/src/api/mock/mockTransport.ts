// A sidecar in the page, for Storybook, the clickable prototype and the grids the engine doesn't serve yet. It
// answers the contract's routes from the real-crawl fixtures, keeps the library's changes in memory (remove, restore,
// delete, start), replays the garden crawl's real progress for a crawl started here, and streams events like the
// sidecar does. Nothing is made up beyond what a running crawl must show: its progress is the garden crawl's own.
import type { ProblemBody } from '../errors';
import type { GridQuery } from '../grids';
import type { Transport, TransportRequest } from '../transport';
import type {
  Analysis,
  CrawlListItem,
  CrawlOptionDefaults,
  CrawlOptions,
  CrawlStatus,
  IssuePage,
  ProgressEvent,
  StartCrawlRequest,
} from '../types';
import {
  MissingFixtureError,
  adaptAnalysis,
  adaptCrawlItem,
  adaptSnapshot,
  adaptStats,
  fixture,
  optionalFixture,
  urlRows,
  type FixtureCrawl,
  type LiveSnapshot,
} from './fixtures';
import { runGridQuery } from './gridQuery';

/** Every answer waits this long, so loading states behave as they will against the sidecar. */
const LATENCY_MS = 30;
/** The replay: a progress event every 500 ms, as the engine sends them, stepping this many seconds of the real crawl. */
const PROGRESS_INTERVAL_MS = 500;
const REPLAY_STEP = 15;
const KEEP_ALIVE_MS = 15_000;

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
  let loaded: Promise<void> | null = null;

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
    })());

  const folderOf = (id: string): FixtureCrawl => folders.get(id) ?? 'garden';

  const liveSnapshots = async () => {
    snapshots ??= (await fixture<{ snapshots: Record<string, unknown>[] }>('garden/live.json')).snapshots.map(
      adaptSnapshot,
    );
    return snapshots;
  };

  const setStatus = (id: string, status: CrawlStatus, extra: Partial<CrawlListItem> = {}) => {
    const item = items.get(id);
    if (!item) return;
    items.set(id, { ...item, ...extra, status, updatedAt: new Date().toISOString() });
    emit('crawl', { crawlId: id, status });
  };

  const progressOf = (simulation: Simulation, snapshot: LiveSnapshot): ProgressEvent => {
    const { pages: _pages, ...counts } = snapshot;
    return { crawlId: simulation.crawlId, status: simulation.status, ...counts };
  };

  const finish = (simulation: Simulation) => {
    if (simulation.timer) clearInterval(simulation.timer);
    simulation.timer = null;
    simulations.delete(simulation.crawlId);
    setStatus(simulation.crawlId, 'analyzing');
    setTimeout(() => {
      setStatus(simulation.crawlId, 'completed', {
        finishedAt: new Date().toISOString(),
        analyzedAt: new Date().toISOString(),
        issues: { error: 2, warning: 6, notice: 11 },
      });
      emit('library', {});
    }, 1_500);
  };

  const tick = async (simulation: Simulation) => {
    const all = await liveSnapshots();
    if (simulation.status === 'running') simulation.index = Math.min(all.length - 1, simulation.index + REPLAY_STEP);
    const snapshot = all[simulation.index];
    if (!snapshot) return;
    const item = items.get(simulation.crawlId);
    if (item) items.set(simulation.crawlId, { ...item, fetchedUrlCount: snapshot.fetched, pageCount: snapshot.pages });
    emit('progress', progressOf(simulation, snapshot));
    if (simulation.status === 'running' && simulation.index === all.length - 1) finish(simulation);
  };

  const run = (simulation: Simulation) => {
    simulations.set(simulation.crawlId, simulation);
    simulation.timer ??= setInterval(() => void tick(simulation), PROGRESS_INTERVAL_MS);
  };

  const live = (id: string) => simulations.get(id);

  const routes: [method: TransportRequest['method'], pattern: RegExp, handler: Handler][] = [
    ['GET', /^\/api\/health$/, async () => json(await fixture('api/health.json'))],
    ['POST', /^\/api\/shutdown$/, () => Promise.resolve(empty(202))],
    ['GET', /^\/api\/checks$/, async () => json(await fixture('api/checks.json'))],
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
      'POST',
      /^\/api\/crawls\/([0-9a-f]{16})\/analyze$/,
      async ([, id = '']) => {
        await load();
        const item = items.get(id);
        if (!item) return problem(crawlNotFound(id));
        if (live(id))
          return problem({ type: 'not-analysable', title: 'A crawl can’t be analysed while it runs.', status: 409 });
        setStatus(id, 'analyzing');
        setTimeout(() => {
          setStatus(id, 'completed', { analyzedAt: new Date().toISOString(), outOfDate: false });
          emit('library', {});
        }, 1_500);
        return empty(202);
      },
    ],
    ['POST', /^\/api\/crawls\/([0-9a-f]{16})\/analyze\/cancel$/, () => Promise.resolve(empty(202))],
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
    [
      'POST',
      /^\/api\/crawls\/([0-9a-f]{16})\/urls$/,
      async ([, id = ''], request) => {
        await load();
        if (!items.has(id)) return problem(crawlNotFound(id));
        const rows = await urlRows(folderOf(id));
        return json(runGridQuery(rows, request.body as GridQuery));
      },
    ],
  ];

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
        send('hello', { live: [...simulations.keys()] });
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
