// What the Links screen says about a crawl with no current analysis: what still works, what doesn't, and what to do.
// The links grid reads the crawl's own tables, so every link is there whatever the analysis's state; what the
// analysis adds is the link summary (which makes filters quick) and the URL summary (which fills some of each end's
// columns). Pure, so each state's words are tested.
import type { SummaryReason, SummaryState } from '../../api/grids';
import { UNAVAILABLE_BECAUSE, listOfNames } from '../../grid/summaryWords';

export interface AnalysisNote {
  /** What's true of the crawl now, and what that means here. */
  text: string;
  /** What would mend it, when something would: the button's words. Null while the crawl runs. */
  action: 'Analyse it' | 'Analyse again' | null;
}

const STATE: Record<Exclude<SummaryReason, 'crawling'>, string> = {
  notAnalyzed:
    'This crawl hasn’t been analysed yet. Every link is here to search, filter and sort, but each filter reads every link, so it’s slower on a big crawl',
  crawlChanged:
    'This crawl was continued after its last analysis. Every link is here to search, filter and sort, but each filter reads every link, so it’s slower on a big crawl',
  engineChanged:
    'Another version of Shouting Iguana analysed this crawl. Every link is here to search, filter and sort by what the crawl itself recorded',
};

/**
 * The note over the grid, or null when there's nothing to say: the crawl has its analysis, or it's running and no
 * column shown waits for one (a running crawl is said by its status and the Live mark, not here).
 *
 * `emptyColumns` are the names of the columns shown that have no values without the analysis.
 */
export function analysisNote(summary: SummaryState | undefined, emptyColumns: readonly string[]): AnalysisNote | null {
  if (!summary || summary.available) return null;
  const reason = summary.reason ?? 'notAnalyzed';
  if (reason === 'crawling') {
    if (emptyColumns.length === 0) return null;
    return { text: `${listOfNames(emptyColumns)} ${UNAVAILABLE_BECAUSE.crawling}.`, action: null };
  }
  const columns =
    emptyColumns.length > 0
      ? `, and ${listOfNames(emptyColumns)} ${emptyColumns.length === 1 ? 'is' : 'are'} empty until it’s analysed${reason === 'notAnalyzed' ? '' : ' again'}.`
      : `. The columns an analysis fills (inlinks, issues, where redirects end) are empty until it’s analysed${reason === 'notAnalyzed' ? '' : ' again'}.`;
  return { text: `${STATE[reason]}${columns}`, action: reason === 'notAnalyzed' ? 'Analyse it' : 'Analyse again' };
}

/** Why the grid has no rows at all, in a title and a sentence, by whether the crawl is still running. */
export function noLinksWords(crawling: boolean): { title: string; why: string } {
  return crawling
    ? { title: 'No links yet', why: 'They arrive as the crawl reads pages.' }
    : { title: 'No links', why: 'The crawl stored none: it read no page with a link on it.' };
}

/** What nothing matched: the search, the filters, or both. */
export function noMatchWords(search: string | undefined, filtered: boolean): string {
  if (search && filtered) return `No links match “${search}” with these filters.`;
  if (search) return `No links match “${search}”.`;
  return 'No links match these filters.';
}
