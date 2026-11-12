// The mode: Simple or Advanced (docs/v2-ui-simple.md, The switch). An app setting, kept with the other preferences,
// Simple on a first start. Every part of the app reads it through `useMode()` (or `getMode()` outside React), and
// changes it through `setMode`, so the switch, Settings > General > Mode, Ctrl+Shift+A and the palette are one value.
import { announce } from '../feedback/announce';
import type { Mode } from '../settings/schema';
import { getSettings, updateSettings, useSettings } from '../settings/settingsStore';

export type { Mode };

export const MODES: readonly Mode[] = ['simple', 'advanced'];

/** Each mode's name, as the switch and Settings write it. */
export const MODE_NAMES: Record<Mode, string> = { simple: 'Simple', advanced: 'Advanced' };

/** The mode now, for React: renders again when it changes. */
export function useMode(): Mode {
  return useSettings((settings) => settings.mode);
}

export function getMode(): Mode {
  return getSettings().mode;
}

/** The other mode: what the switch changes to. */
export function otherMode(mode: Mode): Mode {
  return mode === 'simple' ? 'advanced' : 'simple';
}

/**
 * Sets the mode. A change the user made is announced once ("Advanced is on"); one the app made for them (arriving at
 * an Advanced-only screen) is said by its toast instead, so it passes `announce: false`.
 */
export function setMode(mode: Mode, options: { announce?: boolean } = {}): void {
  if (getMode() === mode) return;
  updateSettings({ mode });
  if (options.announce !== false) announce(`${MODE_NAMES[mode]} is on`);
}
