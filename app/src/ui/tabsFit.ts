/**
 * Which tabs a strip shows, each whole, in the room it has: all of them when they fit; otherwise as many as fit from
 * the first, in order, with the chosen one always among them (taking the place of the last that would have fitted
 * when it comes later). The others are listed in the strip's "N more" menu, never shown cut (polish 2: the inspector's
 * Inlinks tab was cut in half beside "7 more", which listed it too).
 *
 * `widths` are the tabs' widths, `gap` the space between two of them; `room` is the strip's room for tabs when all of
 * them show, and `roomBeside` its room beside the "N more" button. Returns, tab by tab, whether it shows.
 */
export function tabsShown(
  widths: readonly number[],
  chosen: number,
  room: number,
  roomBeside: number,
  gap: number,
): boolean[] {
  const span = (indexes: readonly number[]) =>
    indexes.reduce((sum, index) => sum + (widths[index] ?? 0), 0) + Math.max(0, indexes.length - 1) * gap;
  // Half a pixel either way: widths are fractional, and a tab that fits to the rounding fits.
  if (span(widths.map((_, index) => index)) <= room + 0.5) return widths.map(() => true);
  const shown = chosen >= 0 && chosen < widths.length ? [chosen] : [];
  for (let index = 0; index < widths.length; index++) {
    if (index === chosen) continue;
    if (span([...shown, index]) > roomBeside + 0.5) break;
    shown.push(index);
  }
  return widths.map((_, index) => shown.includes(index));
}
