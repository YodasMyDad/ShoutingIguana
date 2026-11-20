// Switching mode, as the switch, Ctrl+Shift+A, the palette, Settings and "Back to Simple" all do it
// (docs/v2-ui-simple.md, The switch): the same route renders its other view; from a screen Simple doesn't have,
// switching to Simple goes to the nearest one it has, as a navigation, says so, and remembers the place left: Advanced
// chosen again from where it landed goes back there, as Back does, so the filters, the page inspected and the place in
// the list are all as they were.
import type { AnyRouter, RouterHistory } from '@tanstack/react-router';
import { advancedOnlyScreen, simpleLanding } from '../app/modeRoutes';
import { dismissToast, notifyResult } from '../feedback/notify';
import { getMode, setMode, type Mode } from './mode';

/** The "Advanced is on" toast's id: one at a time, and gone once Simple is back. */
export const ADVANCED_ON_TOAST = 'mode:advanced-on';
/** The toast that says where switching to Simple landed: one at a time, and gone once Advanced is back. */
export const SIMPLE_LANDING_TOAST = 'mode:simple-landing';
/** The longest the switch back waits for Back to land before turning Advanced on where it is. */
const BACK_TIMEOUT_MS = 1_000;

/**
 * The history entry Simple landed on when the switch left an Advanced-only screen (the audit of 2026-11-19, H-03). The
 * entry before it is the place left: while that's still where the user is, Advanced again goes back to it.
 */
let landedOn: string | null = null;

/** A history entry's own key. */
function entryKey(router: AnyRouter): string {
  const location = router.state.location;
  const state = location.state as { __TSR_key?: string; key?: string };
  return state.__TSR_key ?? state.key ?? location.href;
}

export function switchMode(router: AnyRouter, target: Mode): void {
  if (getMode() === target) return;
  if (target === 'advanced') {
    dismissToast(SIMPLE_LANDING_TOAST);
    const landing = landedOn;
    landedOn = null;
    const history = router.history as RouterHistory;
    if (landing === null || entryKey(router) !== landing || !history.canGoBack()) {
      setMode('advanced');
      return;
    }
    // Back first, the mode once it has landed: the other way round, Overview would show for a moment on the way. The
    // guard (useModeGuard) sees an Advanced-only screen reached in Simple, and finds Advanced on by the time it asks.
    let done = false;
    const land = () => {
      if (done) return;
      done = true;
      off();
      clearTimeout(timer);
      setMode('advanced');
    };
    const off = router.subscribe('onResolved', land);
    const timer = setTimeout(land, BACK_TIMEOUT_MS);
    history.back();
    return;
  }
  const from = router.state.location.pathname;
  const landing = simpleLanding(from);
  const left = advancedOnlyScreen(from);
  dismissToast(ADVANCED_ON_TOAST);
  landedOn = null;
  // The mode first: the guard (useModeGuard) looks only at where a navigation lands, and this one lands where Simple
  // has a screen.
  setMode('simple');
  if (!landing) return;
  void router.navigate(landing).then(() => {
    // Somewhere else by now (a second switch, a link followed while it loaded): nothing to say.
    if (getMode() !== 'simple' || router.state.location.pathname !== `/crawls/${landing.params.crawl}/overview`) return;
    landedOn = entryKey(router);
    if (left === null) return;
    notifyResult(`Simple doesn’t show ${left}`, {
      id: SIMPLE_LANDING_TOAST,
      description: 'This is the Summary. Advanced takes you back to where you were.',
      action: { label: `Back to ${left}`, onClick: () => switchMode(router, 'advanced') },
    });
  });
}

/** Tests only: no place remembered. */
export function resetSwitchForTests(): void {
  landedOn = null;
}
