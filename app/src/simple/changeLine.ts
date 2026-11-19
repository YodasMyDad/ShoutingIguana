// A problem's change since the site's last check, on its row in Simple (the UX audit of 2026-11-19, S-12 and S-20):
// what got worse is said as plainly as what was fixed. A check that went from 18 pages to 257 read "18 on 17 Nov" in
// the same grey as "same as 17 Nov" on every other row; now it says "▲ 239 more than on 17 Nov" in the warning colour,
// and a row that didn't change says nothing. Two checks made on one day can't be told apart by their date, so then it
// says "last time". Pure.
import type { CheckChange } from '../api/fixLoop';
import { changeDate } from '../changes/changeWords';
import { formatCount } from '../lib/format';

/** What leads a row's line when it got worse: the row reads it for its colour. */
export const WORSE_MARK = '▲';
const BETTER_MARK = '▼';

/** Whether two checks were made on the same day, as their dates read: "17 Nov" and "17 Nov". */
export function sameDay(a: string | null | undefined, b: string | null | undefined): boolean {
  const first = changeDate(a);
  return first !== '' && first === changeDate(b);
}

/**
 * A check's change in its count's second line: "▲ 239 more than on 17 Nov", "▼ 3 fewer than on 17 Nov", "New since 17
 * Nov", "Not looked for last time"; nothing for one that's the same, or wasn't checked this time. `ambiguous` is for a
 * last check made the same day as this one: "than last time", "New since the last check".
 */
export function rowChangeLine(
  change: Pick<CheckChange, 'state' | 'then' | 'now'>,
  since: string | null,
  ambiguous = false,
): string | null {
  const date = ambiguous ? '' : changeDate(since);
  const than = date ? `than on ${date}` : 'than last time';
  const sinceThen = date ? `since ${date}` : 'since the last check';
  const delta = (change.now ?? 0) - (change.then ?? 0);
  switch (change.state) {
    case 'more':
      return `${WORSE_MARK} ${formatCount(delta)} more ${than}`;
    case 'fewer':
      return `${BETTER_MARK} ${formatCount(-delta)} fewer ${than}`;
    case 'new':
      return `New ${sinceThen}`;
    case 'fixed':
      return `Fixed ${sinceThen}`;
    case 'newCheck':
      return 'Not looked for last time';
    default:
      return null;
  }
}

/** Whether a row's line says it got worse (more of it, or new): it's then in the warning colour. */
export function saysWorse(line: string | null | undefined): boolean {
  return Boolean(line && (line.startsWith(WORSE_MARK) || line.startsWith('New ')));
}

/** "2 got worse", "1 got worse": the Summary's count of problems that are on more pages than last time; null for none. */
export function gotWorseWords(more: number): string | null {
  return more > 0 ? `${formatCount(more)} got worse` : null;
}
