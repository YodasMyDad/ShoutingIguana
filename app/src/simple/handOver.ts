// A problem, handed on (docs/v2-ui-simple.md, "One problem": A list to send on). Copy the list puts it on the clipboard
// as plain text: the plain title and the technical one, the site and the date, how to fix it, then each cause with
// the addresses it's on, in full: every cause the check's groups give (an image with its size and pixels), each with
// every page it's on. Save as a spreadsheet writes this check's sheet of the workbook (E5's `check`
// export) through the app's one export flow: the export dialog, the native save dialog, and the export's toast.
import { api } from '../api/client';
import { isAbort } from '../api/errors';
import type { CheckGroupRef, IssueGroup, IssueRow, RelatedFacts } from '../api/grids';
import type { Check, CrawlListItem, PlatformOwner } from '../api/types';
import { announce } from '../feedback/announce';
import { notifyError, notifyProgress, notifyResult } from '../feedback/notify';
import { copyText } from '../lib/clipboard';
import { formatBytes, formatCount } from '../lib/format';
import { openExportDialog } from '../export/exportFlow';
import type { ExportOffer } from '../export/exportOffer';
import { issuesOffer } from '../export/offers';
import { longDate } from './dates';
import { plainDetail, plainDetailColumn } from './plainEvidence';
import { plainTitle } from './plainWords';
import { causeName, causesWords, fileWords, filesOf, rowDetailSaysMore } from './whereWords';

/** The most rows (or groups) one read asks for (the contract's limit). */
const PAGE = 1_000;
/** The groups whose rows are read at once: a check with thousands of causes reads each, so a few go side by side. */
const READERS = 6;
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
  /**
   * X4: the site's files a check about its rules file or a sitemap names (whereWords' filesOf), handed on where its home
   * page's address would be; null or left out hands on the row.
   */
  files?: readonly { url: string; sitemapId: number | null }[] | null | undefined;
  /**
   * Who sets what it found, where the owner can't change it (the Shopify lane, phase B): its reason is handed on in
   * place of how to fix it. Null or left out: the steps.
   */
  platform?: PlatformOwner | null | undefined;
}

/** A cause of a check that groups its issues, with the rows of the addresses it's on, as Copy the list hands it on. */
export interface ProblemCause {
  group: IssueGroup;
  rows: readonly IssueRow[];
}

/** A row's detail as the page says it: a few checks' details are said plainly ("Google will likely cut this title…"). */
const detailOf = (check: Check, row: IssueRow): string | null =>
  plainDetail(check, row as unknown as Record<string, unknown>) ?? row.detail ?? null;

/**
 * What a file a cause names is, where the check judges it by that: "2.1 MB, 4000×3000 pixels" for an oversized
 * image, the pixels alone for one without its size. Null where the check says nothing of the file.
 */
function fileFacts(check: Check, related: RelatedFacts | null | undefined): string | null {
  const facts = check.targetFacts ?? [];
  if (!related) return null;
  const parts = [
    facts.includes('sizeBytes') && typeof related.sizeBytes === 'number' ? formatBytes(related.sizeBytes) : null,
    facts.includes('pixelWidth') && typeof related.pixelWidth === 'number' && typeof related.pixelHeight === 'number'
      ? `${related.pixelWidth}×${related.pixelHeight} pixels`
      : null,
  ].filter((part): part is string => part !== null);
  return parts.length > 0 ? parts.join(', ') : null;
}

/**
 * A cause's line: the text its pages share, in quotes; the address it's about, in full, with what the file is for a
 * check that judges it; else what was found, named as Where it is names it ("No main heading").
 */
function causeLine(check: Check, group: IssueGroup): string {
  if (group.sharedText) return `“${group.sharedText}”`;
  if (group.relatedUrl) {
    const facts = fileFacts(check, group.related);
    return facts ? `${group.relatedUrl} (${facts})` : group.relatedUrl;
  }
  return causeName(check, group.detail);
}

/**
 * The list as text. A check that groups its issues lists each of its causes, as the check's groups give them (an image
 * on every page it's on, not only on the pages it's the first of), with its pages, and a page's own detail where it
 * says more than the cause; one that doesn't (or whose groups came back empty) lists its rows, each with its detail.
 */
