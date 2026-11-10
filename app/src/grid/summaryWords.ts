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

/** "Inlinks", "Inlinks and Issues", "Title, Inlinks and Issues". */
export function listOfNames(names: readonly string[]): string {
  return names.length > 1 ? `${names.slice(0, -1).join(', ')} and ${names.at(-1) ?? ''}` : (names[0] ?? '');
}
