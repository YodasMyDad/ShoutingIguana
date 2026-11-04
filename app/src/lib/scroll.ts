// Bringing something into view inside the panel that scrolls it, without moving the page around it (scrollIntoView
// scrolls every ancestor, the window included).

/**
 * The scroll position that brings a span, `top` to `top + height` in the scroller's content, into a viewport of
 * `viewport` pixels now at `scrollTop`: its start when it's above or doesn't fit, just enough when it's below. Null when
 * it's in view already.
 */
export function revealOffset(top: number, height: number, scrollTop: number, viewport: number): number | null {
  const bottom = top + height;
  if (top >= scrollTop && bottom <= scrollTop + viewport) return null;
  if (top < scrollTop || height > viewport) return Math.max(0, top);
  return Math.max(0, bottom - viewport);
}

/** The nearest ancestor that scrolls vertically, or null. */
export function scrollParent(element: HTMLElement): HTMLElement | null {
  for (let parent = element.parentElement; parent; parent = parent.parentElement) {
    const { overflowY } = getComputedStyle(parent);
    if ((overflowY === 'auto' || overflowY === 'scroll') && parent.scrollHeight > parent.clientHeight) return parent;
  }
  return null;
}

/**
 * Scrolls the nearest scrolling ancestor, and only it, so the element is in view: all of it when it fits, else
 * `focus` (a part of it that matters most, such as a row) from its start.
 */
export function revealInScrollParent(element: HTMLElement, focus: HTMLElement | null = null): void {
  const scroller = scrollParent(element);
  if (!scroller) return;
  const box = scroller.getBoundingClientRect();
  const fits = element.getBoundingClientRect().height <= scroller.clientHeight;
  const rect = (fits || !focus ? element : focus).getBoundingClientRect();
  const next = revealOffset(
    rect.top - box.top + scroller.scrollTop,
    rect.height,
    scroller.scrollTop,
    scroller.clientHeight,
  );
  if (next !== null) scroller.scrollTop = next;
}
