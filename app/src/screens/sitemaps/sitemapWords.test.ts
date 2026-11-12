import { describe, expect, it } from 'vitest';
import type { FacetsPage, Sitemap, SitemapList, UrlOutcome } from '../../api/grids';
import { OUTCOME_WORDS } from '../../inspector/words';
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
  indexBadgeWords,
  indexHoldsWords,
  notReadReason,
  nothingAtDefaultLocation,
  sitemapFailure,
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
    const words = stateWords(testSitemap({ id: 1, state: 'failed', error: '404 Not Found' }), false);
    expect(words).toMatchObject({ tone: 'failed', short: 'Couldn’t be read: 404 Not Found' });
  });

  it('says why one failed in plain words, by its kind, and keeps the crawl’s own text out of the sentence', () => {
    const failed = (error: string) =>
      stateWords(testSitemap({ id: 1, state: 'failed', statusCode: null, error }), false);
    // An XML parser's message: the line it stopped at is what a person can use.
    expect(failed('Unexpected end of file has occurred. Line 17, position 5.')).toMatchObject({
      short: 'Couldn’t be read: its XML is broken at line 17',
      sentence: 'The crawl couldn’t read it: its XML is broken at line 17.',
      recorded: 'Unexpected end of file has occurred. Line 17, position 5.',
    });
    // The line the parser stopped at is the one it names last, far down a long file.
    const mismatch =
      "The 'url' start tag on line 16 position 4 does not match the end tag of 'urlset'. Line 1217, position 3.";
    expect(failed(mismatch).short).toBe('Couldn’t be read: its XML is broken at line 1,217');
    expect(failed('No such host is known. (www.example.com:443)')).toMatchObject({
      short: 'Couldn’t be read: the DNS name wasn’t found',
      recorded: 'No such host is known. (www.example.com:443)',
    });
    expect(failed('The SSL connection could not be established, see inner exception.').short).toBe(
      'Couldn’t be read: a secure connection couldn’t be made',
    );
    // Words it has none for: no reason is made up, and what the crawl recorded is kept for behind the words.
    expect(failed('The archive entry was compressed using an unsupported compression method.')).toMatchObject({
      short: 'Couldn’t be read',
      sentence: 'The crawl couldn’t read it.',
      recorded: 'The archive entry was compressed using an unsupported compression method.',
    });
    // A status is said as it is, with nothing behind it.
    expect(failed('503 Service Unavailable')).toMatchObject({
      short: 'Couldn’t be read: 503 Service Unavailable',
      recorded: null,
    });
    expect(sitemapFailure('Timed out')).toEqual({ kind: 'timeout', words: 'it timed out', recorded: null });
    // The answer over the list takes the same plain words.
    const only = testSitemap({ id: 1, state: 'failed', statusCode: null, error: 'No response within 30 seconds' });
    expect(answerSentence(list([only]), false)).toBe(
      'The crawl found 1 sitemap, which couldn’t be read: it timed out.',
    );
  });

  it('says a sitemap that failed part-way was read in part, and that what was read is listed', () => {
    const partial = testSitemap({ id: 1, state: 'failed', error: 'Timed out', entries: 40 });
    const words = stateWords(partial, false);
    expect(words.short).toBe('Read in part: it timed out');
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

  it('says of an index that was read how far the crawl got with its sitemaps, never that it read them all', () => {
    const index = testSitemap({ id: 1, isIndex: true, sitemaps: 3 });
    // Read whole (or nothing said of its sitemaps): as it was.
    expect(stateWords(index, false).sentence).toBe('The crawl read this index and the sitemaps it lists.');
    expect(stateWords(index, false, { sitemaps: 3, read: 3, more: false }).short).toBe('Read');
    // The crawl has them all, and couldn't read one.
    expect(stateWords(index, false, { sitemaps: 3, read: 2, more: false })).toMatchObject({
      short: 'Read, 2 of its 3 sitemaps',
      sentence:
        'The crawl read this index, and 2 of the 3 sitemaps it lists. What the other lists isn’t known, so the index has no counts of its own.',
    });
    expect(stateWords(index, false, { sitemaps: 12, read: 0, more: false }).sentence).toMatch(
      /^The crawl read this index, and none of the 12 sitemaps it lists\. What the others list isn’t known/,
    );
    // A crawl that stopped first: it has some of them, and can't say how many there are.
    expect(stateWords(index, false, { sitemaps: 1, read: 0, more: true })).toMatchObject({
      short: 'Read, has 1 of its sitemaps',
      sentence:
        'The crawl read this index, and has 1 of the sitemaps it lists, none of them read: it didn’t get to them all. What the index lists in all isn’t known, so it has no counts of its own.',
    });
    expect(stateWords(index, false, { sitemaps: 0, read: 0, more: true }).short).toBe('Read, none of its sitemaps');
    // A crawl still at it: so far, and its counts are on their way.
    expect(stateWords(index, true, { sitemaps: 4, read: 3, more: true })).toMatchObject({
      short: 'Read, 4 sitemaps so far',
      sentence:
        'The crawl read this index, and has 4 of the sitemaps it lists so far, 3 of them read. Its counts come once it has read them all.',
    });
    expect(stateWords(index, true, { sitemaps: 0, read: 0, more: true }).short).toBe('Read, its sitemaps to come');
    for (const subtree of [
      { sitemaps: 3, read: 2, more: false },
      { sitemaps: 1, read: 0, more: true },
    ]) {
      expect(stateWords(index, false, subtree).sentence).not.toMatch(/and the sitemaps it lists\./);
    }
    // Its badge counts its sitemaps once the crawl has them all, where there's room for the count.
    expect(indexBadgeWords({ sitemaps: 12 }, { sitemaps: 12, read: 11, more: false }, true)).toBe('Index of 12');
    expect(indexBadgeWords({ sitemaps: 1_204 }, { sitemaps: 1_204, read: 1_204, more: false }, true)).toBe(
      'Index of 1,204',
    );
    expect(indexBadgeWords({ sitemaps: 12 }, { sitemaps: 12, read: 12, more: false }, false)).toBe('Index');
    expect(indexBadgeWords({ sitemaps: 1 }, { sitemaps: 1, read: 0, more: true }, true)).toBe('Index');
    // What its file holds: its sitemaps, as far as the crawl has them.
    expect(indexHoldsWords({ sitemaps: 3 }, { sitemaps: 3, read: 2, more: false })).toBe('3 sitemaps');
    expect(indexHoldsWords({ sitemaps: 1 }, { sitemaps: 1, read: 0, more: true })).toBe(
      'sitemaps: the crawl has 1 of them',
    );
    expect(indexHoldsWords({ sitemaps: 0 }, { sitemaps: 0, read: 0, more: true })).toBe(
      'sitemaps, none of which the crawl has',
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
    const only = list([testSitemap({ id: 1, state: 'failed', statusCode: 404, error: '404 Not Found' })]);
    expect(answerSentence(only, false)).toBe('The crawl found 1 sitemap, which couldn’t be read: 404 Not Found.');
  });

  it('never calls nothing at the default location a sitemap that was found and couldn’t be read', () => {
    const probe = (statusCode: number | null, error: string) =>
      testSitemap({ id: 1, foundVia: 'defaultLocation', state: 'failed', statusCode, error });
    // Nothing declared one, the crawl tried /sitemap.xml, and it isn't there: no sitemap, not a failed one.
    for (const [status, named] of [
      [404, '404 Not Found'],
      [410, '410 Gone'],
    ] as const) {
      const missing = probe(status, named);
      expect(nothingAtDefaultLocation(missing)).toBe(true);
      expect(answerSentence(list([missing]), false)).toBe(
        'Nothing declared a sitemap, and there’s none at /sitemap.xml.',
      );
      expect(stateWords(missing, false)).toMatchObject({
        short: `None there: ${named}`,
        sentence: `Nothing declared a sitemap, and there’s none at /sitemap.xml. It answered ${named}.`,
      });
    }
    // Any other answer there leaves it unknown whether there's one: it couldn't be read, and it still wasn't "found".
    const refused = probe(403, '403 Forbidden');
    expect(nothingAtDefaultLocation(refused)).toBe(false);
    expect(answerSentence(list([refused]), false)).toBe(
      'Nothing declared a sitemap, and /sitemap.xml couldn’t be read: 403 Forbidden.',
    );
    expect(stateWords(refused, false).short).toBe('Couldn’t be read: 403 Forbidden');
    // A sitemap something declared that answers 404 is a sitemap that couldn't be read.
    const declared = testSitemap({
      id: 1,
      foundVia: 'robotsTxt',
      state: 'failed',
      statusCode: 404,
      error: '404 Not Found',
    });
    expect(nothingAtDefaultLocation(declared)).toBe(false);
    expect(stateWords(declared, false).short).toBe('Couldn’t be read: 404 Not Found');
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
  it('goes by the outcome, not by whether it was fetched, in the inspector’s words', () => {
    // A URL that answered says "Answered": "Crawled" is no outcome, it's that the crawl requested the URL.
    expect(outcomeWords({ fetched: true, outcome: 'success' }, false)).toBe('Answered');
    expect(outcomeWords({ fetched: true, outcome: 'timeout' }, false)).toBe('Timed out');
    for (const outcome of Object.keys(OUTCOME_WORDS) as (keyof typeof OUTCOME_WORDS)[]) {
      expect(outcomeWords({ fetched: true, outcome }, false)).toBe(OUTCOME_WORDS[outcome]);
      expect(outcomeWords({ fetched: true, outcome }, false)).not.toBe('Crawled');
    }
    expect(outcomeWords({ fetched: true, outcome: 'robotsDisallowed' }, false)).toBe('Blocked by robots.txt');
    expect(outcomeWords({ fetched: false, outcome: 'notChecked' }, false)).toBe('Not checked');
    expect(outcomeWords({ fetched: true, outcome: 'redirect' }, false)).toBe('Redirect');
  });

  it('says no more than is known of a URL without an outcome while the crawl runs', () => {
    expect(outcomeWords({ fetched: false, outcome: null }, true)).toBe('Not crawled so far');
    expect(outcomeWords({ fetched: false, outcome: null }, false)).toBe('Not crawled');
    expect(outcomeWords({}, false)).toBe('Not crawled');
  });

  it('doesn’t call a URL crawled when it’s done with and how isn’t said, or is said in a word this version lacks', () => {
    // It may have been requested or refused: no more is said than is known.
    expect(outcomeWords({ fetched: true, outcome: null }, false)).toBe('Unknown');
    expect(outcomeWords({ fetched: true, outcome: 'somethingNew' as UrlOutcome }, false)).toBe('Unknown');
  });
});
