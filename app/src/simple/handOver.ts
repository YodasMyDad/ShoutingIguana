// A problem, handed on (docs/v2-ui-simple.md, "One problem": A list to send on). Copy the list puts it on the clipboard
// as plain text: the plain title and the technical one, the site and the date, how to fix it, then each cause with
// the addresses it's on, in full. Save as a spreadsheet writes this check's sheet of the workbook (E5's `check`
// export) through the app's one export flow: the export dialog, the native save dialog, and the export's toast.
import { api } from '../api/client';
import { isAbort } from '../api/errors';
import type { CheckGrouping, IssueRow } from '../api/grids';
import type { Check, CrawlListItem } from '../api/types';
import { announce } from '../feedback/announce';
import { notifyError, notifyProgress, notifyResult } from '../feedback/notify';
import { copyText } from '../lib/clipboard';
import { formatCount } from '../lib/format';
import { openExportDialog } from '../export/exportFlow';
import type { ExportOffer } from '../export/exportOffer';
import { issuesOffer } from '../export/offers';
import { longDate } from './dates';
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

/**
 * Save as a spreadsheet: this check's sheet of the workbook (E5's `check` export), offered as the export dialog every
 * screen uses, named by the check's plain title ("gardenfurnituresets.co.uk 2026-10-31 Links that lead to broken
 * pages.xlsx"). The dialog asks as what, the save dialog where, and the export's toast follows it from there.
 */
export function checkSheetOffer(item: CrawlListItem, check: Check, crawling = false): ExportOffer {
  const offer = issuesOffer({
    item,
    crawling,
    simple: true,
    check: { id: check.id, title: plainTitle(check), urls: null },
    group: null,
  });
  return {
    ...offer,
    title: 'Save as a spreadsheet',
    choices: offer.choices.filter((choice) => choice.id === 'check'),
  };
}

/** Opens the export dialog on this check's sheet. */
export function saveCheckSheet(item: CrawlListItem, check: Check, crawling = false): void {
  openExportDialog(checkSheetOffer(item, check, crawling), { choiceId: 'check' });
}
