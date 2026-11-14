// A grid screen in a short window (200% zoom of 1280x720 is 640x360): its toolbars fold behind one button, so the grid
// keeps rows to show (gap 13 of the closing audit). The screen's own height says when: the main region it fills.
import { useEffect, useState, type RefObject } from 'react';

/**
 * Below this height a grid screen folds its toolbars, px: what the URL grid's header, presets, facets, filter bar and
 * a banner take at 200% zoom with five rows under them (about 470), so a window that shows them all never folds.
 */
export const SHORT_SCREEN_HEIGHT = 480;

/** Whether the element (a `GridScreen`) is shorter than `SHORT_SCREEN_HEIGHT`. False until measured, and in tests. */
export function useShortScreen(ref: RefObject<HTMLElement | null>): boolean {
  const [short, setShort] = useState(false);
  useEffect(() => {
    const element = ref.current;
    if (!element || typeof ResizeObserver === 'undefined') return undefined;
    const measure = () => setShort(element.clientHeight > 0 && element.clientHeight < SHORT_SCREEN_HEIGHT);
    measure();
    const observer = new ResizeObserver(measure);
    observer.observe(element);
    return () => observer.disconnect();
  }, [ref]);
  return short;
}
