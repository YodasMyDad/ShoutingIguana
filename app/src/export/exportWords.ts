// What an export says, in the app's words: its file's default name, how far it has got, how it ended, and each
// problem the engine can answer with, with the action that helps. Pure, so every word is tested.
import { isProblem, type ProblemBody } from '../api/errors';
import type { ExportFormat, ExportJob, ExportKind } from '../api/exports';
import { formatCount, formatCountOf, formatList, formatMilliseconds } from '../lib/format';

/** The extension each format's file takes: the engine refuses any other. */
export const EXTENSION: Record<ExportFormat, string> = { csv: 'csv', xlsx: 'xlsx', html: 'html' };

/** The native save dialog's filter for each format. */
export const FORMAT_FILTER: Record<ExportFormat, { name: string; extensions: string[] }> = {
  csv: { name: 'CSV (comma-separated values)', extensions: ['csv'] },
  xlsx: { name: 'Excel workbook', extensions: ['xlsx'] },
  html: { name: 'Web page (HTML)', extensions: ['html', 'htm'] },
};

/** The formats in words, as the dialog offers them. */
export const FORMAT_TITLE: Record<ExportFormat, string> = { csv: 'CSV', xlsx: 'Excel', html: 'HTML' };

/** A file name holds none of these on Windows (the engine refuses them), nor a control character. */
const NOT_IN_A_NAME = /[\\/:*?"<>|\p{Cc}]/gu;
/** Room for a folder in front of it: Windows' paths are 260 characters unless long paths are on. */
const MAX_NAME_LENGTH = 120;

/** `2026-10-31`: the date on the computer's own calendar, as file names sort. Today when the crawl's isn't known. */
export function fileDate(iso: string | null | undefined, now: Date = new Date()): string {
  const parsed = iso ? new Date(iso) : null;
  const date = parsed && !Number.isNaN(parsed.getTime()) ? parsed : now;
  const pad = (value: number) => String(value).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

/**
 * The file's default name: the site, the crawl's date and the view, then the format's extension
 * ("example.com 2026-10-31 URLs.xlsx", "gardenfurnituresets.co.uk 2026-10-31 report.html"). Characters no file's name
 * may have become a dash, and a long view is shortened, so the name the dialog offers is always one it can save.
 */
export function exportFileName(site: string, date: string, view: string, format: ExportFormat): string {
  const clean = (text: string) => text.replace(NOT_IN_A_NAME, '-').replace(/\s+/g, ' ').trim();
  const host = clean(site.replace(/^[a-z][a-z\d+.-]*:\/\//i, '').replace(/\/.*$/, '')) || 'crawl';
  let stem = `${host} ${date} ${clean(view)}`.trim();
  if (stem.length > MAX_NAME_LENGTH) stem = `${stem.slice(0, MAX_NAME_LENGTH - 1).trimEnd()}…`;
  // A name Windows won't keep: one that ends in a dot or a space.
  stem = stem.replace(/[. ]+$/, '');
  return `${stem}.${EXTENSION[format]}`;
}

/** The folder of a path as the save dialog gave it, so the next export's dialog opens there. Null for a bare name. */
export function folderOf(path: string): string | null {
  const cut = Math.max(path.lastIndexOf('\\'), path.lastIndexOf('/'));
  return cut > 0 ? path.slice(0, cut) : null;
}

/** A path in that folder, with the separator the folder already uses. */
export function inFolder(folder: string | null, name: string): string {
  if (!folder) return name;
  const separator = folder.includes('\\') || !folder.includes('/') ? '\\' : '/';
  return folder.endsWith(separator) ? `${folder}${name}` : `${folder}${separator}${name}`;
}

/** The file's name, without its folder. */
export function fileNameOf(path: string): string {
  const name = path.split(/[\\/]/).pop();
  return name && name.length > 0 ? name : path;
}

/** The rows an export of each kind writes, in words: "URLs", "links", "pages". */
export function rowNoun(kind: ExportKind): { one: string; many: string } {
  switch (kind) {
    case 'urls':
    case 'sitemapUrls':
    case 'robotsBlocked':
      return { one: 'URL', many: 'URLs' };
    case 'links':
      return { one: 'link', many: 'links' };
    case 'issueRows':
    case 'check':
    case 'workbook':
      return { one: 'row', many: 'rows' };
    case 'report':
      return { one: 'page', many: 'pages' };
  }
}

/** What's being exported, when the screen that started it didn't say (an export the engine told of after a reload). */
export function kindTitle(kind: ExportKind): string {
  switch (kind) {
    case 'urls':
      return 'URLs';
    case 'links':
      return 'Links';
    case 'sitemapUrls':
      return 'A sitemap’s URLs';
    case 'robotsBlocked':
      return 'URLs blocked by robots.txt';
    case 'issueRows':
      return 'A check’s pages';
    case 'check':
      return 'A check';
    case 'workbook':
      return 'Spreadsheet of everything';
    case 'report':
      return 'The report';
  }
}

/**
 * The share of the rows written, for the bar: null while there's nothing to measure (not counted yet, or the report,
 * which has no rows), so the bar moves on its own. Never more than the whole.
 */
export function shareWritten(job: ExportJob): number | null {
  if (job.status !== 'running') return job.status === 'done' ? 1 : null;
  if (job.rowsExpected === null) return null;
  if (job.rowsExpected === 0) return 0;
  return Math.min(1, job.rowsWritten / job.rowsExpected);
}

/**
 * The share the bar shows: never less than it showed before. A running crawl's count can be older than its rows, so
 * `rowsExpected` rises with `rowsWritten` and the share can fall; the bar holds where it was until the rows pass it.
 */
export function nextPeak(previous: number, job: ExportJob): number {
  const share = shareWritten(job);
  return share === null ? previous : Math.max(previous, share);
}

export type ExportPhase = 'starting' | 'counting' | 'writing' | 'done' | 'cancelled' | 'failed' | 'lost';

/** Where an export is, as the toast tells it. */
export function exportPhase(job: ExportJob, lost = false): ExportPhase {
  if (lost) return 'lost';
  switch (job.status) {
    case 'done':
    case 'cancelled':
    case 'failed':
      return job.status;
    default:
      if (job.kind === 'report') return 'writing';
      if (job.rowsExpected === null) return job.rowsWritten > 0 ? 'writing' : 'starting';
      return job.rowsWritten === 0 ? 'counting' : 'writing';
  }
}

/** "41,000 of 112,590 URLs": how far it has got. Null while nothing can be said in numbers. */
export function progressWords(job: ExportJob): string | null {
  const noun = rowNoun(job.kind);
  if (job.kind === 'report') return null;
  if (job.rowsExpected === null)
    return job.rowsWritten > 0 ? formatCountOf(job.rowsWritten, noun.one, noun.many) : null;
  return `${formatCount(job.rowsWritten)} of ${formatCountOf(job.rowsExpected, noun.one, noun.many)}`;
}

/** The time the engine took, start to end, by its own clock (both times are its). Null while it runs. */
export function engineMilliseconds(job: ExportJob): number | null {
  if (job.finishedAt === null) return null;
  const started = Date.parse(job.startedAt);
  const finished = Date.parse(job.finishedAt);
  return Number.isNaN(started) || Number.isNaN(finished) ? null : Math.max(0, finished - started);
}

/** The line under a running export's name. */
export function runningLine(job: ExportJob): string {
  switch (exportPhase(job)) {
    case 'starting':
      return job.kind === 'report' ? 'Writing the report…' : 'Starting…';
    case 'counting':
      return `Counting the ${rowNoun(job.kind).many}…`;
    default:
      return job.kind === 'report' ? 'Writing the report…' : 'Writing…';
  }
}

/**
 * What a finished export says beyond "Saved": its rows and the engine's time, then anything the file holds that isn't
 * what the grid shows: cells cut to what a spreadsheet takes, columns that had no values, a crawl that was running.
 */
export function doneLines(job: ExportJob, columnTitle: (id: string) => string = (id) => id): string[] {
  const result = job.result;
  const lines: string[] = [];
  const took = engineMilliseconds(job);
  const time = took === null ? '' : ` in ${formatMilliseconds(took)}`;
  if (result && job.kind !== 'report') {
    const noun = rowNoun(job.kind);
    const sheets = result.sheets > 1 ? `, on ${formatCount(result.sheets)} sheets` : '';
    lines.push(`${formatCountOf(result.rows, noun.one, noun.many)}${sheets}${time}.`);
  } else if (time) {
    lines.push(`Written${time}.`);
  }
  if (result?.crawlWasRunning) {
    lines.push('The crawl was running: the file holds what it had when the export began.');
  }
  if (result && result.truncatedCells > 0) {
    const limit = job.format === 'csv' ? '32,758' : '32,767';
    const cells = formatCountOf(result.truncatedCells, 'cell was', 'cells were');
    lines.push(`${cells} cut to the ${limit} characters a spreadsheet reads in one.`);
  }
  if (result && result.unavailableColumns.length > 0) {
    const titles = formatList(result.unavailableColumns.map(columnTitle));
    const are = result.unavailableColumns.length === 1 ? 'is' : 'are';
    lines.push(`${titles} ${are} empty: they need a current analysis of the crawl.`);
  }
  return lines;
}

/** What helps with a problem: each a button in the toast, or in the dialog when the export was refused at once. */
export type ExportRemedy =
  /** The save dialog again, for another folder or name. */
  | 'chooseAnother'
  /** The same file again: once the program holding it has let it go, or once an export has finished. */
  | 'tryAgain'
  /** The same export again, of the crawl as it now is. */
  | 'exportAgain'
  /** The same view as CSV, which has no limit on rows. */
  | 'asCsv'
  /** Work out the crawl's results (analyse it). */
  | 'analyse'
  /** The same view in crawl order: a running crawl's export can't be sorted by what it changes. */
  | 'crawlOrder';

export interface ExportProblemWords {
  /** The app's sentence for it. */
  sentence: string;
  remedies: ExportRemedy[];
}

/** A path to another computer: `\\server\share\…`, or a device path (`\\?\`, `\\.\`). */
export function isNetworkPath(path: string): boolean {
  return /^[\\/]{2}/.test(path);
}

/**
 * Each problem an export can end in, or be refused with, in a sentence of the app's with the action that helps. The
 * engine's own title is the fallback for one this app has no words for, as its titles are plain sentences.
 */
export function exportProblemWords(
  problem: Pick<ProblemBody, 'type' | 'title' | 'errors'> & { reason?: string | null | undefined },
  context: { path?: string | undefined; format?: ExportFormat | undefined; simple?: boolean | undefined } = {},
): ExportProblemWords {
  const results = context.simple ? 'work out the results' : 'analyse the crawl';
  switch (problem.type) {
    case 'export-busy':
      return {
        sentence:
          'Four exports are running already, or one is writing this file now. Try again once one has finished, or save to another file.',
        remedies: ['tryAgain', 'chooseAnother'],
      };
    case 'export-not-writable':
      return {
        sentence: 'That folder isn’t there, or this account can’t save files in it. Choose another folder.',
        remedies: ['chooseAnother'],
      };
    case 'export-file-in-use':
      return {
        sentence:
          'Another program has that file open (Excel keeps a workbook it shows), so it can’t be replaced. Close it there and try again, or save under another name.',
        remedies: ['tryAgain', 'chooseAnother'],
      };
    case 'export-disk-full':
      return {
        sentence:
          'The disk filled up before the file was whole, so nothing was saved. Free some space, or save to another drive.',
        remedies: ['chooseAnother', 'tryAgain'],
      };
    case 'export-crawl-changed':
      return {
        sentence:
          'The crawl changed while it was being exported (it was analysed again, continued or finished), so nothing was saved. Export it again to have it as it is now.',
        remedies: ['exportAgain'],
      };
    case 'export-too-large':
      return {
        sentence:
          'That’s more rows than an Excel file holds (16 sheets of 1,048,575). Export it as CSV, which has no limit, or narrow it with a filter.',
        remedies: ['asCsv'],
      };
    case 'not-analyzed':
      return {
        sentence: `The crawl’s results haven’t been worked out, so there’s nothing to export yet. ${capitalise(results)} first.`,
        remedies: ['analyse'],
      };
    case 'needs-analysis':
      return {
        sentence:
          problem.reason === 'crawling'
            ? 'Some of these columns come from the crawl’s analysis, which waits for the crawl to end. Export without them, or once the crawl is done.'
            : `Some of these columns come from the crawl’s analysis, which it doesn’t have. ${capitalise(results)}, then export again.`,
        remedies: problem.reason === 'crawling' ? [] : ['analyse'],
      };
    case 'check-skipped':
      return {
        sentence:
          'The last analysis didn’t run this check, so it has no pages to export. Analyse the crawl again to run it.',
        remedies: ['analyse'],
      };
    case 'check-not-found':
      return { sentence: 'This version of the app doesn’t have that check, so it can’t be exported.', remedies: [] };
    case 'sitemap-not-found':
      return {
        sentence: 'The crawl doesn’t have that sitemap (any more), so there’s nothing to export.',
        remedies: [],
      };
    case 'origin-not-found':
      return { sentence: 'The crawl has no robots.txt for that origin, so there’s nothing to export.', remedies: [] };
    case 'crawl-not-found':
      return { sentence: 'The crawl isn’t in the library any more, so it can’t be exported.', remedies: [] };
    case 'invalid-query':
      if (problem.errors?.sort) {
        return {
          sentence:
            'While the crawl runs, an export is in crawl order or by URL: rows sorted by anything the crawl changes would move as it’s written.',
          remedies: ['crawlOrder'],
        };
      }
      return { sentence: `The engine won’t export this view as it is: ${problem.title}`, remedies: [] };
    case 'invalid-export':
      if (problem.errors?.path) {
        if (context.path && isNetworkPath(context.path)) {
          return {
            sentence:
              'Exports are saved on this computer: a network folder (\\\\server\\share) isn’t allowed. A mapped drive letter is.',
            remedies: ['chooseAnother'],
          };
        }
        const said = problem.errors.path[0];
        return {
          sentence: said
            ? `That file can’t be saved: ${lowerFirst(said)}`
            : 'That file can’t be saved. Choose another name.',
          remedies: ['chooseAnother'],
        };
      }
      return { sentence: `The engine won’t make this export: ${problem.title}`, remedies: [] };
    default:
      return { sentence: problem.title, remedies: [] };
  }
}

/** The words for an error a call ended in: the problem's when it is one. */
export function exportErrorWords(
  error: unknown,
  context: Parameters<typeof exportProblemWords>[1] = {},
): ExportProblemWords | null {
  return isProblem(error) ? exportProblemWords(error, context) : null;
}

/** Each remedy's button. */
export function remedyTitle(remedy: ExportRemedy, simple = false): string {
  switch (remedy) {
    case 'chooseAnother':
      return 'Choose another file…';
    case 'tryAgain':
      return 'Try again';
    case 'exportAgain':
      return 'Export again';
    case 'asCsv':
      return 'Export as CSV…';
    case 'analyse':
      return simple ? 'Work out the results' : 'Analyse the crawl';
    case 'crawlOrder':
      return 'Export in crawl order';
  }
}

function capitalise(text: string): string {
  return text.charAt(0).toUpperCase() + text.slice(1);
}

function lowerFirst(text: string): string {
  // An engine sentence starting with a name ("Windows…") keeps its capital.
  return /^[A-Z][a-z]/.test(text) && !/^(Windows|Excel|CSV)/.test(text)
    ? text.charAt(0).toLowerCase() + text.slice(1)
    : text;
}
