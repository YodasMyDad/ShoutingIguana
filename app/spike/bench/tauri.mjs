// Drives the real Tauri release build (WebView2). Usage: node bench/tauri.mjs <coldstart|lifecycle|perf|reconnect|all>
import { execFileSync, spawn } from 'node:child_process';
import fs from 'node:fs';
import net from 'node:net';
import path from 'node:path';
import { chromium } from 'playwright';
import { processMemory, summarise, waitForQuiet } from './stats.mjs';
import { jsMemory, jumps, liveRun, scrollRun, sleep, SPEEDS } from './scenarios.mjs';

const root = path.resolve(import.meta.dirname, '..');
const EXE = path.join(root, 'src-tauri', 'target', 'release', 'si-spike.exe');
const DATA_DIR = path.join(process.env.LOCALAPPDATA, 'uk.co.aptitude.si-spike');
const results = path.join(root, 'results');
fs.mkdirSync(results, { recursive: true });

async function freePort() {
  return new Promise((resolve) => {
    const server = net.createServer().listen(0, '127.0.0.1', () => {
      const { port } = server.address();
      server.close(() => resolve(port));
    });
  });
}

function readLog(file) {
  if (!fs.existsSync(file)) return [];
  return fs.readFileSync(file, 'utf8').split('\n').filter(Boolean).map((line) => JSON.parse(line));
}

async function waitForLog(file, predicate, timeoutMs = 20_000) {
  const started = Date.now();
  while (Date.now() - started < timeoutMs) {
    const hit = readLog(file).find(predicate);
    if (hit) return hit;
    await sleep(20);
  }
  throw new Error(`timed out waiting for log event in ${file}`);
}

function alive(pid) {
  try {
    process.kill(pid, 0);
    return true;
  } catch {
    return false;
  }
}

async function waitGone(pid, timeoutMs) {
  const started = Date.now();
  while (Date.now() - started < timeoutMs) {
    if (!alive(pid)) return Date.now() - started;
    await sleep(10);
  }
  return null;
}

function processTree(rootPid) {
  const script = 'Get-CimInstance Win32_Process | Select-Object ProcessId, ParentProcessId, Name | ConvertTo-Json -Compress';
  const all = JSON.parse(execFileSync('powershell', ['-NoProfile', '-Command', script], { encoding: 'utf8' }));
  const children = new Map();
  for (const p of all) {
    if (!children.has(p.ParentProcessId)) children.set(p.ParentProcessId, []);
    children.get(p.ParentProcessId).push(p);
  }
  const tree = [];
  const walk = (pid) => {
    for (const child of children.get(pid) ?? []) {
      tree.push({ pid: child.ProcessId, name: child.Name });
      walk(child.ProcessId);
    }
  };
  walk(rootPid);
  return tree;
}

function closeWindow(pid) {
  execFileSync('powershell', ['-NoProfile', '-Command', `(Get-Process -Id ${pid}).CloseMainWindow() | Out-Null`]);
}

async function launch({ env = {}, cdpPort = null, label = 'run' } = {}) {
  const logFile = path.join(results, `tauri-${label}-${Date.now()}.jsonl`);
  const fullEnv = { ...process.env, SPIKE_LOG: logFile, ...env };
  if (cdpPort) fullEnv.WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS = `--remote-debugging-port=${cdpPort}`;
  const t0 = Date.now();
  const child = spawn(EXE, [], { env: fullEnv, detached: true, stdio: 'ignore' });
  child.unref();
  return { pid: child.pid, t0, logFile };
}

async function stopApp(app) {
  if (!alive(app.pid)) return;
  closeWindow(app.pid);
  if ((await waitGone(app.pid, 8_000)) === null) process.kill(app.pid);
}

