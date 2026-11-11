// Where a list was left: the row something was opened from, kept for the session, so coming back to the list (Back,
// "All sitemaps", the sidebar) starts on that row instead of at the top. The list gives it to its grid as `startRow`,
// and, for a list whose rows a running crawl moves, the row's own key with it (`startKey`), so the grid starts on
// that row wherever it is by then.

const places = new Map<string, number>();
const keys = new Map<string, string>();

/**
 * Remembers what a list was left on: `list` names the list and its crawl, `value` what the list finds its row by (a
 * row number, a sitemap's id). `key`: the row's identity, for a list whose rows are read page by page and can move.
 */
export function rememberPlace(list: string, value: number, key?: string): void {
  places.set(list, value);
  if (key === undefined) keys.delete(list);
  else keys.set(list, key);
}

export function rememberedPlace(list: string): number | undefined {
  return places.get(list);
}

/** The identity of the row a list was left on, when it was remembered with one. */
export function rememberedKey(list: string): string | undefined {
  return keys.get(list);
}

/** Tests: forget every list's place. */
export function resetPlaces(): void {
  places.clear();
  keys.clear();
}

/** The row a press landed in, by the grid's own numbering of its rows (the header is row 1), or null outside one. */
export function pressedRow(target: EventTarget | null): number | null {
  const row = target instanceof Element ? target.closest('[role="row"][aria-rowindex]') : null;
  const index = Number(row?.getAttribute('aria-rowindex')) - 2;
  return row && Number.isInteger(index) && index >= 0 ? index : null;
}
