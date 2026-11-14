import { useCallback, useLayoutEffect, useState } from 'react';

/**
 * An element's width as it changes, for a grid whose columns share the room it has (a panel beside the checks list).
 * Null until it's measured, and where there's no ResizeObserver (tests): the caller's widths then stand. Measured before
 * the first paint, so a layout chosen by the width (a card's figures in one row or two) is drawn right from its first
 * frame, never redrawn a frame later (round 5 review R1-10).
 */
export function useElementWidth(): [ref: (element: HTMLElement | null) => void, width: number | null] {
  const [element, setElement] = useState<HTMLElement | null>(null);
  const [width, setWidth] = useState<number | null>(null);
  const ref = useCallback((next: HTMLElement | null) => setElement(next), []);

  useLayoutEffect(() => {
    if (!element || typeof ResizeObserver === 'undefined') return undefined;
    const measure = () => setWidth(Math.floor(element.clientWidth));
    measure();
    const observer = new ResizeObserver(measure);
    observer.observe(element);
    return () => observer.disconnect();
  }, [element]);

  return [ref, width];
}
