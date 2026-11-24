// What to fix's rows (docs/v2-ui-simple.md, "What to fix"): a part's checks in the order asked for, a check a fix of
// another clears entirely folded under that one (related checks), each row with the points fixing it gives back.
// Pure, so every rule is tested.
import { sortChecks, type CheckEntry, type PlainCheckSort } from '../screens/issues/checkList';
import type { Score } from '../api/types';
import { fixShape } from './fixPages';
import type { PlainResults } from './results';

export interface FixRow {
  entry: CheckEntry;
  /** The checks a fix of this one clears entirely, folded under it: one job, not two. */
  nested: CheckEntry[];
  /**
   * The checks that are this one seen from its other end (the UX audit of 2026-11-19, S-04): dead addresses and the
   * pages that link to them are one list of links to change, so they are one row, not a row and its mirror folded
   * under it. The row says both ("8 addresses on 4 pages") and carries the points of both.
   */
  merged: CheckEntry[];
  /** The points fixing it gives back, its folded checks' with its own; null when it gives none (a notice). */
  points: number | null;
}

export interface FixRowOrder {
  sort: PlainCheckSort;
  /** The analysis's `fixFirst`: the engine's order. */
  fixFirst?: readonly string[] | null | undefined;
  /** Points off the score by check id (the score's lines). */
  points?: ReadonlyMap<string, number> | undefined;
}

/**
 * One part's rows, from its checks in the engine's order to fix them (all of one severity). A check is folded under
 * the first check before it in that order whose fix clears every page it flagged (the analysis's `clears`, `all`),
 * when both are in the part: a 404 and the links to it are one job. One level only: a check that has others folded
 * under it isn't folded itself. The rows then come in `order`: fix first, the most points back (a row's with its
 * folded checks'), or the most found.
 */
export function fixRows(entries: readonly CheckEntry[], order: FixRowOrder): FixRow[] {
  const inPart = new Map(entries.map((entry) => [entry.check.id, entry]));
  const nested = new Map<string, CheckEntry[]>();
  const folded = new Set<string>();
  for (const entry of entries) {
    if (folded.has(entry.check.id)) continue;
    for (const cleared of entry.clears) {
      const other = inPart.get(cleared.checkId);
      if (!cleared.all || !other || other === entry || folded.has(other.check.id) || nested.has(other.check.id))
        continue;
      folded.add(other.check.id);
      nested.set(entry.check.id, [...(nested.get(entry.check.id) ?? []), other]);
    }
  }
  const pointsOf = (entry: CheckEntry) => order.points?.get(entry.check.id) ?? 0;
  const rows = new Map(
    entries
      .filter((entry) => !folded.has(entry.check.id))
      .map((entry) => {
        const under = nested.get(entry.check.id) ?? [];
        const points = [entry, ...under].reduce((sum, each) => sum + pointsOf(each), 0);
        const merged = under.filter((other) => mirrors(entry, other));
        return [
          entry.check.id,
          {
            entry,
            nested: under.filter((other) => !merged.includes(other)),
            merged,
            points: points > 0 ? points : null,
          },
        ] as const;
      }),
  );
  const combined = new Map([...rows].map(([id, row]) => [id, row.points ?? 0]));
  return sortChecks(
    [...rows.values()].map((row) => row.entry),
    { sort: order.sort, fixFirst: order.fixFirst, points: combined },
  ).flatMap((entry) => rows.get(entry.check.id) ?? []);
}

/**
 * The five to fix first as jobs, the rows What to fix has for them (docs/v2-ui-simple.md, "Related problems are one
 * job"): a problem a fix of another clears entirely (the analysis's `clears[].all`) is folded under that one, within its
 * severity, the row above carrying the points of both. The folding is done over all the problems of the severity, as
 * What to fix does it, not over the five alone: a job's fix gives back the points of what it clears whether or not that
 * is among the five, so the same problem says the same points here, on What to fix and on its own page (the audit of
 * 2026-11-24: +4 here beside +8 there). The engine's five stay the five, so "Fixing these four takes the score from 87
 * to 100" is still its figure: it counts the jobs.
 */
export function fixFirstJobs(
  results: Pick<PlainResults, 'found' | 'fixFirst'>,
  score: Pick<Score, 'lines'> | null,
): FixRow[] {
  const points = new Map((score?.lines ?? []).map((line) => [line.checkId, line.points]));
  // `found` is in the engine's order to fix them, which is the order What to fix folds in.
  const fixFirst = [...results.found.error, ...results.found.warning].map((entry) => entry.check.id);
  const five = new Set(results.fixFirst.map((entry) => entry.check.id));
  const ofTheFive = (row: FixRow) =>
    [row.entry, ...row.nested, ...row.merged].some((entry) => five.has(entry.check.id));
  return (['error', 'warning'] as const).flatMap((severity) =>
    fixRows(results.found[severity], { sort: 'fixFirst', fixFirst, points }).filter(ofTheFive),
  );
}

/**
 * Whether a folded check is the row's own seen from the other end: the row's is a check of dead addresses, listed by
 * the pages that link to them, and the folded one a check of those pages (fixPages' shapes).
 */
function mirrors(lead: CheckEntry, other: CheckEntry): boolean {
  return fixShape(lead.check) === 'addresses' && fixShape(other.check) === 'pages';
}

/** "on 4 pages": the pages a row's merged checks count, after its own count ("8 addresses on 4 pages"). */
export function mergedWords(merged: readonly CheckEntry[]): string | null {
  const pages = merged.reduce((most, entry) => Math.max(most, entry.count ?? 0), 0);
  return pages > 0 ? `on ${pages === 1 ? '1 page' : `${pages.toLocaleString('en-GB')} pages`}` : null;
}

/** The orders What to fix offers, in its words. */
export const PLAIN_CHECK_SORTS: readonly { value: PlainCheckSort; label: string }[] = [
  { value: 'fixFirst', label: 'Fix first' },
  { value: 'points', label: 'Most points' },
  { value: 'count', label: 'Most found' },
];
