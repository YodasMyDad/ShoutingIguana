// What the crawl did with a URL a sitemap lists, in a few words, for the grid's Outcome column: the inspector's words
// for the engine's `outcome`, so the two say the same of the same URL. It goes by `outcome`, never by `fetched`
// alone: a URL robots.txt refused has a stored outcome and no answer, and one whose host had stopped answering wasn't
// asked at all. "Crawled" isn't an outcome: on this screen it means the crawl requested the URL (sitemapLinks.ts),
// and a URL that answered says "Answered". Pure.
import type { UrlRow } from '../../api/grids';
import { OUTCOME_WORDS } from '../../inspector/words';

/** For an outcome this version has no name for, or a URL the crawl is done with whose outcome isn't given. */
const UNKNOWN_OUTCOME = 'Unknown';

/**
 * `crawling`: the crawl is running, so a URL without an outcome may still be fetched, or may never be (past a limit,
 * outside what the crawl follows). The grid can't tell which, so it says no more than "so far".
 */
export function outcomeWords(row: Pick<UrlRow, 'fetched' | 'outcome'>, crawling: boolean): string {
  const words: Partial<Record<string, string>> = OUTCOME_WORDS;
  if (row.outcome) return words[row.outcome] ?? UNKNOWN_OUTCOME;
  // Done with, and how isn't said: it may have been requested or refused, so it isn't called crawled.
  if (row.fetched === true) return UNKNOWN_OUTCOME;
  return crawling ? 'Not crawled so far' : 'Not crawled';
}
