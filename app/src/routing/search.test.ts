import { describe, expect, it } from 'vitest';
import {
  MAX_FILTERS,
  MAX_SORTS,
  inspectedUrl,
  parseFilters,
  parseSort,
  validateGridSearch,
  validateHomeSearch,
  validateInspectSearch,
  validateIssuesSearch,
  validateNewCrawlSearch,
  validateNoSearch,
} from './search';

/** What TanStack Router gives a screen: the raw search, overlaid with the validator's result. */
const asRouted = (raw: Record<string, unknown>, validate: (search: Record<string, unknown>) => object) =>
  Object.fromEntries(Object.entries({ ...raw, ...validate(raw) }).filter(([, value]) => value !== undefined));

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

  it('lets nothing raw through: every key the URL has is the route’s own, checked, or gone', () => {
    const raw = { q: true, sort: '%%', filter: 'x', inspect: 'abc', cols: [1, 2], view: 'Bad!', bogus: 1 };
    expect(asRouted(raw, validateGridSearch)).toEqual({});
    expect(asRouted({ q: { a: 1 }, bogus: 'x' }, validateHomeSearch)).toEqual({});
    expect(asRouted({ q: true }, validateHomeSearch)).toEqual({});
    expect(asRouted({ url: { a: 1 } }, validateNewCrawlSearch)).toEqual({});
    expect(asRouted({ severity: 'fatal', group: [], inspect: 'abc' }, validateIssuesSearch)).toEqual({});
    expect(asRouted({ inspect: '1.5', q: 'x' }, validateInspectSearch)).toEqual({});
    expect(asRouted({ inspect: 4, q: 'x', sort: 'url' }, validateNoSearch)).toEqual({});
    expect(asRouted({ q: 'garden', inspect: '7', bogus: 1 }, validateGridSearch)).toEqual({ q: 'garden', inspect: 7 });
  });

  it('reads the inspected URL only as a positive whole number', () => {
    expect(inspectedUrl({ inspect: 12 })).toBe(12);
    for (const inspect of ['12', 'abc', 0, -1, 1.5, Number.NaN, null, undefined, {}]) {
      expect(inspectedUrl({ inspect })).toBeUndefined();
    }
    expect(inspectedUrl(undefined)).toBeUndefined();
    expect(validateInspectSearch({ inspect: ' ' }).inspect).toBeUndefined();
  });

  it('keeps a query within the contract: 3 sorts, each column once, and 20 filters', () => {
    expect(parseSort('-issues,url,-url,statusCode,depth')).toEqual([
      { column: 'issues', desc: true },
      { column: 'url', desc: false },
      { column: 'statusCode', desc: false },
    ]);
    expect(MAX_SORTS).toBe(3);
    const filters = Array.from({ length: 25 }, (_, index) => ({ column: `c${index}`, op: 'isEmpty' }));
    expect(parseFilters(filters)).toHaveLength(MAX_FILTERS);
    expect(MAX_FILTERS).toBe(20);
  });

  it('drops a range that can match nothing', () => {
    expect(parseFilters([{ column: 'statusCode', op: 'between', min: 500, max: 400 }])).toBeUndefined();
    expect(parseFilters([{ column: 'statusCode', op: 'between', min: 400, max: 400 }])).toEqual([
      { column: 'statusCode', op: 'between', min: 400, max: 400 },
    ]);
  });

  it('takes a group only where the engine does: one check, with a detail or a related URL that may be null', () => {
    expect(
      parseFilters([{ column: 'check', op: 'in', values: ['empty-anchor-text'], group: { relatedUrlId: 1 } }]),
    ).toEqual([{ column: 'check', op: 'in', values: ['empty-anchor-text'], group: { detail: null, relatedUrlId: 1 } }]);
    expect(
      parseFilters([
        { column: 'check', op: 'in', values: ['open-graph-missing'], group: { detail: 'Missing og:image' } },
      ]),
    ).toEqual([
      {
        column: 'check',
        op: 'in',
        values: ['open-graph-missing'],
        group: { detail: 'Missing og:image', relatedUrlId: null },
      },
    ]);
    // A group on another column, on two checks, or of the wrong shape would widen or break the view: dropped.
    expect(parseFilters([{ column: 'type', op: 'in', values: ['page'], group: { detail: 'x' } }])).toBeUndefined();
    expect(parseFilters([{ column: 'check', op: 'in', values: ['a', 'b'], group: { detail: 'x' } }])).toBeUndefined();
    expect(parseFilters([{ column: 'check', op: 'in', values: ['a'], group: { detail: 5 } }])).toBeUndefined();
    expect(parseFilters([{ column: 'check', op: 'in', values: ['a'], group: 'x' }])).toBeUndefined();
  });

  it('reads the Issues view: one of each severity, the group in view, a flat check’s sort', () => {
    expect(
      validateIssuesSearch({
        severity: ['error', 'error', 'notice'],
        q: 'anchor',
        group: { detail: null, relatedUrlId: 1 },
        sort: '-detail',
        inspect: 7,
      }),
    ).toEqual({
      severity: ['error', 'notice'],
      q: 'anchor',
      group: { detail: null, relatedUrlId: 1 },
      sort: [{ column: 'detail', desc: true }],
      inspect: 7,
    });
    // The group of the issues that name no other URL is a group too; a list or a bad id is none.
    expect(validateIssuesSearch({ group: {} }).group).toEqual({ detail: null, relatedUrlId: null });
    expect(validateIssuesSearch({ group: [1] }).group).toBeUndefined();
    expect(validateIssuesSearch({ group: { detail: 'Missing og:image', relatedUrlId: -1 } }).group).toBeUndefined();
  });
});
