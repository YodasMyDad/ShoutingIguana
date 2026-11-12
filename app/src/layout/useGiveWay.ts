// The top bar's order of giving way (docs/v2-ui-simple.md, Top bar), measured on the bar's own width, not the
// window's, so the order holds whatever the sidebar is doing, in either mode, idle or with a crawl running.
//
// The bar's parts never shrink: each step makes one give way, in order, and the bar takes the fewest steps that let
// everything fit. The steps are marked on the bar as `data-give-1` … `data-give-8`, cumulatively (step 3 sets 1, 2
// and 3), and each part hides or narrows itself on its own step's mark (`group-data-[give-3]/topbar:…`). The measure is
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
  'site', // 7: the site
  'forward', // 8: Forward
] as const;

const MAX_STEP = GIVE_WAY_STEPS.length;

function setStep(bar: HTMLElement, step: number): void {
  for (let index = 1; index <= MAX_STEP; index++) bar.toggleAttribute(`data-give-${index}`, index <= step);
  bar.dataset.give = String(step);
}

/** Whether the bar's parts fit at this step. A bar not laid out (hidden, or a test's DOM) fits. */
function fits(bar: HTMLElement, step: number): boolean {
  setStep(bar, step);
  return bar.scrollWidth <= bar.clientWidth;
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
