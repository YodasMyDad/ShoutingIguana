// A grid's query the engine refused for something the route holds other than a filter (a sort, a column), in the app's
// words. The engine's own sentence names a column by its id ("The grid can't sort by 'title'"), which no screen shows
// (docs/v2-ui-architecture.md, Rules every slice follows, 7): the column is named as its header names it, and one
// the app doesn't know is said to be one this version doesn't have. Pure.
import type { GridSort } from '../api/grids';

export interface RefusedQuery {
  sort: readonly GridSort[];
  columns: readonly string[];
}

/**
 * What to say of a refused view. `errors` is the `invalid-query` answer's field errors; `nameOf` a column's name, or
 * undefined for one the app doesn't know; `noun` what the grid lists ("URLs", "links").
 */
export function queryRefusalWords(
  errors: Readonly<Record<string, readonly string[]>>,
  query: RefusedQuery,
  nameOf: (id: string) => string | undefined,
  noun: string,
): string {
  for (const path of Object.keys(errors)) {
    const sorted = /^sort\[(\d+)\]/.exec(path);
    if (sorted) {
      const id = query.sort[Number(sorted[1])]?.column;
      const name = id === undefined ? undefined : nameOf(id);
      return name
        ? `The ${noun} can’t be sorted by ${name}: filter by it instead.`
        : `The view is sorted by a column this version of the app doesn’t have.`;
    }
    const column = /^columns\[(\d+)\]/.exec(path);
    if (column) {
      const id = query.columns[Number(column[1])];
      const name = id === undefined ? undefined : nameOf(id);
      return name
        ? `The ${noun} can’t show ${name} here.`
        : `The view shows a column this version of the app doesn’t have.`;
    }
  }
  // Anything else the view asked for, said without the engine's ids: the view can be reset.
  const first = Object.values(errors)[0]?.[0];
  return first && !/['‘’"“”]/.test(first) ? first : `The view asks for something the ${noun} can’t show.`;
}
