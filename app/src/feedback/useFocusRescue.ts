import { useEffect, useReducer, useRef, type RefObject } from 'react';

/** Focus is nowhere: on the page itself, as it is once the element that had it has left the document. */
const nowhere = () => document.activeElement === null || document.activeElement === document.body;

/**
 * Keeps keyboard focus from dropping to the page when a view takes away the control that had it: a banner's button
 * goes once its action has worked (Resume, Continue), Pause becomes Resume, a card is swapped for another as the
 * crawl's state changes. When the element last focused inside `container` has left the document and nothing else
 * took the focus, focus goes to `fallback()` (the page's title, the control that took the removed one's place).
 *
 * It looks after the view's own effects have run, so a more particular move (the analysis's banner taking focus to its
 * Cancel) comes first, and this only catches what's left. A click on empty space, or focus moved elsewhere, isn't a
 * loss: only a focused element that was removed is.
 */
export function useFocusRescue(
  container: RefObject<HTMLElement | null>,
  fallback: () => HTMLElement | null | undefined,
): void {
  const last = useRef<Element | null>(null);
  const watched = useRef<{ node: HTMLElement; stop: () => void } | null>(null);
  const [, lookAgain] = useReducer((count: number) => count + 1, 0);

  // After every draw: the container may only be there some of the time (the top bar's controls, with a crawl open).
  useEffect(() => {
    const node = container.current;
    if (watched.current?.node === node) return;
    watched.current?.stop();
    watched.current = null;
    if (!node) return;
    const onFocusIn = (event: FocusEvent) => {
      last.current = event.target instanceof Element ? event.target : null;
    };
    node.addEventListener('focusin', onFocusIn);
    // A part of the view that drew itself (not the component with this hook) may be what took the focused element
    // away: draw again, so the check below runs after it too.
    const observer = new MutationObserver(() => {
      if (last.current !== null && !last.current.isConnected && nowhere()) lookAgain();
    });
    observer.observe(node, { childList: true, subtree: true });
    watched.current = {
      node,
      stop: () => {
        node.removeEventListener('focusin', onFocusIn);
        observer.disconnect();
      },
    };
  });
  useEffect(
    () => () => {
      watched.current?.stop();
      watched.current = null;
    },
    [],
  );

  // After every draw, and after the effects declared before this hook's.
  useEffect(() => {
    if (last.current === null || last.current.isConnected || !nowhere()) return;
    last.current = null;
    fallback()?.focus();
  });
}
