// The URL grid's pieces for a view that is the URL grid narrowed to something with a fixed set of columns (one
// sitemap's URLs): the nine columns the URL grid showed before it had presets, what the query asks for to draw them,
// and their names in a list. `UrlsScreen` exports them, and the filters' chips, under the names they had there.
//
// They look and act as the URL grid's own do (docs/handoff/2026-11-19, D-18): a URL is a link that opens the
// inspector, shown as its path where it's on the crawl's own site, the host named once in the header; a status has
// its class's mark; the URL and the Title take their widths from what they hold (`fit`).
import { createElement, type ReactNode } from 'react';
import type { UrlRow } from '../../api/grids';
import { listOfNames } from '../../grid/summaryWords';
import type { GridColumn } from '../../grid/types';
import { formatBytes, formatCount, formatMilliseconds } from '../../lib/format';
import { StatusCode } from '../../ui/StatusCode';
import { Url } from '../../ui/Url';
import { RESOURCE_URL_TYPES, urlDisplay } from '../../ui/urlText';
import { NOT_A_PAGE, urlCellValue } from './urlColumns';

const plain = (value: number | null | undefined) => (value === null || value === undefined ? '' : String(value));
const isFile = (row: UrlRow) => Boolean(row.type && RESOURCE_URL_TYPES.has(row.type));
const indexability = (row: UrlRow) => {
  if (!row.indexability) return '';
  // A file that answered isn't "Indexable": it isn't a page. One that failed still says how. As the URL grid says it.
  return row.indexability === 'none' && isFile(row) ? NOT_A_PAGE : urlCellValue('indexability', row.indexability);
};
const status = (row: UrlRow) =>
  row.statusCode === null || row.statusCode === undefined
    ? row.statusClass === 'noResponse'
      ? 'No response'
      : ''
    : String(row.statusCode);

const muted = (text: string) => createElement('span', { className: 'text-text-muted' }, text);
/** A cell of words with the whole of them on hover (a title is longer than its column), or a quiet dash for none. */
const words = (text: string): ReactNode => (text ? createElement('span', { title: text }, text) : muted('–'));

/** The room a URL's cell keeps beside its text: the open-in-browser button and the gap before it. */
const OPEN_BUTTON_ROOM = 28;

/** An origin's host ("www.example.com"), or null for none or one that can't be read. */
function hostOf(origin: string | null): string | null {
  try {
    return origin ? new URL(origin).host : null;
  } catch {
    return null;
  }
}

export interface FixedUrlColumnsContext {
  /** The crawl's own scheme and host: URLs on it show their path, the host named once in the header. */
  siteOrigin?: string | null | undefined;
  /** A click on a URL: the view opens the inspector on it. Left out, the URL is text. */
  onGo?: ((row: UrlRow) => void) | undefined;
}

/**
 * Columns use E3a's ids (docs/v2-sidecar-api.md, URLs), so the query asks for exactly these. Their widths are the URL
 * grid's own for the same columns (`urlColumns.tsx`), but the Title's, which keeps more room here; the URL's and
 * the Title's are what they have before the grid has measured what they hold (`fit`).
 */
export function fixedUrlColumns({ siteOrigin = null, onGo }: FixedUrlColumnsContext = {}): GridColumn<UrlRow>[] {
  const host = hostOf(siteOrigin);
  return [
    {
      id: 'url',
      header: 'URL',
      width: 360,
      minWidth: 240,
      mono: true,
      // As wide as the longest URL among the rows read first, never wider: the next column starts where they end.
      fit: 'url',
      fitText: (row) => (row.url ? urlDisplay(row.url, siteOrigin).shown : ''),
      fitExtra: OPEN_BUTTON_ROOM,
      // The host is named once, here, where the rows show its URLs as paths.
      ...(host ? { headerDetail: `on ${host}` } : {}),
      // A link that opens the inspector, with its open-in-browser button, as every grid's URLs have it.
      cell: (row) =>
        row.url
          ? createElement(Url, {
              url: row.url,
              siteOrigin,
              copyable: false,
              tabbable: false,
              // A file a page loads (an image, a stylesheet) is named by its file, not cut in the middle of its name.
              label: isFile(row) ? 'file' : 'address',
              ...(onGo ? { onGo: () => onGo(row) } : {}),
            })
          : muted('–'),
      text: (row) => row.url ?? '',
    },
    {
      id: 'statusCode',
      header: 'Status',
      width: 88,
      // The code with its class's mark, as the URL grid shows it; what happened instead of one is the Outcome's to say.
      cell: (row) =>
        typeof row.statusCode === 'number' ? createElement(StatusCode, { code: row.statusCode }) : status(row) || '–',
      text: status,
    },
    {
      id: 'indexability',
      header: 'Indexability',
      width: 136,
      cell: (row) => words(indexability(row)),
      text: indexability,
    },
    {
      id: 'title',
      header: 'Title',
      width: 320,
      // The words people read: given room before the URL grows past what its longest value needs.
      fit: 'text',
      cell: (row) => words(row.title ?? ''),
      text: (row) => row.title ?? '',
    },
    {
      id: 'depth',
      header: 'Depth',
      width: 64,
      numeric: true,
      cell: (row) => formatCount(row.depth),
      text: (row) => plain(row.depth),
    },
    {
      id: 'responseMs',
      header: 'Response',
      width: 88,
      numeric: true,
      cell: (row) => formatMilliseconds(row.responseMs),
      text: (row) => plain(row.responseMs),
    },
    {
      id: 'sizeBytes',
      header: 'Size',
      width: 80,
      numeric: true,
      cell: (row) => formatBytes(row.sizeBytes),
      text: (row) => plain(row.sizeBytes),
    },
    {
      id: 'inlinks',
      header: 'Inlinks',
      width: 80,
      numeric: true,
      cell: (row) => formatCount(row.inlinks),
      text: (row) => plain(row.inlinks),
    },
    {
      id: 'issues',
      header: 'Issues',
      width: 72,
      numeric: true,
      cell: (row) => formatCount(row.issues),
      text: (row) => plain(row.issues),
    },
  ];
}

/** The columns themselves, for what doesn't draw them: their ids, their names, an export's columns. */
export const FIXED_URL_COLUMNS: readonly GridColumn<UrlRow>[] = fixedUrlColumns();

/**
 * What the query asks for: the columns shown, statusClass, to tell "No response" from "not fetched", and type, to
 * name a file by its file and never call one "Indexable".
 */
export const FIXED_URL_QUERY_COLUMNS = [...FIXED_URL_COLUMNS.map((column) => column.id), 'statusClass', 'type'];

/** "Inlinks", "Inlinks and Issues", "Title, Inlinks and Issues": the columns' names, in the grid's order. */
export function fixedColumnNames(ids: readonly string[]): string {
  return listOfNames(FIXED_URL_COLUMNS.filter((column) => ids.includes(column.id)).map((column) => column.header));
}
