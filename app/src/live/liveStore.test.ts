import { describe, expect, it } from 'vitest';
import {
  HISTORY_POINTS,
  LiveStore,
  MAX_ALERTS,
  PUBLISH_INTERVAL_MS,
  EMPTY_TRAIL,
  addTrailPoint,
  parseSidecarEvent,
  type LiveStoreClock,
  type TrailPoint,
} from './liveStore';

/** A clock the test drives: time moves only when told, and the window can be hidden. */
function manualClock() {
  let now = 0;
  let hidden = false;
  const timers: { at: number; callback: () => void; id: number }[] = [];
  let nextId = 1;
  const clock: LiveStoreClock = {
    now: () => now,
    setTimeout: (callback, ms) => {
      const id = nextId++;
      timers.push({ at: now + ms, callback, id });
      return id;
    },
    clearTimeout: (handle) => {
      const index = timers.findIndex((timer) => timer.id === handle);
      if (index >= 0) timers.splice(index, 1);
    },
    isHidden: () => hidden,
  };
  const advance = (ms: number) => {
    now += ms;
    for (;;) {
      const due = timers.filter((timer) => timer.at <= now).sort((a, b) => a.at - b.at)[0];
      if (!due) break;
      timers.splice(timers.indexOf(due), 1);
      due.callback();
    }
  };
  return { clock, advance, hide: (value: boolean) => (hidden = value) };
}

const progress = (crawlId: string, fetched: number, status = 'running') => ({
  event: 'progress',
  id: '',
  data: JSON.stringify({
    crawlId,
    status,
    fetched,
    discovered: fetched * 2,
    queued: 5,
    inFlight: 2,
    urlsPerSecond: fetched / 10,
  }),
});

describe('LiveStore', () => {
  it('applies events at once but tells React at most 4 times a second', () => {
    const { clock, advance } = manualClock();
    const store = new LiveStore(clock);
    let renders = 0;
    store.subscribe(() => renders++);
    for (let i = 1; i <= 20; i++) {
      store.ingest(progress('a', i));
      advance(50); // 20 events a second
    }
    advance(PUBLISH_INTERVAL_MS);
    // 1 s of events at 20 a second: 4 or 5 renders, never 20.
    expect(renders).toBeGreaterThanOrEqual(4);
    expect(renders).toBeLessThanOrEqual(5);
    expect(store.getState().crawls.a?.progress?.fetched).toBe(20);
  });

  it('keeps up while the window is hidden but renders nothing, then renders once when shown', () => {
    const { clock, advance, hide } = manualClock();
    const store = new LiveStore(clock);
    let renders = 0;
    store.subscribe(() => renders++);
    hide(true);
    store.ingest(progress('a', 10));
    advance(1_000);
    store.ingest(progress('a', 20));
    advance(1_000);
    expect(renders).toBe(0);
    expect(store.getState().crawls.a?.progress?.fetched).toBe(20);
    hide(false);
    store.becameVisible();
    expect(renders).toBe(1);
  });

  it('knows which crawls are live from hello, progress and status changes', () => {
    const { clock, advance } = manualClock();
    const store = new LiveStore(clock);
    store.ingest({ event: 'hello', id: '', data: '{"live":["a","b"]}' });
    store.ingest({ event: 'crawl', id: '', data: '{"crawlId":"b","status":"completed"}' });
    store.ingest(progress('c', 1));
    advance(PUBLISH_INTERVAL_MS);
    expect(store.getState().live).toEqual(['a', 'c']);
    store.ingest({ event: 'hello', id: '', data: '{"live":[]}' });
    advance(PUBLISH_INTERVAL_MS);
    expect(store.getState().live).toEqual([]);
  });

  it('keeps five minutes of rate samples for the sparkline', () => {
    const { clock, advance } = manualClock();
    const store = new LiveStore(clock);
    for (let i = 0; i < HISTORY_POINTS + 50; i++) store.ingest(progress('a', i));
    advance(PUBLISH_INTERVAL_MS);
    const history = store.getState().crawls.a?.history ?? [];
    expect(history).toHaveLength(HISTORY_POINTS);
    expect(history.at(-1)).toBe((HISTORY_POINTS + 49) / 10);
  });

  it('collects alerts, newest last', () => {
    const { clock, advance } = manualClock();
    const store = new LiveStore(clock);
    store.ingest({
      event: 'alert',
      id: '',
      data: '{"crawlId":"a","level":"warning","code":"blocked","message":"403s","at":"2026-11-01T10:00:00Z"}',
    });
    advance(PUBLISH_INTERVAL_MS);
    expect(store.getState().crawls.a?.alerts.map((alert) => alert.code)).toEqual(['blocked']);
  });

  it('passes every event to side-effect listeners at once, unbatched', () => {
    const { clock } = manualClock();
    const store = new LiveStore(clock);
    const seen: string[] = [];
    store.onEvent((event) => seen.push(event.type));
    store.ingest({ event: 'library', id: '', data: '{}' });
    store.ingest(progress('a', 1));
    expect(seen).toEqual(['library', 'progress']);
  });

  it('keeps the same objects for what did not change, so selectors stay cheap', () => {
    const { clock, advance } = manualClock();
    const store = new LiveStore(clock);
    store.ingest(progress('a', 1));
    store.ingest(progress('b', 1));
    advance(PUBLISH_INTERVAL_MS);
    const before = store.getState().crawls.a;
    store.ingest(progress('b', 2));
    advance(PUBLISH_INTERVAL_MS);
    expect(store.getState().crawls.a).toBe(before);
  });
});

