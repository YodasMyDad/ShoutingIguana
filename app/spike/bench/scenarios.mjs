// Measurements shared by the Chromium bench and the WebView2 (Tauri) run. Each takes an open page and its CDP session.
import { cpuSnapshot, cpuStats, frameStats, machineBusyPct, metrics, summarise } from './stats.mjs';

export const SPEEDS = {
  normal: { distance: 15_000, speed: 1_500 }, // about 54 rows/s: steady wheel scrolling while reading
  fast: { distance: 100_000, speed: 20_000 }, // about 710 rows/s: a hard fling
  sweep: { distance: 1_400_000, speed: 50_000 }, // 50k rows in 28 s, to fill the page cache
};

export const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

export async function gridCentre(page) {
  const box = await page.locator('.grid').boundingBox();
  return { x: Math.round(box.x + box.width / 2), y: Math.round(box.y + Math.min(box.height / 2, 300)) };
}

export async function scrollGesture(page, cdp, { distance, speed }) {
  const { x, y } = await gridCentre(page);
  await cdp.send('Input.synthesizeScrollGesture', {
    x,
    y,
    yDistance: -distance,
    speed,
    gestureSourceType: 'mouse',
    preventFling: true,
  });
}

/** Records frames and main-thread busy time while `action` runs. */
export async function recordFrames(page, cdp, action) {
  const fetchesBefore = await page.evaluate(() => window.__spike.fetchTimings.length);
  await page.evaluate(() => window.__spike.startFrames());
  const before = await metrics(cdp);
  const machineBefore = cpuSnapshot();
  const started = Date.now();
  await action();
  const wallMs = Date.now() - started;
  const machinePct = machineBusyPct(machineBefore, cpuSnapshot());
  const after = await metrics(cdp);
  const samples = await page.evaluate(() => window.__spike.stopFrames());
  const fetches = await page.evaluate((from) => window.__spike.fetchTimings.slice(from), fetchesBefore);
  return { wallMs, machinePct, frames: frameStats(samples), cpu: cpuStats(before, after, wallMs), pageFetchMs: summarise(fetches) };
}

export async function scrollRun(page, cdp, speed) {
  return recordFrames(page, cdp, () => scrollGesture(page, cdp, speed));
}

export async function jumps(page) {
  const targets = { middle: 500_000, end: 999_999, a: 123_457, b: 876_543, c: 345_678, d: 654_321, e: 42 };
  const results = {};
  for (const [name, index] of Object.entries(targets)) {
    results[name] = await page.evaluate((i) => window.__spike.jumpTo(i), index);
    await sleep(300);
  }
  const others = ['a', 'b', 'c', 'd', 'e'].map((k) => results[k]?.ms).filter((v) => typeof v === 'number');
  return {
    middleMs: results.middle?.ms ?? null,
    endMs: results.end?.ms ?? null,
    endFirstVisible: results.end?.firstVisible ?? null,
    randomJumpsMs: summarise(others),
  };
}

export async function jsMemory(page, cdp) {
  await cdp.send('HeapProfiler.collectGarbage');
  const m = await metrics(cdp);
  const dom = await cdp.send('Memory.getDOMCounters');
  const cachedPages = await page.evaluate(() => window.__spike.cachedPages());
  return {
    jsHeapUsedMB: Number((m.JSHeapUsedSize / 1048576).toFixed(1)),
    jsHeapTotalMB: Number((m.JSHeapTotalSize / 1048576).toFixed(1)),
    domNodes: dom.nodes,
    listeners: dom.jsEventListeners,
    cachedPages,
  };
}

/** Live panel cost: idle (stream only) and while the grid scrolls at normal speed. */
export async function liveRun(page, cdp) {
  await page.waitForFunction(() => window.__spike.live.published > 4, null, { timeout: 20_000 });
  await sleep(1_000);
  const received0 = await page.evaluate(() => ({ ...window.__spike.live }));
  const idle = await recordFrames(page, cdp, () => sleep(8_000));
  const scrolling = await scrollRun(page, cdp, SPEEDS.normal);
  const fast = await scrollRun(page, cdp, SPEEDS.fast);
  const received1 = await page.evaluate(() => ({ ...window.__spike.live }));
  const seconds = (idle.wallMs + scrolling.wallMs + fast.wallMs) / 1000;
  return {
    idle,
    scrolling,
    fast,
    messagesPerSecond: Number(((received1.received - received0.received) / seconds).toFixed(2)),
    rendersPerSecond: Number(((received1.published - received0.published) / seconds).toFixed(2)),
  };
}
