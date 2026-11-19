// Where a check was left (rule 9 of the 2026-11-19 UX round, A-07): how its rows were shown (the list, or grouped),
// which rows were open, and the row the cursor was on, kept for the session by crawl and check, so coming back to the
// check (Back from Links, from another check, from the inspector's walk) finds it as it was. The route keeps the
// check, the group in view, the sort and the inspected URL; this keeps what the route has no place for.

/** How a grouped check's rows are shown: one row a URL, or its groups as a tree. */
export type CheckView = 'list' | 'groups';

export interface CheckPlace {
  /** The view chosen for it; left out until one is, and the check's own default shows. */
  view?: CheckView;
  /** The list's rows opened or closed by hand, by their place in it: a row not here is as the list opens it. */
  toggled?: ReadonlyMap<number, boolean>;
  /** What `toggled` was of: the list's order and comparison, so another sort starts with none. */
  toggledOf?: string;
  /** The grid's cursor: its row and that row's key, by the grid it was in. */
  cursor?: { grid: string; row: number; key: string | number | undefined };
  /** The tree's page opened to show where it's found: its group's key and the page's URL id. */
  expanded?: { group: string; page: number } | null;
}

const places = new Map<string, CheckPlace>();

const keyOf = (crawlId: string, checkId: string) => `${crawlId}|${checkId}`;

/** Where the check was left; an empty place for one not opened this session. */
export function checkPlace(crawlId: string, checkId: string): CheckPlace {
  return places.get(keyOf(crawlId, checkId)) ?? {};
}

/** Keeps more of where the check is left. */
export function keepCheckPlace(crawlId: string, checkId: string, patch: Partial<CheckPlace>): void {
  const key = keyOf(crawlId, checkId);
  places.set(key, { ...places.get(key), ...patch });
}

/** Tests: every check as if never opened. */
export function forgetCheckPlaces(): void {
  places.clear();
}
