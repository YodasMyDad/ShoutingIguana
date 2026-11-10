// The inspector sheet's size, by one rule for both docks: the view it shares the window with always keeps its
// minimum, so the sheet's range is the settings' own range cut to the room that's left. The resize handle says that
// range (`aria-valuemin`, `aria-valuemax`), and a size chosen in a bigger window is clamped to it when it's read.

export interface SizeRange {
  min: number;
  max: number;
}

/**
 * The sizes the sheet can take: `range` (settings/schema.ts), no more than `room` less what the view keeps. `room` is
 * the size of what holds the view and the sheet, along the dock's axis; null before it's been laid out (the range as
 * it is). In a window too small for both minimums the view keeps its own and the sheet has what's left.
 */
export function sheetRange(range: SizeRange, room: number | null, keep: number): SizeRange {
  if (room === null) return range;
  const max = Math.max(0, Math.min(range.max, Math.floor(room - keep)));
  return { min: Math.min(range.min, max), max };
}

export function clampSize(size: number, range: SizeRange): number {
  return Math.min(range.max, Math.max(range.min, size));
}
