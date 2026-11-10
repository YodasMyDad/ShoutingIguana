import { describe, expect, it } from 'vitest';
import {
  affectedCount,
  affectedNoun,
  affectedWords,
  checkShare,
  partOfAffected,
  showInUrlsWords,
  type CrawlCounts,
} from './checkSubject';

const PAGE = { subject: 'page' } as const;
const URL_CHECK = { subject: 'url' } as const;
/** A check as an engine from before E3b, or the design fixtures, gives it. */
const UNSAID = {};

/** The synthetic crawl's own counts: 153 pages crawled, 301 URLs found. */
const CRAWL: CrawlCounts = { pages: 153, urls: 301 };

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
  it('is the pages crawled for a page check', () => {
    expect(checkShare(PAGE, 20, CRAWL)).toEqual({
      label: 'Share of pages',
      value: '13%',
      context: 'of the 153 pages crawled',
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
    const share = checkShare(URL_CHECK, 9, { pages: 2, urls: 40 });
    expect(share).toEqual({ label: 'Share of URLs', value: '23%', context: 'of the 40 URLs found' });
  });

  it('words a count of one', () => {
    expect(checkShare(PAGE, 1, { pages: 1, urls: 5 })?.context).toBe('of the 1 page crawled');
    expect(checkShare(URL_CHECK, 1, { pages: 1, urls: 1 })?.context).toBe('of the 1 URL found');
  });

  it('keeps six-digit counts whole, with their separators', () => {
    expect(checkShare(PAGE, 312_876, { pages: 987_654, urls: 1_234_567 })).toEqual({
      label: 'Share of pages',
      value: '32%',
      context: 'of the 987,654 pages crawled',
    });
    expect(checkShare(URL_CHECK, 312_876, { pages: 987_654, urls: 1_234_567 })?.context).toBe(
      'of the 1,234,567 URLs found',
    );
  });

  it('keeps its place with a dash until the crawl’s count is read, saying what it will be of', () => {
    expect(checkShare(PAGE, 20, { pages: null, urls: 301 })).toEqual({
      label: 'Share of pages',
      value: '–',
      context: 'of the pages crawled',
    });
    expect(checkShare(URL_CHECK, 4, { pages: 153, urls: null })).toEqual({
      label: 'Share of URLs',
      value: '–',
      context: 'of the URLs found',
    });
  });

  it('says none where it isn’t known to be true', () => {
    // A count past what it would be a part of (an analysis older than the crawl's file): never "100%" or more.
    expect(checkShare(PAGE, 154, CRAWL)).toBeNull();
    expect(checkShare(URL_CHECK, 302, CRAWL)).toBeNull();
    // Nothing to be a part of.
    expect(checkShare(PAGE, 0, { pages: 0, urls: 0 })).toBeNull();
    // A skipped check has no count.
    expect(checkShare(PAGE, null, CRAWL)).toBeNull();
    // The crawl is running: its counts are moving, and the analysis is of what it was.
    expect(checkShare(PAGE, 20, null)).toBeNull();
    expect(checkShare(URL_CHECK, 4, null)).toBeNull();
  });

  it('is a whole when the check flags all of them, and never rounds a part up to it', () => {
    expect(checkShare(PAGE, 153, CRAWL)?.value).toBe('100%');
    expect(checkShare(PAGE, 152, CRAWL)?.value).toBe('99.3%');
    expect(checkShare(URL_CHECK, 301, CRAWL)?.value).toBe('100%');
  });
});
