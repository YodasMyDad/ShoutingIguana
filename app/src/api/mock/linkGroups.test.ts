import { describe, expect, it } from 'vitest';
import { testLinkRows } from '../../test/linksSidecar';
import type { LinkGroupsPage } from '../grids';
import { answerLinkGroups, answerPageImages } from './linkGroups';

const SUMMARY = { available: true, reason: null } as const;
const links = testLinkRows();

function page(body: unknown): LinkGroupsPage {
  const answer = answerLinkGroups(links, body, SUMMARY);
  if (!answer.ok) throw new Error(answer.problem.title);
  return answer.page;
}

function refused(body: unknown): string[] {
  const answer = answerLinkGroups(links, body, SUMMARY);
  if (answer.ok) throw new Error('It was answered');
  return [answer.problem.type, ...Object.keys(answer.problem.errors ?? {})];
}

describe('the mock’s links grouped by target', () => {
  it('lists each target once, the most linked from pages first, with its links, pages, text and positions', () => {
    const all = page({});
    expect([all.total, all.links, all.pages, all.offset]).toEqual([8, 13, 3, 0]);
    expect(all.sort).toEqual({ column: 'pages', desc: true });
    expect(all.rows.map((row) => [row.id, row.pages, row.links])).toEqual([
      [2, 3, 3],
      [1, 2, 2],
      [4, 2, 2],
      [5, 2, 2],
      [3, 1, 1],
      [6, 1, 1],
      [7, 1, 1],
      [8, 1, 1],
    ]);
    const gone = all.rows.find((row) => row.id === 4);
    expect(gone).toMatchObject({ text: 'Gone', texts: 2, positions: ['content', 'footer'], likelyTemplate: false });
    expect(gone?.target).toMatchObject({ statusCode: 404, outcome: 'clientError' });
    expect(all.rows.find((row) => row.id === 5)).toMatchObject({ text: 'Logo', texts: 2 });
    expect(all.rows.find((row) => row.id === 7)).toMatchObject({ text: null, texts: 1 });
  });

  it('groups a view’s links alone, and pages and sorts them as asked', () => {
    const broken = page({ filters: [{ column: 'to.statusClass', op: 'in', values: ['4xx', '5xx'] }] });
    expect([broken.total, broken.links, broken.pages]).toEqual([2, 3, 2]);
    expect(broken.rows.map((row) => row.id)).toEqual([4, 8]);
    expect(page({ sort: [{ column: 'links', desc: false }], limit: 3 }).rows.map((row) => row.id)).toEqual([3, 6, 7]);
    expect(page({ sort: [{ column: 'to.url', desc: false }], offset: 1, limit: 2 }).rows.map((row) => row.url)).toEqual(
      ['https://example.com/about', 'https://example.com/error'],
    );
  });

  it('refuses what the sidecar refuses, by field', () => {
    expect(refused({ sort: [{ column: 'kind', desc: false }] })).toEqual(['invalid-query', 'sort.column']);
    expect(
      refused({
        sort: [
          { column: 'pages', desc: true },
          { column: 'links', desc: true },
        ],
      }),
    ).toEqual(['invalid-query', 'sort']);
    expect(refused({ limit: 201 })).toEqual(['invalid-query', 'limit']);
    expect(refused({ end: 'to' })).toEqual(['bad-request']);
  });

  it('lists a page’s images, each file once, with the alt text of the first that has one', () => {
    expect(answerPageImages(links, 1)).toEqual({
      urlId: 1,
      total: 1,
      links: 1,
      rows: [expect.objectContaining({ id: 5, links: 1, text: 'Logo', positions: ['header'] })],
    });
    expect(answerPageImages(links, 2).rows).toEqual([expect.objectContaining({ id: 5, text: null })]);
    expect(answerPageImages(links, 4)).toEqual({ urlId: 4, total: 0, links: 0, rows: [] });
  });
});
