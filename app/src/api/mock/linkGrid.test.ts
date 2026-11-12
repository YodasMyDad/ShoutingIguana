import { describe, expect, it } from 'vitest';
import type { GridFilter, GridQuery, LinkFacetId, SummaryState } from '../grids';
import { LINKS_COUNTS, testLinkRows } from '../../test/linksSidecar';
import { LINK_COLUMNS, LINK_FACETS, answerLinkFacets, answerLinkQuery, linkColumn } from './linkGrid';

const READY: SummaryState = { available: true, reason: null };
const rows = testLinkRows();

function page(query: Partial<GridQuery>, summary: SummaryState = READY) {
  const answer = answerLinkQuery(rows, { filters: [], sort: [], limit: 100, ...query }, summary);
  if (!answer.ok) throw new Error(`Refused: ${JSON.stringify(answer.problem)}`);
  return answer.page;
}

function refusal(query: Partial<GridQuery>, summary: SummaryState = READY) {
  const answer = answerLinkQuery(rows, { filters: [], sort: [], limit: 100, ...query }, summary);
  if (answer.ok) throw new Error('Answered');
  return answer.problem;
}

describe('the mock’s links grid', () => {
  it('lists a link’s own columns, then each end’s, and sorts only by a link’s id and its target', () => {
    expect(linkColumn('kind')?.values).toContain('hyperlink');
    expect(linkColumn('to.statusClass')?.type).toBe('enum');
    expect(linkColumn('from.inlinks')?.needsAnalysis).toBe(true);
    const sortable = LINK_COLUMNS.filter((column) => column.sortable).map((column) => column.id);
    expect(sortable).toContain('id');
    expect(sortable).toContain('to.statusCode');
    expect(sortable.filter((id) => !id.startsWith('to.'))).toEqual(['id']);
    // A check's own columns are its rows' alone.
    expect(linkColumn('to.detail')).toBeUndefined();
  });

  it('answers with the default columns, in crawl order, each link with its id', () => {
    const first = page({});
    expect(first.columns).toEqual(['id', 'from.url', 'to.url', 'kind', 'text']);
    expect(first.matching).toBe(LINKS_COUNTS.total);
    expect(first.total).toBe(LINKS_COUNTS.total);
    expect(first.rows[1]).toEqual({
      id: 2,
      'from.url': 'https://example.com/',
      'to.url': 'https://example.com/about',
      kind: 'hyperlink',
      text: 'About us',
    });
  });

  it('filters on what a link is and on either end, as the URL grid filters that end', () => {
    const broken = page({ filters: [{ column: 'to.statusClass', op: 'in', values: ['4xx', '5xx'] }] });
    expect(broken.matching).toBe(LINKS_COUNTS.broken);
    const fromAbout = page({
      columns: ['text'],
      filters: [
        { column: 'from.url', op: 'endsWith', value: '/about' },
        { column: 'kind', op: 'in', values: ['hyperlink'] },
      ],
    });
    expect(fromAbout.rows.map((row) => row.text)).toEqual(['Home', 'Gone again', 'Broken server', 'About (this page)']);
    expect(page({ filters: [{ column: 'selfLink', op: 'is', value: true }] }).matching).toBe(2);
  });

  it('works out whether a hyperlink is followed, and leaves any other kind unknown', () => {
    const followed = page({ columns: ['kind', 'followed', 'noFollow'] }).rows;
    expect(followed.find((row) => row.id === 1)).toMatchObject({ kind: 'canonical', followed: null });
    expect(followed.find((row) => row.id === 2)).toMatchObject({ followed: true, noFollow: false });
    expect(followed.find((row) => row.id === 6)).toMatchObject({ followed: false, noFollow: true });
  });

  it('searches the link’s text and both ends’ URLs', () => {
    expect(page({ search: 'gone' }).matching).toBe(2);
    expect(page({ search: 'PARTNER' }).matching).toBe(1);
    expect(page({ search: '/about' }).matching).toBeGreaterThan(2);
  });

  it('sorts by one key, the link’s id or a column of the target, and refuses any other', () => {
    const byStatus = page({ columns: ['to.statusCode'], sort: [{ column: 'to.statusCode', desc: true }] });
    expect(byStatus.rows[0]?.['to.statusCode']).toBe(500);
    // Unknown last, whichever the direction.
    expect(byStatus.rows.at(-1)?.['to.statusCode']).toBeNull();
    expect(refusal({ sort: [{ column: 'text', desc: false }] }).errors).toHaveProperty(['sort[0].column']);
    expect(refusal({ sort: [{ column: 'from.depth', desc: false }] }).errors).toHaveProperty(['sort[0].column']);
    const two = refusal({
      sort: [
        { column: 'to.url', desc: false },
        { column: 'id', desc: false },
      ],
    });
    expect(two.type).toBe('invalid-query');
    expect(two.errors).toHaveProperty('sort');
  });

  it('refuses a column it doesn’t have, and a filter its type doesn’t take', () => {
    expect(refusal({ columns: ['to.nope'] }).errors).toHaveProperty(['columns[0]']);
    expect(refusal({ filters: [{ column: 'kind', op: 'contains', value: 'hyper' }] }).type).toBe('invalid-query');
  });

  it('leaves the analysis’s columns out without a URL summary, and refuses a filter or sort on one', () => {
    const notAnalysed: SummaryState = { available: false, reason: 'notAnalyzed' };
    const without = page({ columns: ['to.url', 'to.inlinks'] }, notAnalysed);
    expect(without.columns).toEqual(['id', 'to.url']);
    expect(without.unavailable).toEqual(['to.inlinks']);
    const refused = refusal({ sort: [{ column: 'to.inlinks', desc: true }] }, notAnalysed);
    expect(refused).toMatchObject({ type: 'needs-analysis', reason: 'notAnalyzed', columns: ['to.inlinks'] });
  });

  it('pages with a cursor, to the end', () => {
    const seen: number[] = [];
    let after: string | undefined;
    for (;;) {
      const next = page({ limit: 5, ...(after ? { after } : {}) });
      seen.push(...next.rows.map((row) => row.id));
      if (!next.next) break;
      after = next.next;
    }
    expect(seen).toEqual(rows.map((row) => row.id));
  });
});

