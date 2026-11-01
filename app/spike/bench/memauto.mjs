// Memory profile with the page driving itself (src/perf/autopilot.ts), so nothing but the OS is watching unless
// "dumps" is on, and then only a raw CDP connection that enables no domain.
// Usage: node bench/memauto.mjs <label> '{"query":"overscan=4","flags":"","dumps":false,"tray":"low","delayMs":0,"idleMs":120000}'
import { execFileSync, spawn } from 'node:child_process';
import fs from 'node:fs';
import net from 'node:net';
import path from 'node:path';
import { attachPage, connectCdp, evaluate, memoryDump } from './cdp.mjs';
import { waitForQuiet } from './stats.mjs';

const root = path.resolve(import.meta.dirname, '..');
const EXE = path.join(root, 'src-tauri', 'target', 'release', 'si-spike.exe');
const WRY_DEFAULT_ARGS = '--disable-features=msWebOOUI,msPdfOOUI,msSmartScreenProtection';
const MB = 1048576;
const round = (bytes) => Math.round((bytes / MB) * 10) / 10;
const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

const label = process.argv[2] ?? 'auto';
const options = JSON.parse(process.argv[3] ?? '{}');
const runDir = path.join(root, 'results', 'memory', `${label}-${Date.now()}`);
fs.mkdirSync(runDir, { recursive: true });

const readLog = (file) =>
  fs.existsSync(file) ? fs.readFileSync(file, 'utf8').split('\n').filter(Boolean).map((line) => JSON.parse(line)) : [];

function alive(pid) {
  try {
    process.kill(pid, 0);
    return true;
  } catch {
    return false;
  }
}

/** The shell, its sidecar, and every msedgewebview2.exe using this app's user data folder, with --type. */
// options.udf gives this instance its own WebView2 user data folder (EBWebView-<udf>), so two can run side by side.
const udfMarker = options.udf ? `EBWebView-${options.udf}` : null;

export function processes(appPid) {
  const script = `
$all = Get-CimInstance Win32_Process | Where-Object { ($_.ProcessId -eq ${appPid} -and ${appPid} -gt 0) -or ($_.Name -eq 'spike-sidecar.exe' -and $_.ParentProcessId -eq ${appPid}) -or ($_.Name -eq 'msedgewebview2.exe' -and $_.CommandLine -like '*uk.co.aptitude.si-spike*') }
$ids = @($all | ForEach-Object { $_.ProcessId })
$perf = @{}; Get-CimInstance Win32_PerfRawData_PerfProc_Process | Where-Object { $ids -contains $_.IDProcess } | ForEach-Object { $perf[[int]$_.IDProcess] = $_ }
$all | ForEach-Object { $c = $perf[[int]$_.ProcessId]; [pscustomobject]@{ pid = $_.ProcessId; name = $_.Name; cmd = $_.CommandLine; privateWs = $c.WorkingSetPrivate; privateBytes = $c.PrivateBytes } } | ConvertTo-Json -Compress -Depth 3`;
  const output = execFileSync('powershell', ['-NoProfile', '-Command', script], { encoding: 'utf8', maxBuffer: 16 * MB }).trim();
  return (output ? [].concat(JSON.parse(output)) : [])
    .filter((p) => p.name !== 'msedgewebview2.exe' || (udfMarker ? (p.cmd ?? '').includes(udfMarker) : !/EBWebView-\w/.test(p.cmd ?? '')))
    .map((p) => {
    let type = p.name === 'si-spike.exe' ? 'shell' : p.name === 'spike-sidecar.exe' ? 'sidecar' : 'browser';
    const typeArg = /--type=(\S+)/.exec(p.cmd ?? '')?.[1];
    if (typeArg) type = typeArg === 'utility' ? (/--utility-sub-type=([\w.]+)/.exec(p.cmd)?.[1] ?? 'utility') : typeArg;
    type = type
      .replace('network.mojom.NetworkService', 'network')
      .replace('storage.mojom.StorageService', 'storage')
      .replace('tracing.mojom.TracingService', 'tracing (measurement)')
      .replace('gpu-process', 'gpu')
      .replace('crashpad-handler', 'crashpad');
    return { pid: p.pid, type, privateWsMB: round(Number(p.privateWs ?? 0)), privateMB: round(Number(p.privateBytes ?? 0)) };
  });
}

