// Whether the sidebar's crawl switcher is open, outside React: the top bar's date crumb opens it too (the audit of
// 2026-11-19, H-17: "the crawl's date did nothing"), and the switcher is the sidebar's.
import { useSyncExternalStore } from 'react';

let open = false;
const listeners = new Set<() => void>();

export function setSwitcherOpen(next: boolean): void {
  if (next === open) return;
  open = next;
  listeners.forEach((listener) => listener());
}

const subscribe = (listener: () => void) => {
  listeners.add(listener);
  return () => listeners.delete(listener);
};

export function useSwitcherOpen(): boolean {
  return useSyncExternalStore(
    subscribe,
    () => open,
    () => false,
  );
}
