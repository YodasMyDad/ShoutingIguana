// Whether the sidebar is collapsed to icons. A window narrower than NARROW_WINDOW px (200% zoom on a 1280 px window
// is 640) shows it collapsed by default, so it doesn't take a third of the width; the toggle still opens it there, for
// as long as the window stays narrow. The user's own choice for wider windows (`sidebarCollapsed`, remembered) is
// never overwritten by a narrow one's.
import { useSyncExternalStore } from 'react';
import { updateSettings, useSettings } from '../settings/settingsStore';

/** Below this width (CSS px), the sidebar starts collapsed. The 1280 px layout keeps it open. */
export const NARROW_WINDOW = 1024;
const NARROW_QUERY = `(max-width: ${NARROW_WINDOW - 1}px)`;

/** The narrow window's own choice, for as long as it stays narrow: null until the toggle is used there. */
let narrowChoice: boolean | null = null;
const listeners = new Set<() => void>();

function changed(): void {
  listeners.forEach((listener) => listener());
}

function media(): MediaQueryList | null {
  return typeof window === 'undefined' || typeof window.matchMedia !== 'function'
    ? null
    : window.matchMedia(NARROW_QUERY);
}

function isNarrow(): boolean {
  return media()?.matches ?? false;
}

function subscribe(listener: () => void): () => void {
  listeners.add(listener);
  const query = media();
  // Leaving the narrow window forgets its choice: the next narrow window starts collapsed again.
  const onChange = () => {
    if (!isNarrow()) narrowChoice = null;
    changed();
  };
  query?.addEventListener('change', onChange);
  return () => {
    listeners.delete(listener);
    query?.removeEventListener('change', onChange);
  };
}

function narrowSnapshot(): string {
  return isNarrow() ? `narrow:${String(narrowChoice)}` : 'wide';
}

/** The toggle (its button, and `view.sidebar`): in a narrow window, for that window only; else the remembered choice. */
export function toggleSidebar(): void {
  if (isNarrow()) {
    narrowChoice = !(narrowChoice ?? true);
    changed();
    return;
  }
  updateSettings((settings) => ({ sidebarCollapsed: !settings.sidebarCollapsed }));
}

export function useSidebarCollapsed(): boolean {
  const wide = useSettings((settings) => settings.sidebarCollapsed);
  const narrow = useSyncExternalStore(subscribe, narrowSnapshot, () => 'wide');
  if (narrow === 'wide') return wide;
  return narrowChoice ?? true;
}

/** Tests only: no narrow window's choice. */
export function resetSidebarForTests(): void {
  narrowChoice = null;
  changed();
}
