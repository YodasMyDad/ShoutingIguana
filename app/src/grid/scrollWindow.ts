// A windowed scroll space for very tall grids (docs/v2-ui-spike.md, risks 1 and 2). 1M rows of 32 px is 32M px:
// Chromium clamps a scroll extent at 33,554,428 px, and the compositor works in float32, so past ~16.7M device px rows
// and the sticky header are drawn at visibly wrong positions. So the real scroll area is at most WINDOW_PX tall and
// maps onto the virtual offset through `base`; near an edge of the window the base moves (in whole rows) and scrollTop
// is adjusted in the same task, so nothing visible changes. Pure maths: useRowWindow applies it to the DOM.

/** The tallest real scroll area. Small enough to be exact at any display scaling. */
export const WINDOW_PX = 2_000_000;

export interface ScrollSpace {
  /** The virtual offset of the window's top: always a whole number of rows. */
  base: number;
  /** The virtual height of all rows. */
  total: number;
  rowHeight: number;
  /** The window's height: a whole number of rows, so every base is too, the last one included. */
  windowPx: number;
}

/**
 * The window for rows of this height: WINDOW_PX cut down to a whole number of rows. 28 px rows don't divide 2,000,000,
 * and a window that isn't whole rows leaves the last base short of the end, with the last row out of reach.
 */
export function windowFor(rowHeight: number, windowPx = WINDOW_PX): number {
  return rowHeight > 0 ? Math.floor(windowPx / rowHeight) * rowHeight : windowPx;
}

export function createScrollSpace(total: number, rowHeight: number, windowPx = WINDOW_PX): ScrollSpace {
  return { base: 0, total, rowHeight, windowPx: windowFor(rowHeight, windowPx) };
}

/** Whether the space is windowed at all: only when the rows don't fit in one window. */
export function isWindowed(space: ScrollSpace): boolean {
  return space.windowPx > 0 && space.total > space.windowPx;
}

/** The height to give the rows' container. */
export function domHeight(space: ScrollSpace): number {
  return isWindowed(space) ? space.windowPx : space.total;
}

/** The largest virtual scroll offset, given the visible height of the rows' area. */
export function maxOffset(space: ScrollSpace, viewportRows: number): number {
  return Math.max(0, space.total - viewportRows);
}

/** A base that puts `virtualOffset` in the middle of the window, in whole rows, within bounds. */
export function centredBase(space: ScrollSpace, virtualOffset: number): number {
  if (!isWindowed(space)) return 0;
  const maxBase = Math.max(0, space.total - space.windowPx);
  const centred = Math.min(maxBase, Math.max(0, virtualOffset - space.windowPx / 2));
  return Math.min(maxBase, Math.round(centred / space.rowHeight) * space.rowHeight);
}

/**
 * After a scroll: the base to use, which moves only when the real position nears an edge of the window and there's
 * more beyond it. `scrollTop` is the real position; `viewport` the visible height of the rows' area.
 */
export function rebaseForScroll(space: ScrollSpace, scrollTop: number, viewport: number): number {
  if (!isWindowed(space)) return 0;
  const edge = space.windowPx / 4;
  const nearTop = scrollTop < edge && space.base > 0;
  const nearBottom = scrollTop > space.windowPx - edge - viewport && space.base < space.total - space.windowPx;
  return nearTop || nearBottom ? centredBase(space, space.base + scrollTop) : space.base;
}

/**
 * For a jump to `virtualOffset`: the base, and the real scrollTop that shows it. The base moves only when the target
 * is outside what the current window can show.
 */
export function placeOffset(
  space: ScrollSpace,
  virtualOffset: number,
  viewport: number,
): { base: number; scrollTop: number } {
  const target = Math.min(Math.max(0, virtualOffset), maxOffset(space, viewport));
  if (!isWindowed(space)) return { base: 0, scrollTop: target };
  const fits = target >= space.base && target <= space.base + space.windowPx - viewport;
  const base = fits ? space.base : centredBase(space, target);
  return { base, scrollTop: target - base };
}

/** The virtual offset a real scrollTop shows. */
export function virtualOffset(space: ScrollSpace, scrollTop: number): number {
  return space.base + scrollTop;
}
