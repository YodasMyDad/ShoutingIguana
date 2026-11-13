// The Report screen's files: their default names (docs/v2-ui-simple.md, "The Report screen") and what the save dialog
// offers. "gardenfurnituresets.co.uk 2026-10-31 report.html", and the spreadsheet of everything beside it with the same
// name, so the two sort together in a folder.
import type { ReportCut } from '../../api/exports';
import type { CrawlListItem } from '../../api/types';
import { crawlName } from '../../crawl/status';

/** What the Report screen saves: the report's document in the cut shown, or the spreadsheet of everything. */
export type ReportFileKind = 'report' | 'workbook';

/** The crawl's date in a file name, as the computer's clock reads it: `2026-10-31`. Null when it has none. */
export function fileDate(iso: string | null | undefined): string | null {
  if (!iso) return null;
  const when = new Date(iso);
  if (Number.isNaN(when.getTime())) return null;
  const pad = (value: number) => String(value).padStart(2, '0');
  return `${when.getFullYear()}-${pad(when.getMonth() + 1)}-${pad(when.getDate())}`;
}

/**
 * A name no file system refuses: the characters Windows keeps for itself (and control characters) become spaces, and a
 * name never ends in a dot or a space, which Windows drops.
 */
// eslint-disable-next-line no-control-regex -- control characters are what this takes out
const RESERVED = /[\\/:*?"<>|\u0000-\u001f]+/g;

function safeName(name: string): string {
  return name
    .replace(RESERVED, ' ')
    .replace(/\s+/g, ' ')
    .trim()
    .replace(/[. ]+$/, '');
}

/** The file's name, without a folder: the site, the crawl's date and what it is. */
export function reportFileName(
  item: Pick<CrawlListItem, 'site' | 'fileName' | 'startedAt' | 'analyzedAt'> | undefined,
  kind: ReportFileKind,
  cut: ReportCut = 'full',
): string {
  const date = fileDate(item?.startedAt ?? item?.analyzedAt);
  const what = kind === 'report' && cut === 'summary' ? 'report summary' : 'report';
  const stem = safeName([crawlName(item), date, what].filter((part) => part !== null).join(' '));
  return `${stem || 'report'}.${kind === 'report' ? 'html' : 'xlsx'}`;
}

/** The save dialog's title and its one kind of file. */
export function saveDialogOptions(kind: ReportFileKind, defaultPath: string) {
  return kind === 'report'
    ? {
        title: 'Save the report',
        defaultPath,
        filters: [{ name: 'Web page (HTML)', extensions: ['html'] }],
      }
    : {
        title: 'Save the spreadsheet of everything',
        defaultPath,
        filters: [{ name: 'Excel workbook', extensions: ['xlsx'] }],
      };
}