describe('the mock’s link facets', () => {
  function facets(filters: GridFilter[] = [], search?: string) {
    const answer = answerLinkFacets(rows, { filters, ...(search ? { search } : {}) }, READY);
    if (!answer.ok) throw new Error('Refused');
    return answer.page;
  }

  it('counts the links by value, each facet adding up to the links matched', () => {
    const all = facets();
    expect(all.matching).toBe(LINKS_COUNTS.total);
    for (const id of LINK_FACETS) {
      const sum = (all.facets[id] ?? []).reduce((total, bucket) => total + bucket.count, 0);
      expect(sum, id).toBe(all.matching);
    }
    expect(all.facets.kind?.find((bucket) => bucket.value === 'hyperlink')?.count).toBe(LINKS_COUNTS.hyperlinks);
    expect(all.facets.follow?.map((bucket) => bucket.value)).toEqual(['followed', 'nofollow', 'notHyperlink']);
    expect(all.facets['to.statusClass']?.at(-1)).toMatchObject({ value: null, filter: { op: 'isEmpty' } });
    expect(all.facets['to.isInternal']).toEqual([
      { value: true, count: 12, filter: { column: 'to.isInternal', op: 'is', value: true } },
      { value: false, count: 1, filter: { column: 'to.isInternal', op: 'is', value: false } },
    ]);
  });

  it('gives each value the filter that selects exactly its links, under any filters and search', () => {
    const views: { filters: GridFilter[]; search?: string }[] = [
      { filters: [] },
      { filters: [{ column: 'kind', op: 'in', values: ['hyperlink'] }] },
      { filters: [{ column: 'to.statusClass', op: 'in', values: ['4xx', '5xx'] }] },
      { filters: [], search: 'about' },
    ];
    for (const view of views) {
      const counted = facets(view.filters, view.search);
      for (const id of Object.keys(counted.facets) as LinkFacetId[]) {
        for (const bucket of counted.facets[id] ?? []) {
          const landed = page({
            filters: [...view.filters, bucket.filter],
            ...(view.search ? { search: view.search } : {}),
          });
          expect(landed.matching, `${id} ${String(bucket.value)}`).toBe(bucket.count);
        }
      }
    }
  });

  it('refuses a facet it doesn’t have', () => {
    const answer = answerLinkFacets(rows, { facets: ['depth' as LinkFacetId] }, READY);
    expect(answer.ok).toBe(false);
  });
});
