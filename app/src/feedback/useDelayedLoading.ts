// The brief's loading rule: a skeleton appears only after 150 ms, so fast loads don't flash, and once shown it stays
// at least 300 ms, so it never flickers away. tokens.css holds the same numbers for CSS.
import { useEffect, useRef, useState } from 'react';

export const SKELETON_DELAY_MS = 150;
export const SKELETON_MIN_MS = 300;

export interface DelayedLoadingOptions {
  delayMs?: number;
  minMs?: number;
}

/** True while the skeleton should show for a load that is `loading`. */
export function useDelayedLoading(loading: boolean, options: DelayedLoadingOptions = {}): boolean {
  const delayMs = options.delayMs ?? SKELETON_DELAY_MS;
  const minMs = options.minMs ?? SKELETON_MIN_MS;
  const [visible, setVisible] = useState(false);
  const shownAt = useRef<number | null>(null);

  useEffect(() => {
    if (loading) {
      if (shownAt.current !== null) return undefined; // already showing: keep it
      const timer = setTimeout(() => {
        shownAt.current = Date.now();
        setVisible(true);
      }, delayMs);
      return () => clearTimeout(timer);
    }
    if (shownAt.current === null) return undefined; // never shown: nothing to hide
    const remaining = Math.max(0, shownAt.current + minMs - Date.now());
    const timer = setTimeout(() => {
      shownAt.current = null;
      setVisible(false);
    }, remaining);
    return () => clearTimeout(timer);
  }, [loading, delayMs, minMs]);

  return visible;
}
