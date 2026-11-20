// Back keeps your place (rule 9 of the 2026-11-19 UX round, A-07): how far a check's rows were scrolled, kept with the
// check for the session (issuesPlace.ts) and put back when its grid is drawn again. The cursor's row comes back by
// itself (`GridCore`'s `startRow`); this is for rows scrolled without moving the cursor, as the wheel does.
import { useEffect, useState } from 'react';
import { checkPlace, keepCheckPlace } from './issuesPlace';

/** The grid's own scroller, inside the frame the screen sizes it by. */
const SCROLLER = '.si-grid';

/** How long the offset is put back for while the rows arrive: they come a page at a time, and the grid is short until then. */
const SETTLE_MS = 1500;

/**
 * Keeps a check's grid scrolled where it was left. Give the returned function the grid's frame (its ref); `grid` names
 * the grid (its query), so another sort or view starts at its top.
 */
export function useKeptScroll(crawlId: string, checkId: string, grid: string): (frame: HTMLElement | null) => void {
  const [frame, setFrame] = useState<HTMLElement | null>(null);
  useEffect(() => {
    const scroller = frame?.querySelector<HTMLElement>(SCROLLER);
    if (!scroller) return undefined;
    const kept = checkPlace(crawlId, checkId).scroll;
    const wanted = kept?.grid === grid ? kept.offset : 0;
    // Until the reader scrolls or the rows have settled, the offset is the one kept: the grid's own first scroll (to
    // the cursor's row) and rows arriving mustn't lose it.
    let settling = wanted > 0;
    let frameId = 0;
    const started = performance.now();
    const settle = () => {
      if (!settling) return;
      if (scroller.scrollHeight - scroller.clientHeight >= wanted && scroller.scrollTop !== wanted) {
        scroller.scrollTop = wanted;
      }
      if (performance.now() - started < SETTLE_MS) frameId = requestAnimationFrame(settle);
      else settling = false;
    };
    if (settling) settle();
    const stopSettling = () => {
      settling = false;
    };
    const keep = () => {
      // While it's being put back, a scroll is the grid's own: the reader's is what's kept.
      if (!settling) keepCheckPlace(crawlId, checkId, { scroll: { grid, offset: scroller.scrollTop } });
    };
    scroller.addEventListener('scroll', keep, { passive: true });
    scroller.addEventListener('wheel', stopSettling, { passive: true });
    scroller.addEventListener('pointerdown', stopSettling, { passive: true });
    scroller.addEventListener('keydown', stopSettling);
    return () => {
      cancelAnimationFrame(frameId);
      scroller.removeEventListener('scroll', keep);
      scroller.removeEventListener('wheel', stopSettling);
      scroller.removeEventListener('pointerdown', stopSettling);
      scroller.removeEventListener('keydown', stopSettling);
    };
  }, [frame, crawlId, checkId, grid]);
  return setFrame;
}
