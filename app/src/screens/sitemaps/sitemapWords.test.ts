import { describe, expect, it } from 'vitest';
import type { FacetsPage, Sitemap, SitemapList } from '../../api/grids';
import { COVERAGE_PAGE, TEST_SITEMAP_LIST, testSitemap } from '../../test/sitemapsRobotsSidecar';
import { ANSWERED_SITE_PAGES } from '../overview/links';
import {
  COVERAGE_NEEDS,
  answerSentence,
  coverageOf,
  coverageWords,
  foundViaWords,
  stateWords,
  tallyOf,
} from './sitemapWords';
import { outcomeWords } from './urlOutcome';

const list = (sitemaps: Sitemap[], discovery: SitemapList['discovery'] = 'done') => ({ discovery, sitemaps });

describe('how a sitemap was found', () => {
  it('has words for each way the crawl comes by one', () => {
    expect(foundViaWords('robotsTxt').short).toBe('robots.txt');
    expect(foundViaWords('options').short).toBe('The crawl’s settings');
    expect(foundViaWords('defaultLocation').sentence).toContain('/sitemap.xml');
    expect(foundViaWords('index').short).toBe('A sitemap index');
  });

  it('says plainly when the crawl can’t say how it found it', () => {
    const words = foundViaWords(null);
    expect(words.short).toBe('Not known');
    expect(words.sentence).toMatch(/can’t say how it found this sitemap/);
  });
});

describe('what became of a sitemap', () => {
  it('says a failed one couldn’t be read, and why in the engine’s words', () => {
    const words = stateWords(testSitemap({ id: 1, state: 'failed', error: 'HTTP 404' }), false);
    expect(words).toMatchObject({ tone: 'failed', short: 'Couldn’t be read: HTTP 404' });
  });

  it('says a sitemap that failed part-way was read in part, and that what was read is listed', () => {
    const partial = testSitemap({ id: 1, state: 'failed', error: 'Timed out', entries: 40 });
    const words = stateWords(partial, false);
    expect(words.short).toBe('Read in part: Timed out');
    expect(words.sentence).toContain('The URLs it read before that are listed');
  });

  it('tells a sitemap still to be read from one a stopped crawl never read', () => {
    const pending = testSitemap({ id: 1, state: 'pending' });
    expect(stateWords(pending, true).short).toBe('Not read yet');
    expect(stateWords(pending, false).short).toBe('Never read');
    expect(stateWords(pending, false).sentence).toBe('The crawl stopped before it read this sitemap.');
  });

  it('says one left unread at the crawler’s limits may not be the only one', () => {
    const words = stateWords(testSitemap({ id: 1, state: 'notRead', error: 'more than 1,000 sitemaps' }), false);
    expect(words.sentence).toBe(
      'The crawl left it unread at its limits: more than 1,000 sitemaps. There may be more like it.',
    );
  });

  it('never fails on a state this version doesn’t know', () => {
    const unknown = { ...testSitemap({ id: 1 }), state: null as unknown as Sitemap['state'] };
    expect(stateWords(unknown, false).short).toBe('Unknown');
  });
});

describe('the answer in a sentence', () => {
  it('counts the sitemaps by what became of them', () => {
    expect(tallyOf(TEST_SITEMAP_LIST.sitemaps)).toEqual({ total: 6, read: 5, failed: 1, notRead: 0, pending: 0 });
    expect(answerSentence(TEST_SITEMAP_LIST, false)).toBe(
      'The crawl found 6 sitemaps: 5 were read and 1 couldn’t be read.',
    );
  });

  it('is short when every one was read', () => {
    expect(answerSentence(list([testSitemap({ id: 1 }), testSitemap({ id: 2 })]), false)).toBe(
      'The crawl read 2 sitemaps.',
    );
    expect(answerSentence(list([testSitemap({ id: 1 })]), false)).toBe('The crawl read 1 sitemap.');
  });

  it('says of a site’s one sitemap that it couldn’t be read, and why', () => {
    const only = list([testSitemap({ id: 1, foundVia: 'defaultLocation', state: 'failed', error: 'HTTP 404' })]);
    expect(answerSentence(only, false)).toBe('The crawl found 1 sitemap, which couldn’t be read: HTTP 404.');
  });

  it('has a sentence for no sitemap at all, by how far the crawl got', () => {
    expect(answerSentence(list([], 'done'), false)).toBe('The crawl found no sitemap.');
    expect(answerSentence(list([], 'off'), false)).toBe('This crawl was told not to read sitemaps.');
    expect(answerSentence(list([], 'running'), true)).toBe('The crawl hasn’t found a sitemap yet.');
    expect(answerSentence(list([], 'unfinished'), false)).toBe('The crawl stopped before it found a sitemap.');
  });

  it('says so when the list isn’t all there is', () => {
    const some = [testSitemap({ id: 1 }), testSitemap({ id: 2, state: 'pending' })];
    expect(answerSentence(list(some, 'running'), true)).toBe(
      'The crawl found 2 sitemaps: 1 was read and 1 is still to be read. It’s still looking for more.',
    );
    expect(answerSentence(list(some, 'unfinished'), false)).toBe(
      'The crawl found 2 sitemaps: 1 was read and 1 was never read. It stopped before it had read them all, so there may be more.',
    );
  });

  it('uses thousands separators', () => {
    const many = Array.from({ length: 1_200 }, (_, index) => testSitemap({ id: index + 1 }));
    expect(answerSentence(list(many), false)).toBe('The crawl read 1,200 sitemaps.');
  });
});

