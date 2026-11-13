// The live stream's part in exports: each `export` event into the store, and on every `hello` (a start, a reload, a
// reconnect, a new engine) the exports running then taken up, so one running before a reload keeps its toast, and
// those the app was following that have ended meanwhile read once to learn how.
import { api } from '../api/client';
import { isProblem } from '../api/errors';
import { liveStore as defaultLiveStore, type LiveStore } from '../live/liveStore';
import { exportStore as defaultExportStore, type ExportStore } from './exportStore';
import { startExportToasts } from './exportToasts';

export interface StartExportsOptions {
  liveStore?: LiveStore;
  store?: ExportStore;
  /** Whether to show toasts for them (the app does; a test of the store alone needn't). */
  toasts?: boolean;
}

export function startExports({
  liveStore = defaultLiveStore,
  store = defaultExportStore,
  toasts = true,
}: StartExportsOptions = {}): () => void {
  const stopToasts = toasts ? startExportToasts(store) : () => undefined;

  /** Reads one export once: to take it up, or to learn how one that ended unseen ended. */
  const read = async (exportId: string) => {
    try {
      store.accept(await api.exportJob(exportId));
    } catch (error) {
      // 64 others have ended since, or the engine that ran it has gone (and with it the export, which left no file).
      if (isProblem(error, 'export-not-found')) store.markLost(exportId);
    }
  };

  const offEvents = liveStore.onEvent((event) => {
    if (event.type === 'export') {
      store.accept(event.data);
      return;
    }
    if (event.type !== 'hello') return;
    const running = new Set(event.data.exports ?? []);
    // Running now and new to this app (a reload): read at once, rather than wait for its next event.
    for (const exportId of running) {
      if (!store.get(exportId)) void read(exportId);
    }
    // Followed as running, and not running now: it ended while no stream was connected.
    for (const tracked of store.running()) {
      if (!running.has(tracked.job.exportId)) void read(tracked.job.exportId);
    }
  });

  return () => {
    offEvents();
    stopToasts();
  };
}
