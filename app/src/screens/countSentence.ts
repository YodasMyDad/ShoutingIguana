// A sentence whose counts of URLs are links to exactly those URLs: plain words, and counts that each carry the URL
// grid's filters for what they count. Built pure, so a sentence's words and each count's landing are unit-tested;
// `CountSentence` draws it.
import type { GridFilter } from '../api/grids';

/** Plain words, or a count ("2 URLs") with the filters that open exactly what it counts in URLs. */
export type SentencePart = string | { text: string; filters: readonly GridFilter[] };

/** The sentence as it reads. */
export function sentenceText(parts: readonly SentencePart[]): string {
  return parts.map((part) => (typeof part === 'string' ? part : part.text)).join('');
}
