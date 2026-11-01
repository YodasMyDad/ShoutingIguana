import { describe, expect, it } from 'vitest';
import { HISTORY_POINTS, LiveStore, PUBLISH_INTERVAL_MS, parseSidecarEvent, type LiveStoreClock } from './liveStore';

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

  it('reads a bad count as 0, never NaN', () => {
    const event = parseSidecarEvent({
      event: 'progress',
      id: '',
      data: '{"crawlId":"a","status":"running","fetched":"lots"}',
    });
    expect(event?.type === 'progress' && event.data.fetched).toBe(0);
  });
});
