// Simple never shows an Advanced-only screen by surprise (docs/v2-ui-simple.md, The switch): arriving at one in Simple,
// by a link that says so, by Back or Forward, or by a reopened session, turns Advanced on, and a toast says so with
// "Back to Simple". It looks at where each navigation lands and at the place the app opens on; never at a change of
// mode, so switching to Simple on such a screen (which then leaves it) isn't undone.
import { useRouter, type AnyRouter } from '@tanstack/react-router';
import { useEffect } from 'react';
import { advancedOnlyScreen } from '../app/modeRoutes';
import { notifyResult } from '../feedback/notify';
import { getMode, setMode } from './mode';
import { ADVANCED_ON_TOAST, switchMode } from './switchMode';

/**
 * Turns Advanced on for what Simple doesn't show (`what`, mid-sentence: "URLs", "the user agent"), with the one toast
 * that says so and offers Back to Simple. The toast says it: an announcement too would say it twice.
 */
export function showAdvancedFor(router: AnyRouter, what: string): void {
  setMode('advanced', { announce: false });
  notifyResult('Advanced is on', {
    id: ADVANCED_ON_TOAST,
    description: `Simple doesn’t show ${what}.`,
    action: { label: 'Back to Simple', onClick: () => switchMode(router, 'simple') },
  });
}

/** Turns Advanced on for an Advanced-only screen reached in Simple. True when it did. */
export function turnAdvancedOnFor(router: AnyRouter, pathname: string): boolean {
  if (getMode() !== 'simple') return false;
  const screen = advancedOnlyScreen(pathname);
  if (screen === null) return false;
  showAdvancedFor(router, screen);
  return true;
}

export function useModeGuard(): void {
  const router = useRouter();
  useEffect(() => {
    turnAdvancedOnFor(router, router.state.location.pathname);
    return router.subscribe('onResolved', ({ toLocation }) => {
      turnAdvancedOnFor(router, toLocation.pathname);
    });
  }, [router]);
}
