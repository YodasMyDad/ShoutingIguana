// Making an export: where to save it (the native save dialog in the shell; the dialog's own field in a browser), the
// call that starts it, and what each problem's action does. The export then lives in the export store, and its toast
// follows it there.
import { useSyncExternalStore } from 'react';
import { toast } from 'sonner';
import { api } from '../api/client';
import { errorTitle } from '../api/errors';
import type { ExportFormat, ReportCut } from '../api/exports';
import { analyseCrawl } from '../crawl/actions';
import { ERROR_DURATION_MS, notifyError } from '../feedback/notify';
import { isTauri } from '../lib/platform';
import { getSettings, updateSettings } from '../settings/settingsStore';
import { pickSavePath } from '../shell/os';
import { choiceOf, requestOf, type ExportAgain, type ExportChoice, type ExportOffer } from './exportOffer';
import { exportStore, type ExportStore, type TrackedExport } from './exportStore';
import { showExportToast } from './exportToasts';
import {
  exportErrorWords,
  exportFileName,
  fileDate,
  folderOf,
  FORMAT_FILTER,
  inFolder,
  remedyTitle,
  type ExportRemedy,
} from './exportWords';

/** The file's default name for a choice: the site, the crawl's date and the view ("… report summary.html"). */
export function defaultFileName(
  offer: ExportOffer,
  choice: ExportChoice,
  format: ExportFormat,
  cut: ReportCut,
): string {
  const view = choice.cuts && cut === 'summary' ? `${choice.fileView} summary` : choice.fileView;
  return exportFileName(offer.site, fileDate(offer.crawlDate), view, format);
}

/** A default path: the default name in the folder the last export went to, or the name alone. */
export function defaultPath(offer: ExportOffer, choice: ExportChoice, format: ExportFormat, cut: ReportCut): string {
  return inFolder(getSettings().exportFolder, defaultFileName(offer, choice, format, cut));
}

/** The native save dialog, opened on the default name in the last export's folder. Null when cancelled. */
export function chooseSavePath(
  offer: ExportOffer,
  choice: ExportChoice,
  format: ExportFormat,
  cut: ReportCut,
): Promise<string | null> {
  return pickSavePath({
    title: `Save ${choice.title.charAt(0).toLowerCase()}${choice.title.slice(1)}`,
    defaultPath: defaultPath(offer, choice, format, cut),
    filters: [FORMAT_FILTER[format]],
  });
}

/**
 * Starts the export and keeps it in the store, with its toast. A refusal (the engine said no before writing a row:
 * `invalid-export`, `export-not-writable`, `export-busy`, …) throws, for the caller to say where it was asked.
 */
export async function beginExport(again: ExportAgain, store: ExportStore = exportStore): Promise<TrackedExport> {
  const choice = choiceOf(again);
  const request = requestOf(again);
  if (!choice || !request) throw new Error(`The offer has no choice ${again.choiceId}`);
  const job = await api.startExport(again.offer.crawlId, request);
  const tracked = store.accept(job, { label: choice.title, again, columnTitles: choice.columnTitles ?? {} });
  // What worked is remembered: the next save dialog opens in this folder, and a grid's next export starts on this format.
  const folder = folderOf(again.path);
  updateSettings({
    ...(folder ? { exportFolder: folder } : {}),
    ...(again.format === 'csv' || (again.format === 'xlsx' && choice.formats.includes('csv'))
      ? { exportFormat: again.format }
      : {}),
  });
  showExportToast(job.exportId, store);
  return tracked;
}

// --------------------------------------------------------------------------------------------- The dialog's state

export interface ExportDialogState {
  offer: ExportOffer;
  choiceId?: string | undefined;
  format?: ExportFormat | undefined;
  cut?: ReportCut | undefined;
  /** The path to show in a browser's field (no native dialog there). */
  path?: string | undefined;
  /** Why the last try was refused, said in the dialog, with its actions. */
  refusal?: { sentence: string; remedies: ExportRemedy[] } | null | undefined;
  crawlOrder?: boolean | undefined;
  /** Each opening's own number, so a dialog opened again starts afresh. */
  seq?: number | undefined;
  /** Where focus was as it opened: it goes back there when the dialog closes. */
  returnTo?: Element | null | undefined;
}

