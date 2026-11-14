// What a crawl in the library is called in its menu and the palette, and Remove from library: shared by the table, its
// menu and Home's palette commands.
import { api } from '../../api/client';
import type { CrawlListItem } from '../../api/types';
import { saidWhileAnalysing } from '../../crawl/actions';
import { crawlName } from '../../crawl/status';
import { notifyError, notifyUndo } from '../../feedback/notify';
import { formatExactTime } from '../../lib/format';

/** "gardenfurnituresets.co.uk", or for an older crawl of a site, "gardenfurnituresets.co.uk, 31 Oct 2026, 14:13". */
export function crawlLabel(item: CrawlListItem, older: boolean): string {
  const name = crawlName(item);
  return older && item.startedAt ? `${name}, ${formatExactTime(item.startedAt)}` : name;
}

/** Takes a crawl off the library, with Undo: its file stays on disk. Shared by the menu and the palette. */
export async function removeFromLibrary(item: CrawlListItem, refresh: () => Promise<unknown>): Promise<void> {
  try {
    await api.removeCrawl(item.id);
    void refresh();
    notifyUndo(`${crawlName(item)} was removed from the library. The file is still on disk.`, () => {
      void api
        .restoreCrawl(item.id)
        .then(refresh)
        .catch((error: unknown) => notifyError(error));
    });
  } catch (error) {
    if (await saidWhileAnalysing(item.id, 'remove', error)) return;
    notifyError(error);
  }
}

/**
 * Deletes a crawl's file, for the confirmation's Delete file: a crawl being analysed since it was stopped says so with
 * Cancel the analysis, and the confirmation closes; any other failure stays in it.
 */
export async function deleteCrawlFile(item: CrawlListItem, refresh: () => Promise<unknown>): Promise<void> {
  try {
    await api.deleteCrawlFile(item.id);
  } catch (error) {
    if (await saidWhileAnalysing(item.id, 'delete', error)) return;
    throw error;
  }
  void refresh();
}
