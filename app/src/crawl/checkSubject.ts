// What a check's count counts, in words (docs/v2-sidecar-api.md, Checks: `subject`). One place, so Issues, Overview's
// "Fix first" and the inspector can't word a check two ways.
// - A `page` check flags only pages, as the engine's page count counts them: "pages affected", a part of the pages
//   crawled.
// - A `url` check can flag any URL the crawl has (a missing image, a redirect, a URL a sitemap lists that was never
//   fetched): "URLs affected", a part of the URLs found. Never of the URLs crawled, as some it flags weren't.
// - A check that doesn't say (an engine from before E3b, the design fixtures, a catalog not read yet) is worded as a
//   `url` check: a page is a URL, and every URL it flags is one the crawl found, so it's true whatever it flags.
import type { CheckSubject } from '../api/types';
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

/** The crawl's own counts, which a check's count is a part of. */
export interface CrawlCounts {
  /** Pages crawled: the engine's page count (the library's `pageCount`). Null until it's read. */
  pages: number | null;
  /** Every URL the crawl found, crawled or not (the URL grid's `total`). Null until it's read. */
  urls: number | null;
}

/** A check's share as a figure: its label, the percentage, and the line that says what it's a share of. */
export interface CheckShare {
  label: string;
  value: string;
  context: string;
}

/**
 * A check's count as a share of what it's a part of: the pages crawled for a `page` check, the URLs found for any
 * other. Until the crawl's count is read it keeps its place with a dash, so nothing moves when it arrives. Null where
 * no share is known to be true: no count (a skipped check), no crawl counts (`crawl` is null while the crawl runs:
 * its counts are moving, and the analysis is of the crawl as it was), nothing to be a part of, or a count past it
 * (an analysis older than the file), which is never shown as a whole or more.
 */
export function checkShare(check: CountedCheck, count: number | null, crawl: CrawlCounts | null): CheckShare | null {
  if (count === null || crawl === null) return null;
  const noun = affectedNoun(check);
  const of = noun === 'page' ? crawl.pages : crawl.urls;
  const what = noun === 'page' ? 'crawled' : 'found';
  const label = WORDS[noun].share;
  if (of === null) return { label, value: '–', context: `of the ${WORDS[noun].plural} ${what}` };
  if (of <= 0 || count > of) return null;
  return { label, value: formatPercent(count, of), context: `of the ${formatCountOf(of, noun)} ${what}` };
}