describe('parseSidecarEvent', () => {
  it('drops anything that is not the contract', () => {
    expect(parseSidecarEvent({ event: 'progress', id: '', data: 'not json' })).toBeNull();
    expect(parseSidecarEvent({ event: 'progress', id: '', data: '{"status":"running"}' })).toBeNull();
    expect(parseSidecarEvent({ event: 'mystery', id: '', data: '{}' })).toBeNull();
  });

  it('reads a bad or missing count as unknown: never NaN, and never 0, which would say nothing happened', () => {
    const event = parseSidecarEvent({
      event: 'progress',
      id: '',
      data: '{"crawlId":"a","status":"running","fetched":"lots","queued":null,"discovered":12}',
    });
    expect(event?.type).toBe('progress');
    if (event?.type !== 'progress') return;
    expect(event.data.fetched).toBeNull();
    expect(event.data.queued).toBeNull();
    expect(event.data.inFlight).toBeNull();
    expect(event.data.discovered).toBe(12);
  });

  it('drops a status the contract doesn’t have, and keeps only the contract’s fields of a status change', () => {
    expect(parseSidecarEvent({ event: 'crawl', id: '', data: '{"crawlId":"a","status":"exploded"}' })).toBeNull();
    expect(parseSidecarEvent({ event: 'progress', id: '', data: '{"crawlId":"a","status":7}' })).toBeNull();
    expect(
      parseSidecarEvent({
        event: 'crawl',
        id: '',
        data: '{"crawlId":"a","status":"completed","error":3,"errorType":"crawl-busy","extra":true}',
      }),
    ).toEqual({ type: 'crawl', data: { crawlId: 'a', status: 'completed', errorType: 'crawl-busy' } });
  });
});

