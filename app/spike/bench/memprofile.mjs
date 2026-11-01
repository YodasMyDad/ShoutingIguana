// Memory profile of the Tauri release build. Usage: node bench/memprofile.mjs <scenario> [label] [json options]
//   floor   the shell with an empty page (hello.html), OS counters only, then one memory-infra dump with CDP attached
//   idle    the real app at idle without CDP attached, OS counters only
//   full    memory-infra dumps at start, first page, after 54k rows, after the end jump, 2 min idle, GC, pressure
//   quick   the same up to the end jump, then 40 s idle, GC and pressure (for comparing fixes)
//   tray    after 54k rows, hide to the tray with options.mode (plain|low|suspend|low+suspend) and watch memory
// Options: {"query":"pos=top","flags":"--disable-gpu-rasterization","delayMs":6000}
import { execFileSync, spawn } from 'node:child_process';
import fs from 'node:fs';
import net from 'node:net';
import path from 'node:path';
import { chromium } from 'playwright';
import { waitForQuiet } from './stats.mjs';
import { scrollGesture, sleep, SPEEDS } from './scenarios.mjs';

const root = path.resolve(import.meta.dirname, '..');
const EXE = path.join(root, 'src-tauri', 'target', 'release', 'si-spike.exe');
const DATA_DIR = path.join(process.env.LOCALAPPDATA, 'uk.co.aptitude.si-spike');
const WRY_DEFAULT_ARGS = '--disable-features=msWebOOUI,msPdfOOUI,msSmartScreenProtection';
const MB = 1048576;
const round = (bytes) => Math.round((bytes / MB) * 10) / 10;

const scenario = process.argv[2] ?? 'full';
const label = process.argv[3] ?? scenario;
const options = JSON.parse(process.argv[4] ?? '{}');
const runDir = path.join(root, 'results', 'memory', `${label}-${Date.now()}`);
fs.mkdirSync(runDir, { recursive: true });

async function freePort() {
  return new Promise((resolve) => {
    const server = net.createServer().listen(0, '127.0.0.1', () => {
      const { port } = server.address();
      server.close(() => resolve(port));
    });
  });
}

function alive(pid) {
  try {
    process.kill(pid, 0);
    return true;
  } catch {
    return false;
  }
}

function readLog(file) {
  return fs.existsSync(file) ? fs.readFileSync(file, 'utf8').split('\n').filter(Boolean).map((l) => JSON.parse(l)) : [];
}

async function waitForLog(file, predicate, timeoutMs = 30_000) {
  const started = Date.now();
  while (Date.now() - started < timeoutMs) {
    const hit = readLog(file).find(predicate);
    if (hit) return hit;
    await sleep(50);
  }
  throw new Error('timed out waiting for a log event');
}

/** Every process of this app: the shell, its sidecar, and each msedgewebview2.exe using our user data folder. */
function processes(appPid) {
  const script = `
$all = Get-CimInstance Win32_Process | Where-Object { ($_.ProcessId -eq ${appPid} -and ${appPid} -gt 0) -or ($_.Name -eq 'spike-sidecar.exe' -and $_.ParentProcessId -eq ${appPid}) -or ($_.Name -eq 'msedgewebview2.exe' -and $_.CommandLine -like '*uk.co.aptitude.si-spike*') }
$ids = @($all | ForEach-Object { $_.ProcessId })
$perf = @{}; Get-CimInstance Win32_PerfRawData_PerfProc_Process | Where-Object { $ids -contains $_.IDProcess } | ForEach-Object { $perf[[int]$_.IDProcess] = $_ }
$all | ForEach-Object { $c = $perf[[int]$_.ProcessId]; [pscustomobject]@{ pid = $_.ProcessId; name = $_.Name; cmd = $_.CommandLine; privateWs = $c.WorkingSetPrivate; privateBytes = $c.PrivateBytes } } | ConvertTo-Json -Compress -Depth 3`;
  const output = execFileSync('powershell', ['-NoProfile', '-Command', script], { encoding: 'utf8', maxBuffer: 16 * MB }).trim();
  const list = output ? [].concat(JSON.parse(output)) : [];
  return list.map((p) => {
    let type = p.name === 'si-spike.exe' ? 'shell' : p.name === 'spike-sidecar.exe' ? 'sidecar' : 'browser';
    const typeArg = /--type=(\S+)/.exec(p.cmd ?? '')?.[1];
    if (typeArg) type = typeArg === 'utility' ? `utility:${/--utility-sub-type=([\w.]+)/.exec(p.cmd)?.[1] ?? '?'}` : typeArg;
    return { pid: p.pid, type, privateWsMB: round(Number(p.privateWs ?? 0)), privateMB: round(Number(p.privateBytes ?? 0)) };
  });
}

