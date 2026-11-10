import { useCallback, useEffect, useState } from 'react';

export interface ElementSize {
  width: number;
  height: number;
}

/**
 * An element's size as it changes, for a chart drawn in real pixels (no stretched viewBox, so strokes and marks keep
 * their weight). Null until it's measured, and where there's no ResizeObserver (tests): the caller's own size then
 * stands.
 */
export function useElementSize(): [ref: (element: HTMLElement | null) => void, size: ElementSize | null] {
  const [element, setElement] = useState<HTMLElement | null>(null);
  const [size, setSize] = useState<ElementSize | null>(null);
  const ref = useCallback((next: HTMLElement | null) => setElement(next), []);

  useEffect(() => {
    if (!element || typeof ResizeObserver === 'undefined') return undefined;
    const measure = () => {
      const width = Math.floor(element.clientWidth);
      const height = Math.floor(element.clientHeight);
      // The same size again (a sibling changed) draws nothing again.
      setSize((previous) => (previous?.width === width && previous.height === height ? previous : { width, height }));
    };
    measure();
    const observer = new ResizeObserver(measure);
    observer.observe(element);
    return () => observer.disconnect();
  }, [element]);

  return [ref, size];
}
