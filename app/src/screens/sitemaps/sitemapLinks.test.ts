import { describe, expect, it } from 'vitest';
import type { GridQuery } from '../../api/grids';
import { TEST_SITEMAPS, sitemapUrlsPage } from '../../test/sitemapsRobotsSidecar';
import {
  COUNT_FILTERS,
  QUICK_FILTERS,
  QUICK_FILTER_WORDS,
  REDIRECTING_OR_BROKEN,
  type SitemapCount,
  countOf,
  quickFilterCount,
  quickFilterFilters,
  quickFilterOf,
} from './sitemapLinks';

const query = (sitemapId: number, filters: GridQuery['filters']) => ({ sitemapId, filters, sort: [], limit: 100 });
const COUNTS = Object.keys(COUNT_FILTERS) as SitemapCount[];

describe('a sitemap’s counts', () => {
  it('counts as crawled what the crawl requested: what it’s done with, less what robots.txt refused', () => {
    // Listed 10: 7 the crawl is done with, of which robots.txt refused 4 (never requested), and 3 it didn't get to.
    const urls = { listed: 10, fetched: 7, indexable: 5, nonIndexable: 2, robotsDisallowed: 4 };
    expect(COUNTS).toEqual(['listed', 'crawled', 'blocked', 'notCrawled', 'indexable', 'nonIndexable']);
    expect(COUNTS.map((count) => countOf({ urls }, count))).toEqual([10, 3, 4, 3, 5, 2]);
    // Listed is the three after it, added up.
    const of = (count: SitemapCount) => countOf({ urls }, count) ?? Number.NaN;
    expect(of('crawled') + of('blocked') + of('notCrawled')).toBe(of('listed'));
  });

  it('never calls a URL robots.txt refused crawled: ten refused are none crawled, ten blocked', () => {
    // The engine is done with all ten (`fetched`), and requested none of them.
    const refused = { urls: { listed: 10, fetched: 10, indexable: 0, nonIndexable: 10, robotsDisallowed: 10 } };
    expect(countOf(refused, 'crawled')).toBe(0);
    expect(countOf(refused, 'blocked')).toBe(10);
    expect(countOf(refused, 'notCrawled')).toBe(0);
    // A URL that was requested and didn't answer is crawled: it's done with, and nothing refused it.
    const unanswered = { urls: { listed: 2, fetched: 2, indexable: 0, nonIndexable: 2, robotsDisallowed: 0 } };
    expect(countOf(unanswered, 'crawled')).toBe(2);
  });

  it('doesn’t know how many were crawled or blocked from an engine that doesn’t count the refused: never `fetched`', () => {
    const urls = { listed: 10, fetched: 7, indexable: 5, nonIndexable: 2 };
    expect(COUNTS.map((count) => countOf({ urls }, count))).toEqual([10, null, null, 3, 5, 2]);
  });

  it('never counts fewer than none not crawled, whatever a running crawl says of the two a moment apart', () => {
    expect(countOf({ urls: { listed: 3, fetched: 5, indexable: 0, nonIndexable: 0 } }, 'notCrawled')).toBe(0);
  });

  it('opens exactly the URLs each count counts: every count of every sitemap lands on itself', () => {
    for (const sitemap of TEST_SITEMAPS) {
      for (const count of COUNTS) {
        const page = sitemapUrlsPage(query(sitemap.id, [...COUNT_FILTERS[count]]));
        expect(page.matching, `${sitemap.url} ${count}`).toBe(countOf(sitemap, count));
      }
    }
  });

  it('selects the crawled as what the crawl is done with less what robots.txt refused, and the blocked as those', () => {
    expect(COUNT_FILTERS).toEqual({
      listed: [],
      crawled: [
        { column: 'fetched', op: 'is', value: true },
        { column: 'outcome', op: 'notIn', values: ['robotsDisallowed'] },
      ],
      blocked: [{ column: 'outcome', op: 'in', values: ['robotsDisallowed'] }],
      notCrawled: [{ column: 'fetched', op: 'is', value: false }],
      indexable: [{ column: 'indexable', op: 'is', value: true }],
      nonIndexable: [{ column: 'indexable', op: 'is', value: false }],
    });
  });
});

describe('the quick filters', () => {
  it('finds the redirecting or broken by their outcome: not the URLs robots.txt refused, nor those not checked', () => {
    expect(REDIRECTING_OR_BROKEN).toEqual([
      { column: 'outcome', op: 'in', values: ['redirect', 'clientError', 'serverError', 'timeout', 'networkError'] },
    ]);
    // The index lists a redirect and a 404.
    expect(sitemapUrlsPage(query(1, [...REDIRECTING_OR_BROKEN])).rows.map((row) => row.url)).toEqual([
      'https://example.com/old',
      'https://example.com/p/gone',
    ]);
  });

  it('knows which quick filter the grid’s filters are, and none for anything else', () => {
    for (const quick of QUICK_FILTERS) expect(quickFilterOf(quickFilterFilters(quick))).toBe(quick);
    expect(quickFilterOf([])).toBe('listed');
    expect(quickFilterOf([{ column: 'fetched', op: 'is', value: true }])).toBeNull();
    expect(quickFilterOf([...quickFilterFilters('notCrawled'), { column: 'depth', op: 'eq', value: 2 }])).toBeNull();
  });

  it('is every count of the list’s row, in its order, then the redirecting or broken', () => {
    expect(QUICK_FILTERS).toEqual(['listed', 'crawled', 'blocked', 'notCrawled', 'nonIndexable', 'broken']);
    expect(QUICK_FILTERS.map((quick) => QUICK_FILTER_WORDS[quick])).toEqual([
      'All',
      'Crawled',
      'Blocked by robots.txt',
      'Not crawled',
      'Non-indexable',
      'Redirecting or broken',
    ]);
  });

  it('carries the count the sitemap has for it, and none for those the engine doesn’t count for the list', () => {
    const counted = {
      urls: { listed: 10, fetched: 7, indexable: 5, nonIndexable: 2, robotsDisallowed: 4, redirectingOrBroken: 1 },
    };
    expect(QUICK_FILTERS.map((quick) => quickFilterCount(counted, quick))).toEqual([10, 3, 4, 3, 2, 1]);
    const before = { urls: { listed: 10, fetched: 7, indexable: 5, nonIndexable: 2 } };
    expect(QUICK_FILTERS.map((quick) => quickFilterCount(before, quick))).toEqual([10, null, null, 3, 2, null]);
  });

  it('lands the list’s blocked and crawled on the test sitemaps’ own URLs: refused by robots.txt, and requested', () => {
    // The old sitemap lists one URL never reached, one robots.txt refused and one that timed out.
    const urls = (filters: GridQuery['filters']) => sitemapUrlsPage(query(6, filters)).rows.map((row) => row.url);
    expect(urls([...COUNT_FILTERS.blocked])).toEqual(['https://example.com/legacy/private']);
    expect(urls([...COUNT_FILTERS.crawled])).toEqual(['https://example.com/legacy/slow']);
    expect(urls([...COUNT_FILTERS.notCrawled])).toEqual(['https://example.com/legacy']);
  });
});
