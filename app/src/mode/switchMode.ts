// Switching mode, as the switch, Ctrl+Shift+A, the palette, Settings and "Back to Simple" all do it
// (docs/v2-ui-simple.md, The switch): the same route renders its other view; from a screen Simple doesn't have,
// switching to Simple goes to the nearest one it has, as a navigation, so Back returns.
import type { AnyRouter } from '@tanstack/react-router';
import { simpleLanding } from '../app/modeRoutes';
import { dismissToast } from '../feedback/notify';
import { getMode, setMode, type Mode } from './mode';

/** The "Advanced is on" toast's id: one at a time, and gone once Simple is back. */
export const ADVANCED_ON_TOAST = 'mode:advanced-on';

export function switchMode(router: AnyRouter, target: Mode): void {
  if (getMode() === target) return;
  if (target === 'advanced') {
    setMode('advanced');
    return;
  }
  const landing = simpleLanding(router.state.location.pathname);
  dismissToast(ADVANCED_ON_TOAST);
  // The mode first: the guard (useModeGuard) looks only at where a navigation lands, and this one lands where Simple
  // has a screen.
  setMode('simple');
  if (landing) void router.navigate(landing);
}
