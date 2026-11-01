// Chromium bench: the built UI, served cross-origin, against the dummy sidecar. Usage: node bench/run.mjs [--rates 1,4]
import fs from 'node:fs/promises';
import path from 'node:path';
import { chromium } from 'playwright';
import { startSidecar } from './sidecar.mjs';
import { serveDist } from './static.mjs';
import { processMemory, summarise, waitForQuiet } from './stats.mjs';
import { jsMemory, jumps, liveRun, scrollRun, sleep, SPEEDS } from './scenarios.mjs';

const root = path.resolve(import.meta.dirname, '..');
const argRates = process.argv.indexOf('--rates');
const rates = argRates > 0 ? process.argv[argRates + 1].split(',').map(Number) : [1, 4];
const only = process.argv.includes('--only') ? process.argv[process.argv.indexOf('--only') + 1].split(',') : null;
const want = (name) => !only || only.includes(name);
// The headless shell paces frames at 60 Hz (a typical laptop panel); --new-headless follows the real display (240 Hz here).
const newHeadless = process.argv.includes('--new-headless');
const extraQuery = process.argv.includes('--query') ? Object.fromEntries(new URLSearchParams(process.argv[process.argv.indexOf('--query') + 1])) : {};

const web = await serveDist(path.join(root, 'dist'));
let sidecar = await startSidecar({ allowedOrigins: [web.origin] });
const browser = await chromium.launch(newHeadless ? { channel: 'chromium', headless: true } : { headless: true });
const browserCdp = await browser.newBrowserCDPSession();
const out = { browser: browser.version(), mode: newHeadless ? 'new headless (display rate)' : 'headless shell (60 Hz)', extraQuery, viewport: '1536x864 @1.25', sidecarReadyMs: Math.round(sidecar.readyMs), rates: {} };
console.log(`Chromium ${out.browser}, sidecar ready in ${out.sidecarReadyMs} ms, port ${sidecar.port}`);

async function open(rate, query = {}) {
  const context = await browser.newContext({ viewport: { width: 1536, height: 864 }, deviceScaleFactor: 1.25 });
  const page = await context.newPage();
  const cdp = await context.newCDPSession(page);
  await cdp.send('Performance.enable');
  if (rate > 1) await cdp.send('Emulation.setCPUThrottlingRate', { rate });
  const params = new URLSearchParams({ port: String(sidecar.port), token: sidecar.token, live: '0', charts: 'none', ...extraQuery, ...query });
  await page.goto(`${web.origin}/?${params}`);
  return { context, page, cdp };
}

async function chromiumMemory() {
  const { processInfo } = await browserCdp.send('SystemInfo.getProcessInfo');
  const byPid = new Map(processInfo.map((p) => [p.id, p.type]));
  const list = processMemory([...byPid.keys()]).map((p) => ({ ...p, type: byPid.get(p.pid) }));
  return {
    rendererPrivateMB: list.filter((p) => p.type === 'renderer').reduce((s, p) => s + p.privateMB, 0),
    allPrivateMB: Number(list.reduce((s, p) => s + p.privateMB, 0).toFixed(1)),
    processes: list.map((p) => `${p.type}:${p.privateMB}`).join(' '),
  };
}

async function sidecarDelta(action) {
  const before = await sidecar.stats();
  const result = await action();
  const after = await sidecar.stats();
  return { result, preflights: after.preflights - before.preflights, pageRequests: after.pageRequests - before.pageRequests, aborted: after.aborted - before.aborted };
}

