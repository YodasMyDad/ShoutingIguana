import { describe, expect, it } from 'vitest';
import type { FacetsPage, Sitemap, SitemapList } from '../../api/grids';
import { COVERAGE_PAGE, TEST_SITEMAP_LIST, testSitemap } from '../../test/sitemapsRobotsSidecar';
import { sentenceText } from '../countSentence';
import { ANSWERED_SITE_PAGES } from '../overview/links';
import {
  COVERAGE_NEEDS,
  answerSentence,
  coverageOf,
  coverageSkippedWords,
  coverageWords,
  foundViaWords,
  notReadReason,
  nothingAtDefaultLocation,
  stateWords,
  tallyOf,
} from './sitemapWords';
import { outcomeWords } from './urlOutcome';

const list = (sitemaps: Sitemap[], discovery: SitemapList['discovery'] = 'done') => ({ discovery, sitemaps });

describe('how a sitemap was found', () => {
  it('has words for each way the crawl comes by one', () => {
    expect(foundViaWords('robotsTxt').short).toBe('robots.txt');
    expect(foundViaWords('options').short).toBe('Crawl settings');
    expect(foundViaWords('defaultLocation').sentence).toContain('/sitemap.xml');
    expect(foundViaWords('index').short).toBe('Sitemap index');
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
    // The engine's own string (SitemapDiscoverer): its "Not read: " isn't said a second time.
    const left = (error: string | null) =>
      stateWords(testSitemap({ id: 1, state: 'notRead', statusCode: null, error }), false).sentence;
    expect(left('Not read: the crawler reads at most 1,000 sitemaps')).toBe(
      'The crawl left it unread at its limits: the crawler reads at most 1,000 sitemaps. There may be more like it.',
    );
    expect(left('Not read: sitemap indexes nested more than 3 deep aren’t followed')).toBe(
      'The crawl left it unread at its limits: sitemap indexes nested more than 3 deep aren’t followed. There may be more like it.',
    );
    // A shared crawl file can hold the prefix with no reason after it, or no error at all.
    for (const bare of ['Not read', 'Not read: ', null]) {
      expect(left(bare)).toBe('The crawl left it unread at its limits. There may be more like it.');
    }
    expect(notReadReason('Not read: the crawler reads at most 1,000 sitemaps')).toBe(
      'the crawler reads at most 1,000 sitemaps',
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
    const only = list([testSitemap({ id: 1, state: 'failed', statusCode: 404, error: 'HTTP 404' })]);
    expect(answerSentence(only, false)).toBe('The crawl found 1 sitemap, which couldn’t be read: HTTP 404.');
  });

  it('never calls nothing at the default location a sitemap that was found and couldn’t be read', () => {
    const probe = (statusCode: number | null, error: string) =>
      testSitemap({ id: 1, foundVia: 'defaultLocation', state: 'failed', statusCode, error });
    // Nothing declared one, the crawl tried /sitemap.xml, and it isn't there: no sitemap, not a failed one.
    for (const status of [404, 410]) {
      const missing = probe(status, `HTTP ${status}`);
      expect(nothingAtDefaultLocation(missing)).toBe(true);
      expect(answerSentence(list([missing]), false)).toBe(
        'Nothing declared a sitemap, and there’s none at /sitemap.xml.',
      );
      expect(stateWords(missing, false)).toMatchObject({
        short: `None there: HTTP ${status}`,
        sentence: `Nothing declared a sitemap, and there’s none at /sitemap.xml. It answered HTTP ${status}.`,
      });
    }
    // Any other answer there leaves it unknown whether there's one: it couldn't be read, and it still wasn't "found".
    const refused = probe(403, 'HTTP 403');
    expect(nothingAtDefaultLocation(refused)).toBe(false);
    expect(answerSentence(list([refused]), false)).toBe(
      'Nothing declared a sitemap, and /sitemap.xml couldn’t be read: HTTP 403.',
    );
    expect(stateWords(refused, false).short).toBe('Couldn’t be read: HTTP 403');
    // A sitemap something declared that answers 404 is a sitemap that couldn't be read.
    const declared = testSitemap({ id: 1, foundVia: 'robotsTxt', state: 'failed', statusCode: 404, error: 'HTTP 404' });
    expect(nothingAtDefaultLocation(declared)).toBe(false);
    expect(stateWords(declared, false).short).toBe('Couldn’t be read: HTTP 404');
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
    const parts = coverage.known ? coverageWords(coverage) : [];
    expect(sentenceText(parts)).toBe('2 of the site’s 9 pages aren’t in any sitemap.');
    // Both counts are links: the pages no sitemap lists, and the site's pages as Overview counts them.
    expect(parts.filter((part) => typeof part !== 'string')).toEqual([
      { text: '2', filters: [...ANSWERED_SITE_PAGES, { column: 'inSitemap', op: 'is', value: false }] },
      { text: '9 pages', filters: ANSWERED_SITE_PAGES },
    ]);
  });

  it('says so when every page is in one, or there are no pages', () => {
    const all: FacetsPage = {
      ...COVERAGE_PAGE,
      facets: { inSitemap: [{ value: true, count: 9, filter: { column: 'inSitemap', op: 'is', value: true } }] },
    };
    const coverage = coverageOf(all);
    expect(coverage.known && sentenceText(coverageWords(coverage))).toBe('All of the site’s 9 pages are in a sitemap.');
    const none = coverageOf({ ...all, matching: 0, facets: { inSitemap: [] } });
    expect(none.known && sentenceText(coverageWords(none))).toBe(
      'The crawl read no pages on this site, so there’s nothing for a sitemap to list.',
    );
  });

  it('agrees in number: one page isn’t, several aren’t', () => {
    const one = coverageOf({
      ...COVERAGE_PAGE,
      facets: { inSitemap: [{ value: false, count: 1, filter: { column: 'inSitemap', op: 'is', value: false } }] },
    });
    expect(one.known && sentenceText(coverageWords(one))).toBe('1 of the site’s 9 pages isn’t in any sitemap.');
    const single = coverageOf({ ...COVERAGE_PAGE, matching: 1, facets: { inSitemap: [] } });
    expect(single.known && sentenceText(coverageWords(single))).toBe('The site’s 1 page is in a sitemap.');
  });

  it('never tells a page the engine couldn’t place that it’s in a sitemap: the analysis skipped the comparison', () => {
    const unplaced = (count: number) => ({
      value: null,
      count,
      filter: { column: 'inSitemap', op: 'isEmpty' } as const,
    });
    // A crawl stopped before its sitemaps were read, then analysed: its one page is in neither bucket.
    const stopped: FacetsPage = { ...COVERAGE_PAGE, matching: 1, facets: { inSitemap: [unplaced(1)] } };
    expect(coverageOf(stopped)).toEqual({ known: false, reason: 'checkSkipped' });
    // One sitemap read and another that failed: the pages the read one lists are placed, the rest aren't.
    const partial: FacetsPage = {
      ...COVERAGE_PAGE,
      matching: 6_172,
      facets: {
        inSitemap: [
          unplaced(1_172),
          { value: true, count: 5_000, filter: { column: 'inSitemap', op: 'is', value: true } },
        ],
      },
    };
    expect(coverageOf(partial)).toEqual({ known: false, reason: 'checkSkipped' });
    // A bucket of none changes nothing: every page was placed.
    const placed = coverageOf({
      ...COVERAGE_PAGE,
      facets: { inSitemap: [unplaced(0), ...(COVERAGE_PAGE.facets.inSitemap ?? [])] },
    });
    expect(placed).toMatchObject({ known: true, missing: 2, pages: 9 });
  });

  it('says the analysis left the count out, with the analysis’s own reason when it gave one', () => {
    expect(coverageSkippedWords('The crawl didn’t finish.')).toBe(
      'The analysis didn’t count the site’s pages no sitemap lists. The crawl didn’t finish.',
    );
    expect(coverageSkippedWords(null)).toBe(
      'The analysis didn’t count the site’s pages no sitemap lists: it couldn’t compare every page with the sitemaps.',
    );
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
