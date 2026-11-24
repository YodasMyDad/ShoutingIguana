import { Outlet, useRouter } from '@tanstack/react-router';
import clsx from 'clsx';
import { useEffect } from 'react';
import { focusFallback, useModalOpen, useOverlay } from '../app/overlays';
import { ExportDialogHost } from '../export/ExportDialog';
import { setCrawlSettingsOpener } from '../crawl/actions';
import { CrawlFileDrop } from '../crawl/CrawlFileDrop';
import { Announcer } from '../feedback/Announcer';
import { AppToaster } from '../feedback/AppToaster';
import { RegionBoundary } from '../feedback/RegionBoundary';
import { useAppCommands } from '../keyboard/useAppCommands';
import { useSimpleScreenShown } from '../app/modeScreen';
import { useChunk } from '../lib/chunk';
import { useModeGuard } from '../mode/useModeGuard';
import { useSettings } from '../settings/settingsStore';
import { InspectorSlot } from './InspectorSlot';
import { paletteChunk, preloadOverlays, shortcutsChunk } from './overlayChunks';
import { useScrollMemory } from './scrollMemory';
import { SidecarBanner } from './SidecarBanner';
import { Sidebar } from './Sidebar';
import { StoppingOverlay } from './StoppingOverlay';
import { TopBar } from './TopBar';
import { useShellIntegration } from './useShellIntegration';
import { useUpdateChecks } from '../updates/useUpdateChecks';
import { useDocumentTitle } from './documentTitle';

/**
 * The shell (docs/v2-ui-ia.md): the sidebar on the frame, then one panel holding the top bar, the main area and the
 * inspector's slot, plus the app-wide overlays. Its parts take their look from the design system (src/ui, styles/components.css).
 *
 * - While anything modal is open (the palette, the `?` sheet, "Stopping crawls…"), everything behind it is inert:
 *   neither the keyboard nor a screen reader can reach it. Toasts and the live regions stay outside, so they're heard.
 * - The main area is the scroller, and each history entry remembers where it was left (scrollMemory.ts): Back returns
 *   there, a new place starts at the top.
 * - Each part of the shell has its own error boundary, as each screen does (its route's): a part that fails shows
 *   why in its place, and the rest keeps working.
 */
export function AppShell() {
  useAppCommands();
  useShellIntegration();
  useUpdateChecks();
  useDocumentTitle();
  useFocusAfterNavigation();
  useScrollMemory();
  useModeGuard();
  useCrawlSettingsOpener();
  // Simple has no inspector: a page's detail is Advanced's. Its `inspect` param stays in the address, for the switch back.
  const inspector = !useSimpleScreenShown();
  const dock = useSettings((settings) => settings.inspectorDock);
  const overlay = useOverlay();
  const modal = useModalOpen();
  // The palette and the shortcut sheet are chunks of their own (overlayChunks.ts), fetched as soon as the app is
  // idle and shown in the render that opens them: the first Ctrl+K is as quick as the next.
  const palette = useChunk(paletteChunk, overlay === 'palette');
  const shortcuts = useChunk(shortcutsChunk, overlay === 'shortcuts');
  useEffect(() => {
    // WebKit has no requestIdleCallback.
    if ('requestIdleCallback' in window) window.requestIdleCallback(preloadOverlays);
    else setTimeout(preloadOverlays, 500);
  }, []);
  return (
    <>
      {/* The framed workspace (docs/v2-ui-design.md): the window is the frame, the sidebar sits on it, and the top bar,
          the screen and the inspector are one panel inset from the window's edge, the screen scrolling inside it. */}
      <div inert={modal} className="relative flex h-full overflow-hidden bg-frame text-text">
        <a
          href="#main"
          className="sr-only z-50 rounded-sm bg-surface-overlay px-3 py-2 text-13 focus:not-sr-only focus:fixed focus:top-2 focus:left-2"
        >
          Skip to the main content
        </a>
        <RegionBoundary name="sidebar">
          <Sidebar />
        </RegionBoundary>
        <div data-panel="" className="si-panel flex min-w-0 flex-1 flex-col">
          <RegionBoundary name="top bar">
            <TopBar />
          </RegionBoundary>
          <RegionBoundary name="engine’s status">
            <SidecarBanner />
          </RegionBoundary>
          <div className={clsx('flex min-h-0 flex-1', dock === 'bottom' && 'flex-col')}>
            <main id="main" tabIndex={-1} className="relative min-h-0 min-w-0 flex-1 overflow-auto outline-none">
              <Outlet />
            </main>
            {inspector ? (
              <RegionBoundary name="inspector">
                <InspectorSlot />
              </RegionBoundary>
            ) : null}
          </div>
        </div>
      </div>
      {palette}
      {shortcuts}
      <StoppingOverlay />
      {/* A crawl file dropped on the window opens it, from any screen (round 5, gap 17). */}
      <CrawlFileDrop />
      <ExportDialogHost />
      <AppToaster />
      <Announcer />
    </>
  );
}

/** Continue's toast, asked for a proxy password again, opens the crawl's settings on the proxy. */
function useCrawlSettingsOpener() {
  const router = useRouter();
  useEffect(() => {
    setCrawlSettingsOpener(
      (crawl) => void router.navigate({ to: '/crawls/$crawl/settings', params: { crawl }, search: { focus: 'proxy' } }),
    );
    return () => setCrawlSettingsOpener(null);
  }, [router]);
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
