// A problem, handed on (docs/v2-ui-simple.md, "One problem": A list to send on). Copy the list puts it on the clipboard
// as plain text: the plain title and the technical one, the site and the date, how to fix it, then each cause with
// the addresses it's on, in full. Save as a spreadsheet writes this check's sheet of the workbook (E5's `check`
// export) through the native save dialog, with its progress and its end in a toast.
//
// TODO(round4-merge): the hand-over slice brings the export dialog and toast every grid uses; until it's merged this
// calls the API client directly with the existing save dialog (shell/os.ts) and the progress toast (feedback/notify.ts).
import { api } from '../api/client';
import { isAbort, ProblemError } from '../api/errors';
import type { CheckGrouping, IssueRow } from '../api/grids';
import type { ExportJob } from '../api/exports';
import type { Check } from '../api/types';
import { announce } from '../feedback/announce';
import { notifyError, notifyProgress, notifyResult } from '../feedback/notify';
import { copyText } from '../lib/clipboard';
import { formatCount } from '../lib/format';
import { liveStore } from '../live/liveStore';
import { pickSavePath } from '../shell/os';
import { fileDate, longDate } from './dates';
import { plainTitle } from './plainWords';

/** The most rows one read asks for (the contract's limit). */
const PAGE = 1_000;
/** A copy that takes longer than this shows its progress, with Cancel. */
const PROGRESS_DELAY_MS = 300;

export interface ProblemListContext {
  crawlId: string;
  check: Check;
  /** The site's name and when it was checked. */
  site: string;
  checkedAt: string | null;
  /** "5 of 265 pages". */
  share: string;
}

/** The cause a row belongs to, as the check groups its issues: its detail, or the address it's about. */
function causeOf(row: IssueRow, grouping: CheckGrouping): string | null {
  if (grouping === 'relatedUrl') return row.relatedUrl ?? null;
  if (grouping === 'detail') return row.detail ?? null;
  return null;
}

/** The list as text, from every row of the check. */
export function problemListText(context: ProblemListContext, rows: readonly IssueRow[]): string {
  const { check } = context;
  const head = [
    plainTitle(check) === check.title ? check.title : `${plainTitle(check)} (${check.title})`,
    `${context.site}${context.checkedAt ? `, checked on ${longDate(context.checkedAt)}` : ''}`,
    context.share,
  ];
  const steps = (check.plainFix ?? []).map((step, index) => `${index + 1}. ${step}`);
  const lines = [...head, ''];
  if (steps.length > 0) lines.push('How to fix it:', ...steps, '');
  if (check.grouping === 'none') {
    lines.push('Where it is:');
    for (const row of rows) lines.push(row.detail ? `${row.url ?? ''} (${row.detail})` : (row.url ?? ''));
    return lines.join('\n');
  }
  const causes = new Map<string, string[]>();
  for (const row of rows) {
    const cause = causeOf(row, check.grouping) ?? '';
    const list = causes.get(cause) ?? [];
    list.push(row.url ?? '');
    causes.set(cause, list);
  }
  lines.push('Where it is:');
  for (const [cause, urls] of causes) {
    lines.push(cause || 'Other', ...urls.map((url) => `  ${url}`));
  }
  return lines.join('\n');
}

/** Reads every row of a check, page after page, until its cursor runs out. */
async function readAllRows(
  crawlId: string,
  checkId: string,
  signal: AbortSignal,
  onPage: (read: number, total: number | null) => void,
): Promise<IssueRow[]> {
  const rows: IssueRow[] = [];
  let after: string | undefined;
  for (;;) {
    const page = await api.issueRows(
      crawlId,
      checkId,
      { columns: ['url', 'detail', 'relatedUrl'], filters: [], sort: [], limit: PAGE, ...(after ? { after } : {}) },
      signal,
    );
    rows.push(...page.rows);
    onPage(rows.length, page.matching);
    if (!page.next || page.rows.length === 0) return rows;
    after = page.next;
  }
}

