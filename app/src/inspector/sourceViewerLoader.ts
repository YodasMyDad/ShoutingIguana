// Loads the source viewer (CodeMirror, a chunk of its own) without `lazy` and Suspense: React holds back what follows
// a Suspense fallback for 300 ms, which made the first Source of a session take a third of a second however small the
// page. Loaded once, as soon as an inspector is open and the app is idle, so the section usually opens with it there.
import { useEffect, useState, type ComponentType } from 'react';
import type { SourceViewerProps } from './SourceViewer';

type Viewer = ComponentType<SourceViewerProps>;

let loaded: Viewer | null = null;
let loading: Promise<Viewer> | null = null;

/** Starts loading the viewer, or returns the load already under way. A load that fails can be tried again. */
export function loadSourceViewer(): Promise<Viewer> {
  loading ??= import('./SourceViewer').then(
    (module) => {
      loaded = module.default;
      return module.default;
    },
    (error: unknown) => {
      loading = null;
      throw error;
    },
  );
  return loading;
}

/** Loads the viewer when the app is next idle: an inspector is open, so its source may be asked for. */
export function preloadSourceViewer(): () => void {
  if (loaded || loading) return () => undefined;
  const load = () => void loadSourceViewer().catch(() => undefined);
  if ('requestIdleCallback' in window) {
    const handle = window.requestIdleCallback(load);
    return () => window.cancelIdleCallback(handle);
  }
  const timer = setTimeout(load, 300);
  return () => clearTimeout(timer);
}

export interface SourceViewerState {
  /** The viewer, once it's loaded. */
  Viewer: Viewer | null;
  /** Why it couldn't be loaded. */
  error: unknown;
  retry: () => void;
}

/** The viewer for a section that shows it: there at once when it's loaded already. */
export function useSourceViewer(): SourceViewerState {
  const [viewer, setViewer] = useState<Viewer | null>(() => loaded);
  const [error, setError] = useState<unknown>(null);
  const [attempt, setAttempt] = useState(0);
  useEffect(() => {
    if (viewer) return undefined;
    let current = true;
    loadSourceViewer().then(
      (component) => {
        if (current) setViewer(() => component);
      },
      (failure: unknown) => {
        if (current) setError(failure ?? new Error('The source viewer couldn’t be loaded.'));
      },
    );
    return () => {
      current = false;
    };
  }, [viewer, attempt]);
  return {
    Viewer: viewer,
    error,
    retry: () => {
      setError(null);
      setAttempt((value) => value + 1);
    },
  };
}
