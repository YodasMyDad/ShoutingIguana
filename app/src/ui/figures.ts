// Figures as the brief shows them: the number leads, the unit follows smaller and muted ("22 min 26 s", "117 ms").

export interface FigurePart {
  text: string;
  unit: boolean;
}

const NUMBER = /([<>]?[-−]?\d[\d,.]*)/;

/**
 * Splits a formatted figure into its numbers and its units: "1 min 12 s" is 1, min, 12, s; "18.3 MB" is 18.3, MB;
 * "12.3/s" is 12.3, /s; "99.6%" is 99.6, %. Anything without a number ("–", "just now") is one part, not a unit.
 */
export function splitFigure(text: string): FigurePart[] {
  if (!NUMBER.test(text)) return [{ text, unit: false }];
  return text
    .split(NUMBER)
    .map((part) => part.trim())
    .filter((part) => part !== '')
    .map((part) => ({ text: part, unit: !NUMBER.test(part) }));
}