describe('a new stream’s hello', () => {
  it('ends the live status of every crawl it doesn’t list, so the library’s status shows again', () => {
    const { clock, advance } = manualClock();
    const store = new LiveStore(clock);
    store.ingest(progress('a', 10));
    store.ingest(progress('b', 20, 'paused'));
    store.ingest({ event: 'crawl', id: '', data: '{"crawlId":"c","status":"analyzing"}' });
    store.ingest({ event: 'analysis', id: '', data: '{"crawlId":"c","phase":"checks","done":1,"total":9}' });
    store.ingest({ event: 'crawl', id: '', data: '{"crawlId":"d","status":"stopped"}' });
    // The sidecar crashed and the shell started another: only b is live there.
    store.ingest({ event: 'hello', id: '', data: '{"live":["b"]}' });
    advance(PUBLISH_INTERVAL_MS);
    const { crawls, live } = store.getState();
    expect(live).toEqual(['b']);
    expect(crawls.a?.status).toBeNull();
    expect(crawls.b?.status).toBe('paused');
    expect(crawls.c?.status).toBeNull();
    expect(crawls.c?.analysis).toBeNull();
    // A status that was never live is the library's to say anyway: left alone.
    expect(crawls.d?.status).toBe('stopped');
    // What it did is history, still there for the Overview.
    expect(crawls.a?.progress?.fetched).toBe(10);
  });

  it('tells side-effect listeners what was true before it', () => {
    const { clock } = manualClock();
    const store = new LiveStore(clock);
    store.ingest(progress('a', 10));
    const before: (string | null | undefined)[] = [];
    store.onEvent((event, previous) => {
      if (event.type === 'hello') before.push(previous.crawls.a?.status);
    });
    store.ingest({ event: 'hello', id: '', data: '{"live":[]}' });
    expect(before).toEqual(['running']);
  });
});

describe('E4’s progress, read at the boundary', () => {
  const e4 = {
    crawlId: 'a',
    status: 'running',
    fetched: 40,
    at: '2026-11-01T14:13:02.500Z',
    phase: 'crawling',
    internalFound: 120,
    internalFetched: 38,
    maxUrls: 10_000,
    statusClasses: { '2xx': 30, '3xx': 4, '4xx': 3, '5xx': 0, other: 0, noResponse: 2, blocked: 1 },
    statusCodes: { '200': 30, '301': 4, '404': 3 },
    noResponse: { robotsDisallowed: 2, timeout: 0, networkError: 0, notChecked: 0, processingError: 0 },
    waits: [{ reason: 'crawlDelay', host: 'www.example.com', durationMs: 2000 }],
    moreWaits: { crawlDelay: 3 },
  };
  const read = (data: Record<string, unknown>) => {
    const event = parseSidecarEvent({ event: 'progress', id: '', data: JSON.stringify(data) });
    if (event?.type !== 'progress') throw new Error('not a progress event');
    return event.data;
  };

  it('keeps every E4 field the contract defines', () => {
    const progress = read(e4);
    expect(progress.at).toBe(Date.parse('2026-11-01T14:13:02.500Z'));
    expect(progress.phase).toBe('crawling');
    expect(progress.internalFound).toBe(120);
    expect(progress.internalFetched).toBe(38);
    expect(progress.maxUrls).toBe(10_000);
    expect(progress.statusClasses).toEqual(e4.statusClasses);
    expect(progress.statusCodes).toEqual(e4.statusCodes);
    expect(progress.noResponse).toEqual(e4.noResponse);
    expect(progress.waits).toEqual(e4.waits);
    expect(progress.moreWaits).toEqual({ crawlDelay: 3 });
  });

  it('reads an engine from before E4 as unknown, never as nothing fetched', () => {
    const progress = read({ crawlId: 'a', status: 'running', fetched: 40 });
    expect(progress.at).toBeNull();
    expect(progress.phase).toBeNull();
    expect(progress.internalFound).toBeNull();
    expect(progress.maxUrls).toBeUndefined();
    expect(progress.statusClasses).toBeNull();
    expect(progress.statusCodes).toBeNull();
    expect(progress.noResponse).toBeNull();
    expect(progress.waits).toEqual([]);
    expect(progress.moreWaits).toEqual({});
  });

  it('tells no URL limit (null) from a limit the stream didn’t give', () => {
    expect(read({ ...e4, maxUrls: null }).maxUrls).toBeNull();
    expect(read({ ...e4, maxUrls: 'lots' }).maxUrls).toBeUndefined();
  });

  it('drops a breakdown with a class missing or malformed, as its parts wouldn’t add up', () => {
    const { blocked: _blocked, ...withoutBlocked } = e4.statusClasses;
    expect(read({ ...e4, statusClasses: withoutBlocked }).statusClasses).toBeNull();
    expect(read({ ...e4, statusClasses: { ...e4.statusClasses, '4xx': -1 } }).statusClasses).toBeNull();
    expect(read({ ...e4, noResponse: { ...e4.noResponse, timeout: 'some' } }).noResponse).toBeNull();
  });

  it('keeps only the waits, codes and phases the contract has', () => {
    const progress = read({
      ...e4,
      phase: 'daydreaming',
      statusCodes: { '200': 30, teapot: 1, '404': 'three' },
      waits: [{ reason: 'siteDown', host: 'https://example.com', durationMs: 'long' }, { reason: 'bored' }, 7],
      moreWaits: { backoff: 0, crawlDelay: 2, bored: 4 },
    });
    expect(progress.phase).toBeNull();
    expect(progress.statusCodes).toEqual({ '200': 30 });
    expect(progress.waits).toEqual([{ reason: 'siteDown', host: 'https://example.com' }]);
    expect(progress.moreWaits).toEqual({ crawlDelay: 2 });
  });

  it('keeps the same empty waits from one event to the next', () => {
    const first = read({ ...e4, waits: [], moreWaits: {} });
    const second = read({ ...e4, waits: [], moreWaits: {} });
    expect(second.waits).toBe(first.waits);
    expect(second.moreWaits).toBe(first.moreWaits);
  });
});

