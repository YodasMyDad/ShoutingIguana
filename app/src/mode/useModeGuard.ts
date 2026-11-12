// Simple never shows an Advanced-only screen by surprise (docs/v2-ui-simple.md, The switch): arriving at one in Simple,
// by a link that says so, by Back or Forward, or by a reopened session, turns Advanced on, and a toast says so with
// "Back to Simple". It looks at where each navigation lands and at the place the app opens on; never at a change of
// mode, so switching to Simple on such a screen (which then leaves it) isn't undone.
import { useRouter, type AnyRouter } from '@tanstack/react-router';
import { useEffect } from 'react';
import { crawlQuery } from '../api/queries';
import { advancedOnlyScreen, crawlIdOf } from '../app/modeRoutes';
import type { RouterContext } from '../app/router';
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

/**
 * Turns Advanced on for an Advanced-only screen reached in Simple, once it's sure the screen is there: a crawl's
 * section only for a crawl the library has (one it hasn't says so in Simple too). Settings' sections are sure by the
 * time a navigation lands, as the route sends one there's no file for to General. True when it did.
 */
export async function turnAdvancedOnFor(router: AnyRouter, pathname: string): Promise<boolean> {
  if (getMode() !== 'simple') return false;
  const screen = advancedOnlyScreen(pathname);
  if (screen === null) return false;
  const crawl = crawlIdOf(pathname);
  if (crawl) {
    const { queryClient } = router.options.context as RouterContext;
    try {
      await queryClient.query(crawlQuery(crawl));
    } catch {
      return false;
    }
    // Still there, and still Simple, once the library has answered.
    if (getMode() !== 'simple' || router.state.location.pathname !== pathname) return false;
  }
  showAdvancedFor(router, screen);
  return true;
}

export function useModeGuard(): void {
  const router = useRouter();
  useEffect(() => {
    // The place the app opened on, once the router has landed there; until then, its landing says it.
    const opened = router.state.resolvedLocation?.pathname;
    if (opened !== undefined) void turnAdvancedOnFor(router, opened);
    return router.subscribe('onResolved', ({ toLocation }) => {
      void turnAdvancedOnFor(router, toLocation.pathname);
    });
  }, [router]);
}
