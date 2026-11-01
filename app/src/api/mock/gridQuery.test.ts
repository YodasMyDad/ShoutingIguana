import { describe, expect, it } from 'vitest';
import { runGridQuery } from './gridQuery';

const rows = [
  { id: 1, url: 'https://a/', title: 'Home', statusCode: 200, statusClass: '2xx', responseMs: 120 },
  { id: 2, url: 'https://a/missing', title: null, statusCode: 404, statusClass: '4xx', responseMs: 30 },
  { id: 3, url: 'https://a/slow', title: 'Slow page', statusCode: 200, statusClass: '2xx', responseMs: 5476 },
  { id: 4, url: 'https://a/gone', title: 'Gone', statusCode: 410, statusClass: '4xx', responseMs: null },
];

const ids = (page: { rows: { id: number }[] }) => page.rows.map((row) => row.id);

describe('the mock grid query (E3a)', () => {
  it('filters, then counts the matching rows and every row', () => {
    const page = runGridQuery(rows, {
      filters: [{ column: 'statusClass', op: 'in', values: ['4xx'] }],
      sort: [],
      offset: 0,
      limit: 10,
    });
    expect(ids(page)).toEqual([2, 4]);
    expect(page.matching).toBe(2);
    expect(page.total).toBe(4);
    expect(page.next).toBeNull();
  });

  it('treats unknown values as the contract says: last in any sort, matched by negative operators', () => {
    const ascending = runGridQuery(rows, { filters: [], sort: [{ column: 'responseMs', desc: false }], limit: 10 });
    expect(ids(ascending)).toEqual([2, 1, 3, 4]);
    const descending = runGridQuery(rows, { filters: [], sort: [{ column: 'responseMs', desc: true }], limit: 10 });
    expect(ids(descending)).toEqual([3, 1, 2, 4]);
    const excluding = runGridQuery(rows, {
      filters: [{ column: 'title', op: 'ne', value: 'Home' }],
      sort: [],
      limit: 10,
    });
    expect(ids(excluding)).toEqual([2, 3, 4]);
  });

  it('takes ranges, text operators and emptiness', () => {
    const between = runGridQuery(rows, {
      filters: [{ column: 'statusCode', op: 'between', min: 400, max: 499 }],
      sort: [],
      limit: 10,
    });
    expect(ids(between)).toEqual([2, 4]);
    const starts = runGridQuery(rows, {
      filters: [{ column: 'url', op: 'endsWith', value: '/GONE' }],
      sort: [],
      limit: 10,
    });
    expect(ids(starts)).toEqual([4]);
    const empty = runGridQuery(rows, { filters: [{ column: 'title', op: 'isEmpty' }], sort: [], limit: 10 });
    expect(ids(empty)).toEqual([2]);
  });

  it('returns only the columns asked for, id first', () => {
    const page = runGridQuery(rows, { columns: ['url', 'statusCode'], filters: [], sort: [], limit: 1 });
    expect(page.columns).toEqual(['id', 'url', 'statusCode']);
    expect(page.rows[0]).toEqual({ id: 1, url: 'https://a/', statusCode: 200 });
  });

  it('searches URLs and titles, and pages by offset or by the next cursor', () => {
    const first = runGridQuery(rows, { filters: [], sort: [], search: 'a/', offset: 0, limit: 2 });
    expect(ids(first)).toEqual([1, 2]);
    expect(first.matching).toBe(4);
    const after = runGridQuery(rows, { filters: [], sort: [], search: 'a/', after: first.next ?? '', limit: 2 });
    expect(ids(after)).toEqual([3, 4]);
    expect(after.next).toBeNull();
  });
});
