import { describe, expect, it } from 'vitest';
import { TEST_SITEMAPS, testSitemap } from '../../test/sitemapsRobotsSidecar';
import { countsKnown, sitemapRows, subtreeOf, wholeSubtree } from './sitemapTree';

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

describe('an index’s counts', () => {
  const none = { listed: 0, fetched: 0, indexable: 0, nonIndexable: 0 };
  const index = testSitemap({ id: 1, isIndex: true, sitemaps: 3, urls: { ...none, listed: 7 } });
  const child = (id: number, over: Parameters<typeof testSitemap>[0] | object = {}) =>
    testSitemap({ id, parentId: 1, foundVia: 'index', ...over });

  it('are known once the crawl has every sitemap under it and has read each', () => {
    const all = [index, child(2), child(3), child(4)];
    const subtree = subtreeOf(all, 1, 'done');
    expect(subtree).toEqual({ sitemaps: 3, read: 3, more: false });
    expect(wholeSubtree(subtree)).toBe(true);
    expect(countsKnown(index, subtree)).toBe(true);
  });

  it('aren’t known, and so aren’t 0, for an index that was read while its sitemaps weren’t', () => {
    // A crawl stopped between the index and its sitemaps: it has one of the three, unread.
    const stopped = [{ ...index, sitemaps: 1, urls: none }, child(2, { state: 'pending', statusCode: null })];
    const subtree = subtreeOf(stopped, 1, 'unfinished');
    expect(subtree).toEqual({ sitemaps: 1, read: 0, more: true });
    expect(countsKnown(stopped[0]!, subtree)).toBe(false);
    // Read before any sitemap under it was taken up: still not 0.
    const alone = subtreeOf([stopped[0]!], 1, 'running');
    expect(alone).toEqual({ sitemaps: 0, read: 0, more: true });
    expect(countsKnown(stopped[0]!, alone)).toBe(false);
  });

  it('aren’t known while the crawl is still reading sitemaps: it may not have every one the index lists', () => {
    const sofar = [index, child(2), child(3)];
    expect(countsKnown(index, subtreeOf(sofar, 1, 'running'))).toBe(false);
    expect(countsKnown(index, subtreeOf(sofar, 1, 'done'))).toBe(true);
  });

  it('aren’t known when one under it couldn’t be read, was left unread, or sits under an index of its own', () => {
    const failed = [index, child(2), child(3, { state: 'failed', statusCode: 404, error: '404 Not Found' })];
    expect(subtreeOf(failed, 1, 'done')).toEqual({ sitemaps: 2, read: 1, more: false });
    expect(countsKnown(index, subtreeOf(failed, 1, 'done'))).toBe(false);
    // One that failed part-way has counts of its own, as its row shows: it's read as far as it goes.
    const partly = [index, child(2), child(3, { state: 'failed', error: 'Timed out', entries: 40 })];
    expect(countsKnown(index, subtreeOf(partly, 1, 'done'))).toBe(true);
    // Left unread at the crawl's limits: there may be more like it.
    const limited = [index, child(2), child(3, { state: 'notRead', statusCode: null, error: 'Not read: x' })];
    expect(subtreeOf(limited, 1, 'done')).toEqual({ sitemaps: 2, read: 1, more: true });
    // An unread sitemap two levels down counts against the index at the top, and against the one between.
    const deep = [
      index,
      child(2, { isIndex: true }),
      testSitemap({ id: 3, parentId: 2, state: 'pending', statusCode: null }),
    ];
    expect(countsKnown(index, subtreeOf(deep, 1, 'done'))).toBe(false);
    expect(countsKnown(deep[1]!, subtreeOf(deep, 2, 'done'))).toBe(false);
  });

  it('leaves a sitemap that’s no index as it was: its own counts, whatever the crawl is doing', () => {
    const plain = testSitemap({ id: 9 });
    expect(countsKnown(plain, subtreeOf([plain], 9, 'running'))).toBe(true);
    expect(countsKnown(plain)).toBe(true);
  });

  it('gives each row how far the crawl got under it', () => {
    const rows = sitemapRows(TEST_SITEMAPS, NONE, 'done');
    // The test list's index has three sitemaps, one of which couldn't be read.
    expect(rows[0]?.subtree).toEqual({ sitemaps: 3, read: 2, more: false });
    expect(rows[1]?.subtree).toEqual({ sitemaps: 0, read: 0, more: false });
    expect(sitemapRows(TEST_SITEMAPS, NONE, 'running')[0]?.subtree.more).toBe(true);
  });
});
