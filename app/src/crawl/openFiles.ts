// Opening crawl files from anywhere: Ctrl+O's dialog, the shell's command line and second launches, and later
// drag-and-drop. Each path is added to the library; the first one that opens is shown.
import type { QueryClient } from '@tanstack/react-query';
import { api } from '../api/client';
import { queryKeys } from '../api/queries';
import type { CrawlListItem } from '../api/types';
import { notifyError } from '../feedback/notify';
import { pickCrawlFiles } from '../shell/os';

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
