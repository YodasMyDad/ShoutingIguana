// A route's screen in the current mode: its Simple screen from the table (app/simpleScreens.ts) in Simple, when it has
// one, and its Advanced screen otherwise. The router preloads the one the mode shows; the other is fetched once the
// app is idle, so a switch never waits for a chunk.
import { useLocation, type RouteComponent } from '@tanstack/react-router';
import { useEffect } from 'react';
import { getMode, useMode, type Mode } from '../mode/mode';
import { sectionOf } from './sections';
import { SIMPLE_SCREENS, type SimpleScreenKey } from './simpleScreens';

/** A screen the router can preload: a lazy route component, or one loaded with the app (Home). */
type Preloadable = ((props: never) => React.ReactNode) & { preload?: () => Promise<void> | undefined };

function screenFor(key: SimpleScreenKey, advanced: Preloadable, mode: Mode): Preloadable {
  const simple = SIMPLE_SCREENS[key];
  return mode === 'simple' && simple ? simple : advanced;
}

function whenIdle(work: () => void): () => void {
  if ('requestIdleCallback' in window) {
    const handle = window.requestIdleCallback(work);
    return () => window.cancelIdleCallback(handle);
  }
  const timer = setTimeout(work, 500);
  return () => clearTimeout(timer);
}

/** The route component for a route with a row in the table. */
export function modeScreen(key: SimpleScreenKey, advanced: Preloadable): RouteComponent {
  function ModeScreen() {
    const mode = useMode();
    const Screen = screenFor(key, advanced, mode) as () => React.ReactNode;
    // The other mode's screen, so the switch shows it at once.
    useEffect(() => {
      const other = screenFor(key, advanced, mode === 'simple' ? 'advanced' : 'simple');
      return other === Screen ? undefined : whenIdle(() => void other.preload?.()?.catch(() => undefined));
    }, [mode, Screen]);
    return <Screen />;
  }
  return Object.assign(ModeScreen, {
    preload: async () => {
      await screenFor(key, advanced, getMode()).preload?.();
    },
  }) as RouteComponent;
}

/** The route component for a screen that is the same in both modes (the Report): the table's, or `missing` until then. */
export function sameInBothModes(key: SimpleScreenKey, missing: Preloadable): RouteComponent {
  function BothModesScreen() {
    const Screen = (SIMPLE_SCREENS[key] ?? missing) as () => React.ReactNode;
    return <Screen />;
  }
  return Object.assign(BothModesScreen, {
    preload: async () => {
      await (SIMPLE_SCREENS[key] ?? missing).preload?.();
    },
  }) as RouteComponent;
}

/** The table's row for a path, or null for a route that has none. */
export function simpleScreenKeyOf(pathname: string): SimpleScreenKey | null {
  if (pathname === '/') return 'home';
  const section = sectionOf(pathname);
  return section === 'overview' || section === 'issues' || section === 'report' || section === 'extraction'
    ? section
    : null;
}

/**
 * Whether the screen shown is a Simple screen: Simple, on a route whose row is set. The shell leaves out what Simple
 * hasn't (the inspector) only then: a route still showing its Advanced screen in Simple works as it does in Advanced.
 */
export function useSimpleScreenShown(): boolean {
  const mode = useMode();
  const key = useLocation({ select: (location) => simpleScreenKeyOf(location.pathname) });
  return mode === 'simple' && key !== null && SIMPLE_SCREENS[key] !== null;
}
