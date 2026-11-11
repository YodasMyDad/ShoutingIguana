// What a check's count counts, in words (docs/v2-sidecar-api.md, Checks: `subject`). One place, so Issues, Overview's
// "Fix first" and the inspector can't word a check two ways.
// - A `page` check flags only pages, among the pages the analysis ran on: "pages affected", a part of the pages
//   analysed (the analysis's own `pagesAnalyzed`, stored with the check's count, so the two are of one moment).
// - A `url` check can flag any URL the crawl has (a missing image, a redirect, a URL a sitemap lists that was never
//   fetched): "URLs affected", a part of the URLs found. Never of the URLs crawled, as some it flags weren't. Nothing
//   stores how many URLs the crawl had when it was analysed, so that share is said only while the file is as the
//   analysis saw it.
// - A check that doesn't say (an engine from before E3b, the design fixtures, a catalog not read yet) is worded as a
//   `url` check: a page is a URL, and every URL it flags is one the crawl found, so it's true whatever it flags.
import type { Analysis, CheckSubject } from '../api/types';
import { formatCount, formatCountOf, formatPercent } from '../lib/format';

/** A check, as far as its count's words go. Null or undefined is a check the catalog doesn't have (yet). */
export type CountedCheck = { readonly subject?: CheckSubject | undefined } | null | undefined;

/** What one of the URLs a check flags is called: "page" only where every one of them is a page. */
export type AffectedNoun = 'page' | 'URL';

export interface AffectedWords {
  /** For a count: "1 page", "9 URLs" (`formatCountOf`, `Num`'s `noun`). */
  noun: AffectedNoun;
  /** More than one of them: "pages". */
  plural: string;
  /** The count's label: "Pages affected". */
  affected: string;
  /** A heading over them: "Affected pages". */
  heading: string;
  /** In a grid's name, after the check's title: "affected pages". */
  named: string;
  /** The share's label: "Share of pages". */
  share: string;
  /** A count column's header: "Pages". */
  column: string;
  /** A group's rows couldn't be read. */
  unread: string;
}

const WORDS: Record<AffectedNoun, AffectedWords> = {
  page: {
    noun: 'page',
    plural: 'pages',
    affected: 'Pages affected',
    heading: 'Affected pages',
    named: 'affected pages',
    share: 'Share of pages',
    column: 'Pages',
    unread: 'The group’s pages couldn’t be read.',
  },
  URL: {
    noun: 'URL',
    plural: 'URLs',
    affected: 'URLs affected',
    heading: 'Affected URLs',
    named: 'affected URLs',
    share: 'Share of URLs',
    column: 'URLs',
    unread: 'The group’s URLs couldn’t be read.',
  },
};

export function affectedNoun(check: CountedCheck): AffectedNoun {
  return check?.subject === 'page' ? 'page' : 'URL';
}

/** The words for what a check flags. */
export function affectedWords(check: CountedCheck): AffectedWords {
  return WORDS[affectedNoun(check)];
}

/** "1 page", "4,521 URLs": a check's count, or a group's. */
export function affectedCount(check: CountedCheck, count: number): string {
  return formatCountOf(count, affectedNoun(check));
}

/** "264 of 265 pages": a group's part of what its check flags. */
export function partOfAffected(check: CountedCheck, part: number, total: number): string {
  return `${formatCount(part)} of ${formatCountOf(total, affectedNoun(check))}`;
}

/**
 * The link from a group to the URL grid filtered to it: "Show all 264 pages in URLs", "Show the page in URLs". URLs
 * aren't named twice: "Show all 9 in URLs", "Show it in URLs".
 */
export function showInUrlsWords(check: CountedCheck, count: number): string {
  if (affectedNoun(check) === 'page') {
    return count === 1 ? 'Show the page in URLs' : `Show all ${formatCount(count)} pages in URLs`;
  }
  return count === 1 ? 'Show it in URLs' : `Show all ${formatCount(count)} in URLs`;
}

/** What a check's count is a part of, and what tells whether each is of the moment the analysis counted the check. */
export interface CrawlCounts {
  /**
   * The analysis the check's count is from: the pages it ran on (`pagesAnalyzed`, stored with the counts), and why it's
   * out of date, if it is.
   */
  analysis: Pick<Analysis, 'pagesAnalyzed' | 'outOfDateReasons'>;
  /** The crawl is running: what it has found is moving. */
  crawling: boolean;
  /** Every URL the crawl has found, crawled or not, as its file is now (the URL grid's `total`). Null until it's read. */
  urlsFound: number | null;
}

/** What a check's share is a share of. */
export type ShareWhole = 'pagesAnalysed' | 'urlsFound';

/**
 * What a check's count can truly be called a share of: the one place that decides it.
 * - A `page` check's: the pages the analysis ran on. Both numbers are the analysis's own, so the share is true however
 *   old the analysis is and whatever the crawl has done since.
 * - Any other check's: the URLs the crawl has found. That is counted in the file as it is now, so there is none once
 *   the file has moved on from the analysis: while the crawl runs, and after it was continued (`crawlChanged`). The old
 *   count over the new total would be a share of nothing (52 of 1,661 pages is 3.1%, never 0.6% of 9,206).
 */
export function shareWhole(check: CountedCheck, crawl: Pick<CrawlCounts, 'analysis' | 'crawling'>): ShareWhole | null {
  if (affectedNoun(check) === 'page') return 'pagesAnalysed';
  return crawl.crawling || crawl.analysis.outOfDateReasons.includes('crawlChanged') ? null : 'urlsFound';
}

/** A check's share as a figure: its label, the percentage, and the line that says what it's a share of. */
export interface CheckShare {
  label: string;
  value: string;
  context: string;
}

/**
 * A check's count as a share of what it's a part of (`shareWhole`): the pages analysed for a `page` check, the URLs
 * found for any other. Until the URLs found are read the share keeps its place with a dash, so nothing moves when they
 * arrive. Null where no share is known to be true: no count (a skipped check), no analysis, a URL count that isn't of
 * the analysis's moment, nothing to be a part of, or a count past it, which is never shown as a whole or more.
 */
export function checkShare(check: CountedCheck, count: number | null, crawl: CrawlCounts | null): CheckShare | null {
  if (count === null || crawl === null) return null;
  const whole = shareWhole(check, crawl);
  if (whole === null) return null;
  const noun = affectedNoun(check);
  const label = WORDS[noun].share;
  const of = whole === 'pagesAnalysed' ? crawl.analysis.pagesAnalyzed : crawl.urlsFound;
  const what = whole === 'pagesAnalysed' ? 'analysed' : 'found';
  if (of === null) return { label, value: '–', context: `of the ${WORDS[noun].plural} ${what}` };
  if (of <= 0 || count > of) return null;
  return { label, value: formatPercent(count, of), context: `of the ${formatCountOf(of, noun)} ${what}` };
}
