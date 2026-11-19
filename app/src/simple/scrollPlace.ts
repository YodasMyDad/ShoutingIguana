// Where a Simple screen is scrolled to (the closing audit's G3: a problem opened from a scrolled What to fix opened part
// way down, its back link out of sight). The app scrolls in its main region (layout/AppShell), not the window, and the
// router keeps no scroll places of its own (app/router: scrollRestoration is off), so each screen says its own.
import { useCallback, useEffect, useLayoutEffect, useState, type RefObject } from 'react';

/** The region a screen scrolls in: the app's main, or the screen's own element outside the app (a story). */
function scrollerOf(element: HTMLElement | null): HTMLElement | null {
  return element?.closest<HTMLElement>('#main') ?? null;
}

/** Each list's place, by its key, for this session: where it was left. */
const places = new Map<string, number>();

/** Opens the screen at its top whenever `key` changes: a problem opens at its head, whatever the list's scroll was. */
export function useOpensAtTop(ref: RefObject<HTMLElement | null>, key: string): void {
  useLayoutEffect(() => {
    const scroller = scrollerOf(ref.current);
    if (scroller) scroller.scrollTop = 0;
  }, [ref, key]);
}

/**
 * Keeps a list's place: where it is scrolled to is kept as it scrolls, and once the list is drawn in full (`ready`) on
 * coming back (Back, or its link), it is there again. Arriving any other way, the first time, it opens at its top.
 */
export function useKeepsPlace(ref: RefObject<HTMLElement | null>, key: string, ready: boolean): void {
  useLayoutEffect(() => {
    if (!ready) return;
    const scroller = scrollerOf(ref.current);
    if (scroller) scroller.scrollTop = places.get(key) ?? 0;
  }, [ref, key, ready]);
  useEffect(() => {
    const scroller = scrollerOf(ref.current);
    if (!scroller || !ready) return undefined;
    const keep = () => places.set(key, scroller.scrollTop);
    scroller.addEventListener('scroll', keep, { passive: true });
    return () => scroller.removeEventListener('scroll', keep);
  }, [ref, key, ready]);
}

/** What a list had opened, by its key, for this session (the UX audit of 2026-11-19, S-10). */
const opened = new Map<string, boolean>();

/**
 * A fold that stays as it was left when the list is come back to (Back from a problem): "Show all 22 things worth a
 * look", open and scrolled to its end, was folded to three again. `initial` opens it whatever it was (a count that
 * lands on it).
 */
export function useKeptOpen(key: string, initial: boolean): [boolean, (open: boolean) => void] {
  const [open, setOpen] = useState(() => initial || opened.get(key) === true);
  const set = useCallback(
    (next: boolean) => {
      opened.set(key, next);
      setOpen(next);
    },
    [key],
  );
  return [open, set];
}

/** For tests: forgets every place, and every fold. */
export function forgetPlaces(): void {
  places.clear();
  opened.clear();
}
