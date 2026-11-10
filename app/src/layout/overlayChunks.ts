// The palette and the shortcut sheet are chunks of their own: the dialog code stays off the first paint. The shell
// fetches them as soon as the app is idle, and shows each without `lazy` and Suspense (lib/chunk.ts), so the first
// Ctrl+K of a session is as quick as the next.
import { chunk } from '../lib/chunk';

export const paletteChunk = chunk(() => import('../keyboard/CommandPalette').then((module) => module.CommandPalette));
export const shortcutsChunk = chunk(() => import('../keyboard/ShortcutSheet').then((module) => module.ShortcutSheet));

/** Fetches both: called once the app is idle. A fetch that fails is tried again when its overlay is opened. */
export function preloadOverlays(): void {
  void paletteChunk.load().catch(() => undefined);
  void shortcutsChunk.load().catch(() => undefined);
}
