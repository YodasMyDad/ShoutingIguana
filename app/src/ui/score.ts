// The score's words (docs/v2-ui-simple.md, "The score"; the glossary's "Very good, Good, Needs work, Poor"): one
// place for the band's word and the ring's accessible name, so Home, the Summary, the breakdown and the report screen
// say the same.
import type { ScoreBand } from '../api/types';

const BAND_WORDS: Record<ScoreBand, string> = {
  veryGood: 'Very good',
  good: 'Good',
  needsWork: 'Needs work',
  poor: 'Poor',
};

/** "Good": the score's word, by its band. */
export function bandWord(band: ScoreBand): string {
  return BAND_WORDS[band];
}

/** The band a score falls in: 90 and over, 70 and over, 50 and over, under 50. The engine sends it; this is for stories. */
export function bandOf(value: number): ScoreBand {
  return value >= 90 ? 'veryGood' : value >= 70 ? 'good' : value >= 50 ? 'needsWork' : 'poor';
}

/** "Good so far" for a partial score: the ring is dashed and says "so far", and so does its word. */
export function scoreWord(band: ScoreBand, partial = false): string {
  return partial ? `${bandWord(band)} so far` : bandWord(band);
}

/** The ring's accessible name: "Score 85 out of 100, Good", "Score 71 out of 100 so far, Good so far", "No score". */
export function scoreLabel(value: number | null, band: ScoreBand | null = null, partial = false): string {
  if (value === null) return 'No score';
  const word = band ? `, ${scoreWord(band, partial)}` : '';
  return `Score ${value} out of 100${partial ? ' so far' : ''}${word}`;
}

/** The ring's circle: its radius and its length, in the 100 × 100 view box. */
export const RADIUS = 44;
export const CIRCUMFERENCE = 2 * Math.PI * RADIUS;
/** The partial arc's dash and gap, along the circle. */
const DASH = 3.2;
const GAP = 2.6;

/**
 * The dashes of a partial arc as one dash array: as many whole dashes as fit in the score's length, the last one cut
 * to fit, then a gap for the rest of the circle. One array rather than a mask, so there's no id to keep unique.
 */
export function partialDashes(length: number): string {
  const parts: number[] = [];
  let drawn = 0;
  while (drawn < length) {
    const dash = Math.min(DASH, length - drawn);
    parts.push(dash);
    drawn += dash;
    if (drawn >= length) break;
    const gap = Math.min(GAP, length - drawn);
    parts.push(gap);
    drawn += gap;
  }
  // An odd count of parts ends on a dash; an even one on a gap, which the closing gap simply lengthens.
  if (parts.length % 2 === 0) parts[parts.length - 1] = (parts.at(-1) ?? 0) + CIRCUMFERENCE;
  else parts.push(CIRCUMFERENCE);
  return parts.map((part) => part.toFixed(2)).join(' ');
}
