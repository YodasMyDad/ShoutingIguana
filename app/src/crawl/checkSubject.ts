// What a check's count counts, in words (docs/v2-sidecar-api.md, Checks: `subject`). One place, so Issues, Overview's
// "Fix first" and the inspector can't word a check two ways.
// - A `page` check flags only pages, among the pages the analysis ran on: "pages affected", a part of the pages
//   analysed (the analysis's own `pagesAnalyzed`, stored with the check's count, so the two are of one moment).
// - A `url` check can flag any URL the crawl has crawled (a missing image, a redirect, a URL on another of the site's
//   versions): "URLs affected", a part of the URLs crawled when the analysis ran (the analysis's own `crawledUrlCount`,
//   stored with the counts as `pagesAnalyzed` is). Not the URLs found, which no analysis keeps: the crawl has found
//   more than it has crawled (6,286 against 4,521 on the garden crawl).
// - A check that doesn't say (an engine from before E3b, the design fixtures, a catalog not read yet) is worded as a
//   `url` check: a page is a URL, and every URL it flags is one the crawl crawled, so it's true whatever it flags.
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

/**
 * The analysis's own totals, which a check's count is a part of: the pages it ran on and the URLs the crawl had
 * crawled when it ran, both stored by the analysis that counted the check. Never the library's counts or a grid's,
 * which are of the crawl as it is now.
 */
export type CrawlCounts = Pick<Analysis, 'pagesAnalyzed' | 'crawledUrlCount'>;

/** What a check's share is a share of. */
export type ShareWhole = 'pagesAnalysed' | 'urlsCrawled';

/**
 * What a check's count can truly be called a share of: the one place that decides it.
 * - A `page` check's: the pages the analysis ran on (`pagesAnalyzed`).
 * - Any other check's: the URLs crawled when the analysis ran (its `crawledUrlCount`, the URL grid's "crawled" as it
 *   was then; every URL such a check flags is one of them).
 * Both numbers are the analysis's own, so either share is true however old the analysis is and whatever the crawl has
 * done since: while it runs again, and after it was continued (52 of the 1,661 pages analysed is 3.1%, never 0.6% of
 * the 9,206 the file has now). There is none of URLs only where the analysis didn't keep their count: one an engine
 * from before the score ran, which sends none.
 */
export function shareWhole(check: CountedCheck, analysis: CrawlCounts): ShareWhole | null {
  if (affectedNoun(check) === 'page') return 'pagesAnalysed';
  return typeof analysis.crawledUrlCount === 'number' ? 'urlsCrawled' : null;
}

/** A check's share as a figure: its label, the percentage, and the line that says what it's a share of. */
export interface CheckShare {
  label: string;
  value: string;
  context: string;
}

/**
 * A check's count as a share of what it's a part of (`shareWhole`): "of the N pages analysed" for a `page` check, "of
 * the N URLs crawled" for any other. Null where no share is known to be true: no count (a skipped check), no
 * analysis, an analysis that didn't keep the URLs' count, nothing to be a part of, or a count past it, which is never
 * shown as a whole or more.
 */
export function checkShare(check: CountedCheck, count: number | null, analysis: CrawlCounts | null): CheckShare | null {
  if (count === null || analysis === null) return null;
  const whole = shareWhole(check, analysis);
  if (whole === null) return null;
  const noun = affectedNoun(check);
  const of = whole === 'pagesAnalysed' ? analysis.pagesAnalyzed : (analysis.crawledUrlCount ?? 0);
  if (of <= 0 || count > of) return null;
  const what = whole === 'pagesAnalysed' ? 'analysed' : 'crawled';
  return {
    label: WORDS[noun].share,
    value: formatPercent(count, of),
    context: `of the ${formatCountOf(of, noun)} ${what}`,
  };
}

/** A check, as far as how much of the site its count is goes: whether it finds one fault of the whole site. */
export type ReachingCheck =
  | (NonNullable<CountedCheck> & {
      readonly siteWide?: boolean | undefined;
      readonly reportedOnce?: boolean | undefined;
    })
  | null
  | undefined;

/** How much of the site a check's count is: a share of what it's a part of, or the whole site. */
export interface CheckReach {
  /** The figure's label: "Share of pages", "Reach". */
  label: string;
  /** The figure: "3.4%", "Whole site". */
  value: string;
  /** What it is of, in a line: "of the 265 pages analysed", "A fault of the whole site, not of its pages". */
  context: string;
  /** All of it in words, for a list row's accessible name and tooltip: "3.4% of the 265 pages analysed". */
  words: string;
}

/**
 * How much of the site a check's count is, Screaming Frog's "% of Total" (docs/v2-comparison.md): the share
 * `checkShare` gives, of the pages analysed for a `page` check and of the URLs crawled for any other; or, for a check
 * that finds a fault of the whole site (`siteWide`, which Simple says as "The whole site"), the whole site, never "1
 * page, <0.1%"; or, for one reported once for the whole site (`reportedOnce`), that it was checked once. Null where no
 * share is known to be true, as `checkShare` says.
 */
export function checkReach(
  check: ReachingCheck,
  count: number | null,
  analysis: CrawlCounts | null,
): CheckReach | null {
  if (count === null || count <= 0) return null;
  if (check?.siteWide) {
    return {
      label: 'Reach',
      value: 'Whole site',
      context: 'A fault of the whole site, whatever its count',
      words: 'the whole site',
    };
  }
  if (check?.reportedOnce) {
    return {
      label: 'Reach',
      value: 'Once',
      context: 'Checked once, for the whole site',
      words: 'checked once, for the whole site',
    };
  }
  const share = checkShare(check, count, analysis);
  return share ? { ...share, words: `${share.value} ${share.context}` } : null;
}
