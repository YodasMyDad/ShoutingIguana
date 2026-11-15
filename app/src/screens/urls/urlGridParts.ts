// The URL grid's pieces for a view that is the URL grid narrowed to something with a fixed set of columns (one
// sitemap's URLs): the nine columns the URL grid showed before it had presets, what the query asks for to draw them,
// and their names in a list. `UrlsScreen` exports them, and the filters' chips, under the names they had there.
import { createElement } from 'react';
import type { UrlRow } from '../../api/grids';
import { listOfNames } from '../../grid/summaryWords';
import type { GridColumn } from '../../grid/types';
import { formatBytes, formatCount, formatMilliseconds } from '../../lib/format';
import { Url } from '../../ui/Url';
import { urlCellValue } from './urlColumns';

const plain = (value: number | null | undefined) => (value === null || value === undefined ? '' : String(value));
const indexability = (row: UrlRow) => (row.indexability ? urlCellValue('indexability', row.indexability) : '');
const status = (row: UrlRow) =>
  row.statusCode === null || row.statusCode === undefined
    ? row.statusClass === 'noResponse'
      ? 'No response'
      : ''
    : String(row.statusCode);

/** Columns use E3a's ids (docs/v2-sidecar-api.md, URLs), so the query asks for exactly these. */
export const FIXED_URL_COLUMNS: readonly GridColumn<UrlRow>[] = [
  {
    id: 'url',
    header: 'URL',
    width: 420,
    mono: true,
    // With its open-in-browser button, as every grid's URLs have it.
    cell: (row) => (row.url ? createElement(Url, { url: row.url, copyable: false, tabbable: false }) : ''),
    text: (row) => row.url ?? '',
  },
  { id: 'statusCode', header: 'Status', width: 104, numeric: true, cell: (row) => status(row) || '–', text: status },
  {
    id: 'indexability',
    header: 'Indexability',
    width: 168,
    cell: (row) => indexability(row) || '–',
    text: indexability,
  },
  { id: 'title', header: 'Title', width: 320, cell: (row) => row.title ?? '', text: (row) => row.title ?? '' },
  {
    id: 'depth',
    header: 'Depth',
    width: 72,
    numeric: true,
    cell: (row) => formatCount(row.depth),
    text: (row) => plain(row.depth),
  },
  {
    id: 'responseMs',
    header: 'Response',
    width: 104,
    numeric: true,
    cell: (row) => formatMilliseconds(row.responseMs),
    text: (row) => plain(row.responseMs),
  },
  {
    id: 'sizeBytes',
    header: 'Size',
    width: 96,
    numeric: true,
    cell: (row) => formatBytes(row.sizeBytes),
    text: (row) => plain(row.sizeBytes),
  },
  {
    id: 'inlinks',
    header: 'Inlinks',
    width: 88,
    numeric: true,
    cell: (row) => formatCount(row.inlinks),
    text: (row) => plain(row.inlinks),
  },
  {
    id: 'issues',
    header: 'Issues',
    width: 80,
    numeric: true,
    cell: (row) => formatCount(row.issues),
    text: (row) => plain(row.issues),
  },
];

/** What the query asks for: the columns shown, and statusClass, to tell "No response" from "not fetched". */
export const FIXED_URL_QUERY_COLUMNS = [...FIXED_URL_COLUMNS.map((column) => column.id), 'statusClass'];

/** "Inlinks", "Inlinks and Issues", "Title, Inlinks and Issues": the columns' names, in the grid's order. */
export function fixedColumnNames(ids: readonly string[]): string {
  return listOfNames(FIXED_URL_COLUMNS.filter((column) => ids.includes(column.id)).map((column) => column.header));
}
