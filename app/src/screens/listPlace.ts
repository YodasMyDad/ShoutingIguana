import { useEffect, type RefObject } from 'react';

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

/** What `opensItsRow` reads of a click: React's event, or the browser's. */
interface RowClick {
  target: EventTarget | null;
  button: number;
  shiftKey: boolean;
  ctrlKey: boolean;
  metaKey: boolean;
  altKey: boolean;
  defaultPrevented: boolean;
}

/**
 * Whether a click in a list of places to go (the sitemaps, the origins' robots.txt) opens the row it landed in: a
 * plain click with the main button, anywhere the row's own links and buttons aren't (they do their own thing: a
 * count's filtered list, the open-in-browser button, an index's chevron). Shift and Ctrl clicks still select rows to
 * copy, and a drag that selected some of the row's text is a selection, not a click.
 */
export function opensItsRow(event: RowClick): boolean {
  if (event.defaultPrevented || event.button !== 0) return false;
  if (event.shiftKey || event.ctrlKey || event.metaKey || event.altKey) return false;
  if (!(event.target instanceof Element) || event.target.closest('a, button, [data-tree-toggle]')) return false;
  const selection = typeof window.getSelection === 'function' ? window.getSelection() : null;
  return selection === null || selection.isCollapsed;
}

/** How long after a page opens a second click still counts as the end of the double-click that opened it. */
const STRAY_CLICK_MS = 500;

/**
 * A list's row opens on one click, so the second click of a double-click (the way in, before) lands on the page it
 * opened, on whatever is under the pointer there: a filter, a row. That click isn't meant for the page: true for a
 * click the browser counts as a second or later one (`detail`), this soon after the page opened (`openedAt`, as
 * `performance.now()` gave it).
 */
export function isStrayClick(detail: number, openedAt: number, now: number = performance.now()): boolean {
  return detail > 1 && now - openedAt < STRAY_CLICK_MS;
}

/**
 * Keeps the second click of the double-click that opened a page (`isStrayClick`) from whatever is under the pointer
 * there: for the page a list's row opens, on the element that holds it. The click is stopped on its way down, so
 * nothing in the page hears it.
 */
export function useStrayClickGuard(page: RefObject<HTMLElement | null>): void {
  useEffect(() => {
    const node = page.current;
    if (!node) return undefined;
    const openedAt = performance.now();
    const stop = (event: MouseEvent) => {
      if (!isStrayClick(event.detail, openedAt)) return;
      event.stopPropagation();
      event.preventDefault();
    };
    node.addEventListener('click', stop, true);
    node.addEventListener('dblclick', stop, true);
    return () => {
      node.removeEventListener('click', stop, true);
      node.removeEventListener('dblclick', stop, true);
    };
  }, [page]);
}