let dialogSeq = 0;
let dialogState: ExportDialogState | null = null;
const dialogListeners = new Set<() => void>();

function setDialog(next: ExportDialogState | null): void {
  dialogState = next;
  for (const listener of dialogListeners) listener();
}

/** Opens the export dialog on an offer, on a choice, a format or a path where one is known. */
export function openExportDialog(offer: ExportOffer, preset: Omit<ExportDialogState, 'offer'> = {}): void {
  dialogSeq += 1;
  const returnTo = dialogState?.returnTo ?? (typeof document === 'undefined' ? null : document.activeElement);
  setDialog({ offer, ...preset, seq: dialogSeq, returnTo });
}

export function closeExportDialog(): void {
  setDialog(null);
}

export function getExportDialog(): ExportDialogState | null {
  return dialogState;
}

export function useExportDialog(): ExportDialogState | null {
  return useSyncExternalStore(
    (listener) => {
      dialogListeners.add(listener);
      return () => dialogListeners.delete(listener);
    },
    () => dialogState,
  );
}

// ------------------------------------------------------------------------------------------------- The remedies

/** Whether a remedy can be offered for this export: one that needs the export asked for again needs it known. */
export function canRun(remedy: ExportRemedy, again: ExportAgain | null): boolean {
  return remedy === 'analyse' ? again !== null : again !== null && choiceOf(again) !== undefined;
}

/**
 * Does what a problem's action says: the save dialog again, the same file again, the same export of the crawl as it
 * now is, CSV in Excel's place, in crawl order, or the crawl's analysis. A new refusal is said in a toast, as the
 * dialog that would have said it is closed; in a browser, where there's no save dialog, the export dialog opens again.
 */
export async function runRemedy(
  remedy: ExportRemedy,
  again: ExportAgain,
  store: ExportStore = exportStore,
): Promise<void> {
  const choice = choiceOf(again);
  if (remedy === 'analyse') {
    await analyseCrawl(again.offer.crawlId);
    return;
  }
  if (!choice) return;
  let next: ExportAgain = again;
  if (remedy === 'asCsv') next = { ...again, format: 'csv', path: again.path.replace(/\.xlsx$/i, '.csv') };
  if (remedy === 'crawlOrder') next = { ...again, crawlOrder: true };
  const askPath = remedy === 'chooseAnother' || remedy === 'asCsv';
  if (askPath && !isTauri) {
    openExportDialog(next.offer, {
      choiceId: next.choiceId,
      format: next.format,
      cut: next.cut,
      path: next.path,
      crawlOrder: next.crawlOrder,
    });
    return;
  }
  if (askPath) {
    const path = await chooseSavePath(next.offer, choice, next.format, next.cut);
    if (path === null) return;
    next = { ...next, path };
  }
  try {
    await beginExport(next, store);
  } catch (error) {
    notifyRefused(error, next);
  }
}

/** A refusal with no dialog open to say it in: a toast, with the problem's first action. */
export function notifyRefused(error: unknown, again: ExportAgain): void {
  const words = exportErrorWords(error, { path: again.path, format: again.format });
  if (!words) {
    notifyError(error, { title: 'The export couldn’t start', description: errorTitle(error) });
    return;
  }
  const first = words.remedies.find((remedy) => canRun(remedy, again));
  toast.error('The export couldn’t start', {
    id: `export-refused:${again.offer.crawlId}:${again.choiceId}`,
    duration: ERROR_DURATION_MS,
    description: words.sentence,
    ...(first ? { action: { label: remedyTitle(first), onClick: () => void runRemedy(first, again) } } : {}),
  });
}
