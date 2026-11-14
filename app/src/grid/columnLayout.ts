// A grid's columns as the user laid them out (docs/v2-ui-brief.md, The grid: "column resize, reorder, pin, hide and
// presets, remembered per view"): their widths, which are pinned to the left edge, and their order where the grid
// keeps it itself (the URL grid's order is its route's `cols`). Kept per grid with the app's preferences, and with a
// saved view. Pure: what a layout is, how it changes, how it applies to a grid's columns, and how a stored one is read.
import type { GridColumn } from './types';

export interface ColumnLayout {
  /** Widths the user set, px, by column id. A column without one has the grid's own width. */
  widths?: Record<string, number> | undefined;
  /** Columns pinned to the grid's left edge, in their order there. */
  pinned?: string[] | undefined;
  /** The columns' order, for a grid that keeps it here (not in its route). Columns it doesn't name keep theirs. */
  order?: string[] | undefined;
}

/** The narrowest a column can be made: room for a short header and its sort arrow. */
export const MIN_COLUMN_WIDTH = 56;
/** The widest: wider than any window the app runs in is never useful, and keeps a stored value sane. */
export const MAX_COLUMN_WIDTH = 1_600;
/** How far a key moves a column's edge (Shift+Left and Shift+Right on its header). */
export const RESIZE_STEP = 16;
/** Each grid keeps at most this many columns' widths, and the app at most this many grids' layouts. */
const MAX_COLUMNS = 120;
const MAX_GRIDS = 24;
const COLUMN_ID = /^[A-Za-z0-9._-]{1,64}$/;
const GRID_ID = /^[a-z][a-z0-9.-]{0,47}$/;

export function clampWidth(width: number): number {
  return Math.round(Math.min(MAX_COLUMN_WIDTH, Math.max(MIN_COLUMN_WIDTH, width)));
}

/** True when the layout changes nothing: no widths, nothing pinned, no order of its own. */
export function isEmptyLayout(layout: ColumnLayout | undefined): boolean {
  return (
    !layout ||
    (Object.keys(layout.widths ?? {}).length === 0 &&
      (layout.pinned ?? []).length === 0 &&
      (layout.order ?? []).length === 0)
  );
}

/** The layout with a column's width set, or put back to the grid's own (`null`). */
export function withWidth(layout: ColumnLayout | undefined, id: string, width: number | null): ColumnLayout {
  const others = Object.fromEntries(Object.entries(layout?.widths ?? {}).filter(([column]) => column !== id));
  return tidy({ ...layout, widths: width === null ? others : { ...others, [id]: clampWidth(width) } });
}

/** The layout with a column pinned to the left edge (after those pinned already), or unpinned. */
export function withPinned(layout: ColumnLayout | undefined, id: string, pinned: boolean): ColumnLayout {
  const others = (layout?.pinned ?? []).filter((other) => other !== id);
  return tidy({ ...layout, pinned: pinned ? [...others, id] : others });
}

/**
 * Ids in their order after one moved to another place among them. `to` is its index afterwards; a column never
 * crosses the pinned columns' edge (a pinned column moves among the pinned, the others among themselves).
 */
export function movedOrder(ids: readonly string[], id: string, to: number, pinned: readonly string[] = []): string[] {
  const from = ids.indexOf(id);
  if (from < 0) return [...ids];
  const pins = new Set(pinned);
  const pinnedCount = ids.filter((other) => pins.has(other)).length;
  const isPinned = pins.has(id);
  const low = isPinned ? 0 : pinnedCount;
  const high = isPinned ? pinnedCount - 1 : ids.length - 1;
  const target = Math.min(high, Math.max(low, to));
  const next = ids.filter((other) => other !== id);
  next.splice(target, 0, id);
  return next;
}

/**
 * A grid's columns as the layout has them: the user's widths (within the grid's least for a column, and the bounds),
 * pinned ones first and marked, then the rest, in the layout's order where the grid keeps one here (`ownOrder`).
 */
