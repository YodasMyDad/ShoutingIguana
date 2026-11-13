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
 * The sort after a column's header is pressed, in a grid that sorts by up to `max` keys (the URL grid's three). A
 * plain press sorts by the column alone: ascending, then descending, then the engine's own order, as `nextSort`; a
 * press on a column of a sort of several keys starts again from it alone, ascending. With Shift (`extend`) the
 * column joins the sort as its last key, or, already in it, turns about and then leaves it, the others kept in their
 * order. `full` says a key couldn't be added: the sort has `max` already.
 */
export function nextMultiSort(
  sort: readonly GridSort[],
  column: string,
  extend: boolean,
  max: number,
): { sort: GridSort[]; full: boolean } {
  const at = sort.findIndex((entry) => entry.column === column);
  if (!extend) {
    return { sort: sort.length > 1 ? [{ column, desc: false }] : nextSort(sort, column), full: false };
  }
  if (at < 0) {
    return sort.length >= max
      ? { sort: [...sort], full: true }
      : { sort: [...sort, { column, desc: false }], full: false };
  }
  const current = sort[at];
  return {
    sort: current?.desc
      ? sort.filter((_, index) => index !== at)
      : sort.map((entry, index) => (index === at ? { column, desc: true } : entry)),
    full: false,
  };
}

/** What a sort of more than one key says when another can't be added. */
export function sortFullWords(max: number): string {
  return `A grid sorts by ${max} columns at most. Shift-click a sorted column’s header to take it out of the sort.`;
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
