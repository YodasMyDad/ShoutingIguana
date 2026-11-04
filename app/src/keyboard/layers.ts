// Keys a layer took only for itself, which what's underneath should still act on. Radix prevents the Esc that closes
// a tooltip, and the registry and the fields leave a prevented key alone, so the first Esc would only close the
// tooltip. The tooltip gives that Esc back (`releaseKey`), and one Esc closes it and does the app's Esc as well. An
// overlay that stays open for a field that clears on Esc gives the key back to the field the same way.
const released = new WeakSet<Event>();

/** Gives a key a layer prevented for itself back to what's underneath it. */
export function releaseKey(event: Event): void {
  released.add(event);
}

/** Whether something already took this key: prevented, and not given back. */
export function isKeyTaken(event: Event): boolean {
  return event.defaultPrevented && !released.has(event);
}