export function applyColumnLayout<Row>(
  columns: readonly GridColumn<Row>[],
  layout: ColumnLayout | undefined,
  ownOrder = false,
): GridColumn<Row>[] {
  if (isEmptyLayout(layout)) return [...columns];
  const widths = layout?.widths ?? {};
  let ordered = [...columns];
  if (ownOrder && layout?.order?.length) {
    const place = new Map(layout.order.map((id, index) => [id, index]));
    // Columns the order names go where it says; one it doesn't (new since) keeps its place after the one before it.
    ordered = sortKeepingStrangers(ordered, (column) => place.get(column.id));
  }
  const pinned = layout?.pinned ?? [];
  const pins = new Map(pinned.map((id, index) => [id, index]));
  const withWidths = ordered.map((column) => {
    const width = widths[column.id];
    const sized =
      width === undefined ? column : { ...column, width: Math.max(column.minWidth ?? 0, clampWidth(width)) };
    return pins.has(column.id) ? { ...sized, pinned: true } : sized;
  });
  const front = withWidths
    .filter((column) => pins.has(column.id))
    .sort((a, b) => (pins.get(a.id) ?? 0) - (pins.get(b.id) ?? 0));
  return [...front, ...withWidths.filter((column) => !pins.has(column.id))];
}

/** Sorted by a rank where one is known; the unranked keep their place after the column before them. */
function sortKeepingStrangers<T>(items: readonly T[], rank: (item: T) => number | undefined): T[] {
  const ranked = items.filter((item) => rank(item) !== undefined).sort((a, b) => (rank(a) ?? 0) - (rank(b) ?? 0));
  const result: T[] = [];
  let next = 0;
  for (const item of items) {
    if (rank(item) === undefined) result.push(item);
    else {
      const placed = ranked[next++];
      if (placed !== undefined) result.push(placed);
    }
  }
  return result;
}

/** The layout without parts that say nothing (an empty width list, no pins, no order). */
function tidy(layout: ColumnLayout): ColumnLayout {
  return {
    ...(layout.widths && Object.keys(layout.widths).length > 0 ? { widths: layout.widths } : {}),
    ...(layout.pinned && layout.pinned.length > 0 ? { pinned: layout.pinned } : {}),
    ...(layout.order && layout.order.length > 0 ? { order: layout.order } : {}),
  };
}

/** The layout with its order set (for a grid that keeps its order here). */
export function withOrder(layout: ColumnLayout | undefined, order: readonly string[]): ColumnLayout {
  return tidy({ ...layout, order: [...order] });
}

/** A stored layout, checked: anything unusable is left out. */
export function readColumnLayout(value: unknown): ColumnLayout | undefined {
  if (typeof value !== 'object' || value === null) return undefined;
  const stored = value as Record<string, unknown>;
  const widths: Record<string, number> = {};
  if (typeof stored.widths === 'object' && stored.widths !== null) {
    for (const [id, width] of Object.entries(stored.widths).slice(0, MAX_COLUMNS)) {
      if (COLUMN_ID.test(id) && typeof width === 'number' && Number.isFinite(width)) widths[id] = clampWidth(width);
    }
  }
  const ids = (list: unknown) =>
    Array.isArray(list)
      ? [...new Set(list.filter((id): id is string => typeof id === 'string' && COLUMN_ID.test(id)))].slice(
          0,
          MAX_COLUMNS,
        )
      : [];
  const layout = tidy({ widths, pinned: ids(stored.pinned), order: ids(stored.order) });
  return isEmptyLayout(layout) ? undefined : layout;
}

/** Every grid's stored layout, checked. */
export function readColumnLayouts(value: unknown): Record<string, ColumnLayout> {
  const layouts: Record<string, ColumnLayout> = {};
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return layouts;
  for (const [grid, stored] of Object.entries(value).slice(0, MAX_GRIDS)) {
    const layout = GRID_ID.test(grid) ? readColumnLayout(stored) : undefined;
    if (layout) layouts[grid] = layout;
  }
  return layouts;
}
