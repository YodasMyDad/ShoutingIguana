// What to fix's rows (docs/v2-ui-simple.md, "What to fix"): a part's checks in the order asked for, a check a fix of
// another clears entirely folded under that one (related checks), each row with the points fixing it gives back.
// Pure, so every rule is tested.
import { sortChecks, type CheckEntry, type PlainCheckSort } from '../screens/issues/checkList';
import { fixShape } from './fixPages';

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
