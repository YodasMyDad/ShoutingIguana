// The inspector's place in history (docs/v2-ui-ia.md, flows 3 and 4), in one place every screen with a grid uses:
// - opened from a grid (Enter, a double-click), and following the grid's cursor while open, it's in place (a
//   replace): back and forward step between views, not between the rows looked at;
// - walking a link inside it (an inlink, an outlink), or the palette's "Go to URL…" while it's open, is a new place
//   (a push), and each entry counts the steps walked from where it opened (`inspectSteps` in the entry's history
//   state), so back and forward retrace the walk; "Go to URL…" with it closed opens it in place, as a grid does;
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

/**
 * From inside the inspector (a link) or the palette ("Go to URL…"): a step of the walk, a new place in history. With
 * the inspector closed there's no walk to add to: it opens in place, as from a grid. A push would leave two entries
 * for the same view once it's closed, and back between them would do nothing to see.
 */
export function walkTo(router: AppRouter, urlId: number): Promise<void> {
  if (!inspecting(router)) return inspectFromGrid(router, urlId);
  const steps = walkedSteps(router.state.location.state) + 1;
  return router.navigate({
    to: '.',
    search: withInspect(urlId),
    state: (previous) => ({ ...previous, inspectSteps: steps }),
  });
}

/** Whether a path is a view of the crawl that opens the inspector: one with `inspect` in its search. */
export function viewInspects(pathname: string, crawlId: string): boolean {
  const [, crawls, crawl, section] = pathname.split('/');
  return (
    crawls === 'crawls' &&
    crawl === encodeURIComponent(crawlId) &&
    (section === 'issues' ||
      section === 'urls' ||
      section === 'links' ||
      section === 'sitemaps' ||
      section === 'robots')
  );
}

/**
 * The palette's "Go to URL…": on a view that has the inspector, a step of the walk, or the inspector opened in place
 * when it's closed; from anywhere else (Overview, the crawl's settings), the crawl's URLs with the inspector open on
 * it.
 */
export function goToUrl(router: AppRouter, crawlId: string, urlId: number): Promise<void> {
  if (viewInspects(router.state.location.pathname, crawlId)) return walkTo(router, urlId);
  return router.navigate({ to: '/crawls/$crawl/urls', params: { crawl: crawlId }, search: { inspect: urlId } });
}

/**
 * How long a step back is waited for. A browser's `history.go` past its oldest entry does nothing and says nothing,
 * so a close never waits on it for longer than this.
 */
export const GO_BACK_TIMEOUT_MS = 1_000;

/** Goes back `steps` entries, resolving once the router has the location it went back to, or history didn't move. */
function goBack(router: AppRouter, steps: number): Promise<void> {
  return new Promise((resolve) => {
    const done = () => {
      stop();
      clearTimeout(timer);
      resolve();
    };
    // The router hears the history first (it subscribed when it mounted), so its location is the new one here.
    const stop = router.history.subscribe(done);
    const timer = setTimeout(done, GO_BACK_TIMEOUT_MS);
    router.history.go(-steps);
  });
}

/**
 * How far back history goes from this entry, as far as can be told: no further than the entries the router counted
 * to it, nor than the entries history holds. A browser keeps only so many (Chromium 50), so after a long walk the
 * first steps are gone, with the entry the inspector opened on.
 */
function entriesBehind(router: AppRouter): number {
  const index = router.state.location.state.__TSR_index;
  const counted = Number.isInteger(index) && index > 0 ? index : 0;
  return Math.min(counted, Math.max(router.history.length - 1, 0));
}

/**
 * Esc or Close: back over the steps walked (never further back than history goes), then `inspect` dropped from the
 * entry it opened on, in place. After a walk longer than history keeps, that's the oldest entry left; and where
 * history can't go back as far as asked (it doesn't say how far it can), the entry shown: the inspector always
 * closes.
 */
export function closeInspector(router: AppRouter): Promise<void> {
  // One close at a time. History goes back a moment after it's asked, and until then the router still has the
  // walked entry: a second Esc in that moment would go back over the walk again, out of the view.
  const underWay = closing.get(router);
  if (underWay) return underWay;
  const close = closeOnce(router).finally(() => closing.delete(router));
  closing.set(router, close);
  return close;
}

/** The close under way on each router, which a second Esc or Close joins. */
const closing = new WeakMap<AppRouter, Promise<void>>();

async function closeOnce(router: AppRouter): Promise<void> {
  const steps = Math.min(walkedSteps(router.state.location.state), entriesBehind(router));
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
  /** A link inside the inspector: a new place, a step back retraces. */
  walk: (urlId: number) => void;
  /** Esc or Close: back to the view as it was before the inspector opened. */
  close: () => void;
  /** The palette's "Go to URL…": a walk where the inspector is open, else it opens (on the crawl's URLs if need be). */
  goTo: (crawlId: string, urlId: number) => void;
}

/** The inspector's history model, for the screens and the inspector itself. */
export function useInspector(): Inspector {
  const router = useRouter();
  return useMemo(
    () => ({
      open: (urlId) => void inspectFromGrid(router, urlId),
      walk: (urlId) => void walkTo(router, urlId),
      close: () => void closeInspector(router),
      goTo: (crawlId, urlId) => void goToUrl(router, crawlId, urlId),
    }),
    [router],
  );
}
