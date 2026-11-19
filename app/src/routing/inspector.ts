// The inspector's place in history (docs/v2-ui-ia.md, flows 3 and 4), in one place every screen with a grid uses:
// - opening it (a click on a URL, Enter, a double-click, the palette's "Go to URL…") is one new place (a push): the
//   entry behind it is the view exactly as it was, so Back closes the inspector and returns there;
// - everything after that is in place (a replace): the grid's cursor moving it to another row, and a hop inside it (an
//   inlink, an outlink, "Go to URL…" while it's open). However long the walk, Back is still one step to the view;
// - the hops are kept beside the entry (`inspectTrail` in its history state) for the inspector's own way back, with
//   the section each was left on;
// - Esc or Close steps back to the entry it opened from where that is still the same view; otherwise (a link opened
//   straight on the inspector, or the view's filters changed while it was open) it drops `inspect` in place.
import { useRouter, useRouterState } from '@tanstack/react-router';
import { useMemo, useSyncExternalStore } from 'react';
import type { AppRouter } from '../app/router';

/** A hop the inspector made: the URL it left, and the section it was on, to come back to. */
export interface InspectStep {
  url: number;
  section?: string | undefined;
}

/**
 * What the inspector shows as it arrives on a URL: a section other than the one last used, and in a list of links the
 * one the reader came for (the URL at its other end), which goes first, marked.
 */
export interface InspectShow {
  section?: string | undefined;
  link?: number | undefined;
}

declare module '@tanstack/history' {
  interface HistoryState {
    /** On the entry that opening the inspector pushed: the address of the view it opened from, one step behind. */
    inspectOpenedFrom?: string;
    /** The hops made inside the inspector since it opened, oldest first. */
    inspectTrail?: InspectStep[];
    /** What to show on arriving at this entry's URL. */
    inspectShow?: InspectShow;
  }
}

type Search = Record<string, unknown>;

/**
 * The search with the inspected URL set or removed. Every view with a grid has `inspect` in its checked search, but
 * from `.` the router types the search as no route's in particular, so the updater is written once, here.
 */
const withInspect = (urlId: number | undefined, more: Search = {}) =>
  ((previous: Search) => ({ ...previous, ...more, inspect: urlId })) as unknown as SearchUpdate;
type SearchUpdate = NonNullable<Parameters<AppRouter['navigate']>[0]['search']>;
type State =
  { inspectTrail?: unknown; inspectOpenedFrom?: unknown; inspectShow?: unknown; __TSR_index?: number } | undefined;

/** The most hops kept: a walk longer than this forgets its first steps. */
export const TRAIL_MAX = 50;

/** The hops made inside the inspector to this history entry, oldest first. */
export function inspectTrail(state: State): InspectStep[] {
  const trail = state?.inspectTrail;
  if (!Array.isArray(trail)) return [];
  return trail.filter((step): step is InspectStep => {
    const url = (step as InspectStep | null)?.url;
    return typeof url === 'number' && Number.isInteger(url) && url > 0;
  });
}

/** Steps the inspector walked to this history entry from the URL it opened on. */
export function walkedSteps(state: State): number {
  return inspectTrail(state).length;
}

/** Whether the current view has the inspector open. */
function inspecting(router: AppRouter): boolean {
  return inspectedNow(router) !== undefined;
}

function inspectedNow(router: AppRouter): number | undefined {
  const inspect = (router.state.location.search as Search).inspect;
  return typeof inspect === 'number' ? inspect : undefined;
}

/** The state with what to show on arrival set, or cleared: a request to show something is for one arrival. */
function arriving<T extends { inspectShow?: InspectShow }>(state: T, show: InspectShow | undefined): T {
  const { inspectShow: _shown, ...rest } = state;
  return (show ? { ...rest, inspectShow: show } : rest) as T;
}

/**
 * From a grid: opens the inspector on a URL (a new place: back returns to the view as it was), or moves it to another
 * as the grid's cursor moves (in place: the walk inside it ends, as the grid chose this URL). `more` is what else of
 * the view's search goes with the URL, set in the same step: which of a link's two ends it is (Links' `end`). `show`
 * is the section to arrive on and the link to mark there.
 */
