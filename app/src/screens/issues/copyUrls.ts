// Copy URLs: a check's flagged URLs, or one group's, one per line. They're read from the check's rows in pages of
// 1,000 with the `after` cursor, so a check of 100,000 URLs is 100 reads, never one huge answer. A copy that takes
// longer than a moment shows its progress in a toast with Cancel.
import { api } from '../../api/client';
import { isAbort } from '../../api/errors';
import type { CheckGroupRef, GridPage, IssueRow } from '../../api/grids';
import { announce } from '../../feedback/announce';
import { notifyError, notifyProgress, notifyResult } from '../../feedback/notify';
import { copyText } from '../../lib/clipboard';
import { formatCount, formatCountOf } from '../../lib/format';

/** The most rows one read may ask for (the contract's limit). */
export const COPY_PAGE_SIZE = 1_000;
/** A copy that takes longer than this shows its progress, with Cancel. */
export const COPY_PROGRESS_DELAY_MS = 300;

export type RowsPageReader = (after: string | undefined, signal: AbortSignal) => Promise<GridPage<IssueRow>>;

/** Reads every URL the reader gives, page after page, until its cursor runs out. `onPage` hears the running count. */
export async function readAllUrls(
  read: RowsPageReader,
  signal: AbortSignal,
  onPage?: (read: number, total: number | null) => void,
): Promise<string[]> {
  const urls: string[] = [];
  let after: string | undefined;
  for (;;) {
    const page = await read(after, signal);
    for (const row of page.rows) if (row.url) urls.push(row.url);
    onPage?.(urls.length, page.matching);
    if (!page.next || page.rows.length === 0) return urls;
    after = page.next;
  }
}

/** A reader of one check's URLs, or of one of its groups. */
export function checkUrlsReader(crawlId: string, checkId: string, group: CheckGroupRef | null): RowsPageReader {
  return (after, signal) =>
    api.issueRows(
      crawlId,
      checkId,
      {
        columns: ['url'],
        filters: [],
        sort: [],
        limit: COPY_PAGE_SIZE,
        ...(after ? { after } : {}),
        ...(group ? { group } : {}),
      },
      signal,
    );
}

export interface CopyUrlsOptions {
  read: RowsPageReader;
  /** How many there should be, for the progress toast: the check's or the group's pages. */
  expected: number | null;
  /** Tests: the clipboard and the clock. */
  copy?: (text: string) => Promise<boolean>;
  progressDelayMs?: number;
}

/**
 * Copies the URLs, one per line. After COPY_PROGRESS_DELAY_MS a toast shows how far it's got, with Cancel; at the end
 * "Copied 265 URLs" (in the same toast when there was one), announced to screen readers. Resolves with how many were
 * copied, or null when cancelled or failed.
 */
export async function copyUrls({
  read,
  expected,
  copy = copyText,
  progressDelayMs = COPY_PROGRESS_DELAY_MS,
}: CopyUrlsOptions): Promise<number | null> {
  const controller = new AbortController();
  let progress: ReturnType<typeof notifyProgress> | null = null;
  const title = expected === null ? 'Copying URLs' : `Copying ${formatCountOf(expected, 'URL')}`;
  const timer = setTimeout(() => {
    progress = notifyProgress(title, { onCancel: () => controller.abort() });
  }, progressDelayMs);
  const shown = () => progress;
  try {
    const urls = await readAllUrls(read, controller.signal, (count, total) => {
      shown()?.update(`${formatCount(count)} of ${formatCount(total ?? expected)} read`);
    });
    clearTimeout(timer);
    if (controller.signal.aborted) throw new DOMException('Cancelled', 'AbortError');
    if (!(await copy(urls.join('\n')))) throw new ClipboardRefused();
    const result = `Copied ${formatCountOf(urls.length, 'URL')}`;
    const toast = shown();
    if (toast) toast.done(result);
    else notifyResult(result);
    announce(`${result}.`);
    return urls.length;
  } catch (error) {
    clearTimeout(timer);
    const toast = shown();
    if (isAbort(error) || controller.signal.aborted) {
      toast?.dismiss();
      announce('Copying stopped.');
      return null;
    }
    toast?.dismiss();
    notifyError(error, {
      title: 'The URLs couldn’t be copied.',
      ...(error instanceof ClipboardRefused ? { description: 'The clipboard didn’t take them. Try again.' } : {}),
    });
    return null;
  }
}

/** The clipboard said no. */
class ClipboardRefused extends Error {
  constructor() {
    super('The clipboard refused the text.');
  }
}
