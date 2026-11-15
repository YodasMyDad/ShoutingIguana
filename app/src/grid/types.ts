import type { ReactNode } from 'react';

/** A column of the grid primitive. The full grid (presets, chooser, filters) builds on this in the slices. */
export interface GridColumn<Row> {
  id: string;
  /** The header's text, also the copied header. */
  header: string;
  /** px. */
  width: number;
  /** The least the user can make it, px, where a column needs more than the grid's least (`MIN_COLUMN_WIDTH`). */
  minWidth?: number;
  /** Pinned to the grid's left edge: it stays in view while the grid scrolls sideways. Pinned columns come first. */
  pinned?: boolean;
  /** Numbers right-align with tabular figures. */
  numeric?: boolean;
  /** URLs and code in the monospace face. */
  mono?: boolean;
  /** `ascending` or `descending` when the grid is sorted by this column. */
  sort?: 'ascending' | 'descending';
  /**
   * Its place in a sort of more than one key (1 to 3), shown beside its arrow; left out when the grid sorts by one
   * column or none. Only the first key's header carries `aria-sort`; the others say their place in words.
   */
  sortOrder?: number;
  /** Words after the header's own, quieter: what the column's values have in common ("on example.com"). Not copied. */
  headerDetail?: string;
  /** Its header is a button that sorts by it (the grid's `onSort`). */
  sortable?: boolean;
  /**
   * Why this column doesn't sort, in a grid whose other columns do ("Links can’t be sorted by their anchor text"):
   * its header's tooltip, and what Enter or Space on its header says, so a header that does nothing says why.
   */
  sortHint?: string;
  /** What the cell shows. Crawled text renders as text, never as HTML. */
  cell: (row: Row) => ReactNode;
  /** The cell as plain text, for copying and for the accessible name of rich cells. */
  text: (row: Row) => string;
}

/**
 * Rows grouped under rows that open and close (the ARIA treegrid pattern), by index into the grid's rows: a group's
 * row, then its rows while it's open.
 */
export interface GridTree {
  /** 1 for a group's row, 2 for a row in a group. */
  level: (index: number) => number;
  /** Whether a group is open; undefined for a row that doesn't open. */
  expanded: (index: number) => boolean | undefined;
  /** The row of the group a row is in, or null for a group's own row. */
  parent: (index: number) => number | null;
  /**
   * Opens or closes the group at an index. Returns its row's index afterwards when that moved (opening one group
   * closed another above it).
   */
  onToggle: (index: number, open: boolean) => number | undefined;
  /**
   * A group's row is a thing of its own that Enter and a double-click open, as they do any other row (a sitemap
   * index has URLs of its own to show): the group then opens and closes with Right, Left and its chevron only. When
   * left out, Enter and a double-click on a group open and close it. A function says it of each row: an issue's URL
   * opens to show where it's found, while Enter still inspects it, and a group's row still opens on Enter.
   */
  activates?: boolean | ((index: number) => boolean) | undefined;
}

/** What Ctrl+C asks the data layer for: rows by index, fetched if they aren't loaded. */
export type RowFetcher<Row> = (indices: readonly number[]) => Promise<readonly (Row | undefined)[]>;

/** The rows in view, overscan included, as the grid tells the data layer. */
export interface GridRange {
  first: number;
  last: number;
  /** The scrollbar's thumb is held: rows fly past, so pages are fetched once it settles, and nothing is prefetched. */
  scrubbing: boolean;
}
