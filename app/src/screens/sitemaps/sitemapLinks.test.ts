import { describe, expect, it } from 'vitest';
import type { GridQuery } from '../../api/grids';
import { TEST_SITEMAPS, sitemapUrlsPage } from '../../test/sitemapsRobotsSidecar';
import {
  COUNT_FILTERS,
  QUICK_FILTERS,
  REDIRECTING_OR_BROKEN,
  SITEMAP_COUNTS,
  countOf,
  quickFilterCount,
  quickFilterFilters,
  quickFilterOf,
} from './sitemapLinks';

const query = (sitemapId: number, filters: GridQuery['filters']) => ({ sitemapId, filters, sort: [], limit: 100 });

describe('a sitemap’s counts', () => {
  it('reads each count from what the engine counted, and “not crawled” as what’s listed less what was fetched', () => {
    const urls = { listed: 10, fetched: 7, indexable: 5, nonIndexable: 2 };
    expect(SITEMAP_COUNTS.map((count) => countOf({ urls }, count))).toEqual([10, 7, 3, 5, 2]);
  });

  it('never counts fewer than none not crawled, whatever a running crawl says of the two a moment apart', () => {
    expect(countOf({ urls: { listed: 3, fetched: 5, indexable: 0, nonIndexable: 0 } }, 'notCrawled')).toBe(0);
  });

  it('opens exactly the URLs each count counts: every count of every sitemap lands on itself', () => {
    for (const sitemap of TEST_SITEMAPS) {
      for (const count of SITEMAP_COUNTS) {
        const page = sitemapUrlsPage(query(sitemap.id, [...COUNT_FILTERS[count]]));
        expect(page.matching, `${sitemap.url} ${count}`).toBe(countOf(sitemap, count));
      }
    }
  });

  it('judges crawled by `fetched` and indexable by `indexable`, as the engine counts them', () => {
    expect(COUNT_FILTERS).toEqual({
      listed: [],
      crawled: [{ column: 'fetched', op: 'is', value: true }],
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

  it('carries the count the sitemap has for it, and none for the one nothing counts apart', () => {
    const sitemap = { urls: { listed: 10, fetched: 7, indexable: 5, nonIndexable: 2 } };
    expect(QUICK_FILTERS.map((quick) => quickFilterCount(sitemap, quick))).toEqual([10, 3, 2, null]);
  });
});
