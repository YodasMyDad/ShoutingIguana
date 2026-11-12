// What a grid says of the columns the analysis fills when there's no analysis to read them from: why, in plain
// words, by the URL summary's reason (docs/v2-sidecar-api.md, URLs: "The summary"). The URL grid and the links grid
// say it the same way.
import type { SummaryReason } from '../api/grids';

/** Why the analysis's columns are empty now, after the columns' names: the summary's reason, in plain words. */
export const UNAVAILABLE_BECAUSE: Record<SummaryReason, string> = {
  crawling: 'fill in once the crawl has finished and been analysed',
  notAnalyzed: 'need an analysis: this crawl hasn’t been analysed yet',
  engineChanged: 'need this crawl analysed again: another version of Shouting Iguana analysed it',
  crawlChanged: 'need this crawl analysed again: it was continued after its last analysis',
};

/**
 * The same while the crawl is being analysed (the analysis that follows a crawl, or one the user asked for): the
 * summary's reason still says what was true before it began, but there's nothing to press, and "hasn't been analysed
 * yet" would be the wrong words for a crawl that is.
 */
export const UNAVAILABLE_WHILE_ANALYSING = 'fill in when the analysis that’s running is done';

/** What the button that mends it says: a crawl never analysed is analysed, any other is analysed again. */
export type AnalyseWords = 'Analyse it' | 'Analyse again';

/**
 * The words for the button that analyses a crawl, by why its analysis can't be read (the summary's reason, or a
 * `needs-analysis` answer's): "Analyse it" only for a crawl that never was, "Analyse again" for one analysed by
 * another version or continued since. One place, so a banner and the state under it can't offer the same thing in
 * different words.
 */
export function analyseWords(reason: string | null | undefined): AnalyseWords {
  return reason === 'engineChanged' || reason === 'crawlChanged' ? 'Analyse again' : 'Analyse it';
}

/** "Inlinks", "Inlinks and Issues", "Title, Inlinks and Issues". */
export function listOfNames(names: readonly string[]): string {
  return names.length > 1 ? `${names.slice(0, -1).join(', ')} and ${names.at(-1) ?? ''}` : (names[0] ?? '');
}