describe('E4’s alerts and hello', () => {
  it('keeps an alert’s id and its facts, each of its own type', () => {
    const event = parseSidecarEvent({
      event: 'alert',
      id: '',
      data: JSON.stringify({
        crawlId: 'a',
        id: 7,
        level: 'warning',
        code: 'blocked',
        message: 'Blocked',
        at: '2026-11-01T10:00:00.250Z',
        data: { host: 'www.example.com', reason: 'Cloudflare challenge', count: 1, sampled: 'twenty', extra: true },
      }),
    });
    expect(event).toEqual({
      type: 'alert',
      data: {
        crawlId: 'a',
        id: 7,
        level: 'warning',
        code: 'blocked',
        message: 'Blocked',
        at: '2026-11-01T10:00:00.250Z',
        data: { host: 'www.example.com', reason: 'Cloudflare challenge', count: 1 },
      },
    });
  });

  it('reads an alert without facts, or an id that isn’t a whole number, as having none', () => {
    const event = parseSidecarEvent({
      event: 'alert',
      id: '',
      data: '{"crawlId":"a","id":1.5,"level":"info","code":"sitemaps","message":"m","at":"2026-11-01T10:00:00Z"}',
    });
    if (event?.type !== 'alert') throw new Error('not an alert');
    expect(event.data.id).toBeUndefined();
    expect(event.data.data).toBeNull();
  });

  it('knows the crawls a hello says are being analysed, though it missed their status change', () => {
    const { clock, advance } = manualClock();
    const store = new LiveStore(clock);
    store.ingest({ event: 'hello', id: '', data: '{"live":["a"],"analyzing":["b"]}' });
    store.ingest({ event: 'analysis', id: '', data: '{"crawlId":"b","phase":"pageRules","done":1,"total":9}' });
    advance(PUBLISH_INTERVAL_MS);
    const { crawls, live } = store.getState();
    expect(live).toEqual(['a', 'b']);
    expect(crawls.b?.status).toBe('analyzing');
    expect(crawls.b?.analysis?.phase).toBe('pageRules');
    // The next hello doesn't list it: its status is the library's again.
    store.ingest({ event: 'hello', id: '', data: '{"live":["a"]}' });
    advance(PUBLISH_INTERVAL_MS);
    expect(store.getState().crawls.b?.status).toBeNull();
    expect(store.getState().live).toEqual(['a']);
  });
});

