// Saving the report, or the spreadsheet of everything, from the Report screen (docs/v2-ui-simple.md, "The Report
// screen"): the native save dialog, then the export (E5) the sidecar writes, told through one toast that ends with
// "Open folder". The sidecar writes the file; the page only names where. An export is a job: `startExport` answers at
// once, and its `export` events say how it goes and how it ended.
import { api } from '../../api/client';
import { ProblemError } from '../../api/errors';
import { isExportOver, type ExportJob, type ExportRequest, type ReportCut } from '../../api/exports';
import { notifyProgress, notifyResult } from '../../feedback/notify';
import { formatCount } from '../../lib/format';
import { isTauri } from '../../lib/platform';
import { liveStore, type LiveStore } from '../../live/liveStore';
import { pickSavePath, showExportInFolder } from '../../shell/os';
import { saveDialogOptions, type ReportFileKind } from './reportFile';

export interface SaveReportFile {
  crawlId: string;
  kind: ReportFileKind;
  /** The report's cut: what the screen shows. */
  cut: ReportCut;
  /** The default name, from the site and the crawl's date. */
  fileName: string;
  /** The report's document as the screen shows it, for a plain browser, which has no save dialog: it downloads it. */
  documentText?: string | null | undefined;
}

/** How often the job is read while it runs, in case its events don't arrive (the stream reconnecting). */
export const EXPORT_POLL_MS = 2_000;

const WORDS: Record<ReportFileKind, { saving: string; saved: string; cancelled: string; noBrowser: string }> = {
  report: {
    saving: 'Saving the report…',
    saved: 'Report saved',
    cancelled: 'The report wasn’t saved: saving it was cancelled.',
    noBrowser: '',
  },
  workbook: {
    saving: 'Saving the spreadsheet of everything…',
    saved: 'Spreadsheet saved',
    cancelled: 'The spreadsheet wasn’t saved: saving it was cancelled.',
    noBrowser: 'A spreadsheet is saved from the Shouting Iguana app, which asks where to put it. A browser can’t.',
  },
};

/** The end-to-end tests' folder for a save, as a browser has no save dialog: set in local storage, in that build only. */
export const E2E_SAVE_FOLDER_KEY = 'si.e2e.saveFolder';

/** Where to save: the native dialog in the shell; null when cancelled, or when there's no dialog to ask. */
async function choosePath(kind: ReportFileKind, fileName: string): Promise<string | null> {
  if (isTauri) return pickSavePath(saveDialogOptions(kind, fileName));
  if (import.meta.env.MODE === 'e2e') {
    const folder = localStorage.getItem(E2E_SAVE_FOLDER_KEY);
    return folder ? `${folder.replace(/[\\/]+$/, '')}\\${fileName}` : null;
  }
  return null;
}

/** A plain browser's nearest equivalent for the report: the document it shows, downloaded under its name. */
function download(text: string, fileName: string): void {
  const url = URL.createObjectURL(new Blob([text], { type: 'text/html;charset=utf-8' }));
  const link = document.createElement('a');
  link.href = url;
  link.download = fileName;
  link.click();
  setTimeout(() => URL.revokeObjectURL(url), 0);
}

function request(save: SaveReportFile, path: string): ExportRequest {
  return save.kind === 'report'
    ? { kind: 'report', format: 'html', path, cut: save.cut }
    : { kind: 'workbook', format: 'xlsx', path };
}

/** The file's own name, from the path the sidecar resolved. */
export function fileNameOf(path: string): string {
  return path.split(/[\\/]/).pop() ?? path;
}

/** How far a spreadsheet has got: "1,200 of 4,521 rows". Nothing for the report, which has no rows. */
export function exportProgressText(job: ExportJob): string | null {
  if (job.kind === 'report' || job.rowsWritten === 0) return null;
  return job.rowsExpected === null
    ? `${formatCount(job.rowsWritten)} rows written`
    : `${formatCount(job.rowsWritten)} of ${formatCount(job.rowsExpected)} rows`;
}

/**
 * Follows one export to its end: its `export` events, and a read of the job now and then in case they don't come.
 * `onJob` hears the job each time it's newer; the returned function stops following it.
 */
export function followExport(
  exportId: string,
  onJob: (job: ExportJob) => void,
  store: LiveStore = liveStore,
  pollMs: number = EXPORT_POLL_MS,
): () => void {
  let stopped = false;
  const stop = () => {
    stopped = true;
    unsubscribe();
    clearInterval(timer);
  };
  const hear = (job: ExportJob) => {
    if (stopped || job.exportId !== exportId) return;
    onJob(job);
    if (isExportOver(job)) stop();
  };
  const unsubscribe = store.onEvent((event) => {
    if (event.type === 'export') hear(event.data);
  });
  // Read once at once too: a job that ended before its events were listened for (a small report) ends here.
  const read = () => void api.exportJob(exportId).then(hear, () => undefined);
  const timer = setInterval(read, pollMs);
  read();
  return stop;
}

/**
 * Saves the report or the spreadsheet: asks where, starts the export and tells how it goes in one toast, with Cancel
 * while it runs and "Open folder" once it's done. Resolves once the export has started (or wasn't): the toast follows
 * it from there, whatever screen is shown. False when nothing was started (the dialog cancelled, a browser).
 */
export async function saveReportFile(save: SaveReportFile): Promise<boolean> {
  const words = WORDS[save.kind];
  const path = await choosePath(save.kind, save.fileName);
  if (path === null) {
    if (isTauri || import.meta.env.MODE === 'e2e') return false;
    if (save.kind === 'report' && save.documentText) {
      download(save.documentText, save.fileName);
      return true;
    }
    if (words.noBrowser) notifyResult(words.noBrowser, { id: `save:${save.crawlId}:${save.kind}:browser` });
    return false;
  }

  // Cancel may be pressed before the sidecar has answered with the export's id: it's cancelled as soon as it has.
  const pending: { exportId: string | null; cancelled: boolean } = { exportId: null, cancelled: false };
  const toast = notifyProgress(words.saving, {
    onCancel: () => {
      pending.cancelled = true;
      if (pending.exportId !== null) void api.cancelExport(pending.exportId).catch(() => undefined);
    },
  });
  let job: ExportJob;
  try {
    job = await api.startExport(save.crawlId, request(save, path));
  } catch (error) {
    toast.fail(error);
    return false;
  }
  pending.exportId = job.exportId;
  if (pending.cancelled) void api.cancelExport(job.exportId).catch(() => undefined);

  // The rows written only go up: an event that arrives late never takes the toast back.
  let shown = -1;
  const show = (next: ExportJob) => {
    switch (next.status) {
      case 'running': {
        if (next.rowsWritten <= shown) return;
        shown = next.rowsWritten;
        const text = exportProgressText(next);
        if (text) toast.update(text);
        return;
      }
      case 'done': {
        const name = fileNameOf(next.path);
        toast.done(
          `${words.saved}: ${name}`,
          isTauri ? { label: 'Open folder', onClick: () => void showExportInFolder(next.exportId) } : undefined,
        );
        return;
      }
      case 'cancelled':
        toast.dismiss();
        notifyResult(words.cancelled, { id: `save:${next.exportId}:cancelled` });
        return;
      case 'failed':
        toast.fail(next.error ? new ProblemError(next.error) : new Error(next.status));
        return;
    }
  };
  show(job);
  if (!isExportOver(job)) followExport(job.exportId, show);
  return true;
}