export function inspectFromGrid(router: AppRouter, urlId: number, more?: Search, show?: InspectShow): Promise<void> {
  if (!inspecting(router)) {
    const from = router.state.location.href;
    return router.navigate({
      to: '.',
      search: withInspect(urlId, more),
      state: (previous) => arriving({ ...previous, inspectOpenedFrom: from, inspectTrail: [] }, show),
    });
  }
  const same = inspectedNow(router) === urlId;
  return router.navigate({
    to: '.',
    search: withInspect(urlId, more),
    // The same URL again (the grid's cursor catching up with a hop) keeps the hops made to it and what it shows.
    state: (previous) =>
      same && show === undefined
        ? { ...previous }
        : arriving({ ...previous, inspectTrail: same ? (previous.inspectTrail ?? []) : [] }, show),
    replace: true,
  });
}

/**
 * From inside the inspector (a link) or the palette ("Go to URL…"): a hop, in place, with the URL left added to the
 * trail the inspector's own back control retraces (`leaving` is the section it was on). With the inspector closed
 * there's nothing to hop from: it opens, as from a grid.
 */
export function walkTo(
  router: AppRouter,
  urlId: number,
  options: { show?: InspectShow | undefined; leaving?: string | undefined } = {},
): Promise<void> {
  const from = inspectedNow(router);
  if (from === undefined) return inspectFromGrid(router, urlId, undefined, options.show);
  if (from === urlId && options.show === undefined) return Promise.resolve();
  const trail = inspectTrail(router.state.location.state);
  const next = from === urlId ? trail : [...trail, { url: from, section: options.leaving }].slice(-TRAIL_MAX);
  return router.navigate({
    to: '.',
    search: withInspect(urlId),
    state: (previous) => arriving({ ...previous, inspectTrail: next }, options.show),
    replace: true,
  });
}