describe('E5’s export events', () => {
  const job = {
    exportId: 'e1',
    crawlId: 'a',
    kind: 'urls',
    format: 'csv',
    path: 'C:\\out\\urls.csv',
    status: 'running',
    rowsWritten: 5000,
    rowsExpected: 112577,
    startedAt: '2026-11-01T17:42:00.118Z',
    finishedAt: null,
    result: null,
    error: null,
  };

  it('hands an export to its listeners whole, and keeps none of it in a crawl’s live state', () => {
    const { clock, advance } = manualClock();
    const store = new LiveStore(clock);
    store.ingest({ event: 'hello', id: '', data: '{"live":["a"],"analyzing":[],"exports":["e1"]}' });
    advance(PUBLISH_INTERVAL_MS);
    const before = store.getState();
    const heard: unknown[] = [];
    store.onEvent((event) => {
      if (event.type === 'export') heard.push(event.data);
    });
    store.ingest({ event: 'export', id: '', data: JSON.stringify(job) });
    store.ingest({
      event: 'export',
      id: '',
      data: JSON.stringify({ ...job, status: 'cancelled', finishedAt: job.startedAt }),
    });
    advance(PUBLISH_INTERVAL_MS);
    expect(heard).toEqual([job, { ...job, status: 'cancelled', finishedAt: job.startedAt }]);
    expect(store.getState()).toBe(before);
  });

  it('reads the exports a hello says are running, and drops an export event that isn’t one', () => {
    const hello = parseSidecarEvent({ event: 'hello', id: '', data: '{"live":[],"analyzing":[],"exports":["e1",7]}' });
    if (hello?.type !== 'hello') throw new Error('not a hello');
    expect(hello.data.exports).toEqual(['e1']);
    // A sidecar from before E5 lists none.
    const older = parseSidecarEvent({ event: 'hello', id: '', data: '{"live":[]}' });
    if (older?.type !== 'hello') throw new Error('not a hello');
    expect(older.data.exports).toEqual([]);
    expect(
      parseSidecarEvent({ event: 'export', id: '', data: JSON.stringify({ ...job, status: 'exploded' }) }),
    ).toBeNull();
    expect(parseSidecarEvent({ event: 'export', id: '', data: JSON.stringify(job) })).toEqual({
      type: 'export',
      data: job,
    });
  });
});

describe('the found-and-crawled trail', () => {
  const point = (at: number): TrailPoint => ({
    at,
    runMs: at,
    fetched: at,
    internalFound: at * 2,
    internalFetched: at,
  });

  it('keeps a point per progress event at first', () => {
    let trail = EMPTY_TRAIL;
    for (let i = 0; i < 10; i++) trail = addTrailPoint(trail, point(i * 500));
    expect(trail.points.map((entry) => entry.at)).toEqual([0, 500, 1000, 1500, 2000, 2500, 3000, 3500, 4000, 4500]);
    expect(trail.every).toBe(1);
  });

  it.each([16, 17])(
    'stays within %i points however long the crawl runs, evenly spread, the first and the newest kept',
    (capacity) => {
      let trail = EMPTY_TRAIL;
      for (let i = 0; i < 5_000; i++) {
        trail = addTrailPoint(trail, point(i * 500), capacity);
        expect(trail.points.length).toBeLessThanOrEqual(capacity);
        expect(trail.points.at(-1)?.at).toBe(i * 500);
        // Every gap but the last (the newest stands in until its turn comes) is one kept point's worth of events.
        const gaps = trail.points.slice(1, -1).map((entry, index) => entry.at - (trail.points[index]?.at ?? 0));
        expect([...new Set(gaps)]).toEqual(gaps.length > 0 ? [trail.every * 500] : []);
      }
      expect(trail.points[0]?.at).toBe(0);
      expect(trail.points.length).toBeGreaterThan(capacity / 2);
      expect(trail.every).toBeGreaterThan(1);
    },
  );

  it('ignores a point no newer than its newest', () => {
    const start = addTrailPoint(EMPTY_TRAIL, point(1_000));
    expect(addTrailPoint(start, point(1_000))).toBe(start);
    expect(addTrailPoint(start, point(500))).toBe(start);
  });

  it('grows from progress events that say when they were taken', () => {
    const { clock, advance } = manualClock();
    const store = new LiveStore(clock);
    const at = (seconds: number) => new Date(Date.UTC(2026, 10, 1, 10, 0, seconds)).toISOString();
    const event = (seconds: number, extra: Record<string, unknown>) => ({
      event: 'progress',
      id: '',
      data: JSON.stringify({ crawlId: 'a', status: 'running', fetched: seconds * 10, at: at(seconds), ...extra }),
    });
    store.ingest(event(1, { internalFound: 50, internalFetched: 9 }));
    store.ingest(event(2, { internalFound: 80, internalFetched: 19 }));
    // An event that doesn't say what the site's own counts are adds nothing, rather than a false point.
    store.ingest(event(3, {}));
    advance(PUBLISH_INTERVAL_MS);
    // Each sits on the run's clock: the page first heard of the run here, so from there.
    expect(store.getState().crawls.a?.trail.points).toEqual([
      { at: Date.parse(at(1)), runMs: 0, fetched: 10, internalFound: 50, internalFetched: 9 },
      { at: Date.parse(at(2)), runMs: 1_000, fetched: 20, internalFound: 80, internalFetched: 19 },
    ]);
  });
});