/** Copy the list: every cause and address of the problem, as text. Says what it copied, once. */
export async function copyProblemList(
  context: ProblemListContext,
  copy: (text: string) => Promise<boolean> = copyText,
): Promise<void> {
  const controller = new AbortController();
  let progress: ReturnType<typeof notifyProgress> | null = null;
  const timer = setTimeout(() => {
    progress = notifyProgress('Copying the list…', { onCancel: () => controller.abort() });
  }, PROGRESS_DELAY_MS);
  try {
    const rows = await readAllRows(context.crawlId, context.check.id, controller.signal, (read, total) =>
      progress?.update(total === null ? `${formatCount(read)} read` : `${formatCount(read)} of ${formatCount(total)}`),
    );
    clearTimeout(timer);
    const copied = await copy(problemListText(context, rows));
    const words = copied
      ? `Copied the list: ${rows.length === 1 ? '1 address' : `${formatCount(rows.length)} addresses`}`
      : 'The list couldn’t be copied';
    const shown = progress as ReturnType<typeof notifyProgress> | null;
    if (shown) shown.done(words);
    else if (copied) notifyResult(words);
    else notifyError(new Error(words), { title: words });
    announce(words);
  } catch (error) {
    clearTimeout(timer);
    const shown = progress as ReturnType<typeof notifyProgress> | null;
    if (isAbort(error)) {
      shown?.dismiss();
      return;
    }
    if (shown) shown.fail(error);
    else notifyError(error, { title: 'The list couldn’t be copied' });
  }
}

/** A file's name from a title: the characters Windows and macOS refuse in a name are left out. */
export function safeFileName(name: string): string {
  return name
    .replace(/[<>:"/\\|?*]/g, '')
    .replace(/\s+/g, ' ')
    .trim();
}

/** "gardenfurnituresets.co.uk 2026-10-31 Links that lead to broken pages.xlsx". */
export function sheetFileName(context: Pick<ProblemListContext, 'site' | 'checkedAt' | 'check'>): string {
  return safeFileName(`${context.site} ${fileDate(context.checkedAt)} ${plainTitle(context.check)}`) + '.xlsx';
}

/** How an export's end reaches this module: the live stream's `export` events, by the export's id. */
function whenExportEnds(exportId: string, onEnd: (job: ExportJob) => void): () => void {
  return liveStore.onEvent((event) => {
    if (event.type !== 'export' || event.data.exportId !== exportId || event.data.status === 'running') return;
    onEnd(event.data);
  });
}

/**
 * Save as a spreadsheet: this check's sheet of the workbook, where the user chooses, with the export's progress and
 * its end in one toast. Nothing happens when the dialog is cancelled; outside the desktop app there's no dialog.
 */
export async function saveCheckSheet(
  context: Pick<ProblemListContext, 'crawlId' | 'check' | 'site' | 'checkedAt'>,
  pick: typeof pickSavePath = pickSavePath,
): Promise<void> {
  let path: string | null;
  try {
    path = await pick({
      title: 'Save as a spreadsheet',
      defaultPath: sheetFileName(context),
      filters: [{ name: 'Excel workbook', extensions: ['xlsx'] }],
    });
  } catch (error) {
    notifyError(error, { title: 'The save dialog couldn’t open' });
    return;
  }
  if (path === null) return;
  let exportId: string | null = null;
  const progress = notifyProgress('Saving the spreadsheet…', {
    onCancel: () => {
      if (exportId) void api.cancelExport(exportId);
    },
  });
  let ended = false;
  const finish = (job: ExportJob) => {
    if (ended) return;
    ended = true;
    if (job.status === 'done') {
      const rows = job.result?.rows ?? job.rowsWritten;
      const words = `Saved the spreadsheet: ${rows === 1 ? '1 row' : `${formatCount(rows)} rows`}`;
      progress.done(words);
      announce(words);
    } else if (job.status === 'cancelled') {
      progress.dismiss();
    } else {
      progress.fail(job.error ? new ProblemError(job.error) : new Error('The spreadsheet couldn’t be saved'));
    }
  };
  try {
    const job = await api.startExport(context.crawlId, {
      kind: 'check',
      format: 'xlsx',
      checkId: context.check.id,
      path,
    });
    exportId = job.exportId;
    if (job.status !== 'running') {
      finish(job);
      return;
    }
    const stop = whenExportEnds(job.exportId, (endedJob) => {
      stop();
      finish(endedJob);
    });
    // The end may have come before the listener: read the job once to be sure.
    const now = await api.exportJob(job.exportId);
    if (now.status !== 'running') {
      stop();
      finish(now);
    }
  } catch (error) {
    progress.fail(error);
  }
}
