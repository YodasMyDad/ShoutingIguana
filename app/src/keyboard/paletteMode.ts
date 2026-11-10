// What the palette is doing: listing commands, or taking a URL to go to ("Go to URL…"). Outside React, so the
// command that opens the palette for a URL can say so before the palette mounts.
import { useSyncExternalStore } from 'react';

export type PaletteMode = { kind: 'commands' } | { kind: 'url'; text: string };

const COMMANDS: PaletteMode = { kind: 'commands' };

let mode: PaletteMode = COMMANDS;
const listeners = new Set<() => void>();

export function getPaletteMode(): PaletteMode {
  return mode;
}

export function setPaletteMode(next: PaletteMode): void {
  if (next === mode || (next.kind === 'commands' && mode.kind === 'commands')) return;
  mode = next.kind === 'commands' ? COMMANDS : next;
  listeners.forEach((listener) => listener());
}

const subscribe = (listener: () => void) => {
  listeners.add(listener);
  return () => listeners.delete(listener);
};

export function usePaletteMode(): PaletteMode {
  return useSyncExternalStore(subscribe, getPaletteMode);
}