function totals(list) {
  const ui = list.filter((p) => p.type !== 'sidecar');
  const sum = (key) => Math.round(ui.reduce((s, p) => s + p[key], 0) * 10) / 10;
  return { uiPrivateWsMB: sum('privateWsMB'), uiPrivateMB: sum('privateMB'), sidecarPrivateMB: list.find((p) => p.type === 'sidecar')?.privateMB ?? null };
}

function shortType(type) {
  return type
    .replace('utility:network.mojom.NetworkService', 'network')
    .replace('utility:storage.mojom.StorageService', 'storage')
    .replace('gpu-process', 'gpu')
    .replace('crashpad-handler', 'crashpad');
}

function describe(list) {
  const t = totals(list);
  const parts = list
    .filter((p) => p.type !== 'sidecar')
    .sort((a, b) => b.privateMB - a.privateMB)
    .map((p) => `${shortType(p.type)} ${p.privateWsMB}/${p.privateMB}`);
  return `UI ${t.uiPrivateWsMB} MB private WS / ${t.uiPrivateMB} MB private bytes [${parts.join(', ')}] sidecar ${t.sidecarPrivateMB}`;
}

async function launch({ cdp, page = 'index.html', query = options.query ?? '', flags = options.flags ?? '', delayMs = 0 }) {
  const logFile = path.join(runDir, 'shell.jsonl');
  const env = { ...process.env, SPIKE_LOG: logFile, SPIKE_START_PAGE: page, SPIKE_QUERY: query };
  if (delayMs) env.SI_STARTUP_DELAY_MS = String(delayMs);
  const port = cdp ? await freePort() : null;
  if (cdp || flags) env.WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS = [WRY_DEFAULT_ARGS, port ? `--remote-debugging-port=${port}` : '', flags].filter(Boolean).join(' ');
  const child = spawn(EXE, [], { env, detached: true, stdio: 'ignore' });
  child.unref();
  return { pid: child.pid, port, logFile };
}

async function stop(app) {
  if (alive(app.pid)) {
    execFileSync('powershell', ['-NoProfile', '-Command', `(Get-Process -Id ${app.pid}).CloseMainWindow() | Out-Null`]);
    for (let i = 0; i < 160 && alive(app.pid); i++) await sleep(50);
    if (alive(app.pid)) process.kill(app.pid);
  }
  await sleep(1_500); // let WebView2's processes go
}

async function attach(app) {
  for (let i = 0; i < 200; i++) {
    try {
      const response = await fetch(`http://127.0.0.1:${app.port}/json/version`);
      if (response.ok) break;
    } catch {
      // not listening yet
    }
    await sleep(50);
  }
  const browser = await chromium.connectOverCDP(`http://127.0.0.1:${app.port}`);
  const context = browser.contexts()[0];
  let page = context.pages()[0];
  for (let i = 0; !page && i < 100; i++) {
    await sleep(50);
    page = context.pages()[0];
  }
  const cdp = await context.newCDPSession(page);
  const bcdp = await browser.newBrowserCDPSession();
  return { browser, page, cdp, bcdp };
}

/** Builds an allocator tree from dump names; a node's size is its own size, or the sum of its children. */
function allocatorTree(allocators) {
  const root = { children: new Map() };
  for (const [name, entry] of Object.entries(allocators ?? {})) {
    let node = root;
    for (const part of name.split('/')) {
      if (!node.children.has(part)) node.children.set(part, { children: new Map() });
      node = node.children.get(part);
    }
    const size = entry.attrs?.size;
    if (size && size.units === 'bytes') node.size = parseInt(size.value, 16);
    const used = entry.attrs?.allocated_objects_size;
    if (used && used.units === 'bytes') node.used = parseInt(used.value, 16);
  }
  const total = (node) => node.size ?? [...node.children.values()].reduce((s, c) => s + total(c), 0);
  return { root, total };
}

const SKIP = new Set(['tracing', 'shared_memory', 'blink_objects', 'java_heap', 'global']);
const DETAIL = ['partition_alloc/partitions', 'v8/main/heap', 'gpu', 'cc', 'skia', 'font_caches', 'discardable', 'malloc'];