// ---------------------------------------------------------------------------------------------------------------
async function coldStart(runs = 6) {
  fs.rmSync(DATA_DIR, { recursive: true, force: true }); // first launch creates the WebView2 profile, as on a new install
  const rows = [];
  for (let i = 0; i < runs; i++) {
    const app = await launch({ label: `coldstart${i}` });
    const rowsPainted = await waitForLog(app.logFile, (e) => e.event === 'webview' && e.name === 'first-rows-painted', 30_000);
    const log = readLog(app.logFile);
    const at = (predicate) => log.find(predicate)?.epochMs;
    const webviewAt = (name) => log.find((e) => e.event === 'webview' && e.name === name)?.value?.epochMs;
    const row = {
      firstLaunch: i === 0,
      rustMainMs: at((e) => e.event === 'rust-main') - app.t0,
      pageLoadStartedMs: at((e) => e.event === 'page-load' && e.phase === 'Started') - app.t0,
      firstPaintMs: webviewAt('first-paint') - app.t0,
      firstContentfulPaintMs: webviewAt('first-contentful-paint') - app.t0,
      sidecarReadyMs: at((e) => e.event === 'sidecar-ready') - app.t0,
      sidecarSpawnToReadyMs: log.find((e) => e.event === 'sidecar-ready')?.spawnToReadyMs,
      firstRowsPaintedMs: rowsPainted.value.epochMs - app.t0,
    };
    rows.push(row);
    console.log(JSON.stringify(Object.fromEntries(Object.entries(row).map(([k, v]) => [k, typeof v === 'number' ? Math.round(v) : v]))));
    await sleep(1_000);
    await stopApp(app);
    const stopped = readLog(app.logFile).find((e) => e.event === 'exit-sidecar-stopped');
    if (i === 0) console.log('normal exit:', JSON.stringify(stopped));
    await sleep(1_500);
  }
  const warm = rows.filter((r) => !r.firstLaunch);
  return {
    firstLaunch: rows[0],
    warm: Object.fromEntries(['rustMainMs', 'pageLoadStartedMs', 'firstContentfulPaintMs', 'sidecarReadyMs', 'firstRowsPaintedMs'].map((k) => [k, summarise(warm.map((r) => r[k]))])),
  };
}

/** A sidecar that takes 3 s to start: the shell must still paint at its usual time. */
async function slowSidecar() {
  const app = await launch({ label: 'slowsidecar', env: { SI_STARTUP_DELAY_MS: '3000' } });
  const rowsPainted = await waitForLog(app.logFile, (e) => e.event === 'webview' && e.name === 'first-rows-painted', 30_000);
  const log = readLog(app.logFile);
  const result = {
    firstContentfulPaintMs: Math.round(log.find((e) => e.event === 'webview' && e.name === 'first-contentful-paint')?.value?.epochMs - app.t0),
    sidecarReadyMs: Math.round(log.find((e) => e.event === 'sidecar-ready')?.epochMs - app.t0),
    firstRowsPaintedMs: Math.round(rowsPainted.value.epochMs - app.t0),
  };
  await stopApp(app);
  console.log('slow sidecar', JSON.stringify(result));
  return result;
}

// ---------------------------------------------------------------------------------------------------------------
async function lifecycleCase(label, env, how) {
  const app = await launch({ env, label: `life-${label}` });
  const spawned = await waitForLog(app.logFile, (e) => e.event === 'sidecar-spawned');
  await waitForLog(app.logFile, (e) => e.event === 'sidecar-ready');
  await sleep(1_500);
  const webview = processTree(app.pid).filter((p) => p.name === 'msedgewebview2.exe').map((p) => p.pid);
  const killedAt = Date.now();
  if (how === 'close') closeWindow(app.pid);
  else process.kill(app.pid); // TerminateProcess, like Task Manager's End task on the process
  const appGoneAfterMs = (await waitGone(app.pid, 10_000)) === null ? null : Date.now() - killedAt;
  const sidecarGoneMs = await waitGone(spawned.pid, 6_000);
  const sidecarExitedAfterMs = sidecarGoneMs === null ? null : Date.now() - killedAt;
  await sleep(1_000);
  const webviewLeft = webview.filter(alive);
  const result = {
    label,
    how,
    appGoneAfterMs,
    sidecarExitedAfterMs: sidecarExitedAfterMs ?? 'still running after 6 s (orphaned)',
    webviewProcessesLeft: webviewLeft.length,
    log: readLog(app.logFile).filter((e) => e.event.startsWith('exit') || e.event === 'sidecar-terminated').map((e) => ({ event: e.event, graceful: e.graceful, waitedMs: e.waitedMs && Math.round(e.waitedMs), code: e.code })),
  };
  if (sidecarGoneMs === null) process.kill(spawned.pid); // clean up the orphan
  console.log(JSON.stringify(result));
  return result;
}

