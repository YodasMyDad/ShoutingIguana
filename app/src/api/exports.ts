// Export and the report (docs/v2-sidecar-api.md, "Export (E5)" and "Report (E5)"). The sidecar writes the file to a
// path the user chose in a native save dialog; an export is a job, as an analysis is: `startExport` answers at once
// with the job, `export` events on the live stream carry it as it goes on and as it ends, and `exportJob` reads it.
import type { ProblemBody } from './errors';
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
  /** An Excel file's cells cut to the 32,767 characters a cell holds. */
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
  /** Null until counted, and for the report. A running crawl's is its count when the export began. */
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
 * every 250 ms while it writes rows, and as it ends. It isn't in `SidecarEvent` yet: the live store drops the events it
 * doesn't know, and whoever builds the export's progress adds it to the union and to the store together.
 */
export interface ExportEvent {
  type: 'export';
  data: ExportJob;
}
