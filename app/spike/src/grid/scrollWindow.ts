// A windowed scroll space for very tall grids. 1M rows x 28 px is 28M px: Chromium clamps scroll extents at
// 33,554,428 px, and the compositor works in float32, so past ~16.7M device px rows and the sticky header are drawn
// at visibly wrong positions. Here the real scroll area is at most WINDOW_PX tall and maps onto the virtual offset
// through `base`; when the scroll position nears an edge of the window, the window is moved (whole rows) and scrollTop
// is adjusted in the same task, so nothing visible changes.
import type { Virtualizer } from '@tanstack/react-virtual';
import { ROW_HEIGHT } from '../perf/probe';

import { params } from '../params';
export const WINDOW_PX = Number(params.get('window') ?? 2_000_000); // 0 turns windowing off
const EDGE_PX = WINDOW_PX / 4;

export type ScrollWindow = {
  base: number;
  total: number;
  rebases: number;
  enabled: () => boolean;
  domHeight: () => number;
  observeElementOffset: (instance: Virtualizer<HTMLDivElement, Element>, cb: (offset: number, isScrolling: boolean) => void) => () => void;
  scrollToFn: (offset: number, options: { adjustments?: number; behavior?: ScrollBehavior }, instance: Virtualizer<HTMLDivElement, Element>) => void;
};

export function createScrollWindow(): ScrollWindow {
  // Re-basing can leave scrollTop unchanged (every jump centres the target), and then no scroll event fires, so the
  // virtualizer must be told about the new offset directly.
  let notify: ((isScrolling: boolean) => void) | null = null;
  const window: ScrollWindow = {
    base: 0,
    total: 0,
    rebases: 0,
    enabled: () => WINDOW_PX > 0 && window.total > WINDOW_PX,
    domHeight: () => (window.enabled() ? WINDOW_PX : window.total),
    observeElementOffset(instance, cb) {
      const element = instance.scrollElement;
      if (!element) return () => undefined;
      let idleTimer = 0;
      const handle = (isScrolling: boolean) => {
        let top = element.scrollTop;
        if (window.enabled()) {
          const nearTop = top < EDGE_PX && window.base > 0;
          const nearBottom = top > WINDOW_PX - EDGE_PX - element.clientHeight && window.base < window.total - WINDOW_PX;
          if (nearTop || nearBottom) {
            rebase(element, window.base + top);
            top = element.scrollTop;
          }
        } else if (window.base !== 0) {
          window.base = 0;
        }
        cb(window.base + top, isScrolling);
      };
      const onScroll = () => {
        handle(true);
        clearTimeout(idleTimer);
        idleTimer = setTimeout(() => handle(false), 150) as unknown as number;
      };
      notify = handle;
      handle(false);
      element.addEventListener('scroll', onScroll, { passive: true });
      return () => {
        notify = null;
        clearTimeout(idleTimer);
        element.removeEventListener('scroll', onScroll);
      };
    },
    scrollToFn(offset, { adjustments = 0, behavior }, instance) {
      const element = instance.scrollElement;
      if (!element) return;
      const target = offset + adjustments;
      if (window.enabled() && (target < window.base || target > window.base + WINDOW_PX - element.clientHeight)) {
        rebase(element, target);
        queueMicrotask(() => notify?.(false));
        return;
      }
      element.scrollTo({ top: target - window.base, behavior });
    },
  };

  function rebase(element: HTMLElement, virtualOffset: number) {
    const maxBase = Math.max(0, window.total - WINDOW_PX);
    const centred = Math.min(maxBase, Math.max(0, virtualOffset - WINDOW_PX / 2));
    window.base = Math.round(centred / ROW_HEIGHT) * ROW_HEIGHT;
    window.rebases++;
    element.scrollTop = virtualOffset - window.base;
  }

  return window;
}