export function problemListText(
  context: ProblemListContext,
  rows: readonly IssueRow[],
  causes: readonly ProblemCause[] | null = null,
): string {
  const { check } = context;
  const head = [
    plainTitle(check) === check.title ? check.title : `${plainTitle(check)} (${check.title})`,
    `${context.site}${context.checkedAt ? `, checked on ${longDate(context.checkedAt)}` : ''}`,
    context.share,
  ];
  const steps = (check.plainFix ?? []).map((step, index) => `${index + 1}. ${step}`);
  const lines = [...head, ''];
  if (context.platform) lines.push(context.platform.reason, '');
  else if (steps.length > 0) lines.push('How to fix it:', ...steps, '');
  lines.push('Where it is:');
  // X4: a check about a file names its files, then what was found, never the home page its issue is on.
  const files = filesOf(check, context);
  if (files && check.about) {
    const label = fileWords(check.about).label;
    for (const file of files) lines.push(`${label}: ${file.url}`);
    const found = rows.map((row) => detailOf(check, row)).filter((detail): detail is string => Boolean(detail));
    for (const detail of new Set(found)) lines.push(`What was found: ${detail}`);
    return lines.join('\n');
  }
  if (check.grouping === 'none' || causes === null) {
    for (const row of rows) {
      const detail = detailOf(check, row);
      lines.push(detail ? `${row.url ?? ''} (${detail})` : (row.url ?? ''));
    }
    return lines.join('\n');
  }
  for (const { group, rows: pages } of causes) {
    lines.push(causeLine(check, group));
    for (const row of pages) {
      const detail = rowDetailSaysMore(check, group, row) ? detailOf(check, row) : null;
      lines.push(detail ? `  ${row.url ?? ''} (${detail})` : `  ${row.url ?? ''}`);
    }
  }
  return lines.join('\n');
}

/** The columns each row is read with: its address, its detail and the address it's about, and a plain line's column. */
function rowColumns(checkId: string): string[] {
  const plain = plainDetailColumn({ id: checkId });
  return ['url', 'detail', 'relatedUrl', ...(plain ? [plain] : [])];
}

/** Reads every row of a check, or of one of its groups, page after page, until its cursor runs out. */
async function readAllRows(
  crawlId: string,
  checkId: string,
  signal: AbortSignal,
  onPage: (read: number, total: number | null) => void,
  group: CheckGroupRef | null = null,
): Promise<IssueRow[]> {
  const rows: IssueRow[] = [];
  let after: string | undefined;
  for (;;) {
    const page = await api.issueRows(
      crawlId,
      checkId,
      {
        columns: rowColumns(checkId),
        filters: [],
        sort: [],
        limit: PAGE,
        ...(group ? { group } : {}),
        ...(after ? { after } : {}),
      },
      signal,
    );
    rows.push(...page.rows);
    onPage(rows.length, page.matching);
    if (!page.next || page.rows.length === 0) return rows;
    after = page.next;
  }
}

/** Reads every group of a check, in the check's own order, a page of them at a time. */
async function readAllGroups(crawlId: string, checkId: string, signal: AbortSignal): Promise<IssueGroup[]> {
  const groups: IssueGroup[] = [];
  for (;;) {
    const page = await api.issueGroups(crawlId, checkId, { offset: groups.length, limit: PAGE }, signal);
    groups.push(...page.groups);
    if (page.groups.length === 0 || groups.length >= (page.groupCount ?? 0)) return groups;
  }
}

/**
 * Reads each group's rows, a few groups at a time (a check can have thousands of causes, each its own read), keeping
 * the groups' order. Says how many causes are read as it goes.
 */
async function readCauses(
  crawlId: string,
  checkId: string,
  groups: readonly IssueGroup[],
  signal: AbortSignal,
  onRead: (read: number) => void,
): Promise<ProblemCause[]> {
  const causes: ProblemCause[] = groups.map((group) => ({ group, rows: [] }));
  let next = 0;
  let read = 0;
  const reader = async () => {
    for (let cause = causes[next++]; cause; cause = causes[next++]) {
      const ref: CheckGroupRef = { detail: cause.group.detail, relatedUrlId: cause.group.relatedUrlId };
      cause.rows = await readAllRows(crawlId, checkId, signal, () => undefined, ref);
      onRead(++read);
    }
  };
  await Promise.all(Array.from({ length: Math.min(READERS, groups.length) }, reader));
  return causes;
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
  const { check } = context;
  const update = (read: number, total: number | null) =>
    progress?.update(total === null ? `${formatCount(read)} read` : `${formatCount(read)} of ${formatCount(total)}`);
  try {
    // A check that groups its issues hands over its causes as its groups give them; its rows alone where it has none.
    const groups = check.grouping === 'none' ? [] : await readAllGroups(context.crawlId, check.id, controller.signal);
    const causes =
      groups.length > 0
        ? await readCauses(context.crawlId, check.id, groups, controller.signal, (read) => update(read, groups.length))
        : null;
    const rows = causes ? [] : await readAllRows(context.crawlId, check.id, controller.signal, update);
    clearTimeout(timer);
    const copied = await copy(problemListText(context, rows, causes));
    const pages = causes ? new Set(causes.flatMap((cause) => cause.rows.map((row) => row.url))).size : rows.length;
    const files = filesOf(check, context);
    const fileNoun = check.about ? fileWords(check.about) : null;
    const words = !copied
      ? 'The list couldn’t be copied'
      : files && fileNoun
        ? `Copied the list: ${formatCount(files.length)} ${files.length === 1 ? fileNoun.one : fileNoun.many}`
        : causes
          ? `Copied the list: ${causesWords(check, causes.length, pages)}`
          : `Copied the list: ${pages === 1 ? '1 address' : `${formatCount(pages)} addresses`}`;
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
    // One cause's read failed: the others reading beside it stop too.
    controller.abort();
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