describe('the run’s clock: how long the run being watched has spent crawling', () => {
  const START = Date.UTC(2026, 10, 10, 9, 0, 0);
  /** A progress event `wallMs` after the crawl first started; `elapsedMs` is the engine's, the run's own wall time. */
  const event = (wallMs: number, extra: Record<string, unknown> = {}) => ({
    event: 'progress',
    id: '',
    data: JSON.stringify({
      crawlId: 'a',
      status: 'running',
      elapsedMs: wallMs,
      at: new Date(START + wallMs).toISOString(),
      fetched: wallMs / 10,
      internalFound: 9_000,
      internalFetched: wallMs / 10,
      ...extra,
    }),
  });
  const crawlAfter = (events: readonly ReturnType<typeof event>[]) => {
    const { clock, advance } = manualClock();
    const store = new LiveStore(clock);
    events.forEach((message) => store.ingest(message));
    advance(PUBLISH_INTERVAL_MS);
    const crawl = store.getState().crawls.a;
    if (!crawl) throw new Error('no crawl');
    return crawl;
  };

  it('runs while the crawl runs, and stands still while it’s paused, though progress keeps arriving', () => {
    const running = [0, 500, 1_000, 1_500, 2_000].map((ms) => event(ms));
    expect(crawlAfter(running).clock?.runMs).toBe(2_000);
    // Paused for 45 s: the engine's `elapsedMs` runs on through it, and an event comes every half second.
    const paused = Array.from({ length: 90 }, (_, index) =>
      event(2_500 + index * 500, { status: 'paused', internalFetched: 200, fetched: 200 }),
    );
    const through = crawlAfter([...running, ...paused]);
    expect(through.progress?.elapsedMs).toBe(47_000);
    // The half second in which it was paused counts: the stream can't place the pause closer than that.
    expect(through.clock?.runMs).toBe(2_500);
    const resumed = crawlAfter([...running, ...paused, event(47_500), event(48_000)]);
    expect(resumed.clock?.runMs).toBe(3_000);
    // The chart's points sit on it, so a pause isn't a flat stretch 45 s wide; the pace's samples are only of running.
    expect(resumed.trail.points.at(-1)?.runMs).toBe(3_000);
    expect(resumed.pace.map((sample) => sample.runMs)).toEqual([0, 500, 1_000, 1_500, 2_000, 2_500, 3_000]);
  });

  it('starts from the engine’s count of a run the page comes to part-way', () => {
    // All that's known of what came before: the wall time since the run began.
    const crawl = crawlAfter([event(120_000), event(120_500)]);
    expect(crawl.clock?.runMs).toBe(120_500);
    expect(crawl.trail.points.map((point) => point.runMs)).toEqual([120_000, 120_500]);
  });

  it('starts again when the crawl is continued, and the run’s chart and pace with it', () => {
    const first = [0, 500, 1_000].map((ms) => event(ms));
    const stopped = event(1_010, { status: 'stopped', phase: 'finishing' });
    // Three days later: the engine's `elapsedMs` starts again, the counts carry on.
    const later = 3 * 86_400_000;
    const second = [0, 500, 1_000, 1_500].map((ms) =>
      event(later + ms, { elapsedMs: ms, internalFetched: 101 + ms / 10, fetched: 101 + ms / 10 }),
    );
    const crawl = crawlAfter([...first, stopped, ...second]);
    expect(crawl.clock?.runMs).toBe(1_500);
    expect(crawl.trail.points.map((point) => point.runMs)).toEqual([0, 500, 1_000, 1_500]);
    expect(crawl.trail.points[0]?.internalFetched).toBe(101);
    expect(crawl.pace).toHaveLength(4);
    // An engine that doesn't count the run's time still starts it again: the last event was of a run that had ended.
    const bare = crawlAfter([
      event(0, { elapsedMs: 'x' }),
      event(500, { elapsedMs: 'x' }),
      event(510, { elapsedMs: 'x', status: 'stopped' }),
      event(later, { elapsedMs: 'x' }),
      event(later + 500, { elapsedMs: 'x' }),
    ]);
    expect(bare.clock?.runMs).toBe(500);
    expect(bare.trail.points).toHaveLength(2);
  });

  it('isn’t moved by an event that isn’t newer than the last (a reconnect replays the latest)', () => {
    const crawl = crawlAfter([event(0), event(500), event(500), event(0), event(1_000)]);
    expect(crawl.clock?.runMs).toBe(1_000);
    expect(crawl.trail.points.map((point) => point.runMs)).toEqual([0, 500, 1_000]);
    expect(crawl.pace).toHaveLength(3);
  });

  it('counts a stretch it heard nothing of as the crawl’s status was when it began (a hidden window, a reconnect)', () => {
    expect(crawlAfter([event(0), event(500), event(60_500)]).clock?.runMs).toBe(60_500);
    expect(crawlAfter([event(0), event(500, { status: 'paused' }), event(60_500)]).clock?.runMs).toBe(500);
  });

  it('keeps the pace’s samples to the last minute of crawling, however long the crawl runs', () => {
    const crawl = crawlAfter(Array.from({ length: 1_000 }, (_, index) => event(index * 500)));
    expect(crawl.pace).toHaveLength(121);
    expect((crawl.pace.at(-1)?.runMs ?? 0) - (crawl.pace[0]?.runMs ?? 0)).toBe(60_000);
    // A burst of events closer together than the engine's pace never grows it without bound.
    const burst = crawlAfter(Array.from({ length: 2_000 }, (_, index) => event(index * 10)));
    expect(burst.pace.length).toBeLessThanOrEqual(240);
  });
});

