// A check's rows as a treegrid whose rows open in place (A-06): a URL's row, then, while it's open, a row for each
// of its other references (the other pages linking to it, the other URLs its page names). Any number can be open at
// once, so where a row is comes from the open rows above it. Pure, so the index arithmetic is unit-tested.

/** An open row: its place among the check's rows, and how many rows it shows under it. */
export interface OpenRow {
  row: number;
  children: number;
}

export type RowPlace =
  | { kind: 'row'; row: number }
  /** A row under an open one: `child` from 0. */
  | { kind: 'child'; row: number; child: number };

export interface RowsLayout {
  /** The check's rows and every open row's rows under it. */
  rowCount: number;
  place: (index: number) => RowPlace;
  /** Where one of the check's own rows is in the grid. */
  indexOf: (row: number) => number;
}

/**
 * The layout of `rowCount` rows with these open. `open` in any order; an open row past the end, or one that shows
 * nothing under it, takes no room.
 */
export function rowsLayout(rowCount: number, open: readonly OpenRow[]): RowsLayout {
  const opened = open
    .filter((entry) => entry.row >= 0 && entry.row < rowCount && entry.children > 0)
    .sort((a, b) => a.row - b.row);
  const children = opened.reduce((sum, entry) => sum + entry.children, 0);
  return {
    rowCount: rowCount + children,
    place: (index) => {
      let above = 0;
      for (const entry of opened) {
        const at = entry.row + above;
        if (index <= at) break;
        if (index <= at + entry.children) return { kind: 'child', row: entry.row, child: index - at - 1 };
        above += entry.children;
      }
      return { kind: 'row', row: index - above };
    },
    indexOf: (row) => {
      let above = 0;
      for (const entry of opened) {
        if (entry.row >= row) break;
        above += entry.children;
      }
      return row + above;
    },
  };
}

/** Up to this many rows, a check's list opens every row that has more to show: the whole of it is read at once. */
export const OPEN_ALL_ROWS = 20;
