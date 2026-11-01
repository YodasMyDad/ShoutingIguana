// Summaries of frame samples and CDP metrics.
import { execFileSync } from 'node:child_process';
import os from 'node:os';

/** Whole-machine CPU use (all logical CPUs), to spot runs disturbed by other work on the machine. */
export const cpuSnapshot = () => os.cpus().map((cpu) => ({ ...cpu.times }));
export function machineBusyPct(before, after) {
  let busy = 0;
  let total = 0;
  after.forEach((a, i) => {
    const b = before[i];
    const all = a.user - b.user + (a.nice - b.nice) + (a.sys - b.sys) + (a.idle - b.idle) + (a.irq - b.irq);
    busy += all - (a.idle - b.idle);
    total += all;
  });
  return total ? Number(((100 * busy) / total).toFixed(1)) : 0;
}

export async function waitForQuiet(maxPct = 8, timeoutMs = 600_000) {
  const started = Date.now();
  for (;;) {
    const before = cpuSnapshot();
    await new Promise((resolve) => setTimeout(resolve, 3_000));
    const pct = machineBusyPct(before, cpuSnapshot());
    if (pct <= maxPct || Date.now() - started > timeoutMs) return { pct, waitedMs: Date.now() - started };
  }
}

const round = (value, digits = 1) => (value === null || value === undefined ? null : Number(value.toFixed(digits)));

export function percentile(sorted, p) {
  if (!sorted.length) return null;
  return sorted[Math.min(sorted.length - 1, Math.floor(sorted.length * p))];
}

/** Frame intervals and empty rows per frame. "missed" counts intervals over 1.5x the refresh interval. */
export function frameStats(samples) {
  const intervals = samples.slice(1).map((s) => s.dt).filter((dt) => dt > 0);
  const sorted = [...intervals].sort((a, b) => a - b);
  const refresh = percentile(sorted, 0.5) ?? 16.7;
  const empties = samples.map((s) => s.placeholders + s.blanks);
  const emptySorted = [...empties].sort((a, b) => a - b);
  const sum = intervals.reduce((a, b) => a + b, 0);
  return {
    frames: intervals.length,
    fps: round(intervals.length ? 1000 / (sum / intervals.length) : 0),
    p50: round(percentile(sorted, 0.5)),
    p95: round(percentile(sorted, 0.95)),
    p99: round(percentile(sorted, 0.99)),
    max: round(sorted[sorted.length - 1] ?? null),
    over16_7: round((100 * intervals.filter((dt) => dt > 16.7).length) / Math.max(1, intervals.length)),
    over33_4: round((100 * intervals.filter((dt) => dt > 33.4).length) / Math.max(1, intervals.length)),
    missedPct: round((100 * intervals.filter((dt) => dt > refresh * 1.5).length) / Math.max(1, intervals.length)),
    emptyFramesPct: round((100 * empties.filter((n) => n > 0).length) / Math.max(1, empties.length)),
    emptyRowsMean: round(empties.reduce((a, b) => a + b, 0) / Math.max(1, empties.length), 2),
    emptyRowsP95: percentile(emptySorted, 0.95),
    emptyRowsMax: emptySorted[emptySorted.length - 1] ?? 0,
    blankRowsMax: Math.max(0, ...samples.map((s) => s.blanks)),
    visibleRows: Math.max(0, ...samples.map((s) => s.visible)),
  };
}

export async function metrics(cdp) {
  const { metrics: list } = await cdp.send('Performance.getMetrics');
  return Object.fromEntries(list.map((m) => [m.name, m.value]));
}

/** Main-thread busy share over a window, from Chromium's own task accounting. */
export function cpuStats(before, after, wallMs) {
  const seconds = wallMs / 1000;
  const share = (name) => round((100 * (after[name] - before[name])) / seconds);
  return {
    busyPct: share('TaskDuration'),
    scriptPct: share('ScriptDuration'),
    layoutPct: share('LayoutDuration'),
    stylePct: share('RecalcStyleDuration'),
  };
}

export function summarise(values) {
  const sorted = values.filter((v) => typeof v === 'number' && Number.isFinite(v)).sort((a, b) => a - b);
  return { n: sorted.length, p50: round(percentile(sorted, 0.5)), p95: round(percentile(sorted, 0.95)), max: round(sorted[sorted.length - 1] ?? null) };
}

/** Private bytes and working set of the given Windows processes, in MB. */
export function processMemory(pids) {
  if (!pids.length) return [];
  const script = `Get-Process -Id ${pids.join(',')} -ErrorAction SilentlyContinue | Select-Object Id, ProcessName, PrivateMemorySize64, WorkingSet64 | ConvertTo-Json -Compress`;
  const output = execFileSync('powershell', ['-NoProfile', '-Command', script], { encoding: 'utf8' }).trim();
  if (!output) return [];
  const parsed = JSON.parse(output);
  return (Array.isArray(parsed) ? parsed : [parsed]).map((p) => ({
    pid: p.Id,
    name: p.ProcessName,
    privateMB: round(p.PrivateMemorySize64 / 1048576),
    workingSetMB: round(p.WorkingSet64 / 1048576),
  }));
}
