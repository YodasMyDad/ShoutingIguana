// A component kept in a chunk of its own, loaded without `lazy` and Suspense. `lazy` suspends the first time it
// renders even when its chunk has been fetched, the Suspense fallback is shown, and React holds back what follows a
// fallback for 300 ms: a session's first Ctrl+K took a third of a second, with the palette's code there all along.
// Here a chunk that's loaded renders in the very render that first wants it, and one that isn't shows nothing until
// it arrives, as a `null` fallback did.
import { createElement, useEffect, useState, type ComponentType, type ReactNode } from 'react';

export interface Chunk {
  /** Starts loading it, or returns the load under way. A load that failed can be tried again. */
  load: () => Promise<ComponentType>;
  /** The component, once it's loaded. */
  loaded: () => ComponentType | null;
}

/** A component behind a dynamic import: `chunk(() => import('./Big').then((module) => module.Big))`. */
export function chunk(importer: () => Promise<ComponentType>): Chunk {
  let component: ComponentType | null = null;
  let loading: Promise<ComponentType> | null = null;
  return {
    loaded: () => component,
    load: () =>
      (loading ??= importer().then(
        (found) => {
          component = found;
          return found;
        },
        (error: unknown) => {
          loading = null;
          throw error;
        },
      )),
  };
}

/**
 * The chunk's component, rendered while it's `wanted`: at once when it's loaded already (load it while the app is
 * idle), else once it has arrived. Nothing while it isn't wanted. A load that fails is thrown to the error boundary,
 * as `lazy` throws it.
 */
export function useChunk(source: Chunk, wanted: boolean): ReactNode {
  const [, setArrived] = useState(0);
  const [failure, setFailure] = useState<{ error: unknown } | null>(null);
  const component = source.loaded();
  useEffect(() => {
    if (!wanted || component) return undefined;
    let current = true;
    source.load().then(
      () => {
        if (current) setArrived((count) => count + 1);
      },
      (error: unknown) => {
        if (current) setFailure({ error });
      },
    );
    return () => {
      current = false;
    };
  }, [source, wanted, component]);
  if (failure) throw failure.error;
  return wanted && component ? createElement(component) : null;
}
