// Row selection as sorted, disjoint ranges, so selecting a million rows (Ctrl+A, Shift+End) stores two numbers.
// Shift-extension is kept apart from what's committed, so extending again replaces the last extension.

export type Range = readonly [first: number, last: number];

export interface Selection {
  /** Committed ranges: sorted, disjoint, never touching. */
  ranges: readonly Range[];
  /** Where a Shift-extension starts. */
  anchor: number | null;
  /** The live Shift-extension, from the anchor to here. */
  extent: number | null;
}

export const EMPTY_SELECTION: Selection = { ranges: [], anchor: null, extent: null };

/** Merges ranges into sorted, disjoint, non-adjacent ones. */
export function normalise(ranges: readonly Range[]): Range[] {
  const sorted = ranges.map(([a, b]) => (a <= b ? ([a, b] as const) : ([b, a] as const))).sort((x, y) => x[0] - y[0]);
  const merged: [number, number][] = [];
  for (const [first, last] of sorted) {
    const previous = merged[merged.length - 1];
    if (previous && first <= previous[1] + 1) previous[1] = Math.max(previous[1], last);
    else merged.push([first, last]);
  }
  return merged;
}

/** Everything selected now: committed ranges plus the live extension. */
export function effectiveRanges(selection: Selection): Range[] {
  if (selection.anchor === null || selection.extent === null) return [...selection.ranges];
  return normalise([...selection.ranges, [selection.anchor, selection.extent]]);
}

export function isSelected(selection: Selection, index: number): boolean {
  return effectiveRanges(selection).some(([first, last]) => index >= first && index <= last);
}

export function selectedCount(selection: Selection): number {
  return effectiveRanges(selection).reduce((sum, [first, last]) => sum + last - first + 1, 0);
}

/** Space, Ctrl+click: flips one row (committing any extension first) and makes it the anchor. */
export function toggleRow(selection: Selection, index: number): Selection {
  const committed = effectiveRanges(selection);
  const selected = committed.some(([first, last]) => index >= first && index <= last);
  const ranges = selected
    ? committed.flatMap<Range>(([first, last]) => {
        if (index < first || index > last) return [[first, last]];
        return [
          ...(first < index ? [[first, index - 1] as const] : []),
          ...(index < last ? [[index + 1, last] as const] : []),
        ];
      })
    : normalise([...committed, [index, index]]);
  return { ranges, anchor: index, extent: null };
}

/** Shift+arrow, Shift+click: selects from the anchor (or `from`, when there's none) to `index`. */
export function extendTo(selection: Selection, from: number, index: number): Selection {
  const anchor = selection.anchor ?? from;
  return { ranges: selection.ranges, anchor, extent: index };
}

/** A plain click: just this row. */
export function selectOnly(index: number): Selection {
  return { ranges: [[index, index]], anchor: index, extent: null };
}

/** Ctrl+A. */
export function selectAll(count: number): Selection {
  return count > 0 ? { ranges: [[0, count - 1]], anchor: 0, extent: null } : EMPTY_SELECTION;
}

/** The first `limit` selected rows, in order, and whether more were selected than that. */
export function selectedIndices(selection: Selection, limit: number): { indices: number[]; truncated: boolean } {
  const indices: number[] = [];
  for (const [first, last] of effectiveRanges(selection)) {
    for (let index = first; index <= last; index++) {
      if (indices.length === limit) return { indices, truncated: true };
      indices.push(index);
    }
  }
  return { indices, truncated: false };
}

/** Drops rows past a new end, when the row count shrinks. */
export function clampSelection(selection: Selection, count: number): Selection {
  if (count <= 0) return EMPTY_SELECTION;
  const max = count - 1;
  const ranges = selection.ranges
    .filter(([first]) => first <= max)
    .map(([first, last]) => [first, Math.min(last, max)] as const);
  const clamp = (value: number | null) => (value === null ? null : Math.min(value, max));
  return { ranges, anchor: clamp(selection.anchor), extent: clamp(selection.extent) };
}