function totals(list) {
  const ui = list.filter((p) => p.type !== 'sidecar' && !p.type.startsWith('tracing'));
  const sum = (key) => Math.round(ui.reduce((s, p) => s + p[key], 0) * 10) / 10;
  return { uiPrivateWsMB: sum('privateWsMB'), uiPrivateMB: sum('privateMB'), sidecarPrivateWsMB: list.find((p) => p.type === 'sidecar')?.privateWsMB ?? null };
}

function describe(list) {
  const t = totals(list);
  const parts = list
    .filter((p) => p.type !== 'sidecar')
    .sort((a, b) => b.privateWsMB - a.privateWsMB)
    .map((p) => `${p.type} ${p.privateWsMB}/${p.privateMB}`);
  return `UI ${t.uiPrivateWsMB} WS / ${t.uiPrivateMB} private [${parts.join(', ')}] sidecar WS ${t.sidecarPrivateWsMB}`;
}

/** Renderer allocators that matter, in MB: committed size and, where reported, live objects. */
function rendererAllocators(events, rendererPids) {
  const allocators = Object.assign({}, ...events.filter((e) => e.ph === 'v' && rendererPids.includes(e.pid)).map((e) => e.args.dumps.allocators ?? {}));
  const value = (name, attr) => {
    const a = allocators[name]?.attrs?.[attr];
    return a ? round(parseInt(a.value, 16)) : undefined;
  };
  const sizeOf = (prefix) => {
    if (value(prefix, 'size') !== undefined) return value(prefix, 'size');
    const children = Object.keys(allocators).filter((n) => n.startsWith(prefix + '/') && n.split('/').length === prefix.split('/').length + 1);
    return Math.round(children.reduce((s, n) => s + (sizeOf(n) ?? 0), 0) * 10) / 10;
  };
  return {
    oilpan: `${sizeOf('blink_gc')} (live ${value('blink_gc/main/allocated_objects', 'size') ?? '?'})`,
    partitionBuffer: `${sizeOf('partition_alloc/partitions/buffer')} (live ${value('partition_alloc/partitions/buffer', 'allocated_objects_size') ?? '?'})`,
    partitionArrayBuffer: sizeOf('partition_alloc/partitions/array_buffer'),
    malloc: `${sizeOf('malloc')} (live ${value('malloc', 'allocated_objects_size') ?? '?'})`,
    v8: sizeOf('v8/main/heap'),
    fontCaches: sizeOf('font_caches'),
    gpuTransfer: sizeOf('gpu'),
    ccTilesGpuSide: sizeOf('cc'),
  };
}

async function freePort() {
  return new Promise((resolve) => {
    const server = net.createServer().listen(0, '127.0.0.1', () => {
      const { port } = server.address();
      server.close(() => resolve(port));
    });
  });
}

const quiet = options.noQuietWait ? { pct: 0, waitedMs: 0 } : await waitForQuiet(10, 300_000);
console.log(`machine load ${quiet.pct}% after ${Math.round(quiet.waitedMs / 1000)} s; ${label} ${JSON.stringify(options)}`);

const logFile = path.join(runDir, 'shell.jsonl');
const query = ['autopilot=1', options.tray ? `tray=${options.tray}` : '', options.idleMs ? `idleMs=${options.idleMs}` : '', options.query ?? '']
  .filter(Boolean)
  .join('&');
