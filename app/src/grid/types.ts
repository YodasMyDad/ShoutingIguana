import type { ReactNode } from 'react';

/** A column of the grid primitive. The full grid (presets, chooser, filters) builds on this in the slices. */
export interface GridColumn<Row> {
  id: string;
  /** The header's text, also the copied header. */
  header: string;
  /** px. */
  width: number;
  /** Numbers right-align with tabular figures. */
  numeric?: boolean;
  /** URLs and code in the monospace face. */
  mono?: boolean;
  /** `ascending` or `descending` when the grid is sorted by this column. */
  sort?: 'ascending' | 'descending';
  /** What the cell shows. Crawled text renders as text, never as HTML. */
  cell: (row: Row) => ReactNode;
  /** The cell as plain text, for copying and for the accessible name of rich cells. */
  text: (row: Row) => string;
}

/** What Ctrl+C asks the data layer for: rows by index, fetched if they aren't loaded. */
export type RowFetcher<Row> = (indices: readonly number[]) => Promise<readonly (Row | undefined)[]>;
