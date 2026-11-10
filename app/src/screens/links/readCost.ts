// Which reads of the links grid are quick and which pass over every link (docs/v2-sidecar-api.md, "How the links are
// read"), so the screen can say what it's doing when one takes a while. A crawl of 100,000 pages stores 5 million
// links: the engine finds most views by the counts its analysis keeps, but some it can only answer by reading each
// link once, which takes about a second there. This is the engine's rule as its contract states it, not a
// measurement: it words a wait, and never holds a read back.
import type { GridFilter, GridSort, SummaryReason } from '../../api/grids';
import { formatCount } from '../../lib/format';
import { endColumn } from './linkWords';

/** Why a read looks at every link, or null when the engine finds its rows without. */
export type PassReason = 'search' | 'text' | 'source' | 'crawling' | 'noSummary';

export interface LinkReadState {
  /** The crawl is running: its links are counted afresh every 2 s, and every filter reads them all. */
  crawling: boolean;
  /**
   * Why the crawl has no URL summary, or null when it has one. A crawl never analysed, or continued since, has no
   * link summary either; one another version analysed may have (the link summary outlives an engine update).
   */
  summaryReason: SummaryReason | null | undefined;
}

const narrows = (filter: GridFilter) => filter.op !== 'isEmpty' && filter.op !== 'isNotEmpty';

/**
 * Why this view's first read passes over every link, if it does. The first that applies, in the order a user would
 * recognise their own doing: a search, a filter on the anchor text, one on its source page; then the crawl's state.
 */
export function passReason(
  view: { filters: readonly GridFilter[]; search?: string | undefined; sort: readonly GridSort[] },
  state: LinkReadState,
): PassReason | null {
  if (view.search) return 'search';
  // Whether a link has text is counted by the analysis; what the text says is only in the link.
  const onText = view.filters.some(
    (filter) => (filter.column === 'text' && narrows(filter)) || filter.column === 'fragment' || filter.column === 'id',
  );
  if (onText) return 'text';
  // One source page named outright is read through the links' index by source: only its own links.
  const onSource = view.filters.some((filter) => {
    const end = endColumn(filter.column);
    return end?.end === 'from' && end.column !== 'id';
  });
  if (onSource) return 'source';
  const filtered = view.filters.length > 0 || view.sort.some((key) => key.column !== 'id');
  if (!filtered) return null;
  if (state.crawling || state.summaryReason === 'crawling') return 'crawling';
  if (state.summaryReason === 'notAnalyzed' || state.summaryReason === 'crawlChanged') return 'noSummary';
  return null;
}

const BECAUSE: Record<PassReason, string> = {
  search: 'a search looks at each link’s anchor text and both its URLs',
  text: 'a filter on what the anchor text says looks at each link',
  source: 'a filter on the source page looks at each of its links',
  crawling: 'while the crawl runs, a filter or sort looks at each one',
  noSummary: 'without a current analysis, a filter or sort looks at each one',
};

/**
 * What the grid says while a view's first read is still on its way after 150 ms: "Reading all 5,053,909 links: a
 * search looks at each link’s anchor text and both its URLs." With no reason the engine names, only that it's reading.
 * `total` is the crawl's links when a read has said so, else null.
 */
export function slowReadWords(reason: PassReason | null, total: number | null): string {
  if (reason === null) return 'Reading the links…';
  const all = total === null ? 'Reading every link' : `Reading all ${formatCount(total)} links`;
  return `${all}: ${BECAUSE[reason]}.`;
}