async function lifecycle() {
  return [
    await lifecycleCase('close-nothing', { SPIKE_EXIT_MODE: 'none', SPIKE_ORPHAN_FIX: 'none' }, 'close'),
    await lifecycleCase('close-kill', { SPIKE_EXIT_MODE: 'kill', SPIKE_ORPHAN_FIX: 'none' }, 'close'),
    await lifecycleCase('close-graceful', { SPIKE_EXIT_MODE: 'graceful', SPIKE_ORPHAN_FIX: 'stdin' }, 'close'),
    await lifecycleCase('hardkill-nothing', { SPIKE_ORPHAN_FIX: 'none' }, 'kill'),
    await lifecycleCase('hardkill-ppid', { SPIKE_ORPHAN_FIX: 'ppid' }, 'kill'),
    await lifecycleCase('hardkill-stdin', { SPIKE_ORPHAN_FIX: 'stdin' }, 'kill'),
    await lifecycleCase('hardkill-job', { SPIKE_ORPHAN_FIX: 'job' }, 'kill'),
  ];
}

// ---------------------------------------------------------------------------------------------------------------
async function withWebView2(label, action) {
  const cdpPort = await freePort();
  const app = await launch({ cdpPort, label });
  await waitForLog(app.logFile, (e) => e.event === 'webview' && e.name === 'first-rows-painted', 30_000);
  const browser = await chromium.connectOverCDP(`http://127.0.0.1:${cdpPort}`);
  try {
    const page = browser.contexts()[0].pages()[0];
    return await action({ app, browser, page });
  } finally {
    await browser.close().catch(() => undefined);
    await stopApp(app);
  }
}

function webviewMemory(appPid) {
  const tree = processTree(appPid);
  const pids = [appPid, ...tree.filter((p) => p.name === 'msedgewebview2.exe').map((p) => p.pid)];
  const list = processMemory(pids);
  const sidecar = tree.find((p) => p.name === 'spike-sidecar.exe');
  return {
    uiPrivateMB: Number(list.reduce((s, p) => s + p.privateMB, 0).toFixed(1)),
    uiWorkingSetMB: Number(list.reduce((s, p) => s + p.workingSetMB, 0).toFixed(1)),
    processes: list.map((p) => `${p.name}:${p.privateMB}`).join(' '),
    sidecarPrivateMB: sidecar ? processMemory([sidecar.pid])[0]?.privateMB : null,
  };
}

