// Focus the app moves itself (back to a menu's button as the menu closes, on to the next chip when one is removed)
// isn't the user arriving somewhere: the tooltip keyboard focus opens would pop up unasked, and take the next Esc.
// Tooltip.tsx opens nothing for such a focus.
const quiet = new WeakSet<Element>();

/** The next focus on this element opens no tooltip. It lasts until that focus lands, or until this task ends. */
export function quietNextFocus(element: Element | null | undefined): void {
  if (!element) return;
  quiet.add(element);
  setTimeout(() => quiet.delete(element), 0);
}

/** For a tooltip's trigger being focused: whether the app moved focus there, so the tooltip stays shut. */
export function takeQuietFocus(element: Element): boolean {
  return quiet.delete(element);
}

/** Moves focus without opening the element's tooltip. */
export function focusQuietly(element: HTMLElement | null | undefined): void {
  if (!element) return;
  quietNextFocus(element);
  element.focus();
}
