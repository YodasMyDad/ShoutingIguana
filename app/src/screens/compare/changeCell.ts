// A change as Advanced's lists and grids show it (docs/v2-fix-loop.md, Advanced: the checks list's "Since 3 Nov"
// column, the head's "9 on 3 Nov", a group's "3 Nov" and state cells, a row's "Since 3 Nov"): text for the eye ("−5",
// "New", "Fixed"), words for a screen reader, and the rows' split in a tooltip. A comparison with a later crawl reads
// forward ("by 20 Nov"). Pure.
import type { CheckChange, GroupChange, RowChange } from '../../api/fixLoop';
import { changeDate } from '../../changes/changeWords';
import { checkSplitWords } from '../../changes/changeWords';
import { formatCount } from '../../lib/format';

/** The crawl compared with: when it started, and whether it's a later one. */
export interface ComparedPeriod {
  iso: string | null;
  later: boolean;
}

/** "since 17 Oct", or "by 20 Nov" for a later crawl. */
function sinceOrBy(period: ComparedPeriod): string {
  const date = changeDate(period.iso) || 'the other crawl';
  return period.later ? `by ${date}` : `since ${date}`;
}

/** "Since 17 Oct", or "Until 20 Nov": a column's or a tooltip's head. */
export function periodLabel(period: ComparedPeriod): string {
  const date = changeDate(period.iso) || 'the other crawl';
  return period.later ? `Until ${date}` : `Since ${date}`;
}

/** "on 17 Oct". */
function onDate(period: ComparedPeriod): string {
  const date = changeDate(period.iso);
  return date ? `on ${date}` : 'then';
}

const signed = (move: number) => (move > 0 ? `+${formatCount(move)}` : move < 0 ? `−${formatCount(-move)}` : '±0');

export interface ChangeCell {
  shown: string;
  spoken: string;
  title: string;
}

/** A check's change: "−5" (5 fewer since 17 Oct), "+2", "±0", "New", "Fixed", "New check", "Not checked". */
export function checkChangeCell(change: CheckChange, period: ComparedPeriod): ChangeCell {
  const move = (change.now ?? 0) - (change.then ?? 0);
  const when = sinceOrBy(period);
  const counts =
    change.then !== null && change.now !== null
      ? `${periodLabel(period)}: ${formatCount(change.then)} then, ${formatCount(change.now)} now`
      : periodLabel(period);
  const split = checkSplitWords(change);
  const title = split ? `${counts}. ${split}` : counts;
  switch (change.state) {
    case 'fewer':
      return { shown: signed(move), spoken: `${formatCount(-move)} fewer ${when}`, title };
    case 'more':
      return { shown: signed(move), spoken: `${formatCount(move)} more ${when}`, title };
    case 'same':
      return { shown: '±0', spoken: `the same as ${onDate(period)}`, title };
    case 'new':
      return { shown: 'New', spoken: `new ${when}`, title };
    case 'fixed':
      return { shown: 'Fixed', spoken: `fixed ${when}`, title };
    case 'newCheck':
      return { shown: 'New check', spoken: `not looked for ${onDate(period)}`, title };
    case 'notCheckedNow':
      return { shown: 'Not checked', spoken: 'not checked this time', title };
  }
}

/**
 * A group's change: its URLs then ("5", "–" for a cause it didn't have) and its move ("−2", "New", "±0"). Against a
 * later crawl, read forward: what the cause came to by then.
 */
export function groupChangeCell(
  change: GroupChange,
  urlsNow: number,
  period: ComparedPeriod,
): { then: string; shown: string; spoken: string } {
  const when = sinceOrBy(period);
  const thenText = change.state === 'new' ? '–' : formatCount(change.then);
  if (change.state === 'new') {
    return period.later
      ? { then: thenText, shown: 'Gone by then', spoken: `gone ${when}` }
      : { then: thenText, shown: 'New', spoken: `new ${when}` };
  }
  const move = period.later ? change.then - urlsNow : urlsNow - change.then;
  const spoken =
    move === 0
      ? `the same as ${onDate(period)}`
      : `${formatCount(Math.abs(move))} ${move < 0 ? 'fewer' : 'more'} ${when}`;
  return { then: thenText, shown: signed(move), spoken };
}

/** A row's change: "Still", "New", "New URL", "Not compared"; against a later crawl, what became of it by then. */
export function rowChangeCell(change: RowChange, period: ComparedPeriod): { shown: string; title: string } {
  const on = onDate(period);
  if (period.later) {
    switch (change.state) {
      case 'still':
        return { shown: 'Still', title: `Still flagged ${on}${change.detailThen ? `: ${change.detailThen}` : ''}` };
      case 'new':
        return { shown: 'Fixed by then', title: `Read ${on} and not flagged` };
      case 'newUrl':
        return { shown: 'Gone by then', title: `Not found ${on}` };
      case 'notComparedThen':
        return { shown: 'Not compared', title: `Found but not read ${on}` };
    }
  }
  switch (change.state) {
    case 'still':
      return { shown: 'Still', title: `Flagged ${on} too${change.detailThen ? `: ${change.detailThen}` : ''}` };
    case 'new':
      return { shown: 'New', title: `Read ${on} and not flagged then` };
    case 'newUrl':
      return { shown: 'New URL', title: `Not found ${on}` };
    case 'notComparedThen':
      return { shown: 'Not compared', title: `Found but not read ${on}, or the check didn’t run` };
  }
}
