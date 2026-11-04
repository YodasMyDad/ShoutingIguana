// The inspector's place in history (docs/v2-ui-ia.md, flows 3 and 4), in one place every screen with a grid uses:
// - opened from a grid (Enter, a double-click), and following the grid's cursor while open, it's in place (a
//   replace): back and forward step between views, not between the rows looked at;
// - walking a link inside it (an inlink, an outlink), or the palette's "Go to URL…", is a new place (a push), and each
//   entry counts the steps walked from where it opened (`inspectSteps` in the entry's history state), so back and
//   forward retrace the walk;
// - Esc or Close goes back over the steps walked to the entry it opened on, then drops `inspect` from it in place: the
//   screen is as it was before the inspector opened, and back from there leaves the screen.
import { useRouter } from '@tanstack/react-router';
import { useMemo } from 'react';
import type { AppRouter } from '../app/router';

declare module '@tanstack/history' {
  interface HistoryState {
    /** Steps the inspector walked to this entry from the one it opened on: 0, or missing, where it opened. */
    inspectSteps?: number;
  }
}

type Search = Record<string, unknown>;

/**
 * The search with the inspected URL set or removed. Every view with a grid has `inspect` in its checked search, but
 * from `.` the router types the search as no route's in particular, so the updater is written once, here.
 */
const withInspect = (urlId: number | undefined) =>
  ((previous: Search) => ({ ...previous, inspect: urlId })) as unknown as SearchUpdate;
type SearchUpdate = NonNullable<Parameters<AppRouter['navigate']>[0]['search']>;
type State = { inspectSteps?: number; __TSR_index?: number } | undefined;

/** Steps the inspector walked to this history entry from the one it opened on. */
export function walkedSteps(state: State): number {
  const steps = state?.inspectSteps;
  return typeof steps === 'number' && Number.isInteger(steps) && steps > 0 ? steps : 0;
}

/** Whether the current view has the inspector open. */
function inspecting(router: AppRouter): boolean {
  return (router.state.location.search as Search).inspect !== undefined;
}

/**
 * From a grid: opens the inspector on a URL, or moves it to another as the grid's cursor moves. In place, keeping the
 * steps walked to this entry, so Esc still goes back over them.
 */
export function inspectFromGrid(router: AppRouter, urlId: number): Promise<void> {
  const steps = inspecting(router) ? walkedSteps(router.state.location.state) : 0;
  return router.navigate({
    to: '.',
    search: withInspect(urlId),
    state: (previous) => ({ ...previous, inspectSteps: steps }),
    replace: true,
  });
}

/** From inside the inspector (a link) or the palette ("Go to URL…"): a step of the walk, a new place in history. */
export function walkTo(router: AppRouter, urlId: number): Promise<void> {
  const steps = inspecting(router) ? walkedSteps(router.state.location.state) + 1 : 0;
  return router.navigate({
    to: '.',
    search: withInspect(urlId),
    state: (previous) => ({ ...previous, inspectSteps: steps }),
  });
}

/** Goes back `steps` entries, resolving once the router has the location it went back to. */
function goBack(router: AppRouter, steps: number): Promise<void> {
  return new Promise((resolve) => {
    // The router hears the history first (it subscribed when it mounted), so its location is the new one here.
    const stop = router.history.subscribe(() => {
      stop();
      resolve();
    });
    router.history.go(-steps);
  });
}

/**
 * Esc or Close: back over the steps walked (never further back than history goes), then `inspect` dropped from the
 * entry it opened on, in place.
 */
export async function closeInspector(router: AppRouter): Promise<void> {
  const location = router.state.location;
  const index = location.state.__TSR_index;
  const steps = Math.min(walkedSteps(location.state), Number.isInteger(index) && index > 0 ? index : 0);
  if (steps > 0) await goBack(router, steps);
  await router.navigate({
    to: '.',
    search: withInspect(undefined),
    state: (previous) => {
      const { inspectSteps: _steps, ...rest } = previous;
      return rest;
    },
    replace: true,
  });
}

export interface Inspector {
  /** A grid's Enter or double-click, or its cursor moving while the inspector is open: in place. */
  open: (urlId: number) => void;
  /** A link inside the inspector, or the palette's "Go to URL…": a new place, a step back retraces. */
  walk: (urlId: number) => void;
  /** Esc or Close: back to the view as it was before the inspector opened. */
  close: () => void;
}

/** The inspector's history model, for the screens and the inspector itself. */
export function useInspector(): Inspector {
  const router = useRouter();
  return useMemo(
    () => ({
      open: (urlId) => void inspectFromGrid(router, urlId),
      walk: (urlId) => void walkTo(router, urlId),
      close: () => void closeInspector(router),
    }),
    [router],
  );
}
