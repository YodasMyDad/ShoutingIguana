import { describe, expect, it } from 'vitest';
import {
  HISTORY_POINTS,
  LiveStore,
  MAX_ALERTS,
  PUBLISH_INTERVAL_MS,
  parseSidecarEvent,
  type LiveStoreClock,
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
