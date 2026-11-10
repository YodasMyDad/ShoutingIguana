// Where a list was left: the row something was opened from, kept for the session, so coming back to the list (Back,
// "All sitemaps", the sidebar) starts on that row instead of at the top. The list gives it to its grid as `startRow`.

const places = new Map<string, number>();

/** Remembers what a list was left on: `list` names the list and its crawl, `value` what the list finds its row by. */
export function rememberPlace(list: string, value: number): void {
  places.set(list, value);
}

export function rememberedPlace(list: string): number | undefined {
  return places.get(list);
}

/** Tests: forget every list's place. */
export function resetPlaces(): void {
  places.clear();
}

/** The row a press landed in, by the grid's own numbering of its rows (the header is row 1), or null outside one. */
export function pressedRow(target: EventTarget | null): number | null {
  const row = target instanceof Element ? target.closest('[role="row"][aria-rowindex]') : null;
  const index = Number(row?.getAttribute('aria-rowindex')) - 2;
  return row && Number.isInteger(index) && index >= 0 ? index : null;
}