describe('what the store keeps, so memory stays flat over a long crawl', () => {
  it('keeps the newest alerts only, up to the cap', () => {
    const { clock, advance } = manualClock();
    const store = new LiveStore(clock);
    for (let i = 0; i < MAX_ALERTS + 25; i++) {
      store.ingest({
        event: 'alert',
        id: '',
        data: JSON.stringify({ crawlId: 'a', level: 'info', code: `c${i}`, message: 'm', at: '2026-11-01T10:00:00Z' }),
      });
    }
    advance(PUBLISH_INTERVAL_MS);
    const alerts = store.getState().crawls.a?.alerts ?? [];
    expect(alerts).toHaveLength(MAX_ALERTS);
    expect(alerts[0]?.code).toBe('c25');
    expect(alerts.at(-1)?.code).toBe(`c${MAX_ALERTS + 24}`);
  });

  it('leaves a gap in the rate history for a rate the stream didn’t give', () => {
    const { clock, advance } = manualClock();
    const store = new LiveStore(clock);
    store.ingest(progress('a', 10));
    store.ingest({ event: 'progress', id: '', data: '{"crawlId":"a","status":"running","urlsPerSecond":"fast"}' });
    advance(PUBLISH_INTERVAL_MS);
    expect(store.getState().crawls.a?.history).toEqual([1]);
  });
});
