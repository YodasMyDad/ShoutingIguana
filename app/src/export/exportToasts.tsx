import { toast } from 'sonner';
import { ERROR_DURATION_MS } from '../feedback/notify';
import { ExportToast } from './ExportToast';
import { exportStore, type ExportStore } from './exportStore';
import { exportPhase } from './exportWords';

/** A finished export's toast stays long enough to reach Open file; the toaster holds it while hovered or focused. */
export const EXPORT_DONE_DURATION_MS = 12_000;

/** The toast of one export: one at a time, updated in place. */
export function exportToastId(exportId: string): string {
  return `export:${exportId}`;
}

/**
 * Shows an export's toast, or brings it back once closed (the sidebar's list does): running, it stays until the export
 * ends; ended, for a while. Its content follows the export in the store, so this is called only as it starts and ends.
 */
export function showExportToast(exportId: string, store: ExportStore = exportStore): void {
  const tracked = store.get(exportId);
  if (!tracked) return;
  const phase = exportPhase(tracked.job, tracked.lost);
  const duration =
    tracked.endedAt === null
      ? Number.POSITIVE_INFINITY
      : phase === 'failed' || phase === 'lost'
        ? ERROR_DURATION_MS
        : EXPORT_DONE_DURATION_MS;
  const id = exportToastId(exportId);
  toast.custom((toastId) => <ExportToast exportId={exportId} store={store} onClose={() => toast.dismiss(toastId)} />, {
    id,
    duration,
  });
}

/**
 * Keeps every export's toast in step with the store: shown as the app first hears of an export (from the call that
 * started it, an event, or `hello` after a reload), and shown again as it ends, so its end is seen even when its toast
 * was closed while it ran. Returns the unsubscribe.
 */
export function startExportToasts(store: ExportStore = exportStore): () => void {
  const told = new Map<string, boolean>();
  const sync = () => {
    for (const tracked of store.list()) {
      const ended = tracked.endedAt !== null;
      const before = told.get(tracked.job.exportId);
      if (before === ended) continue;
      told.set(tracked.job.exportId, ended);
      // An export first heard of as already ended (one read after a reload) is shown once, as it ended.
      showExportToast(tracked.job.exportId, store);
    }
  };
  sync();
  return store.subscribe(sync);
}