/** The inspector's own back: to the URL the last hop left, on the section it was left on. Nothing with no hop made. */
export function walkBack(router: AppRouter): Promise<void> {
  const trail = inspectTrail(router.state.location.state);
  const last = trail.at(-1);
  if (!last || !inspecting(router)) return Promise.resolve();
  return router.navigate({
    to: '.',
    search: withInspect(last.url),
    state: (previous) =>
      arriving({ ...previous, inspectTrail: trail.slice(0, -1) }, last.section ? { section: last.section } : undefined),
    replace: true,
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

/** The URL "Go to URL…" last went to, until the inspector has taken the keyboard for it. */
let goneToByName: number | null = null;
const byNameListeners = new Set<() => void>();
function setGoneToByName(urlId: number | null): void {
  goneToByName = urlId;
  byNameListeners.forEach((listener) => listener());
}
const subscribeByName = (listener: () => void) => {
  byNameListeners.add(listener);
  return () => byNameListeners.delete(listener);
};

/**
 * Whether the inspector should take the keyboard for this URL: it was gone to by name ("Go to URL…"), not found under
 * a grid's cursor, so focus follows it into the inspector instead of staying on the view (or on nothing, from a view
 * without a grid). The inspector says once it has (`arrivedByName`).
 */
export function useGoneToByName(urlId: number): boolean {
  return useSyncExternalStore(
    subscribeByName,
    () => goneToByName === urlId,
    () => false,
  );
}

export function arrivedByName(): void {
  setGoneToByName(null);
}

/**
 * The palette's "Go to URL…": on a view that has the inspector, a hop where it's open, else it opens there (one new
 * place); from anywhere else (Overview, the crawl's settings), the crawl's URLs with the inspector open on it.
 */
export function goToUrl(router: AppRouter, crawlId: string, urlId: number): Promise<void> {
  setGoneToByName(urlId);
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
 * to it, nor than the entries history holds.
 */
function entriesBehind(router: AppRouter): number {
  const index = router.state.location.state.__TSR_index;
  const counted = Number.isInteger(index) && index > 0 ? index : 0;
  return Math.min(counted, Math.max(router.history.length - 1, 0));
}

/** An address without what the inspector adds to it: the view itself, to tell whether two entries show the same one. */
function viewOf(href: string): string {
  const url = new URL(href, 'http://app');
  url.searchParams.delete('inspect');
  url.searchParams.delete('end');
  url.searchParams.sort();
  return `${url.pathname}?${url.searchParams.toString()}`;
}

/**
 * Esc or Close. Where this entry is the one opening the inspector pushed, and the view hasn't changed since, one step
 * back: the view exactly as it was, and no second entry for it. Otherwise (a link straight to the inspector, the
 * view's filters changed while it was open, or history that can't step back) `inspect` is dropped in place: the
 * inspector always closes.
 */
export function closeInspector(router: AppRouter): Promise<void> {
  // One close at a time. History goes back a moment after it's asked, and until then the router still has the
  // inspector's entry: a second Esc in that moment would go back again, out of the view.
  const underWay = closing.get(router);
  if (underWay) return underWay;
  const close = closeOnce(router).finally(() => closing.delete(router));
  closing.set(router, close);
  return close;
}

/** The close under way on each router, which a second Esc or Close joins. */
const closing = new WeakMap<AppRouter, Promise<void>>();

async function closeOnce(router: AppRouter): Promise<void> {
  if (!inspecting(router)) return;
  const { location } = router.state;
  const openedFrom = location.state.inspectOpenedFrom;
  if (typeof openedFrom === 'string' && viewOf(openedFrom) === viewOf(location.href) && entriesBehind(router) > 0) {
    await goBack(router, 1);
    // Back where it opened from. If history didn't move (it doesn't say), the entry is closed in place below.
    if (!inspecting(router)) return;
  }
  await router.navigate({
    to: '.',
    search: withInspect(undefined),
    state: (previous) => {
      const { inspectOpenedFrom: _from, inspectTrail: _trail, inspectShow: _show, ...rest } = previous;
      return rest;
    },
    replace: true,
  });
}

export interface Inspector {
  /**
   * A click on a URL in a grid, its Enter or double-click (a new place: Back closes the inspector), or the grid's
   * cursor moving while the inspector is open (in place). `more` is what else of the view's own search is set with it
   * (Links: which end of the row the URL is); `show` the section to arrive on and the link to mark there.
   */
  open: (urlId: number, more?: Record<string, unknown>, show?: InspectShow) => void;
  /** A link inside the inspector: a hop, in place, which `back` retraces. `leaving` is the section it was on. */
  walk: (urlId: number, options?: { show?: InspectShow | undefined; leaving?: string | undefined }) => void;
  /** The inspector's own back: the URL the last hop left. */
  back: () => void;
  /** Esc or Close: back to the view as it was before the inspector opened. */
  close: () => void;
  /** The palette's "Go to URL…": a hop where the inspector is open, else it opens (on the crawl's URLs if need be). */
  goTo: (crawlId: string, urlId: number) => void;
}

/** The inspector's history model, for the screens and the inspector itself. */
export function useInspector(): Inspector {
  const router = useRouter();
  return useMemo(
    () => ({
      open: (urlId, more, show) => void inspectFromGrid(router, urlId, more, show),
      walk: (urlId, options) => void walkTo(router, urlId, options),
      back: () => void walkBack(router),
      close: () => void closeInspector(router),
      goTo: (crawlId, urlId) => void goToUrl(router, crawlId, urlId),
    }),
    [router],
  );
}

const NO_STEPS: InspectStep[] = [];

/** The hops made inside the inspector to where it is now, oldest first: what its back control retraces. */
export function useInspectTrail(): InspectStep[] {
  const trail = useRouterState({ select: (state) => state.location.state.inspectTrail });
  return useMemo(() => (trail ? inspectTrail({ inspectTrail: trail }) : NO_STEPS), [trail]);
}

/** What the inspector was asked to show on arriving at the URL it is on, if anything. */
export function useInspectShow(): InspectShow | undefined {
  return useRouterState({ select: (state) => state.location.state.inspectShow });
}
