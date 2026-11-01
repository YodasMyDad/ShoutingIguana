// Measurement hooks for the bench. Per animation frame: the frame interval, and how many rows inside the viewport
// were placeholders (data not loaded yet) or blank (no row element rendered there at all).
import { report } from '../connection';

export const ROW_HEIGHT = 28;
export const HEADER_HEIGHT = 32;

export type GridHandle = {
  scroller: HTMLElement;
  rowCount: () => number;
  /** Virtual scroll offset (the grid may map a small DOM scroll range onto a taller virtual one). */
  scrollOffset: () => number;
  scrollToIndex: (index: number) => void;
};

export type FrameSample = { t: number; dt: number; placeholders: number; blanks: number; visible: number };

let grid: GridHandle | null = null;
let rowElements: HTMLCollectionOf<Element> | null = null;
let recording: FrameSample[] | null = null;
let lastFrameAt = 0;

export const marks: Record<string, number> = {};

export function registerGrid(handle: GridHandle | null) {
  grid = handle;
  rowElements = handle ? handle.scroller.getElementsByClassName('row') : null;
}

export function measureViewport() {
  if (!grid || !rowElements) return { placeholders: 0, blanks: 0, visible: 0, first: 0 };
  const scroller = grid.scroller;
  const top = grid.scrollOffset();
  const height = scroller.clientHeight - HEADER_HEIGHT;
  const first = Math.floor(top / ROW_HEIGHT);
  const last = Math.min(grid.rowCount() - 1, Math.floor((top + height - 1) / ROW_HEIGHT));
  const visible = Math.max(0, last - first + 1);
  let rendered = 0;
  let placeholders = 0;
  for (let i = 0; i < rowElements.length; i++) {
    const element = rowElements[i] as HTMLElement;
    const index = Number(element.dataset.index);
    if (index >= first && index <= last) {
      rendered++;
      if (element.classList.contains('placeholder')) placeholders++;
    }
  }
  return { placeholders, blanks: visible - rendered, visible, first };
}

function onFrame(t: number) {
  if (!recording) return;
  const dt = lastFrameAt ? t - lastFrameAt : 0;
  lastFrameAt = t;
  const viewport = measureViewport();
  recording.push({ t, dt, placeholders: viewport.placeholders, blanks: viewport.blanks, visible: viewport.visible });
  requestAnimationFrame(onFrame);
}

export function startFrames() {
  recording = [];
  lastFrameAt = 0;
  requestAnimationFrame(onFrame);
}

export function stopFrames() {
  const samples = recording ?? [];
  recording = null;
  return samples;
}

/** Resolves just after the browser has painted the frame it is about to produce. */
function afterNextPaint(): Promise<number> {
  return new Promise((resolve) =>
    requestAnimationFrame(() => {
      const channel = new MessageChannel();
      channel.port1.onmessage = () => resolve(performance.now());
      channel.port2.postMessage(0);
    }),
  );
}

/** Resolves when every row in the viewport has data, at the paint that shows it. */
export function waitFilled(timeoutMs = 10_000): Promise<number | null> {
  const started = performance.now();
  return new Promise((resolve) => {
    const check = () => {
      const viewport = measureViewport();
      if (viewport.visible > 0 && viewport.placeholders === 0 && viewport.blanks === 0) {
        const channel = new MessageChannel();
        channel.port1.onmessage = () => resolve(performance.now());
        channel.port2.postMessage(0);
        return;
      }
      if (performance.now() - started > timeoutMs) {
        resolve(null);
        return;
      }
      requestAnimationFrame(check);
    };
    requestAnimationFrame(check);
  });
}

export async function jumpTo(index: number) {
  if (!grid) return null;
  await afterNextPaint();
  const started = performance.now();
  grid.scrollToIndex(index);
  const filledAt = await waitFilled();
  const viewport = measureViewport();
  return { ms: filledAt === null ? null : filledAt - started, firstVisible: viewport.first };
}

export function markGridMount() {
  if (marks.gridMount !== undefined) return;
  marks.gridMount = performance.now();
  void waitFilled(30_000).then((at) => {
    if (at === null) return;
    marks.firstRowsPainted = at;
    report('first-rows-painted', { epochMs: performance.timeOrigin + at, sinceMountMs: at - marks.gridMount });
  });
}

export function markConnection() {
  if (marks.connection === undefined) marks.connection = performance.now();
}

export function observePaints() {
  new PerformanceObserver((list) => {
    for (const entry of list.getEntries()) {
      marks[entry.name] = entry.startTime;
      report(entry.name, { epochMs: performance.timeOrigin + entry.startTime, sinceNavigationMs: entry.startTime });
    }
  }).observe({ type: 'paint', buffered: true });
}