function summariseDump(events, osList) {
  const typeByPid = new Map(osList.map((p) => [p.pid, shortType(p.type)]));
  // Chromium sends a process's totals and its allocators in separate events of the same dump: merge them by pid.
  const merged = new Map();
  for (const event of events.filter((e) => e.ph === 'v' && e.args?.dumps)) {
    const current = merged.get(event.pid) ?? { allocators: {}, process_totals: undefined };
    Object.assign(current.allocators, event.args.dumps.allocators ?? {});
    current.process_totals ??= event.args.dumps.process_totals;
    merged.set(event.pid, current);
  }
  const out = [];
  for (const [pid, dumps] of merged) {
    const { root, total } = allocatorTree(dumps.allocators);
    const top = {};
    for (const [name, node] of root.children) if (!SKIP.has(name)) top[name] = round(total(node));
    const detail = {};
    for (const prefix of DETAIL) {
      let node = root;
      for (const part of prefix.split('/')) node = node?.children.get(part);
      if (!node) continue;
      for (const [name, child] of node.children) {
        const size = total(child);
        if (size >= 0.5 * MB) detail[`${prefix}/${name}`] = round(size) + (child.used !== undefined ? ` (used ${round(child.used)})` : '');
      }
    }
    const footprint = dumps.process_totals?.private_footprint_bytes;
    out.push({
      pid,
      type: typeByPid.get(pid) ?? 'tracing service (measurement only)',
      footprintMB: footprint ? round(parseInt(footprint, 16)) : null,
      allocatorsMB: Object.fromEntries(Object.entries(top).sort((a, b) => b[1] - a[1]).filter(([, v]) => v >= 0.1)),
      detailMB: detail,
    });
  }
  return out.sort((a, b) => (b.footprintMB ?? 0) - (a.footprintMB ?? 0));
}

async function memoryDump(bcdp) {
  const events = [];
  const onData = (e) => events.push(...e.value);
  bcdp.on('Tracing.dataCollected', onData);
  const complete = new Promise((resolve) => bcdp.once('Tracing.tracingComplete', resolve));
  await bcdp.send('Tracing.start', {
    traceConfig: { includedCategories: ['disabled-by-default-memory-infra'], memoryDumpConfig: { triggers: [] } },
    transferMode: 'ReportEvents',
  });
  const result = await bcdp.send('Tracing.requestMemoryDump', { deterministic: false, levelOfDetail: 'detailed' });
  await bcdp.send('Tracing.end');
  await complete;
  bcdp.off('Tracing.dataCollected', onData);
  return { success: result.success, events };
}

async function pageFacts(page, cdp) {
  const facts = await page
    .evaluate(() => ({
      jsHeapMB: Math.round(performance.memory.usedJSHeapSize / 104857.6) / 10,
      domNodes: document.getElementsByTagName('*').length,
      cachedPages: window.__spike?.cachedPages?.() ?? null,
    }))
    .catch(() => ({}));
  try {
    await cdp.send('LayerTree.enable');
    const layers = await new Promise((resolve) => {
      const timer = setTimeout(() => resolve(null), 2_000);
      cdp.once('LayerTree.layerTreeDidChange', (e) => {
        clearTimeout(timer);
        resolve(e.layers?.length ?? null);
      });
    });
    await cdp.send('LayerTree.disable');
    facts.layers = layers;
  } catch {
    facts.layers = null;
  }
  try {
    const { detachedNodes } = await cdp.send('DOM.getDetachedDomNodes');
    facts.detachedNodes = detachedNodes.length;
  } catch {
    facts.detachedNodes = null;
  }
  return facts;
}

const report = { scenario, label, options, points: [] };

async function point(app, session, name) {
  const os = processes(app.pid);
  const entry = { name, at: new Date().toISOString(), ...totals(os), processes: os };
  if (session) {
    entry.page = await pageFacts(session.page, session.cdp);
    const dump = await memoryDump(session.bcdp);
    fs.writeFileSync(path.join(runDir, `${report.points.length}-${name}.json`), JSON.stringify(dump.events));
    entry.dumpOk = dump.success;
    entry.chromium = summariseDump(dump.events, os);
  }
  report.points.push(entry);
  console.log(`\n## ${name}: ${describe(os)}`);
  if (entry.page) console.log(`   page: ${JSON.stringify(entry.page)}`);
  for (const p of entry.chromium ?? []) {
    const allocators = Object.entries(p.allocatorsMB).slice(0, 9).map(([k, v]) => `${k} ${v}`).join(', ');
    console.log(`   ${p.type} (footprint ${p.footprintMB}): ${allocators}`);
  }
  return entry;
}

