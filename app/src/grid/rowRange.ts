// Which rows to render. Every row is the same height, so the range is one division and nothing is stored per row:
// TanStack Virtual's per-row storage was 94 MB of JS heap after scrolling 1M rows in the spike.

export interface RowRange {
  /** The first row to render, overscan included. */
  first: number;
  /** The last row to render, overscan included; first - 1 when there are none. */
  last: number;
  /** The rows actually in view, without overscan. */
  firstVisible: number;
  lastVisible: number;
}

/** The rows overscanned above and below the viewport: enough for a fling, few enough to keep its garbage down. */
export const DEFAULT_OVERSCAN = 6;

export function computeRange(
  offset: number,
  viewport: number,
  rowHeight: number,
  count: number,
  overscan = DEFAULT_OVERSCAN,
): RowRange {
  if (count <= 0 || rowHeight <= 0) return { first: 0, last: -1, firstVisible: 0, lastVisible: -1 };
  const firstVisible = Math.min(count - 1, Math.max(0, Math.floor(offset / rowHeight)));
  const lastVisible = Math.min(count - 1, Math.max(firstVisible, Math.ceil((offset + viewport) / rowHeight) - 1));
  return {
    first: Math.max(0, firstVisible - overscan),
    last: Math.min(count - 1, lastVisible + overscan),
    firstVisible,
    lastVisible,
  };
}

/** Rows that fit in the viewport: what Page Up and Page Down move by. */
export function rowsPerPage(viewport: number, rowHeight: number): number {
  return Math.max(1, Math.floor(viewport / rowHeight));
}

/** The first row whose whole height is in view: the row at the top may be partly under the sticky header. */
export function firstFullyVisible(offset: number, rowHeight: number, range: RowRange): number {
  if (rowHeight <= 0 || range.lastVisible < range.firstVisible) return range.firstVisible;
  return Math.min(range.lastVisible, Math.max(range.firstVisible, Math.ceil(offset / rowHeight)));
}

/**
 * The scrollLeft that brings a column into view: unchanged when it's already fully visible, otherwise just enough to
 * show it at the left (moving left) or right (moving right). A column wider than the view shows its start.
 */
export function scrollLeftToReveal(left: number, width: number, scrollLeft: number, viewport: number): number {
  if (left < scrollLeft) return left;
  if (left + width > scrollLeft + viewport) return Math.min(left, left + width - viewport);
  return scrollLeft;
}

/**
 * The offset that brings `index` into view: unchanged when it's already fully visible, otherwise just enough to show
 * it at the top (moving up) or bottom (moving down), or at the alignment asked for.
 */
export function offsetToReveal(
  index: number,
  offset: number,
  viewport: number,
  rowHeight: number,
  align: 'auto' | 'start' | 'center' | 'end' = 'auto',
): number {
  const top = index * rowHeight;
  const bottom = top + rowHeight;
  switch (align) {
    case 'start':
      return top;
    case 'end':
      return bottom - viewport;
    case 'center':
      return top - (viewport - rowHeight) / 2;
    case 'auto':
      if (top < offset) return top;
      if (bottom > offset + viewport) return bottom - viewport;
      return offset;
  }
}
