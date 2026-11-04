// Which app-wide overlay is open (the command palette or the shortcut sheet: one at a time), and what being modal means
// for the rest of the app:
// - while anything modal is open (an overlay, a dialog, or "Stopping crawls…"), the app behind it is inert (AppShell) and the
//   shortcut registry leaves its keys alone (keyboard/registry.ts), so F7 or Ctrl+1 never act behind it;
// - when an overlay closes, focus goes back where it was, or somewhere sensible when that's gone: the view's grid, else
//   its main area. Overlays opened from the keyboard have no trigger for Radix to give focus back to.
import { useEffect, useSyncExternalStore } from 'react';

export type Overlay = 'palette' | 'shortcuts' | null;

let open: Overlay = null;
/** Where focus was when the overlay opened. */
let returnFocus: Element | null = null;
/** Modal states the app draws itself, such as "Stopping crawls…". */
const modals = new Set<symbol>();
const listeners = new Set<() => void>();

function changed(): void {
  listeners.forEach((listener) => listener());
}

function subscribe(listener: () => void): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

export function setOverlay(next: Overlay): void {
  if (open === next) return;
  if (open === null && next !== null && typeof document !== 'undefined') returnFocus = document.activeElement;
  open = next;
  changed();
}

export function getOverlay(): Overlay {
  return open;
}

export function useOverlay(): Overlay {
  return useSyncExternalStore(subscribe, getOverlay, getOverlay);
}

/** Whether anything modal is open: an overlay, or a modal state such as "Stopping crawls…". */
export function isModalOpen(): boolean {
  return open !== null || modals.size > 0;
}

export function useModalOpen(): boolean {
  return useSyncExternalStore(subscribe, isModalOpen, isModalOpen);
}

/** Makes the app modal while `active`: for modal states the app draws itself. */
export function useModal(active: boolean): void {
  useEffect(() => {
    if (!active) return undefined;
    const token = Symbol('modal');
    modals.add(token);
    changed();
    return () => {
      modals.delete(token);
      changed();
    };
  }, [active]);
}

/** Focus somewhere sensible when it has nowhere to be: the view's grid (its cursor is kept), else the main area. */
export function focusFallback(): void {
  const target = document.querySelector<HTMLElement>('#main [role="grid"]') ?? document.getElementById('main') ?? null;
  target?.focus({ preventScroll: true });
}

/**
 * Gives focus back after an overlay closes: to where it was when the overlay opened, if that's still there, else
 * focusFallback. Overlays call it from Radix's onCloseAutoFocus, after preventing Radix's own (which focuses the
 * trigger, and there is none for a keyboard shortcut).
 */
export function restoreFocus(): void {
  const target = returnFocus;
  returnFocus = null;
  if (target instanceof HTMLElement && target.isConnected && target !== document.body) {
    target.focus({ preventScroll: true });
    if (document.activeElement === target) return;
  }
  focusFallback();
}

/** Tests only: no overlay, no modal state, nothing to give focus back to. */
export function resetOverlaysForTests(): void {
  open = null;
  returnFocus = null;
  modals.clear();
  changed();
}
