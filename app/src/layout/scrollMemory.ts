// Back keeps your place (the audit of 2026-11-19, H-02; fix-common's rule 9). The main pane is the app's scroller, not
// the window, and the router's own scroll restoration is off, so a figure followed from far down Overview came back, on
// Back, to the top. Here each history entry remembers where its main pane was left:
//
// - a new place (another path) starts at the top;
// - Back, Forward and a reload return to where that entry was left, once the screen's data has painted enough for the
//   pane to reach it;
// - a change of the search alone (a sheet opened, a filter set) leaves the pane where it is;
// - on a route with a screen per mode (Overview and the Summary), each mode's screen keeps its own place, so Advanced
//   again is where Advanced was left.
//
// A screen's own scrollers (a grid, a list beside a detail) keep their place themselves.
import { useRouter, type AnyRouter, type ParsedLocation } from '@tanstack/react-router';
import { useEffect, useLayoutEffect, useRef } from 'react';
import { useSimpleScreenShown } from '../app/modeScreen';

const STORAGE_KEY = 'si.scroll';
/** How many places are remembered: the oldest go first. */
const MAX_PLACES = 200;
/** How long a place waits for its screen to grow tall enough to be scrolled back to. */
export const SCROLL_SETTLE_MS = 4_000;

type Variant = 'simple' | 'advanced';

const places = new Map<string, number>();
let read = false;

function readPlaces(): void {
  if (read) return;
  read = true;
  try {
    const stored: unknown = JSON.parse(sessionStorage.getItem(STORAGE_KEY) ?? '[]');
    if (!Array.isArray(stored)) return;
    for (const entry of stored as unknown[]) {
      if (Array.isArray(entry) && typeof entry[0] === 'string' && typeof entry[1] === 'number') {
        places.set(entry[0], entry[1]);
      }
    }
  } catch {
    // Nothing kept, or it can't be read: every place starts at the top.
  }
}

function writePlaces(): void {
  try {
    sessionStorage.setItem(STORAGE_KEY, JSON.stringify([...places]));
  } catch {
    // No room, or no storage: a reload starts at the top.
  }
}

function remember(key: string, top: number): void {
  places.delete(key);
  places.set(key, top);
  while (places.size > MAX_PLACES) {
    const oldest = places.keys().next().value;
    if (oldest === undefined) break;
    places.delete(oldest);
  }
}

/** A history entry's own key, and which of the route's screens it shows. */
function placeKey(location: ParsedLocation, variant: Variant): string {
  const state = location.state as { __TSR_key?: string; key?: string };
  return `${state.__TSR_key ?? state.key ?? location.href}|${variant}`;
}

const USER_EVENTS = ['wheel', 'touchstart', 'pointerdown', 'keydown'] as const;

/**
 * Scrolls the pane to `target`, again each time its content changes, until it gets there, the user takes over, or the
 * wait runs out. Returns how to give up early. `done` is called once, whichever way it ends.
 */
function scrollBack(main: HTMLElement, target: number, done: () => void): () => void {
  let over = false;
  const finish = () => {
    if (over) return;
    over = true;
    clearTimeout(timer);
    resize.disconnect();
    mutation.disconnect();
    for (const name of USER_EVENTS) main.removeEventListener(name, finish);
    done();
  };
  const attempt = () => {
    if (over) return;
    main.scrollTop = target;
    if (Math.abs(main.scrollTop - target) <= 1) finish();
  };
  const resize = new ResizeObserver(attempt);
  const mutation = new MutationObserver(() => {
    for (const child of main.children) resize.observe(child);
    attempt();
  });
  const timer = setTimeout(finish, SCROLL_SETTLE_MS);
  for (const name of USER_EVENTS) main.addEventListener(name, finish, { passive: true });
  mutation.observe(main, { childList: true, subtree: true });
  for (const child of main.children) resize.observe(child);
  attempt();
  return finish;
}

interface ScrollMemory {
  /** The route's other screen is shown (the mode changed on a route with a screen per mode). */
  showVariant: (variant: Variant) => void;
  stop: () => void;
}

function watchScroll(router: AnyRouter, main: HTMLElement, firstVariant: Variant): ScrollMemory {
  readPlaces();
  let variant = firstVariant;
  let key = placeKey(router.state.location, variant);
  /** Where the user last left the pane, on the place shown. */
  let top = main.scrollTop;
  /** Between leaving a place and settling on the next, the pane's scrolling isn't the user's. */
  let settling = false;
  let giveUp: () => void = () => undefined;
  let moved: 'new' | 'history' = 'new';

  const arrive = (nextKey: string, target: number | null) => {
    giveUp();
    key = nextKey;
    const settled = () => {
      settling = false;
      top = main.scrollTop;
    };
    if (target === null) {
      settled();
      return;
    }
    settling = true;
    giveUp = scrollBack(main, target, settled);
  };

  const onScroll = () => {
    if (!settling) top = main.scrollTop;
  };
  main.addEventListener('scroll', onScroll, { passive: true });

  const offHistory = router.history.subscribe(({ action }) => {
    moved = action.type === 'BACK' || action.type === 'FORWARD' || action.type === 'GO' ? 'history' : 'new';
  });
  const offBefore = router.subscribe('onBeforeNavigate', ({ fromLocation }) => {
    // The app's first load leaves nowhere.
    if (!fromLocation) return;
    giveUp();
    remember(key, top);
    settling = true;
  });
  const offResolved = router.subscribe('onResolved', ({ fromLocation, toLocation, pathChanged }) => {
    const nextKey = placeKey(toLocation, variant);
    // The app's first load, of an entry it may have been on before (a reload): there, or left alone.
    if (!fromLocation) arrive(nextKey, places.get(nextKey) ?? null);
    else if (moved === 'history') arrive(nextKey, places.get(nextKey) ?? 0);
    else arrive(nextKey, pathChanged ? 0 : null);
    moved = 'new';
  });
  const onHide = () => {
    remember(key, top);
    writePlaces();
  };
  window.addEventListener('pagehide', onHide);

  // A reload: the entry the app opens on is one it has been on.
  const kept = places.get(key);
  if (kept !== undefined && kept > 0) arrive(key, kept);

  return {
    showVariant: (next) => {
      if (next === variant) return;
      giveUp();
      remember(key, top);
      variant = next;
      const nextKey = placeKey(router.state.location, variant);
      arrive(nextKey, places.get(nextKey) ?? 0);
    },
    stop: () => {
      giveUp();
      main.removeEventListener('scroll', onScroll);
      window.removeEventListener('pagehide', onHide);
      offHistory();
      offBefore();
      offResolved();
    },
  };
}

/** Keeps the main pane's place per history entry (see the file's head). For the shell, once. */
export function useScrollMemory(): void {
  const router = useRouter();
  const variant: Variant = useSimpleScreenShown() ? 'simple' : 'advanced';
  const memory = useRef<ScrollMemory | null>(null);
  const shown = useRef(variant);
  // Before the browser has drawn the other screen, so the place left is the one the user saw.
  useLayoutEffect(() => {
    shown.current = variant;
    memory.current?.showVariant(variant);
  }, [variant]);
  useEffect(() => {
    const main = document.getElementById('main');
    if (!main) return undefined;
    const watching = watchScroll(router, main, shown.current);
    memory.current = watching;
    return () => {
      watching.stop();
      memory.current = null;
    };
  }, [router]);
}

/** Tests only: no place remembered. */
export function forgetScrollForTests(): void {
  places.clear();
  try {
    sessionStorage.removeItem(STORAGE_KEY);
  } catch {
    // No storage: nothing to forget.
  }
}
