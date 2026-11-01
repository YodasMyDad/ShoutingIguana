import { Outlet } from '@tanstack/react-router';
import clsx from 'clsx';
import { Suspense, lazy, useEffect } from 'react';
import { useOverlay } from '../app/overlays';
import { Announcer } from '../feedback/Announcer';
import { AppToaster } from '../feedback/AppToaster';
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
 */
export function AppShell() {
  useAppCommands();
  useShellIntegration();
  const dock = useSettings((settings) => settings.inspectorDock);
  const overlay = useOverlay();
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
    <div className="flex h-full overflow-hidden bg-canvas text-text">
      <a
        href="#main"
        className="sr-only z-50 rounded-sm bg-surface-overlay px-3 py-2 text-13 focus:not-sr-only focus:fixed focus:top-2 focus:left-2"
      >
        Skip to the main content
      </a>
      <Sidebar />
      <div className="flex min-w-0 flex-1 flex-col">
        <TopBar />
        <SidecarBanner />
        <div className={clsx('flex min-h-0 flex-1', dock === 'bottom' && 'flex-col')}>
          <main id="main" tabIndex={-1} className="min-h-0 min-w-0 flex-1 overflow-auto outline-none">
            <Outlet />
          </main>
          <InspectorSlot />
        </div>
      </div>
      <Suspense fallback={null}>
        {overlay === 'palette' ? <CommandPalette /> : null}
        {overlay === 'shortcuts' ? <ShortcutSheet /> : null}
      </Suspense>
      <StoppingOverlay />
      <AppToaster />
      <Announcer />
    </div>
  );
}
