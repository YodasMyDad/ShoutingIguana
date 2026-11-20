// What the Issues tables share that isn't a component: the keys said under their rows, and the room that takes.
import type { GridKeyHint } from '../../grid/gridFoot';

/** The grid's footer, where its keys are said (tokens.css `--grid-foot-height`). */
export const GRID_FOOT_HEIGHT = 36;

/** The keys an Issues table answers, under its rows, as the other grids say theirs (A-03). */
export const ISSUE_KEY_HINTS: readonly GridKeyHint[] = [
  { chord: 'arrowup arrowdown', words: 'Move' },
  { chord: 'enter', words: 'Inspect' },
  { chord: 'shift+enter', words: 'Open in the browser' },
  { chord: 'arrowright', words: 'Show more' },
  { chord: 'mod+c', words: 'Copy for Excel' },
];

/**
 * The least an Issues table is squeezed to before the pane scrolls instead: its header, three rows (less where the
 * rows are tall, wrapped ones) and the keys under them.
 */
export function leastFrameHeight(rowHeight: number, headerHeight: number, legend: boolean): number {
  return headerHeight + Math.min(3 * rowHeight, 132) + (legend ? GRID_FOOT_HEIGHT : 0) + 2;
}
