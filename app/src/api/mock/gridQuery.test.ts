import { describe, expect, it } from 'vitest';
import { answerFacets, answerGridQuery, runGridQuery } from './gridQuery';

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

describe('the mock’s answers, as the sidecar gives them at 46a2703', () => {
  const analysed = { available: true, reason: null } as const;
  const crawling = { available: false, reason: 'crawling' } as const;
  const scored = rows.map((row, index) => ({ ...row, issues: index, inlinks: index * 2 }));

  it('refuses a query it can’t run, with messages by field', () => {
    const answer = answerGridQuery(
      rows,
      {
        columns: ['url', 'detail', 'bogus'],
        filters: [
          { column: 'title', op: 'gt', value: 3 },
          { column: 'url', op: 'regex', value: '(' },
          { column: 'statusClass', op: 'in', values: ['6xx'] },
        ],
        sort: [{ column: 'outcome', desc: true }],
        limit: 5_000,
      },
      analysed,
    );
    expect(answer.ok).toBe(false);
    if (answer.ok) return;
    expect(answer.problem).toMatchObject({ type: 'invalid-query', status: 422 });
    expect(Object.keys(answer.problem.errors ?? {})).toEqual([
      'columns[1]',
      'columns[2]',
      'filters[0].op',
      'filters[1].value',
      'filters[2].values',
      'sort[0].column',
      'limit',
    ]);
    expect(answer.problem.detail).toBe(answer.problem.errors?.['columns[1]']?.[0]);
  });

  it('leaves the analysis’s columns out of the page without a summary, and says so', () => {
    const answer = answerGridQuery(
      scored,
      { columns: ['url', 'issues', 'inlinks'], filters: [], sort: [], limit: 2 },
      crawling,
    );
    expect(answer.ok).toBe(true);
    if (!answer.ok) return;
    expect(answer.page.columns).toEqual(['id', 'url']);
    expect(answer.page.unavailable).toEqual(['issues', 'inlinks']);
    expect(answer.page.rows[0]).toEqual({ id: 1, url: 'https://a/' });
    expect(answer.page.summary).toEqual(crawling);
  });

  it('refuses a sort or filter on them, naming the columns and why', () => {
    const answer = answerGridQuery(
      scored,
      {
        filters: [
          { column: 'inlinks', op: 'gt', value: 0 },
          { column: 'check', op: 'in', values: ['title-too-long'] },
        ],
        sort: [{ column: 'issues', desc: true }],
        limit: 10,
      },
      { available: false, reason: 'crawlChanged' },
    );
    expect(answer.ok).toBe(false);
    if (answer.ok) return;
    expect(answer.problem).toMatchObject({
      type: 'needs-analysis',
      status: 409,
      reason: 'crawlChanged',
      columns: ['inlinks', 'check', 'issues'],
      title:
        'Filtering or sorting by these columns needs an analysis, and this crawl was continued after its last analysis, so it needs analysing again.',
    });
  });

  it('reads on from a cursor with no offset to give', () => {
    const first = runGridQuery(rows, { filters: [], sort: [], limit: 2 });
    const after = runGridQuery(rows, { filters: [], sort: [], after: first.next ?? '', limit: 2 });
    expect(first.offset).toBe(0);
    expect(after.offset).toBeNull();
  });

  it('takes a check’s own columns only for its rows', () => {
    const issueRows = rows.map((row) => ({ ...row, detail: 'Links to 1 URL', relatedUrlId: 1 }));
    const query = { columns: ['url', 'detail'], filters: [], sort: [{ column: 'detail', desc: false }], limit: 5 };
    expect(answerGridQuery(issueRows, query, analysed).ok).toBe(false);
    const answer = answerGridQuery(issueRows, query, analysed, { issueRows: true });
    expect(answer.ok && answer.page.columns).toEqual(['id', 'url', 'detail']);
  });

  it('counts facets with the filter that opens each count', () => {
    const page = answerFacets(
      [
        ...scored,
        {
          id: 5,
          url: 'https://a/huge',
          title: 'Huge',
          statusCode: 200,
          statusClass: '2xx',
          responseMs: 2_400,
          issues: 0,
          inlinks: 0,
        },
      ],
      { facets: ['responseTime', 'severity', 'statusClass'] },
      analysed,
    );
    expect(page.facets.responseTime?.find((bucket) => bucket.value === '2000+')).toMatchObject({
      count: 2,
      min: 2000,
      filter: { column: 'responseMs', op: 'gte', value: 2000 },
    });
    expect(page.facets.statusClass?.map((bucket) => [bucket.value, bucket.count])).toEqual([
      ['2xx', 3],
      ['4xx', 2],
    ]);
    expect(answerFacets(scored, { facets: ['severity', 'type'] }, crawling).unavailable).toEqual(['severity']);
  });
});
