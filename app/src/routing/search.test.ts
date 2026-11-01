import { describe, expect, it } from 'vitest';
import { parseFilters, parseSort, validateGridSearch, validateHomeSearch, validateIssuesSearch } from './search';

describe('search params', () => {
  it('reads the grid’s sort, filters, columns, search and inspected URL', () => {
    expect(
      validateGridSearch({
        sort: [{ column: 'statusCode', desc: true }, { column: 'url' }],
        filter: [{ column: 'statusClass', op: 'in', values: ['4xx'] }],
        cols: ['url', 'statusCode'],
        q: 'garden',
        inspect: '42',
        view: 'indexability',
      }),
    ).toEqual({
      sort: [
        { column: 'statusCode', desc: true },
        { column: 'url', desc: false },
      ],
      filter: [{ column: 'statusClass', op: 'in', values: ['4xx'] }],
      cols: ['url', 'statusCode'],
      q: 'garden',
      inspect: 42,
      view: 'indexability',
    });
  });

  it('accepts the compact sort form', () => {
    expect(parseSort('-statusCode,url')).toEqual([
      { column: 'statusCode', desc: true },
      { column: 'url', desc: false },
    ]);
  });

  it('drops anything unusable instead of breaking the view', () => {
    expect(
      validateGridSearch({
        sort: [{ column: 'drop table' }, 7],
        filter: [{ column: 'url', op: 'eval', value: 'x' }, { column: 'title', op: 'contains' }, 'junk'],
        cols: ['url', '<script>'],
        inspect: -3,
        view: 'Not A Preset!',
      }),
    ).toEqual({ cols: ['url'] });
    expect(parseFilters([{ column: 'title', op: 'isEmpty' }])).toEqual([{ column: 'title', op: 'isEmpty' }]);
    expect(parseFilters([{ column: 'statusCode', op: 'between', min: 400 }])).toEqual([
      { column: 'statusCode', op: 'between', min: 400 },
    ]);
    expect(
      parseFilters([
        { column: 'statusCode', op: 'between' },
        { column: 'type', op: 'in', values: [] },
      ]),
    ).toBeUndefined();
  });

  it('keeps only real severities', () => {
    expect(validateIssuesSearch({ severity: ['error', 'fatal', 'notice'] })).toEqual({ severity: ['error', 'notice'] });
    expect(validateIssuesSearch({ severity: 'warning' })).toEqual({ severity: ['warning'] });
  });

  it('leaves an empty search empty', () => {
    expect(validateHomeSearch({ q: '' })).toEqual({});
  });
});
