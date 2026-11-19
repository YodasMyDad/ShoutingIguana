// How wide a URL draws in an Issues table, for the box IssueUrl gives it and the column its table gives it. Pure but
// for the measurer it's handed.
import { fileLabelOf, urlDisplay } from '../../ui/urlText';
import type { linkTextMeasure } from '../links/linkColumns';

/** What a URL keeps beside its text: its open button and the gap before it. */
export const URL_OPEN_ROOM = 26;
/** The gap between a file's name and what follows it (its folder or query). */
const FILE_GAP = 6;

/** How wide a URL draws as the tables show it: its path on the site's own origin, a file by its name. */
export function shownUrlWidth(
  url: string,
  siteOrigin: string | null,
  label: 'address' | 'file',
  measure: ReturnType<typeof linkTextMeasure>,
): number {
  if (label === 'file') {
    const file = fileLabelOf(url, siteOrigin);
    return measure(file.name, 'url') + (file.rest ? FILE_GAP + measure(file.rest, 'url') : 0);
  }
  return measure(urlDisplay(url, siteOrigin).shown, 'url');
}
