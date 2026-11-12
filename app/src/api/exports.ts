// Export and the report (docs/v2-sidecar-api.md, "Export (E5)" and "Report (E5)"). The sidecar writes the file to a
// path the user chose in a native save dialog; an export is a job, as an analysis is: `startExport` answers at once
// with the job, `export` events on the live stream carry it as it goes on and as it ends, and `exportJob` reads it.
import { parseProblem, type ProblemBody } from './errors';
import type { CheckGroupRef, GridFilter, GridSort, RobotsBlockReason } from './grids';

/** What an export writes. */
export type ExportKind =
  'urls' | 'links' | 'sitemapUrls' | 'robotsBlocked' | 'issueRows' | 'check' | 'workbook' | 'report';

/** The grids write `csv` or `xlsx`; `check` either; `workbook` is `xlsx` and `report` is `html`. */
export type ExportFormat = 'csv' | 'xlsx' | 'html';

export type ExportStatus = 'running' | 'done' | 'cancelled' | 'failed';

/** `summary` is the full report's first page: its head, summary, first fixes, score and foot. */
export type ReportCut = 'full' | 'summary';

/** A grid's own read, without a page: an export writes every row the grid would show, in its order. */
export interface ExportGridQuery {
  /** The file's columns, in this order, under the engine's titles. The grid's own when left out. */
  columns?: string[];
  filters?: GridFilter[];
  search?: string;
  /** A crawl that's still running is exported in crawl order or by URL only: another sort is `invalid-query`. */
  sort?: GridSort[];
}

/** How the report is cut and how many of each problem's pages it lists (0 to 50). */
export interface ReportOptions {
  cut?: ReportCut;
  /** The first pages of each problem to fix. 5 when left out. */
  examples?: number;
  /** The first pages of each thing worth a look. 3 when left out. */
  lookExamples?: number;
}

interface ExportTarget {
  /** The file to write: a full path on this computer, ending in the format's extension (`.csv`, `.xlsx`, `.html`). */
  path: string;
}

/**
 * `POST /crawls/{id}/export`. Each kind takes its own fields, and one that belongs to another kind is refused
 * (`invalid-export`), not ignored.
 */
export type ExportRequest =
  | ({ kind: 'urls' | 'links'; format: 'csv' | 'xlsx' } & ExportTarget & ExportGridQuery)
  | ({ kind: 'sitemapUrls'; format: 'csv' | 'xlsx'; sitemapId: number } & ExportTarget & ExportGridQuery)
  | ({ kind: 'issueRows'; format: 'csv' | 'xlsx'; checkId: string; group?: CheckGroupRef } & ExportTarget &
      ExportGridQuery)
  | ({
      kind: 'robotsBlocked';
      format: 'csv' | 'xlsx';
      origin: string;
      line?: number;
      reason?: RobotsBlockReason;
    } & ExportTarget)
  | ({ kind: 'check'; format: 'xlsx' | 'csv'; checkId: string } & ExportTarget)
  | ({ kind: 'workbook'; format: 'xlsx' } & ExportTarget)
  | ({ kind: 'report'; format: 'html' } & ExportTarget & ReportOptions);

/** What a finished export wrote. */
export interface ExportResult {
  /** Rows of data, headers apart. The workbook's are its checks' rows; the report has none. */
  rows: number;
  bytes: number;
  /** An Excel file's sheets: more than 1 for a grid of over 1,048,575 rows, which goes on to further sheets. */
  sheets: number;
  /** Columns asked for that are in the file with no values: the crawl has no URL summary to read them from. */
  unavailableColumns: string[];
  /** Cells cut to what a spreadsheet takes: 32,767 characters in an Excel file, 32,758 in CSV (what Excel reads of a field). */
  truncatedCells: number;
  /** The crawl was running: the file holds what it had when the export began. */
  crawlWasRunning: boolean;
}