for (const rate of rates) {
  const quiet = await waitForQuiet();
  const r = (out.rates[`${rate}x`] = { machineLoadBeforePct: quiet.pct });
  console.log(`\n=== CPU throttling ${rate}x === (machine load ${quiet.pct}% after waiting ${Math.round(quiet.waitedMs / 1000)} s)`);

  if (want('load')) {
    // First page after mount, in a fresh context each time (so the CORS preflight is not cached).
    const loads = [];
    for (let i = 0; i < 6; i++) {
      const { context, page } = await open(rate);
      await page.waitForFunction(() => window.__spike.marks.firstRowsPainted !== undefined, null, { timeout: 20_000 });
      const marks = await page.evaluate(() => ({ ...window.__spike.marks }));
      loads.push({ sinceMount: marks.firstRowsPainted - marks.gridMount, sinceNavigation: marks.firstRowsPainted, fcp: marks['first-contentful-paint'] });
      await context.close();
    }
    const runs = loads.slice(1); // the first run warms the static server and Chromium
    r.firstPage = {
      sinceMountMs: summarise(runs.map((l) => l.sinceMount)),
      sinceNavigationMs: summarise(runs.map((l) => l.sinceNavigation)),
      fcpMs: summarise(runs.map((l) => l.fcp)),
    };
    console.log('first page', JSON.stringify(r.firstPage));
  }

  if (want('grid')) {
    const { context, page, cdp } = await open(rate);
    await page.waitForFunction(() => window.__spike.marks.firstRowsPainted !== undefined, null, { timeout: 20_000 });
    await sleep(500);
    r.memoryAtStart = { ...(await jsMemory(page, cdp)), ...(await chromiumMemory()) };
    for (const [name, speed] of Object.entries(SPEEDS)) {
      const run = await sidecarDelta(() => scrollRun(page, cdp, speed));
      r[`scroll_${name}`] = { ...run.result, preflights: run.preflights, pageRequests: run.pageRequests, aborted: run.aborted };
      console.log(`scroll ${name} (machine ${run.result.machinePct}%)`, JSON.stringify(r[`scroll_${name}`].frames), 'cpu', JSON.stringify(r[`scroll_${name}`].cpu), 'fetch', JSON.stringify(r[`scroll_${name}`].pageFetchMs), `preflights ${run.preflights}/${run.pageRequests}`);
    }
    r.memoryAfterSweep = { ...(await jsMemory(page, cdp)), ...(await chromiumMemory()) };
    console.log('memory after ~54k rows', JSON.stringify(r.memoryAfterSweep));
    r.jumps = await jumps(page);
    console.log('jumps', JSON.stringify(r.jumps));
    r.memoryAfterJumpToEnd = { ...(await jsMemory(page, cdp)), ...(await chromiumMemory()) };
    console.log('memory after jumps', JSON.stringify(r.memoryAfterJumpToEnd));
    if (rate === 1) {
      await page.evaluate(() => window.__spike.jumpTo(999_999));
      await sleep(500);
      await page.screenshot({ path: path.join(root, 'results', 'grid-end.png') });
      r.maxScrollHeight = await page.evaluate(() => window.__spike.probeMaxScrollHeight(40_000_000));
    }
    await context.close();
  }

  if (want('post')) {
    // Same fast scroll with one fixed URL (POST): the preflight cache is per URL.
    const { context, page, cdp } = await open(rate, { fetch: 'post' });
    await page.waitForFunction(() => window.__spike.marks.firstRowsPainted !== undefined, null, { timeout: 20_000 });
    const run = await sidecarDelta(() => scrollRun(page, cdp, SPEEDS.fast));
    r.scroll_fast_post = { ...run.result, preflights: run.preflights, pageRequests: run.pageRequests };
    console.log('scroll fast (POST)', JSON.stringify(run.result.frames), 'fetch', JSON.stringify(run.result.pageFetchMs), `preflights ${run.preflights}/${run.pageRequests}`);
    await context.close();
  }

  if (want('live')) {
    r.live = {};
    for (const [label, query] of [
      ['counters-only', { live: '1', charts: 'none' }],
      ['echarts', { live: '1', charts: 'echarts' }],
      ['visx', { live: '1', charts: 'visx' }],
      ['echarts-20hz', { live: '1', charts: 'echarts', sseInterval: '50' }],
      ['visx-20hz', { live: '1', charts: 'visx', sseInterval: '50' }],
    ]) {
      const { context, page, cdp } = await open(rate, query);
      r.live[label] = await liveRun(page, cdp);
      const l = r.live[label];
      console.log(`live ${label} (machine ${l.idle.machinePct}/${l.scrolling.machinePct}/${l.fast.machinePct}%): idle ${JSON.stringify(l.idle.frames)} cpu ${JSON.stringify(l.idle.cpu)}`);
      console.log(`  scrolling ${JSON.stringify(l.scrolling.frames)} cpu ${JSON.stringify(l.scrolling.cpu)}`);
      console.log(`  fast ${JSON.stringify(l.fast.frames)} cpu ${JSON.stringify(l.fast.cpu)} msgs/s ${l.messagesPerSecond} renders/s ${l.rendersPerSecond}`);
      await context.close();
    }
  }
}

if (want('reconnect')) {
  // Kill the sidecar hard, start a new one on a new port with a new token, hand it over as the shell would.
  const { context, page } = await open(1, { live: '1', charts: 'visx' });
  await page.waitForFunction(() => (window.__spike.hooks.sse?.messages ?? 0) > 2, null, { timeout: 20_000 });
  const before = await page.evaluate(() => ({ ...window.__spike.hooks.sse }));
  const killedAt = Date.now();
  sidecar.child.kill();
  await sidecar.exited;
  const detected = await page.waitForFunction((n) => window.__spike.hooks.sse.reconnects > n, before.reconnects, { timeout: 10_000 }).then(() => Date.now() - killedAt);
  sidecar = await startSidecar({ allowedOrigins: [web.origin] });
  await page.evaluate(([port, token]) => window.__spike.setConnection(port, token, 2), [sidecar.port, sidecar.token]);
  await page.waitForFunction((m) => window.__spike.hooks.sse.connects >= 2 && window.__spike.hooks.sse.messages > m, before.messages, { timeout: 15_000 });
  const recoveredMs = Date.now() - killedAt;
  const jumpAfter = await page.evaluate(() => window.__spike.jumpTo(777_777));
  const after = await page.evaluate(() => ({ ...window.__spike.hooks.sse }));
  out.reconnect = { lossDetectedMs: detected, recoveredMs, sidecarReadyMs: Math.round(sidecar.readyMs), jumpAfterRestartMs: jumpAfter?.ms ?? null, connects: after.connects, errors: after.errors.slice(-3) };
  console.log('\nreconnect', JSON.stringify(out.reconnect));
  await context.close();
}

await fs.mkdir(path.join(root, 'results'), { recursive: true });
const tag = `${newHeadless ? 'display' : '60hz'}${extraQuery.window === '0' ? '-nowindow' : ''}`;
const file = path.join(root, 'results', `bench-${tag}-${rates.join('-')}x-${Date.now()}.json`);
await fs.writeFile(file, JSON.stringify(out, null, 2));
console.log(`\nwrote ${file}`);
await browser.close();
await sidecar.stop();
await web.close();
