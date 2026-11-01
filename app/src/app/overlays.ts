// Which app-wide overlay is open: the command palette or the shortcut sheet. One at a time.
import { useSyncExternalStore } from 'react';

export type Overlay = 'palette' | 'shortcuts' | null;

let open: Overlay = null;
const listeners = new Set<() => void>();

export function setOverlay(next: Overlay): void {
  if (open === next) return;
  open = next;
  listeners.forEach((listener) => listener());
}

export function getOverlay(): Overlay {
  return open;
}

export function useOverlay(): Overlay {
  return useSyncExternalStore(
    (listener) => {
      listeners.add(listener);
      return () => listeners.delete(listener);
    },
    getOverlay,
    getOverlay,
  );
}
