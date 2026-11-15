// How wide a text draws in a font, for a column sized to what it holds (a grid's column widths are numbers).

let context: OffscreenCanvasRenderingContext2D | null | undefined;

/** Roughly how wide a character of 12 to 14 px text is, where nothing can measure (jsdom has no canvas). */
const FALLBACK_CHAR_PX = 8;

/** The text's width in this CSS font ("500 12px Inter, sans-serif"), measured on a canvas; estimated where there's none. */
export function textWidth(text: string, font: string): number {
  if (context === undefined) {
    context = typeof OffscreenCanvas === 'undefined' ? null : new OffscreenCanvas(1, 1).getContext('2d');
  }
  if (!context) return text.length * FALLBACK_CHAR_PX;
  context.font = font;
  return context.measureText(text).width;
}

/**
 * The least width a name takes on at most two lines, breaking at a space or after a hyphen ("Blocked by" over
 * "robots.txt", "Non-" over "indexable"): the narrower of each way to break it, by its wider line.
 */
export function twoLineWidth(text: string, font: string): number {
  // Each word with what follows it: a space (dropped at a break) or nothing after a hyphen.
  const words = text.match(/[^\s-]+-?\s*|-\s*/g) ?? [text];
  const line = (from: number, to: number) => textWidth(words.slice(from, to).join('').trimEnd(), font);
  let least = line(0, words.length);
  for (let at = 1; at < words.length; at += 1) least = Math.min(least, Math.max(line(0, at), line(at, words.length)));
  return least;
}

/** A token's value as the page has it now (`--text-12` → "12px"), or the fallback where there's no stylesheet. */
export function tokenValue(name: string, fallback: string, root: HTMLElement = document.documentElement): string {
  return getComputedStyle(root).getPropertyValue(name).trim() || fallback;
}
