// What moved since the crawl compared with, in Advanced's headline unit (round 3's AnswerHeadline: "About 77 things to
// fix · about 775 more worth a look"): the things to fix and the things worth a look then and now, worked out from each
// analysis as the headline works them out, and the things fixed and new, check by check. Pure.
import type { Analysis, Checks } from '../../api/types';
import { formatCount } from '../../lib/format';
import { analysisTotals, checkEntries, countedEntries, type CheckEntry } from '../issues/checkList';

export interface ThingsMoved {
  /** The headline's things to fix (the errors' and warnings'), then and now. */
  fixThen: number;
  fixNow: number;
  /** The headline's things worth a look (the notices'), then and now. */
  lookThen: number;
  lookNow: number;
  /** Things to fix gone since (a check's fewer), and new (a check's more), over the checks that ran in both. */
  fixed: number;
  added: number;
}

const found = (entry: CheckEntry) => entry.outcome !== 'passed' && entry.outcome !== 'skipped';

/** A check's things in an analysis: its causes when it found something, 0 when it didn't; null when not counted. */
function thingsOf(entry: CheckEntry): number | null {
  return found(entry) ? entry.causes : 0;
}

/**
 * The headline's figures then and now, and the things fixed and new; null when either analysis didn't count its things
 * (an earlier engine's), so nothing is said in another unit. Hidden checks are left out, as the headline leaves them.
 */
export function thingsMoved(
  checks: Checks,
  now: Analysis,
  then: Analysis,
  hidden: ReadonlySet<string>,
): ThingsMoved | null {
  const entriesNow = checkEntries(checks, now, hidden);
  const entriesThen = checkEntries(checks, then, hidden);
  const totalsNow = analysisTotals(countedEntries(entriesNow));
  const totalsThen = analysisTotals(countedEntries(entriesThen));
  const { fixCauses: fixNow, noticeCauses: lookNow } = totalsNow;
  const { fixCauses: fixThen, noticeCauses: lookThen } = totalsThen;
  if (fixNow == null || lookNow == null || fixThen == null || lookThen == null) return null;
  const before = new Map(entriesThen.map((entry) => [entry.check.id, entry]));
  let fixed = 0;
  let added = 0;
  for (const entry of entriesNow) {
    const earlier = before.get(entry.check.id);
    // Things to fix only, of a check that ran in both, as the headline leads with them.
    if (entry.hidden || !earlier || entry.outcome === 'skipped' || earlier.outcome === 'skipped') continue;
    if (entry.check.severity === 'notice') continue;
    const [was, is] = [thingsOf(earlier), thingsOf(entry)];
    if (was === null || is === null) return null;
    fixed += Math.max(0, was - is);
    added += Math.max(0, is - was);
  }
  return { fixThen, fixNow, lookThen, lookNow, fixed, added };
}

/** "about 12", or "1": things are counted as the headline counts them. */
const about = (count: number) => (count === 1 ? '1' : `about ${formatCount(count)}`);

/**
 * The move as the headline says its figures: "About 12 things fixed · about 3 new · about 40 fewer worth a look";
 * "About 3 new things to fix" with none fixed; null when nothing moved.
 */
export function thingsMovedWords({ fixed, added, lookThen, lookNow }: ThingsMoved): string | null {
  const parts: string[] = [];
  if (fixed > 0) {
    parts.push(`${about(fixed)} ${fixed === 1 ? 'thing' : 'things'} fixed`);
    if (added > 0) parts.push(`${about(added)} new`);
  } else if (added > 0) {
    parts.push(`${about(added)} new ${added === 1 ? 'thing' : 'things'} to fix`);
  }
  const look = lookNow - lookThen;
  if (look !== 0) parts.push(`${about(Math.abs(look))} ${look < 0 ? 'fewer' : 'more'} worth a look`);
  if (parts.length === 0) return null;
  const line = parts.join(' · ');
  return `${line.charAt(0).toUpperCase()}${line.slice(1)}`;
}