async function settle(ms = 1_500) {
  await sleep(ms);
}

const quiet = await waitForQuiet(10, 300_000);
console.log(`machine load ${quiet.pct}% after ${Math.round(quiet.waitedMs / 1000)} s; run dir ${runDir}`);

if (scenario === 'floor') {
  let app = await launch({ cdp: false, page: 'hello.html' });
  await waitForLog(app.logFile, (e) => e.event === 'page-load' && e.phase === 'Finished');
  await settle(8_000);
  for (let i = 0; i < 3; i++) {
    await point(app, null, `hello-no-cdp-${i + 1}`);
    await settle(2_000);
  }
  await stop(app);
  app = await launch({ cdp: true, page: 'hello.html' });
  await waitForLog(app.logFile, (e) => e.event === 'page-load' && e.phase === 'Finished');
  const session = await attach(app);
  await settle(5_000);
  await point(app, session, 'hello-with-cdp');
  await session.browser.close().catch(() => undefined);
  await stop(app);
} else if (scenario === 'idle') {
  const app = await launch({ cdp: false });
  await waitForLog(app.logFile, (e) => e.event === 'webview' && e.name === 'first-rows-painted');
  await settle(10_000);
  for (let i = 0; i < 3; i++) {
    await point(app, null, `app-idle-no-cdp-${i + 1}`);
    await settle(2_000);
  }
  await stop(app);
} else {
  const app = await launch({ cdp: true, delayMs: options.delayMs ?? (scenario === 'full' ? 6_000 : 0) });
  const session = await attach(app);
  const { page, cdp } = session;
  await page.waitForFunction(() => window.__spike?.marks?.['first-contentful-paint'] !== undefined, null, { timeout: 30_000 });
  if (scenario === 'full') {
    await settle(1_000);
    await point(app, session, 'start (shell, no data yet)');
  }
  await page.waitForFunction(() => window.__spike.marks.firstRowsPainted !== undefined, null, { timeout: 30_000 });
  await settle();
  await point(app, session, 'first page shown');
  await scrollGesture(page, cdp, SPEEDS.sweep);
  await settle();
  await point(app, session, 'after 54k rows');
  if (scenario === 'tray') {
    await settle(3_000);
    const mode = options.mode ?? 'plain';
    await page.evaluate((m) => window.__TAURI_INTERNALS__.invoke('spike_tray', { mode: m, ms: 75_000 }), mode);
    let elapsed = 0;
    for (const at of [5, 30, 60]) {
      await sleep((at - elapsed) * 1000);
      elapsed = at;
      await point(app, null, `hidden (${mode}) +${at} s`);
    }
    await waitForLog(app.logFile, (e) => e.event === 'tray-shown', 60_000);
    await settle(3_000);
    await point(app, session, 'shown again');
    report.trayLog = readLog(app.logFile).filter((e) => e.event.startsWith('tray-'));
    console.log('tray log', JSON.stringify(report.trayLog.map((e) => ({ event: e.event, result: e.result, suspended: e.suspended }))));
  } else {
    await page.evaluate(() => window.__spike.jumpTo(999_999));
    await settle();
    await point(app, session, 'after jump to the end');
    await sleep(scenario === 'full' ? 120_000 : 40_000);
    await point(app, session, scenario === 'full' ? 'after 2 min idle' : 'after 40 s idle');
    await cdp.send('HeapProfiler.collectGarbage');
    await settle();
    await point(app, session, 'after GC');
    await cdp.send('Memory.simulatePressureNotification', { level: 'critical' });
    await settle(3_000);
    await point(app, session, 'after critical pressure');
  }
  await session.browser.close().catch(() => undefined);
  await stop(app);
}

fs.writeFileSync(path.join(runDir, 'summary.json'), JSON.stringify(report, null, 2));
console.log(`\nwrote ${path.join(runDir, 'summary.json')}`);
const leftovers = processes(0).filter((p) => p.type !== 'shell');
if (leftovers.length) console.log('LEFTOVER processes:', JSON.stringify(leftovers));
void DATA_DIR;
