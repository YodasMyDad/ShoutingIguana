// Opening crawl files from anywhere: Ctrl+O's dialog, the shell's command line and second launches, and later
// drag-and-drop. Each path is added to the library; the first one that opens is shown.
import type { QueryClient } from '@tanstack/react-query';
import { api } from '../api/client';
import { queryKeys } from '../api/queries';
import type { CrawlListItem } from '../api/types';
import { notifyError } from '../feedback/notify';
import { getConnection, subscribeSidecarStatus } from '../shell/connection';
import { pickCrawlFiles } from '../shell/os';

/** The shell's side of the files it holds for the page: `ipc` in the app, a fake in tests. */
export interface PendingOpenFiles {
  /** Everything waiting, each path handed over once. */
  take: () => Promise<string[]>;
  /** Calls `handler` whenever more files arrive; resolves with the function that stops listening. */
  onArrived: (handler: () => void) => Promise<() => void>;
}

/**
 * Opens the crawl files the shell holds for the page (double-clicked, or passed on by a second launch) as soon as the
 * engine can open them: at once if it's ready, when it becomes ready, and whenever more arrive. Until then they wait
 * in the shell, so a slow start loses none. Returns a function that stops.
 */
export function takeOpenFilesWhenReady(files: PendingOpenFiles, open: (paths: string[]) => unknown): () => void {
  let stopped = false;
  let stopListening: (() => void) | null = null;
  const take = async () => {
    if (stopped || getConnection() === null) return;
    const paths = await files.take();
    // Taken is taken: they're opened even if this stopped meanwhile, or they would be lost.
    if (paths.length > 0) await open(paths);
  };
  // A failed hand-over leaves the files in the shell for the next try.
  const tryTaking = () => void take().catch(() => undefined);
  const offStatus = subscribeSidecarStatus(tryTaking);
  void files
    .onArrived(tryTaking)
    .then((stop) => {
      if (stopped) stop();
      else stopListening = stop;
    })
    .catch(() => undefined);
  tryTaking();
  return () => {
    stopped = true;
    offStatus();
    stopListening?.();
  };
}

export async function openCrawlPaths(
  paths: readonly string[],
  queryClient: QueryClient,
  show: (item: CrawlListItem) => void,
): Promise<void> {
  let shown = false;
  for (const path of paths) {
    try {
      const item = await api.openCrawl(path);
      if (!shown) {
        shown = true;
        show(item);
      }
    } catch (error) {
      notifyError(error, { title: `${path.split(/[\\/]/).pop() ?? path} couldn’t be opened.` });
    }
  }
  if (paths.length > 0) void queryClient.invalidateQueries({ queryKey: queryKeys.library, exact: true });
}

/** Ctrl+O: the native dialog, then open what was picked. */
export async function pickAndOpenCrawls(queryClient: QueryClient, show: (item: CrawlListItem) => void): Promise<void> {
  const paths = await pickCrawlFiles();
  if (paths) await openCrawlPaths(paths, queryClient, show);
}
