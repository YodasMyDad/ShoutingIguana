import { describe, expect, it } from 'vitest';
import { carriedSearch } from './switchSearch';

describe('what the crawl switcher carries to another crawl of the same site', () => {
  it('keeps the filters that compare values, the text, the sort and the columns', () => {
    const search = {
      filter: [{ column: 'statusClass', op: 'in', values: ['4xx'] }],
      q: 'blog',
      sort: [{ column: 'depth', desc: true }],
      cols: ['url', 'statusCode'],
      view: 'indexability',
    };
    expect(carriedSearch(search)).toEqual(search);
  });

  it('keeps Issues’ severity, category and change filters', () => {
    expect(carriedSearch({ severity: ['error'], category: 'links', change: 'more', passed: 1 })).toEqual({
      severity: ['error'],
      category: 'links',
      change: 'more',
      passed: 1,
    });
  });

  it('leaves behind what names a row of this crawl’s file', () => {
    expect(
      carriedSearch({
        inspect: 42,
        end: 'from',
        group: { detail: null, relatedUrlId: 7 },
        line: 3,
        url: 'https://example.com/sitemap.xml',
        links: { end: 'to' },
        score: 1,
        q: undefined,
      }),
    ).toEqual({});
  });

  it('drops a filter on a row id or a check’s group, and keeps the others beside it', () => {
    expect(
      carriedSearch({
        filter: [
          { column: 'to.id', op: 'eq', value: 9 },
          { column: 'id', op: 'in', values: [1, 2] },
          { column: 'check', op: 'in', values: ['broken-internal'], group: { detail: 'x', relatedUrlId: 4 } },
          { column: 'kind', op: 'in', values: ['anchor'] },
        ],
      }),
    ).toEqual({ filter: [{ column: 'kind', op: 'in', values: ['anchor'] }] });
  });

  it('carries no empty filter list', () => {
    expect(carriedSearch({ filter: [{ column: 'from.id', op: 'eq', value: 2 }] })).toEqual({});
  });
});
