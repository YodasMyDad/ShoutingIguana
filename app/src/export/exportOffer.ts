// What a screen offers to export (docs/v2-ui-ia.md, flow 5 "Hand over"): its own view as CSV or Excel, and on Overview
// and Issues the report and the spreadsheet of everything. A screen builds an offer from what it shows when Export
// (Ctrl+E) is pressed; the dialog asks which and as what, the save dialog where, and the request is the offer's.
import type { ExportFormat, ExportRequest, ReportCut } from '../api/exports';

export interface ExportBuildOptions {
  format: ExportFormat;
  path: string;
  /** The report's cut. */
  cut: ReportCut;
  /** Without the view's sort: a running crawl's export is in crawl order or by URL only. */
  crawlOrder: boolean;
}

export interface ExportChoice {
  /** Stable within the offer: `view`, `check`, `group`, `report`, `workbook`. */
  id: string;
  /** "This view", "This check", "The report", "Spreadsheet of everything". */
  title: string;
  /** What the file will hold, in a line: "4,521 URLs: these filters, this sort, these 7 columns". */
  description?: string | undefined;
  /** The formats it can be written as, the usual first. */
  formats: readonly ExportFormat[];
  /** The report's two cuts, Full report and Summary page. */
  cuts?: boolean | undefined;
  /** The view's words in the file's default name: "URLs", "Broken links", "report". */
  fileView: string;
  build: (options: ExportBuildOptions) => ExportRequest;
  /** Each column's title, to name the columns a finished export left empty. */
  columnTitles?: Readonly<Record<string, string>> | undefined;
  /** Why it can't be exported now, in words: shown in its place, with nothing to press but `remedy`. */
  unavailable?: string | null | undefined;
  /** What helps when it's unavailable: the crawl's analysis. */
  remedy?: 'analyse' | undefined;
  /** It sorts by something a running crawl changes: said beside it, with the export in crawl order. */
  sortedWhileCrawling?: boolean | undefined;
}

export interface ExportOffer {
  crawlId: string;
  /** The site, for the file's default name. */
  site: string;
  /** When the crawl started (ISO 8601), for the file's default name: its date. */
  crawlDate: string | null;
  /** The dialog's title: "Export URLs". */
  title: string;
  choices: readonly ExportChoice[];
  /** A line above the choices: the crawl is running, and the file will hold what it has when the export begins. */
  note?: string | null | undefined;
  /** The choice the dialog starts on; the first when left out. */
  initialChoice?: string | undefined;
}

/** One export as it was asked for, to make it again: another file, CSV in Excel's place, in crawl order. */
export interface ExportAgain {
  offer: ExportOffer;
  choiceId: string;
  format: ExportFormat;
  cut: ReportCut;
  path: string;
  crawlOrder: boolean;
}

export function choiceOf(again: ExportAgain): ExportChoice | undefined {
  return again.offer.choices.find((choice) => choice.id === again.choiceId);
}

/** The request for an export asked for again. */
export function requestOf(again: ExportAgain): ExportRequest | null {
  const choice = choiceOf(again);
  return choice
    ? choice.build({ format: again.format, path: again.path, cut: again.cut, crawlOrder: again.crawlOrder })
    : null;
}
