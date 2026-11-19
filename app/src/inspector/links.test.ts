// The link lists' own order and status filter, for a list one read holds: the link the reader came for first, then
// the broken ones, then the rest as the crawl found them.
import { describe, expect, it } from 'vitest';
import type { LinkRow } from '../api/inspector';
import { arrangeLinks, isBrokenEnd, isRedirectEnd, linkCountWords } from './links';

const row = (id: number, statusCode: number | null, more: Partial<LinkRow> = {}): LinkRow => ({
  id,
  url: `https://example.com/${id}`,
  kind: 'hyperlink',
  text: `Link ${id}`,
  position: 'content',
  flags: [],
  followed: true,
  fragment: null,
  statusCode,
  indexable: true,
  fetched: true,
  ...more,
});

const ids = (rows: readonly LinkRow[]) => rows.map((link) => link.id);

describe('a link’s other end', () => {
  it('is broken when it answered 4xx or 5xx, or gave no answer at all', () => {
    expect(isBrokenEnd(row(1, 404))).toBe(true);
    expect(isBrokenEnd(row(1, 500))).toBe(true);
    expect(isBrokenEnd(row(1, null, { outcome: 'timeout' }))).toBe(true);
    expect(isBrokenEnd(row(1, null, { outcome: 'networkError' }))).toBe(true);
  });

  it('isn’t broken when it answered, redirects, was refused by robots.txt, or was never fetched', () => {
    expect(isBrokenEnd(row(1, 200))).toBe(false);
    expect(isBrokenEnd(row(1, 301))).toBe(false);
    expect(isBrokenEnd(row(1, null, { outcome: 'robotsDisallowed' }))).toBe(false);
    expect(isBrokenEnd(row(1, null, { outcome: 'notChecked' }))).toBe(false);
    expect(isBrokenEnd(row(1, null, { outcome: null, fetched: false }))).toBe(false);
    // An engine that doesn't say what became of it: no guess.
    expect(isBrokenEnd(row(1, null))).toBe(false);
  });

  it('redirects when it answered 3xx', () => {
    expect(isRedirectEnd(row(1, 301))).toBe(true);
    expect(isRedirectEnd(row(1, 308))).toBe(true);
    expect(isRedirectEnd(row(1, 200))).toBe(false);
    expect(isRedirectEnd(row(1, 404))).toBe(false);
    expect(isRedirectEnd(row(1, null, { outcome: 'redirect' }))).toBe(true);
  });
});

describe('a list of links one read holds', () => {
  const rows = [row(1, 200), row(2, 404), row(3, 301), row(4, 200), row(5, 500)];

  it('lists the broken links first, the rest as the crawl found them', () => {
    expect(ids(arrangeLinks(rows, 'all', undefined))).toEqual([2, 5, 1, 3, 4]);
  });

  it('puts the link the reader came for first, before the broken ones', () => {
    expect(ids(arrangeLinks(rows, 'all', 4))).toEqual([4, 2, 5, 1, 3]);
    // Every link to the URL come for, where the page has more than one.
    expect(ids(arrangeLinks([...rows, row(4, 200, { text: 'Again' })], 'all', 4))).toEqual([4, 4, 2, 5, 1, 3]);
  });

  it('keeps only the broken links, or only the redirects, when asked', () => {
    expect(ids(arrangeLinks(rows, 'broken', undefined))).toEqual([2, 5]);
    expect(ids(arrangeLinks(rows, 'redirects', undefined))).toEqual([3]);
    expect(ids(arrangeLinks(rows, 'broken', 5))).toEqual([5, 2]);
  });

  it('leaves a list as it was found when nothing is broken and no link was come for', () => {
    const fine = [row(1, 200), row(2, 200)];
    expect(ids(arrangeLinks(fine, 'all', undefined))).toEqual([1, 2]);
    expect(ids(arrangeLinks(fine, 'all', 9))).toEqual([1, 2]);
  });
});

describe('the count a link list says', () => {
  it('is every link’s, or the ones of a status among them', () => {
    expect(linkCountWords(78, 78, 'all', 'outlink')).toBe('78 outlinks');
    expect(linkCountWords(1, 1, 'all', 'inlink')).toBe('1 inlink');
    expect(linkCountWords(2, 78, 'broken', 'outlink')).toBe('2 broken of 78 outlinks');
    expect(linkCountWords(1, 78, 'redirects', 'outlink')).toBe('1 redirect of 78 outlinks');
    expect(linkCountWords(0, 7, 'redirects', 'inlink')).toBe('0 redirects of 7 inlinks');
  });
});
