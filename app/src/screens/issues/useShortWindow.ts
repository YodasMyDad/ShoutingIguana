import { useSyncExternalStore } from 'react';

/** A window this short (1280 × 720, a laptop at 150%) gives the check's rows what room it has (A-09). */
const SHORT = '(max-height: 800px)';

function subscribe(onChange: () => void): () => void {
  if (typeof window.matchMedia !== 'function') return () => undefined;
  const query = window.matchMedia(SHORT);
  query.addEventListener('change', onChange);
  return () => query.removeEventListener('change', onChange);
}

const isShort = () => typeof window.matchMedia === 'function' && window.matchMedia(SHORT).matches;

/**
 * Whether the window is short: the Issues screen then leaves out what a row of the check's list is worth more than
 * (the keys under the list; with the check alone in view, the page's sentences and notes, which stay with the list).
 */
export function useShortWindow(): boolean {
  return useSyncExternalStore(subscribe, isShort, () => false);
}
