// Runs the memory scenario from inside the page, so it can be measured with no DevTools attached (DevTools itself
// retains memory, such as every response body while the Network domain is on). Each phase is reported to the shell's
// log; the harness takes its measurements after each report, while the page waits.
import { invoke } from '@tauri-apps/api/core';
import { report } from '../connection';
import { params } from '../params';
import { jumpTo, marks, measureViewport } from './probe';

const SETTLE_MS = 8_000; // time for the harness to measure after each report
const SWEEP_ROWS = Number(params.get('sweepRows') ?? 50_000);
const REPORT_EVERY = Number(params.get('reportEvery') ?? 0); // 0: one report after the whole sweep
const SWEEP_PX_PER_SECOND = 50_000;

const sleep = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

async function phase(name: string, extra: Record<string, unknown> = {}) {
  const heap = (performance as unknown as { memory?: { usedJSHeapSize: number } }).memory?.usedJSHeapSize ?? 0;
  report('autopilot', { phase: name, jsHeapMB: Math.round(heap / 104857.6) / 10, ...extra });
  await sleep(SETTLE_MS);
}

/** Scrolls the grid by itself at a steady speed until `rows` rows have passed, or the end of the list. */
function sweep(rows: number): Promise<void> {
  const scroller = document.querySelector<HTMLElement>('.grid');
  if (!scroller) return Promise.resolve();
  const startRow = measureViewport().first;
  return new Promise((resolve) => {
    let last = 0;
    let previousRow = -1;
    let stuckFrames = 0;
    const step = (now: number) => {
      if (last) scroller.scrollTop += (SWEEP_PX_PER_SECOND * (now - last)) / 1000;
      last = now;
      const row = measureViewport().first;
      stuckFrames = row === previousRow ? stuckFrames + 1 : 0;
      previousRow = row;
      if (row - startRow >= rows || stuckFrames > 60) resolve();
      else requestAnimationFrame(step);
    };
    requestAnimationFrame(step);
  });
}

export async function runAutopilot() {
  while (marks.firstRowsPainted === undefined) await sleep(100);
  await phase('first page shown');
  if (REPORT_EVERY > 0) {
    for (let done = REPORT_EVERY; done <= SWEEP_ROWS; done += REPORT_EVERY) {
      await sweep(REPORT_EVERY);
      await phase(`after ${done / 1000}k rows`);
    }
  } else {
    await sweep(SWEEP_ROWS);
    await phase(`after ${SWEEP_ROWS / 1000}k rows`);
  }
  await jumpTo(999_999);
  await phase('after jump to the end');
  const tray = params.get('tray');
  if (tray) {
    // The shell hides the window for 75 s, optionally lowering WebView2's memory target or suspending it.
    await invoke('spike_tray', { mode: tray, ms: 75_000 });
    report('autopilot', { phase: 'hidden' });
    await sleep(85_000);
    await phase('shown again');
  } else {
    await sleep(Number(params.get('idleMs') ?? 120_000));
    await phase('after idle');
    const gc = (globalThis as unknown as { gc?: () => void }).gc;
    if (gc) {
      gc();
      await phase('after GC');
    }
  }
  report('autopilot', { phase: 'done' });
}
