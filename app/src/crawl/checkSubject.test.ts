import { describe, expect, it } from 'vitest';
import {
  affectedCount,
  affectedNoun,
  affectedWords,
  checkReach,
  checkShare,
  partOfAffected,
  shareWhole,
  showInUrlsWords,
  type CrawlCounts,
} from './checkSubject';

const PAGE = { subject: 'page' } as const;
const URL_CHECK = { subject: 'url' } as const;
/** A check as an engine from before E3b, or the design fixtures, gives it. */
const UNSAID = {};

/** An analysis's own totals: the pages it ran on, and the URLs the crawl had crawled when it ran. */
const counts = (pagesAnalyzed: number, crawledUrlCount: number): CrawlCounts => ({ pagesAnalyzed, crawledUrlCount });
const CRAWL = counts(152, 301);
/** The review's crawl as its analysis saw it: 1,661 pages and 9,464 URLs crawled. It was continued afterwards. */
const CONTINUED = counts(1_661, 9_464);

describe('what a check’s count is called', () => {
  it('is pages only where the engine says every URL the check flags is one', () => {
    expect(affectedNoun(PAGE)).toBe('page');
    expect(affectedNoun(URL_CHECK)).toBe('URL');
  });

  it('is URLs for a check that doesn’t say, or that the catalog doesn’t have: a page is a URL, so it’s true', () => {
    expect(affectedNoun(UNSAID)).toBe('URL');
    expect(affectedNoun(undefined)).toBe('URL');
    expect(affectedNoun(null)).toBe('URL');
  });

  it('words every label of a page check in pages', () => {
    expect(affectedWords(PAGE)).toEqual({
      noun: 'page',
      plural: 'pages',
      affected: 'Pages affected',
      heading: 'Affected pages',
      named: 'affected pages',
      share: 'Share of pages',
      column: 'Pages',
      unread: 'The group’s pages couldn’t be read.',
    });
  });

  it('words every label of a URL check, and of one that doesn’t say, in URLs', () => {
    const urls = {
      noun: 'URL',
      plural: 'URLs',
      affected: 'URLs affected',
      heading: 'Affected URLs',
      named: 'affected URLs',
      share: 'Share of URLs',
      column: 'URLs',
      unread: 'The group’s URLs couldn’t be read.',
    };
    expect(affectedWords(URL_CHECK)).toEqual(urls);
    expect(affectedWords(UNSAID)).toEqual(urls);
    expect(affectedWords(undefined)).toEqual(urls);
  });

  it('counts with the right plural, 1 included', () => {
    expect(affectedCount(PAGE, 1)).toBe('1 page');
    expect(affectedCount(PAGE, 4_521)).toBe('4,521 pages');
    expect(affectedCount(URL_CHECK, 1)).toBe('1 URL');
    expect(affectedCount(URL_CHECK, 9)).toBe('9 URLs');
    expect(affectedCount(UNSAID, 3)).toBe('3 URLs');
  });

  it('says a part of them: "264 of 265 pages"', () => {
    expect(partOfAffected(PAGE, 264, 265)).toBe('264 of 265 pages');
    expect(partOfAffected(URL_CHECK, 1_200, 1_300)).toBe('1,200 of 1,300 URLs');
    expect(partOfAffected(PAGE, 1, 1)).toBe('1 of 1 page');
    expect(partOfAffected(UNSAID, 1, 1)).toBe('1 of 1 URL');
  });

  it('words the link to a group’s URLs without saying "URLs in URLs"', () => {
    expect(showInUrlsWords(PAGE, 264)).toBe('Show all 264 pages in URLs');
    expect(showInUrlsWords(PAGE, 1)).toBe('Show the page in URLs');
    expect(showInUrlsWords(URL_CHECK, 1_264)).toBe('Show all 1,264 in URLs');
    expect(showInUrlsWords(URL_CHECK, 1)).toBe('Show it in URLs');
    expect(showInUrlsWords(UNSAID, 5)).toBe('Show all 5 in URLs');
  });
});