describe('the site’s pages no sitemap lists', () => {
  it('counts them from the facets, with the filters that open exactly them', () => {
    const coverage = coverageOf(COVERAGE_PAGE);
    expect(coverage).toEqual({
      known: true,
      missing: 2,
      pages: 9,
      filters: [...ANSWERED_SITE_PAGES, { column: 'inSitemap', op: 'is', value: false }],
    });
    expect(coverage.known && coverageWords(coverage)).toEqual({
      count: '2',
      rest: ' of the site’s 9 pages aren’t in any sitemap.',
    });
  });

  it('says so when every page is in one, or there are no pages', () => {
    const all: FacetsPage = {
      ...COVERAGE_PAGE,
      facets: { inSitemap: [{ value: true, count: 9, filter: { column: 'inSitemap', op: 'is', value: true } }] },
    };
    const coverage = coverageOf(all);
    expect(coverage.known && coverageWords(coverage)).toBe('All of the site’s 9 pages are in a sitemap.');
    const none = coverageOf({ ...all, matching: 0, facets: { inSitemap: [] } });
    expect(none.known && coverageWords(none)).toBe(
      'The crawl read no pages on this site, so there’s nothing for a sitemap to list.',
    );
  });

  it('agrees in number: one page isn’t, several aren’t', () => {
    const one = coverageOf({
      ...COVERAGE_PAGE,
      facets: { inSitemap: [{ value: false, count: 1, filter: { column: 'inSitemap', op: 'is', value: false } }] },
    });
    expect(one.known && coverageWords(one)).toEqual({
      count: '1',
      rest: ' of the site’s 9 pages isn’t in any sitemap.',
    });
  });

  it('says what it needs when the analysis that counts it isn’t there: never 0', () => {
    const without: FacetsPage = {
      ...COVERAGE_PAGE,
      summary: { available: false, reason: 'notAnalyzed' },
      unavailable: ['inSitemap'],
      facets: {},
    };
    expect(coverageOf(without)).toEqual({ known: false, reason: 'notAnalyzed' });
    expect(COVERAGE_NEEDS.notAnalyzed).toMatch(/hasn’t been analysed yet/);
    expect(COVERAGE_NEEDS.crawling).toMatch(/once the crawl has finished/);
  });
});

describe('what the crawl did with a listed URL', () => {
  it('goes by the outcome, not by whether it was fetched', () => {
    expect(outcomeWords({ fetched: true, outcome: 'success' }, false)).toBe('Crawled');
    expect(outcomeWords({ fetched: true, outcome: 'robotsDisallowed' }, false)).toBe('Blocked by robots.txt');
    expect(outcomeWords({ fetched: false, outcome: 'notChecked' }, false)).toBe('Not checked');
    expect(outcomeWords({ fetched: true, outcome: 'redirect' }, false)).toBe('Redirect');
  });

  it('says no more than is known of a URL without an outcome while the crawl runs', () => {
    expect(outcomeWords({ fetched: false, outcome: null }, true)).toBe('Not crawled so far');
    expect(outcomeWords({ fetched: false, outcome: null }, false)).toBe('Not crawled');
    expect(outcomeWords({}, false)).toBe('Not crawled');
  });

  it('reads an outcome this version has no name for as crawled', () => {
    expect(outcomeWords({ fetched: true, outcome: null }, false)).toBe('Crawled');
  });
});