async function perf() {
  return withWebView2('perf', async ({ app, page }) => {
    const out = { userAgent: await page.evaluate(() => navigator.userAgent), dpr: await page.evaluate(() => devicePixelRatio) };
    const cdp = await page.context().newCDPSession(page);
    await cdp.send('Performance.enable');
    out.firstLoadMarks = await page.evaluate(() => ({ ...window.__spike.marks }));
    out.memoryAtStart = { ...(await jsMemory(page, cdp)), ...webviewMemory(app.pid) };
    for (const rate of [1, 4]) {
      await page.goto('http://tauri.localhost/?live=0&charts=none');
      await page.waitForFunction(() => window.__spike.marks.firstRowsPainted !== undefined, null, { timeout: 20_000 });
      await cdp.send('Emulation.setCPUThrottlingRate', { rate });
      const r = (out[`${rate}x`] = {});
      r.firstRowsSinceMountMs = await page.evaluate(() => window.__spike.marks.firstRowsPainted - window.__spike.marks.gridMount);
      for (const [name, speed] of Object.entries(SPEEDS)) {
        r[`scroll_${name}`] = await scrollRun(page, cdp, speed);
        console.log(`${rate}x scroll ${name}`, JSON.stringify(r[`scroll_${name}`].frames), JSON.stringify(r[`scroll_${name}`].cpu));
      }
      r.memoryAfterSweep = { ...(await jsMemory(page, cdp)), ...webviewMemory(app.pid) };
      r.jumps = await jumps(page);
      r.memoryAfterJumps = { ...(await jsMemory(page, cdp)), ...webviewMemory(app.pid) };
      console.log(`${rate}x jumps`, JSON.stringify(r.jumps), 'memory', JSON.stringify(r.memoryAfterJumps));
      for (const charts of ['echarts', 'visx']) {
        await page.goto(`http://tauri.localhost/?live=1&charts=${charts}`);
        await page.waitForFunction(() => window.__spike.marks.firstRowsPainted !== undefined, null, { timeout: 20_000 });
        r[`live_${charts}`] = await liveRun(page, cdp);
        const l = r[`live_${charts}`];
        console.log(`${rate}x live ${charts}: idle ${JSON.stringify(l.idle.frames)} ${JSON.stringify(l.idle.cpu)} scrolling ${JSON.stringify(l.scrolling.frames)} ${JSON.stringify(l.scrolling.cpu)}`);
      }
      await cdp.send('Emulation.setCPUThrottlingRate', { rate: 1 });
    }
    return out;
  });
}

async function reconnect() {
  return withWebView2('reconnect', async ({ app, page }) => {
    await page.waitForFunction(() => (window.__spike.hooks.sse?.messages ?? 0) > 2, null, { timeout: 20_000 });
    const before = await page.evaluate(() => ({ ...window.__spike.hooks.sse }));
    const spawned = readLog(app.logFile).filter((e) => e.event === 'sidecar-spawned').at(-1);
    const killedAt = Date.now();
    process.kill(spawned.pid); // the sidecar crashes
    await page.waitForFunction((m) => window.__spike.hooks.sse.connects > 1 && window.__spike.hooks.sse.messages > m, before.messages, { timeout: 20_000 });
    const recoveredMs = Date.now() - killedAt;
    const log = readLog(app.logFile);
    const respawned = log.filter((e) => e.event === 'sidecar-ready').at(-1);
    const jumpAfter = await page.evaluate(() => window.__spike.jumpTo(777_777));
    const after = await page.evaluate(() => ({ ...window.__spike.hooks.sse }));
    const shapes = log.filter((e) => e.event === 'sidecar-log' && e.entry?.event === 'request-shape').map((e) => e.entry);
    return {
      recoveredMs,
      newSidecarReadyAfterKillMs: Math.round(respawned.epochMs - killedAt),
      generation: respawned.generation,
      jumpAfterRestartMs: jumpAfter?.ms ?? null,
      connects: after.connects,
      lastErrors: after.errors.slice(-2),
      requestShapes: [...new Map(shapes.map((s) => [`${s.method} ${s.path} ${s.origin} ${s.outcome}`, s])).values()],
    };
  });
}

const which = process.argv[2] ?? 'all';
const out = {};
if (which !== 'lifecycle') {
  out.machineLoadBefore = await waitForQuiet();
  console.log('machine load before start', JSON.stringify(out.machineLoadBefore));
}
if (which === 'coldstart' || which === 'all') {
  out.coldStart = await coldStart();
  out.slowSidecar = await slowSidecar();
}
if (which === 'lifecycle' || which === 'all') out.lifecycle = await lifecycle();
if (which === 'reconnect' || which === 'all') {
  out.reconnect = await reconnect();
  console.log('reconnect', JSON.stringify({ ...out.reconnect, requestShapes: out.reconnect.requestShapes.length }));
}
if (which === 'perf' || which === 'all') out.perf = await perf();
const file = path.join(results, `tauri-${which}-${Date.now()}.json`);
fs.writeFileSync(file, JSON.stringify(out, null, 2));
console.log(`wrote ${file}`);