describe('what a check’s count is a share of', () => {
  it('is the pages the analysis ran on for a page check', () => {
    expect(checkShare(PAGE, 20, CRAWL)).toEqual({
      label: 'Share of pages',
      value: '13%',
      context: 'of the 152 pages analysed',
    });
  });

  it('is the URLs crawled when the analysis ran for a URL check', () => {
    expect(checkShare(URL_CHECK, 4, CRAWL)).toEqual({
      label: 'Share of URLs',
      value: '1.3%',
      context: 'of the 301 URLs crawled',
    });
  });

  it('is the URLs crawled for a check that doesn’t say what it flags: true whatever that is', () => {
    expect(checkShare(UNSAID, 20, CRAWL)).toEqual({
      label: 'Share of URLs',
      value: '6.6%',
      context: 'of the 301 URLs crawled',
    });
    expect(checkShare(undefined, 20, CRAWL)?.context).toBe('of the 301 URLs crawled');
  });

  it('never takes a URL check’s count as a share of the pages, however few pages there are', () => {
    // 9 broken images and redirects on a crawl of 2 pages.
    const share = checkShare(URL_CHECK, 9, counts(2, 40));
    expect(share).toEqual({ label: 'Share of URLs', value: '23%', context: 'of the 40 URLs crawled' });
  });

  it('is of the analysis’s own totals, whatever the crawl has done since: both are stored with the counts', () => {
    // The review's crawl: analysed at 1,661 pages and 9,464 URLs crawled, then continued to 9,206 pages. A title
    // duplicated on 52 of the 1,661 pages analysed is 3.1%, and 209 URLs of the 9,464 crawled then are 2.2%. Nothing
    // here is asked of the crawl as it is now: there is no such number to give this.
    expect(checkShare(PAGE, 52, CONTINUED)).toEqual({
      label: 'Share of pages',
      value: '3.1%',
      context: 'of the 1,661 pages analysed',
    });
    expect(checkShare(URL_CHECK, 209, CONTINUED)).toEqual({
      label: 'Share of URLs',
      value: '2.2%',
      context: 'of the 9,464 URLs crawled',
    });
    expect(checkShare(UNSAID, 209, CONTINUED)?.context).toBe('of the 9,464 URLs crawled');
  });

  it('says no share of URLs for an analysis that didn’t keep their count: an engine from before the score ran it', () => {
    // Such an analysis sends no count, or null: there is no total its URL counts are known to be a part of.
    expect(checkShare(URL_CHECK, 4, { pagesAnalyzed: 152 })).toBeNull();
    expect(checkShare(URL_CHECK, 4, { pagesAnalyzed: 152, crawledUrlCount: null })).toBeNull();
    expect(checkShare(UNSAID, 4, { pagesAnalyzed: 152, crawledUrlCount: null })).toBeNull();
    // Its pages it did keep: a page check's share stands.
    expect(checkShare(PAGE, 20, { pagesAnalyzed: 152, crawledUrlCount: null })?.context).toBe(
      'of the 152 pages analysed',
    );
  });

  it('says which total a share is of, and that there is none of URLs without their count', () => {
    expect(shareWhole(PAGE, CRAWL)).toBe('pagesAnalysed');
    expect(shareWhole(PAGE, { pagesAnalyzed: 152, crawledUrlCount: null })).toBe('pagesAnalysed');
    expect(shareWhole(URL_CHECK, CRAWL)).toBe('urlsCrawled');
    expect(shareWhole(UNSAID, CRAWL)).toBe('urlsCrawled');
    expect(shareWhole(URL_CHECK, CONTINUED)).toBe('urlsCrawled');
    expect(shareWhole(URL_CHECK, { pagesAnalyzed: 152, crawledUrlCount: null })).toBeNull();
    expect(shareWhole(URL_CHECK, { pagesAnalyzed: 152 })).toBeNull();
  });

  it('words a count of one', () => {
    expect(checkShare(PAGE, 1, counts(1, 5))?.context).toBe('of the 1 page analysed');
    expect(checkShare(URL_CHECK, 1, counts(1, 1))?.context).toBe('of the 1 URL crawled');
  });

  it('keeps six-digit counts whole, with their separators', () => {
    expect(checkShare(PAGE, 312_876, counts(987_654, 1_234_567))).toEqual({
      label: 'Share of pages',
      value: '32%',
      context: 'of the 987,654 pages analysed',
    });
    expect(checkShare(URL_CHECK, 312_876, counts(987_654, 1_234_567))?.context).toBe('of the 1,234,567 URLs crawled');
  });

  it('says none where it isn’t known to be true', () => {
    // A count past what it would be a part of: never "100%" or more.
    expect(checkShare(PAGE, 153, CRAWL)).toBeNull();
    expect(checkShare(URL_CHECK, 302, CRAWL)).toBeNull();
    // Nothing to be a part of.
    expect(checkShare(PAGE, 0, counts(0, 0))).toBeNull();
    expect(checkShare(URL_CHECK, 0, counts(0, 0))).toBeNull();
    // A skipped check has no count.
    expect(checkShare(PAGE, null, CRAWL)).toBeNull();
    // No analysis to take it from.
    expect(checkShare(PAGE, 20, null)).toBeNull();
    expect(checkShare(URL_CHECK, 4, null)).toBeNull();
  });

  it('is a whole when the check flags all of them, and never rounds a part up to it', () => {
    expect(checkShare(PAGE, 152, CRAWL)?.value).toBe('100%');
    expect(checkShare(PAGE, 151, CRAWL)?.value).toBe('99.3%');
    expect(checkShare(URL_CHECK, 301, CRAWL)?.value).toBe('100%');
  });
});

describe('how much of the site a check’s count is ("% of Total")', () => {
  it('is its share, of the pages analysed for a page check and of the URLs crawled for any other, said whole', () => {
    expect(checkReach(PAGE, 15, CRAWL)).toEqual({
      label: 'Share of pages',
      value: '9.9%',
      context: 'of the 152 pages analysed',
      words: '9.9% of the 152 pages analysed',
    });
    expect(checkReach(URL_CHECK, 3, CRAWL)?.words).toBe('1% of the 301 URLs crawled');
  });

  /** The closing audit's gap 28: a site-wide fault read "1 page, <0.1%" in Advanced, where Simple says the whole site. */
  it('is the whole site for a fault of the whole site, whatever its count', () => {
    expect(checkReach({ subject: 'page', siteWide: true, reportedOnce: true }, 1, counts(101_106, 110_251))).toEqual({
      label: 'Reach',
      value: 'Whole site',
      context: 'A fault of the whole site, whatever its count',
      words: 'the whole site',
    });
    // Reported once for the whole site (or once for each of its other versions), but no fault of the whole site: not
    // a share of its URLs either, as Simple says it (round 5 review R1-9: sitemap-error read "1 URL, <0.1%").
    for (const count of [1, 3]) {
      expect(checkReach({ subject: 'url', reportedOnce: true }, count, CRAWL)).toEqual({
        label: 'Reach',
        value: 'Whole site',
        context: 'Checked once for the whole site, not URL by URL',
        words: 'checked once for the whole site',
      });
    }
  });

  it('is nothing for a check that found nothing, or was skipped', () => {
    expect(checkReach(PAGE, 0, CRAWL)).toBeNull();
    expect(checkReach({ siteWide: true }, null, CRAWL)).toBeNull();
  });
});
