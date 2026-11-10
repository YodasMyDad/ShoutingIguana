// Copying what a whole view holds, not only the rows selected: a column's values, or the URLs of its rows. The rows
// are read in pages of 1,000 with the `after` cursor (docs/v2-sidecar-api.md, URLs: "Paging"), as far as
// MAX_COPY_ROWS, so a view of five million links is ten reads and a clipboard of ten thousand lines, never one huge
// answer. A copy that takes longer than a moment shows its progress in a toast with Cancel.
import { isAbort } from '../api/errors';
import type { GridPage } from '../api/grids';
import { announce } from '../feedback/announce';
import { notifyError, notifyProgress, notifyResult } from '../feedback/notify';
import { copyText } from '../lib/clipboard';
import { formatCount } from '../lib/format';
import { MAX_COPY_ROWS } from './tsv';

/** The most rows one read may ask for (the contract's limit). */
export const COPY_ALL_PAGE_SIZE = 1_000;
/** A copy that takes longer than this shows its progress, with Cancel. */
export const COPY_ALL_PROGRESS_DELAY_MS = 300;

/** Reads the page after a cursor (the first, with none) of the view being copied, with the columns the copy needs. */
export type PageReader<Row> = (after: string | undefined, limit: number, signal: AbortSignal) => Promise<GridPage<Row>>;

export interface ReadRows<Row> {
  rows: Row[];
  /** How many rows the view has: more than were read, when it has more than `limit`. */
  matching: number | null;
}

/** Reads a view's rows from its first, page after page, until its cursor runs out or `limit` rows are read. */
export async function readRows<Row>(
  read: PageReader<Row>,
  signal: AbortSignal,
  limit: number = MAX_COPY_ROWS,
  onPage?: (read: number, matching: number | null) => void,
): Promise<ReadRows<Row>> {
  const rows: Row[] = [];
  let matching: number | null = null;
  let after: string | undefined;
  while (rows.length < limit) {
    const page = await read(after, Math.min(COPY_ALL_PAGE_SIZE, limit - rows.length), signal);
    rows.push(...page.rows);
    matching = page.matching;
    onPage?.(rows.length, matching);
    if (!page.next || page.rows.length === 0) break;
    after = page.next;
  }
  return { rows, matching };
}

export interface CopyAllOptions<Row> {
  read: PageReader<Row>;
  /** The text to copy, from the rows read, and how many things it holds (lines, URLs). */
  text: (rows: readonly Row[]) => { text: string; count: number };
  /** What's copied, for the toasts: singular and plural ("target URL", "target URLs"). */
  noun: readonly [singular: string, plural: string];
  /** What a failure says: "The target URLs couldn’t be copied." */
  failed: string;
  /** Tests: the clipboard and the clock. */
  copy?: (text: string) => Promise<boolean>;
  progressDelayMs?: number;
}

/** The clipboard said no. */
class ClipboardRefused extends Error {
  constructor() {
    super('The clipboard refused the text.');
  }
}

const counted = (count: number, [singular, plural]: readonly [string, string]) =>
  `${formatCount(count)} ${count === 1 ? singular : plural}`;

/**
 * Copies what `text` makes of a view's rows. After COPY_ALL_PROGRESS_DELAY_MS a toast shows how far the reading has
 * got, with Cancel; at the end "Copied 265 target URLs" (in the same toast when there was one), announced to screen
 * readers, and when the view has more rows than a copy takes, that it's of the first 10,000. Resolves with how many
 * were copied, or null when cancelled or failed.
 */
export async function copyAll<Row>({
  read,
  text,
  noun,
  failed,
  copy = copyText,
  progressDelayMs = COPY_ALL_PROGRESS_DELAY_MS,
}: CopyAllOptions<Row>): Promise<number | null> {
  const controller = new AbortController();
  let progress: ReturnType<typeof notifyProgress> | null = null;
  const timer = setTimeout(() => {
    progress = notifyProgress(`Copying the ${noun[1]}`, { onCancel: () => controller.abort() });
  }, progressDelayMs);
  const shown = () => progress;
  try {
    const { rows, matching } = await readRows(read, controller.signal, MAX_COPY_ROWS, (count, total) => {
      const of = total === null ? null : Math.min(total, MAX_COPY_ROWS);
      shown()?.update(
        of === null ? `${formatCount(count)} rows read` : `${formatCount(count)} of ${formatCount(of)} rows read`,
      );
    });
    clearTimeout(timer);
    if (controller.signal.aborted) throw new DOMException('Cancelled', 'AbortError');
    const made = text(rows);
    if (!(await copy(made.text))) throw new ClipboardRefused();
    const truncated = matching !== null && matching > rows.length;
    const result = `Copied ${counted(made.count, noun)}`;
    const description = truncated
      ? `From the first ${formatCount(rows.length)} of ${formatCount(matching)} rows: a copy takes no more.`
      : undefined;
    // One toast says it: the progress toast when there was one, unless there's more to say than it has room for.
    const toast = shown();
    if (toast && !description) toast.done(result);
    else {
      toast?.dismiss();
      notifyResult(result, description ? { description } : {});
    }
    announce(description ? `${result}. ${description}` : `${result}.`);
    return made.count;
  } catch (error) {
    clearTimeout(timer);
    const toast = shown();
    toast?.dismiss();
    if (isAbort(error) || controller.signal.aborted) {
      announce('Copying stopped.');
      return null;
    }
    notifyError(error, {
      title: failed,
      ...(error instanceof ClipboardRefused ? { description: 'The clipboard didn’t take them. Try again.' } : {}),
    });
    return null;
  }
}
