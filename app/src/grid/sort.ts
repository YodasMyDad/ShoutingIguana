// Sorting a grid from its headers: one column at a time, as a header press does.
import type { GridSort } from '../api/grids';

/** The sort after a column's header is pressed: by it ascending, then descending, then the engine's own order. */
export function nextSort(sort: readonly GridSort[], column: string): GridSort[] {
  const current = sort.find((entry) => entry.column === column);
  if (!current) return [{ column, desc: false }];
  return current.desc ? [] : [{ column, desc: true }];
}
