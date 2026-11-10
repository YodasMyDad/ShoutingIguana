// What the crawl did with a URL a sitemap lists, in a few words, for the grid's Outcome column. It goes by the URL's
// `outcome`, never by `fetched` alone: a URL robots.txt refused has a stored outcome and no answer, and one whose host
// had stopped answering wasn't asked at all. Pure.
import type { UrlRow } from '../../api/grids';
import { OUTCOME_WORDS } from '../../inspector/words';

/**
 * `crawling`: the crawl is running, so a URL without an outcome may still be fetched, or may never be (past a limit,
 * outside what the crawl follows). The grid can't tell which, so it says no more than "so far".
 */
export function outcomeWords(row: Pick<UrlRow, 'fetched' | 'outcome'>, crawling: boolean): string {
  if (row.outcome) return row.outcome === 'success' ? 'Crawled' : OUTCOME_WORDS[row.outcome];
  // An outcome this version has no name for: the crawl did get to it.
  if (row.fetched === true) return 'Crawled';
  return crawling ? 'Not crawled so far' : 'Not crawled';
}
