import { describe, expect, it } from 'vitest';
import { filterSentences } from './filterSentence';

const words = {
  column: (id: string) => ({ statusCode: 'Status', check: 'Check', depth: 'Depth', title: 'Title' })[id] ?? id,
  value: (column: string, value: string | number) =>
    column === 'check' && value === 'empty-anchor-text' ? 'Internal links without anchor text' : String(value),
};

describe('a filter as a sentence', () => {
  it('reads each operator in words', () => {
    expect(filterSentences({ column: 'statusCode', op: 'eq', value: 404 }, words)).toEqual([
      { field: 'Status', op: 'is', value: '404' },
    ]);
    expect(filterSentences({ column: 'depth', op: 'gte', value: 4 }, words)).toEqual([
      { field: 'Depth', op: 'is at least', value: '4' },
    ]);
    expect(filterSentences({ column: 'depth', op: 'between', min: 1, max: 3 }, words)).toEqual([
      { field: 'Depth', op: 'is between', value: '1 and 3' },
    ]);
    expect(filterSentences({ column: 'title', op: 'isEmpty' }, words)).toEqual([
      { field: 'Title', op: 'is', value: 'empty' },
    ]);
    expect(filterSentences({ column: 'statusCode', op: 'in', values: [301, 302, 308] }, words)).toEqual([
      { field: 'Status', op: 'is', value: '301, 302 or 308' },
    ]);
  });

  it('names a check by its title, and its group as a chip of its own', () => {
    expect(
      filterSentences(
        { column: 'check', op: 'in', values: ['empty-anchor-text'], group: { detail: null, relatedUrlId: 1 } },
        words,
        () => '/',
      ),
    ).toEqual([
      { field: 'Check', op: 'is', value: 'Internal links without anchor text' },
      { field: 'Related URL', op: 'is', value: '/' },
    ]);
    expect(
      filterSentences(
        {
          column: 'check',
          op: 'in',
          values: ['internal-client-error'],
          group: { detail: 'HTTP 404', relatedUrlId: null },
        },
        words,
      ),
    ).toEqual([
      { field: 'Check', op: 'is', value: 'internal-client-error' },
      { field: 'Detail', op: 'is', value: 'HTTP 404' },
    ]);
  });
});