const port = options.dumps || options.throttle ? await freePort() : null;
const env = { ...process.env, SPIKE_LOG: logFile, SPIKE_QUERY: query };
if (options.delayMs) env.SI_STARTUP_DELAY_MS = String(options.delayMs);
if (udfMarker) env.WEBVIEW2_USER_DATA_FOLDER = path.join(process.env.LOCALAPPDATA, 'uk.co.aptitude.si-spike', udfMarker);
env.WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS = [WRY_DEFAULT_ARGS, '--js-flags=--expose-gc', port ? `--remote-debugging-port=${port}` : '', options.flags ?? '']
  .filter(Boolean)
  .join(' ');
const child = spawn(EXE, [], { env, detached: true, stdio: 'ignore' });
child.unref();
const appPid = child.pid;

let cdp = null;
let pageSession = null;
if (port) {
  cdp = await connectCdp(port);
  pageSession = await attachPage(cdp);
  if (options.throttle) await cdp.send('Emulation.setCPUThrottlingRate', { rate: options.throttle }, pageSession);
}

const points = [];
async function measure(name) {
  const list = processes(appPid);
  const entry = { name, ...totals(list), processes: list };
  if (cdp && options.dumps && !name.startsWith('hidden')) {
    const dump = await memoryDump(cdp);
    fs.writeFileSync(path.join(runDir, `${points.length}-${name.replace(/\W+/g, '-')}.json`), JSON.stringify(dump.events));
    entry.renderer = rendererAllocators(dump.events, list.filter((p) => p.type === 'renderer').map((p) => p.pid));
  }
  points.push(entry);
  console.log(`## ${name}: ${describe(list)}`);
  if (entry.renderer) console.log(`   renderer allocators MB: ${JSON.stringify(entry.renderer)}`);
}

// "start": the shell before any data (needs delayMs so the sidecar is still starting).
if (options.delayMs) {
  const started = Date.now();
  while (!readLog(logFile).some((e) => e.event === 'webview' && e.name === 'first-contentful-paint') && Date.now() - started < 30_000) await sleep(50);
  await sleep(1_500);
  await measure('start (shell, no data)');
}

const seen = new Set();
const deadline = Date.now() + (options.deadlineMin ?? 10) * 60_000;
while (Date.now() < deadline) {
  const phases = readLog(logFile).filter((e) => e.event === 'webview' && e.name === 'autopilot').map((e) => e.value);
  const next = phases.find((p) => !seen.has(p.phase));
  if (!next) {
    await sleep(100);
    continue;
  }
  seen.add(next.phase);
  if (next.phase === 'done') break;
  if (next.phase === 'hidden') {
    let elapsed = 0;
    for (const at of [5, 30, 60]) {
      await sleep((at - elapsed) * 1000);
      elapsed = at;
      await measure(`hidden (${options.tray}) +${at} s`);
    }
    continue;
  }
  await sleep(1_500);
  await measure(`${next.phase} (JS heap ${next.jsHeapMB} MB)`);
  if (next.phase === 'after GC' && cdp) {
    await cdp.send('Memory.simulatePressureNotification', { level: 'critical' }, pageSession);
    await sleep(3_000);
    await measure('after critical pressure');
  }
}

const trayLog = readLog(logFile).filter((e) => e.event.startsWith('tray-'));
if (trayLog.length) console.log('tray:', JSON.stringify(trayLog.map(({ event, result, suspended, setIsVisible }) => ({ event, result, suspended, setIsVisible }))));
if (cdp) cdp.close();
if (alive(appPid)) {
  execFileSync('powershell', ['-NoProfile', '-Command', `(Get-Process -Id ${appPid}).CloseMainWindow() | Out-Null`]);
  for (let i = 0; i < 160 && alive(appPid); i++) await sleep(50);
  if (alive(appPid)) process.kill(appPid);
}
await sleep(2_000);
const leftovers = processes(0);
if (leftovers.length) console.log('LEFTOVER:', JSON.stringify(leftovers));
fs.writeFileSync(path.join(runDir, 'summary.json'), JSON.stringify({ label, options, points }, null, 2));
console.log(`wrote ${path.join(runDir, 'summary.json')}`);
void evaluate;
