// Sorting a grid from its headers: one column at a time, as a header press does.
import { useState } from 'react';
import type { GridSort } from '../api/grids';

/** The sort after a column's header is pressed: by it ascending, then descending, then the engine's own order. */
export function nextSort(sort: readonly GridSort[], column: string): GridSort[] {
  const current = sort.find((entry) => entry.column === column);
  if (!current) return [{ column, desc: false }];
  return current.desc ? [] : [{ column, desc: true }];
}

/**
 * Keeps the cursor on a header across the new grid a sort brings (a grid's `key` has its sort in it): a grid sorted
 * from its header while it had focus hands the header to the next one, for `GridCore`'s `focusHeader`. Only the grid
 * for that very sort takes it, so going back to another sort never pulls focus into a grid.
 */
export function useHeaderHandoff(sort: readonly GridSort[]) {
  const [handoff, setHandoff] = useState<{ column: string; sort: string } | null>(null);
  return {
    focusHeader: handoff !== null && handoff.sort === JSON.stringify(sort) ? handoff.column : undefined,
    sorted: (column: string, focused: boolean, next: readonly GridSort[]) =>
      setHandoff(focused ? { column, sort: JSON.stringify(next) } : null),
  };
}
