// The top bar's order of giving way (docs/v2-ui-simple.md, Top bar), measured on the bar's own width, not the
// window's, so the order holds whatever the sidebar is doing, in either mode, idle or with a crawl running.
//
// The bar's parts never shrink: each step makes one give way, in order, and the bar takes the fewest steps that let
// everything fit. The steps are marked on the bar as `data-give-1` … `data-give-13`, cumulatively (step 3 sets 1, 2
// and 3), and each part hides or narrows itself on its own step's mark (`group-data-give-3/topbar:…`). The site and
// the section you're on narrow before they go: shortened, never to a letter, then gone. The switch never gives way. The measure is
// taken before the browser paints, so no in-between state is ever seen: when the bar is resized, and when what it holds
// changes (a crawl's count, a crumb, a control coming or going).
import { useLayoutEffect, type RefObject } from 'react';

/** The steps, in the order things give way. */
export const GIVE_WAY_STEPS = [
  'keys', // 1: the keys inside buttons (F7, F8)
  'date', // 2: the crawl's date
  'search', // 3: the search field becomes its icon
  'home', // 4: Home
  'count', // 5: the count in the status pill
  'controls', // 6: Pause, Stop and Continue become icons
  'site-short', // 7: the site is shortened, as far as its least width…
  'site', // 8: …then goes
  'forward', // 9: Forward
  'more', // 10: Pause, Resume, Stop, Continue and the search icon go into one More menu
  'status', // 11: the status pill shows only its dot or icon
  'section-short', // 12: the section you're on is shortened, as far as its least width…
  'section', // 13: …then goes
] as const;

const MAX_STEP = GIVE_WAY_STEPS.length;

function setStep(bar: HTMLElement, step: number): void {
  for (let index = 1; index <= MAX_STEP; index++) bar.toggleAttribute(`data-give-${index}`, index <= step);
  bar.dataset.give = String(step);
}

/**
 * Whether the bar's parts fit at this step: the bar, and each part that may narrow (`data-give-fit`, the crumbs, which
 * clip what doesn't fit), hold all they have. A bar not laid out (hidden, or a test's DOM) fits.
 */
function fits(bar: HTMLElement, step: number): boolean {
  setStep(bar, step);
  if (bar.scrollWidth > bar.clientWidth) return false;
  for (const part of bar.querySelectorAll<HTMLElement>('[data-give-fit]')) {
    if (part.scrollWidth > part.clientWidth) return false;
  }
  return true;
}

/** The fewest steps that let everything fit, starting from the step the bar is at: usually a read or two. */
export function measureGiveWay(bar: HTMLElement): number {
  let step = Number(bar.dataset.give ?? 0);
  if (fits(bar, step)) {
    while (step > 0 && fits(bar, step - 1)) step--;
  } else {
    while (step < MAX_STEP && !fits(bar, step + 1)) step++;
    step = Math.min(step + 1, MAX_STEP);
  }
  setStep(bar, step);
  return step;
}

export function useGiveWay(ref: RefObject<HTMLElement | null>): void {
  useLayoutEffect(() => {
    const bar = ref.current;
    if (!bar) return undefined;
    let measuring = false;
    const measure = () => {
      // Its own changes (the marks it sets) are no news.
      if (measuring) return;
      measuring = true;
      measureGiveWay(bar);
      measuring = false;
    };
    measure();
    const resize = new ResizeObserver(measure);
    resize.observe(bar);
    // A count that grows, a crumb or a control that comes or goes: the parts' own changes, not the marks.
    const content = new MutationObserver(measure);
    content.observe(bar, { subtree: true, childList: true, characterData: true, attributeFilter: ['class', 'hidden'] });
    // Fonts arriving change every width. (A test's DOM has no fonts.)
    if ('fonts' in document) void document.fonts.ready.then(measure);
    return () => {
      resize.disconnect();
      content.disconnect();
    };
  }, [ref]);
}
