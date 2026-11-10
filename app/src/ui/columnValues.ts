// How a column chart writes a column's value when the whole number is wider than the column: pure, so the choice is
// unit-tested apart from the measuring, which needs a browser's layout.
import { formatCompactCount } from '../lib/format';

/**
 * The ways to write a column's value, the fullest first: "312,876", then "313k", then "310k". The chart shows the
 * first that fits its column.
 */
export function valueTexts(column: { value: number; valueText: string }): string[] {
  const shorter = [formatCompactCount(column.value, 3), formatCompactCount(column.value, 2)];
  return [column.valueText, ...shorter].filter((text, index, all) => all.indexOf(text) === index);
}

/**
 * The fullest way to write a value that fits `available` px, given each way's width. When none fits, the narrowest
 * (the fuller of two as narrow: "313k" before "310k"); when the room isn't known (nothing measured, as in tests),
 * the fullest.
 */
export function fittingText(texts: readonly { text: string; width: number }[], available: number): string {
  const fullest = texts[0];
  if (!fullest || !(available > 0)) return fullest?.text ?? '';
  const fits = texts.find((candidate) => candidate.width <= available);
  if (fits) return fits.text;
  return texts.reduce((narrowest, candidate) => (candidate.width < narrowest.width ? candidate : narrowest), fullest)
    .text;
}
