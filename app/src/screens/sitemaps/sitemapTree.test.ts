import { describe, expect, it } from 'vitest';
import { TEST_SITEMAPS, testSitemap } from '../../test/sitemapsRobotsSidecar';
import { ancestorsOf, countsKnown, sitemapRows } from './sitemapTree';

const NONE = new Set<number>();
const shape = (rows: ReturnType<typeof sitemapRows>) =>
  rows.map((row) => [row.sitemap.id, row.level, row.expanded, row.parent]);

describe('the sitemaps as one treegrid', () => {
  it('puts an index’s sitemaps under it, in the order the crawl found them', () => {
    expect(shape(sitemapRows(TEST_SITEMAPS, NONE))).toEqual([
      [1, 1, true, null],
      [2, 2, undefined, 0],
      [3, 2, undefined, 0],
      [4, 2, undefined, 0],
      [5, 1, undefined, null],
      [6, 1, undefined, null],
    ]);
  });

  it('leaves a closed index’s sitemaps out, and says it’s closed', () => {
    const rows = sitemapRows(TEST_SITEMAPS, new Set([1]));
    expect(shape(rows)).toEqual([
      [1, 1, false, null],
      [5, 1, undefined, null],
      [6, 1, undefined, null],
    ]);
    expect(rows[0]?.children).toBe(3);
  });

  it('nests an index under an index, each row knowing the row above it', () => {
    const nested = [
      testSitemap({ id: 1, isIndex: true }),
      testSitemap({ id: 2, parentId: 1, isIndex: true }),
      testSitemap({ id: 3, parentId: 2 }),
      testSitemap({ id: 4, parentId: 1 }),
    ];
    expect(shape(sitemapRows(nested, NONE))).toEqual([
      [1, 1, true, null],
      [2, 2, true, 0],
      [3, 3, undefined, 1],
      [4, 2, undefined, 0],
    ]);
    // Closing the inner index keeps the outer one's other sitemap, with its parent's row still right.
    expect(shape(sitemapRows(nested, new Set([2])))).toEqual([
      [1, 1, true, null],
      [2, 2, false, 0],
      [4, 2, undefined, 0],
    ]);
  });

  it('shows a sitemap on its own when the index that lists it isn’t in the list', () => {
    expect(shape(sitemapRows([testSitemap({ id: 7, parentId: 99 })], NONE))).toEqual([[7, 1, undefined, null]]);
  });

  it('never loses or repeats a sitemap, even when two list each other or one lists itself', () => {
    const loop = [
      testSitemap({ id: 1, parentId: 2 }),
      testSitemap({ id: 2, parentId: 1 }),
      testSitemap({ id: 3, parentId: 3 }),
    ];
    const rows = sitemapRows(loop, NONE);
    expect(rows.map((row) => row.sitemap.id).sort()).toEqual([1, 2, 3]);
  });

  it('is empty for a crawl with no sitemaps', () => {
    expect(sitemapRows([], NONE)).toEqual([]);
  });
});

describe('ancestorsOf', () => {
  it('names the indexes above a sitemap, nearest first', () => {
    const nested = [testSitemap({ id: 1 }), testSitemap({ id: 2, parentId: 1 }), testSitemap({ id: 3, parentId: 2 })];
    expect(ancestorsOf(nested, 3)).toEqual([2, 1]);
    expect(ancestorsOf(nested, 1)).toEqual([]);
    expect(ancestorsOf(nested, 99)).toEqual([]);
  });

  it('stops at a loop', () => {
    const loop = [testSitemap({ id: 1, parentId: 2 }), testSitemap({ id: 2, parentId: 1 })];
    expect(ancestorsOf(loop, 1)).toEqual([2]);
  });
});

describe('countsKnown', () => {
  const none = { listed: 0, fetched: 0, indexable: 0, nonIndexable: 0 };
  it('knows what a read sitemap lists, nothing included', () => {
    expect(countsKnown({ state: 'read', entries: 0, urls: none })).toBe(true);
  });

  it('doesn’t know what a sitemap lists when it failed before an entry was read: a dash, never 0', () => {
    expect(countsKnown({ state: 'failed', entries: 0, urls: none })).toBe(false);
  });

  it('knows what was read of a sitemap that failed part-way', () => {
    expect(countsKnown({ state: 'failed', entries: 12, urls: { ...none, listed: 12 } })).toBe(true);
  });

  it('doesn’t know what one left unread, or not read yet, lists', () => {
    expect(countsKnown({ state: 'notRead', entries: 0, urls: none })).toBe(false);
    expect(countsKnown({ state: 'pending', entries: 0, urls: none })).toBe(false);
  });
});
