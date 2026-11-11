import { describe, expect, it } from 'vitest';
import {
  affectedCount,
  affectedNoun,
  affectedWords,
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

/** A crawl as its analysis saw it: the analysis ran on 152 pages, and the crawl has 301 URLs. */
const counts = (pagesAnalyzed: number, urlsFound: number | null, more: Partial<CrawlCounts> = {}): CrawlCounts => ({
  analysis: { pagesAnalyzed, outOfDateReasons: [] },
  crawling: false,
  urlsFound,
  ...more,
});
const CRAWL = counts(152, 301);
/** The review's crawl: analysed at 1,661 pages and 40,826 URLs, then continued to 9,206 pages and 41,629 URLs. */
const CONTINUED = counts(1_661, 41_629, { analysis: { pagesAnalyzed: 1_661, outOfDateReasons: ['crawlChanged'] } });

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

  it('is the URLs found for a URL check, which can flag a URL that was never crawled', () => {
    expect(checkShare(URL_CHECK, 4, CRAWL)).toEqual({
      label: 'Share of URLs',
      value: '1.3%',
      context: 'of the 301 URLs found',
    });
  });

  it('is the URLs found for a check that doesn’t say what it flags: true whatever that is', () => {
    expect(checkShare(UNSAID, 20, CRAWL)).toEqual({
      label: 'Share of URLs',
      value: '6.6%',
      context: 'of the 301 URLs found',
    });
    expect(checkShare(undefined, 20, CRAWL)?.context).toBe('of the 301 URLs found');
  });

  it('never takes a URL check’s count as a share of the pages, however few pages there are', () => {
    // 9 broken images and redirects on a crawl of 2 pages.
    const share = checkShare(URL_CHECK, 9, counts(2, 40));
    expect(share).toEqual({ label: 'Share of URLs', value: '23%', context: 'of the 40 URLs found' });
  });

  it('is of the analysis’s own pages after the crawl was continued, never of the pages the file has now', () => {
    // Title duplicated on 52 of the 1,661 pages analysed: 3.1%, whatever the crawl has read since (9,206 pages now).
    expect(checkShare(PAGE, 52, CONTINUED)).toEqual({
      label: 'Share of pages',
      value: '3.1%',
      context: 'of the 1,661 pages analysed',
    });
    // An analysis another engine made, or other settings: the same pages, the same share.
    const older = counts(1_661, 40_826, {
      analysis: { pagesAnalyzed: 1_661, outOfDateReasons: ['engineChanged', 'settingsChanged'] },
    });
    expect(checkShare(PAGE, 52, older)?.context).toBe('of the 1,661 pages analysed');
    // And while it is being continued: both numbers are the analysis's.
    expect(checkShare(PAGE, 52, { ...CONTINUED, crawling: true })?.value).toBe('3.1%');
  });

  it('says no share of the URLs found once the crawl has moved on from its analysis', () => {
    // 3,451 of the 40,826 URLs the analysis saw; the file has 41,629 now, and nothing stored the 40,826.
    expect(checkShare(URL_CHECK, 3_451, CONTINUED)).toBeNull();
    expect(checkShare(UNSAID, 3_451, CONTINUED)).toBeNull();
    // Nor while the crawl runs, before the analysis is known to be out of date: its URLs are moving.
    expect(checkShare(URL_CHECK, 4, { ...CRAWL, crawling: true })).toBeNull();
    // Nor a dash that would never be filled.
    expect(checkShare(URL_CHECK, 4, { ...CONTINUED, urlsFound: null })).toBeNull();
    // The file is as the analysis saw it when only the engine or the settings changed.
    const older = counts(1_661, 40_826, {
      analysis: { pagesAnalyzed: 1_661, outOfDateReasons: ['engineChanged', 'settingsChanged'] },
    });
    expect(checkShare(URL_CHECK, 3_451, older)?.context).toBe('of the 40,826 URLs found');
  });

  it('says which count a share needs, so the URLs found are read only when they will be said', () => {
    expect(shareWhole(PAGE, CRAWL)).toBe('pagesAnalysed');
    expect(shareWhole(PAGE, CONTINUED)).toBe('pagesAnalysed');
    expect(shareWhole(URL_CHECK, CRAWL)).toBe('urlsFound');
    expect(shareWhole(UNSAID, CRAWL)).toBe('urlsFound');
    expect(shareWhole(URL_CHECK, CONTINUED)).toBeNull();
    expect(shareWhole(URL_CHECK, { ...CRAWL, crawling: true })).toBeNull();
  });

  it('words a count of one', () => {
    expect(checkShare(PAGE, 1, counts(1, 5))?.context).toBe('of the 1 page analysed');
    expect(checkShare(URL_CHECK, 1, counts(1, 1))?.context).toBe('of the 1 URL found');
  });

  it('keeps six-digit counts whole, with their separators', () => {
    expect(checkShare(PAGE, 312_876, counts(987_654, 1_234_567))).toEqual({
      label: 'Share of pages',
      value: '32%',
      context: 'of the 987,654 pages analysed',
    });
    expect(checkShare(URL_CHECK, 312_876, counts(987_654, 1_234_567))?.context).toBe('of the 1,234,567 URLs found');
  });

  it('keeps its place with a dash until the URLs found are read, saying what it will be of', () => {
    expect(checkShare(URL_CHECK, 4, counts(152, null))).toEqual({
      label: 'Share of URLs',
      value: '–',
      context: 'of the URLs found',
    });
    // The pages analysed come with the analysis: a page check never waits for the crawl's count.
    expect(checkShare(PAGE, 20, counts(152, null))?.value).toBe('13%');
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