/** An export: the answer to `startExport`, `exportJob`, and the data of every `export` event. */
export interface ExportJob {
  exportId: string;
  crawlId: string;
  kind: ExportKind;
  format: ExportFormat;
  /** The file it writes, as the sidecar resolved the path. */
  path: string;
  status: ExportStatus;
  rowsWritten: number;
  /**
   * Null until counted, and for the report. A running crawl's is its count when the export began; never less than
   * `rowsWritten`.
   */
  rowsExpected: number | null;
  startedAt: string;
  finishedAt: string | null;
  /** Once `done`. */
  result: ExportResult | null;
  /** Once `failed`: the problem a call would have been answered with. Nothing was written. */
  error: ProblemBody | null;
}

/**
 * The live stream's `export` event (docs/v2-sidecar-api.md, Live events): the job, whole, as it starts, at most
 * every 250 ms while it writes rows, and as it ends. It is one of `SidecarEvent`: the live store reads it at the
 * boundary with `parseExportJob` and hands it to its listeners (`onEvent`), keeping none of it. `hello.exports` lists
 * the exports running when a stream connects, and each one's latest event follows it.
 */
export interface ExportEvent {
  type: 'export';
  data: ExportJob;
}

const EXPORT_KINDS: readonly ExportKind[] = [
  'urls',
  'links',
  'sitemapUrls',
  'robotsBlocked',
  'issueRows',
  'check',
  'workbook',
  'report',
];
const EXPORT_FORMATS: readonly ExportFormat[] = ['csv', 'xlsx', 'html'];
const EXPORT_STATUSES: readonly ExportStatus[] = ['running', 'done', 'cancelled', 'failed'];

const isCount = (value: unknown): value is number => typeof value === 'number' && Number.isFinite(value) && value >= 0;

/** True once an export has ended, however it ended: only `done` left a file. */
export const isExportOver = (job: ExportJob): boolean => job.status !== 'running';

/**
 * Reads an export at the boundary, as an `export` event carries it: anything that doesn't match the contract's shape
 * gives null, so a malformed event is dropped and never shown as progress. A `result` or an `error` that doesn't
 * read is null, with the job's status standing.
 */
export function parseExportJob(value: unknown): ExportJob | null {
  if (typeof value !== 'object' || value === null) return null;
  const record = value as Record<string, unknown>;
  const { exportId, crawlId, kind, format, path, status, rowsWritten, rowsExpected, startedAt, finishedAt } = record;
  if (typeof exportId !== 'string' || typeof crawlId !== 'string' || typeof path !== 'string') return null;
  if (!EXPORT_KINDS.includes(kind as ExportKind) || !EXPORT_FORMATS.includes(format as ExportFormat)) return null;
  if (!EXPORT_STATUSES.includes(status as ExportStatus) || !isCount(rowsWritten)) return null;
  if (typeof startedAt !== 'string') return null;
  return {
    exportId,
    crawlId,
    kind: kind as ExportKind,
    format: format as ExportFormat,
    path,
    status: status as ExportStatus,
    rowsWritten,
    rowsExpected: isCount(rowsExpected) ? rowsExpected : null,
    startedAt,
    finishedAt: typeof finishedAt === 'string' ? finishedAt : null,
    result: parseExportResult(record.result),
    error: parseProblem(record.error),
  };
}

function parseExportResult(value: unknown): ExportResult | null {
  if (typeof value !== 'object' || value === null) return null;
  const record = value as Record<string, unknown>;
  const { rows, bytes, sheets, unavailableColumns, truncatedCells, crawlWasRunning } = record;
  if (!isCount(rows) || !isCount(bytes)) return null;
  return {
    rows,
    bytes,
    sheets: isCount(sheets) ? sheets : 0,
    unavailableColumns: Array.isArray(unavailableColumns)
      ? unavailableColumns.filter((column): column is string => typeof column === 'string')
      : [],
    truncatedCells: isCount(truncatedCells) ? truncatedCells : 0,
    crawlWasRunning: crawlWasRunning === true,
  };
}
