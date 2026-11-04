import { Outlet, useRouter } from '@tanstack/react-router';
import clsx from 'clsx';
import { Suspense, lazy, useEffect } from 'react';
import { focusFallback, useModalOpen, useOverlay } from '../app/overlays';
import { Announcer } from '../feedback/Announcer';
import { AppToaster } from '../feedback/AppToaster';
import { RegionBoundary } from '../feedback/RegionBoundary';
import { useAppCommands } from '../keyboard/useAppCommands';
import { useSettings } from '../settings/settingsStore';
import { InspectorSlot } from './InspectorSlot';
import { SidecarBanner } from './SidecarBanner';
import { Sidebar } from './Sidebar';
import { StoppingOverlay } from './StoppingOverlay';
import { TopBar } from './TopBar';
import { useShellIntegration } from './useShellIntegration';

// The palette and the shortcut sheet load when first opened (the dialog code stays off the first paint), and are fetched
// as soon as the app is idle, so the first Ctrl+K is as quick as the next.
const loadPalette = () => import('../keyboard/CommandPalette');
const loadShortcuts = () => import('../keyboard/ShortcutSheet');
const CommandPalette = lazy(() => loadPalette().then((module) => ({ default: module.CommandPalette })));
const ShortcutSheet = lazy(() => loadShortcuts().then((module) => ({ default: module.ShortcutSheet })));

/**
 * The shell (docs/v2-ui-ia.md): the sidebar, the top bar, the main area and the inspector's slot, plus the
 * app-wide overlays. Structure only; the visual design comes with the design-system pass.
 *
 * - While anything modal is open (the palette, the `?` sheet, "Stopping crawls…"), everything behind it is inert:
 *   neither the keyboard nor a screen reader can reach it. Toasts and the live regions stay outside, so they're heard.
 * - Each part of the shell has its own error boundary, as each screen does (its route's): a part that fails shows
 *   why in its place, and the rest keeps working.
 */
export function AppShell() {
  useAppCommands();
  useShellIntegration();
  useFocusAfterNavigation();
  const dock = useSettings((settings) => settings.inspectorDock);
  const overlay = useOverlay();
  const modal = useModalOpen();
  useEffect(() => {
    const preload = () => {
      void loadPalette();
      void loadShortcuts();
    };
    // WebKit has no requestIdleCallback.
    if ('requestIdleCallback' in window) window.requestIdleCallback(preload);
    else setTimeout(preload, 500);
  }, []);
  return (
    <>
      <div inert={modal} className="flex h-full overflow-hidden bg-canvas text-text">
        <a
          href="#main"
          className="sr-only z-50 rounded-sm bg-surface-overlay px-3 py-2 text-13 focus:not-sr-only focus:fixed focus:top-2 focus:left-2"
        >
          Skip to the main content
        </a>
        <RegionBoundary name="sidebar">
          <Sidebar />
        </RegionBoundary>
        <div className="flex min-w-0 flex-1 flex-col">
          <RegionBoundary name="top bar">
            <TopBar />
          </RegionBoundary>
          <RegionBoundary name="engine’s status">
            <SidecarBanner />
          </RegionBoundary>
          <div className={clsx('flex min-h-0 flex-1', dock === 'bottom' && 'flex-col')}>
            <main id="main" tabIndex={-1} className="min-h-0 min-w-0 flex-1 overflow-auto outline-none">
              <Outlet />
            </main>
            <RegionBoundary name="inspector">
              <InspectorSlot />
            </RegionBoundary>
          </div>
        </div>
      </div>
      <Suspense fallback={null}>
        {overlay === 'palette' ? <CommandPalette /> : null}
        {overlay === 'shortcuts' ? <ShortcutSheet /> : null}
      </Suspense>
      <StoppingOverlay />
      <AppToaster />
      <Announcer />
    </>
  );
}

const hasFocus = () => document.activeElement !== null && document.activeElement !== document.body;

/**
 * A navigation can take away the element that had focus (the palette gives it back to the grid, then "Go to Home"
 * removes the grid): focus then lands in the new view instead of nowhere. Focus that was nowhere stays put.
 */
function useFocusAfterNavigation() {
  const router = useRouter();
  useEffect(() => {
    let hadFocus = false;
    const offBefore = router.subscribe('onBeforeNavigate', () => {
      hadFocus = hasFocus();
    });
    const offRendered = router.subscribe('onRendered', () => {
      if (!hadFocus) return;
      hadFocus = false;
      requestAnimationFrame(() => {
        if (!hasFocus()) focusFallback();
      });
    });
    return () => {
      offBefore();
      offRendered();
    };
  }, [router]);
}
