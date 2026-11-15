// A grid's footer (docs/v2-ui-design.md: "the grid's key hints in its footer"): the keys a grid's own screen shows
// under its rows, and where the cursor is, in words. Pure.
import { formatCount, formatCountOf } from '../lib/format';

/** A key hint in a grid's footer: its keys and what they do ("Enter", "Inspect"). */
export interface GridKeyHint {
  chord: string;
  words: string;
}

/** The grid's footer hints as the design has them (docs/v2-ui-design.md: "the grid's key hints in its footer"). */
export const GRID_KEY_HINTS: readonly GridKeyHint[] = [
  { chord: 'arrowup arrowdown', words: 'Move' },
  { chord: 'enter', words: 'Inspect' },
  { chord: 'space', words: 'Select' },
  { chord: 'mod+c', words: 'Copy for Excel' },
];

/** "Row 12 of 264", "3 rows selected": where the cursor is, or what's selected, in the footer's words. */
export function footWords(
  cursor: number | null,
  count: number,
  selected: number,
  loading: boolean,
  noun: [string, string],
): string {
  if (loading) return '';
  // No rows: the empty state says why, and the footer has nothing to add.
  if (count === 0) return '';
  if (selected > 1) return `${formatCountOf(selected, noun[0], noun[1])} selected`;
  const capital = `${noun[0].charAt(0).toUpperCase()}${noun[0].slice(1)}`;
  return cursor === null
    ? formatCountOf(count, noun[0], noun[1])
    : `${capital} ${formatCount(cursor + 1)} of ${formatCount(count)}`;
}
