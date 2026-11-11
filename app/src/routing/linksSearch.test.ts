import { describe, expect, it } from 'vitest';
import { validateGridSearch, validateLinksSearch } from './search';

/** What TanStack Router gives a screen: the raw search, overlaid with the validator's result. */
const asRouted = (raw: Record<string, unknown>, validate: (search: Record<string, unknown>) => object) =>
  Object.fromEntries(Object.entries({ ...raw, ...validate(raw) }).filter(([, value]) => value !== undefined));

describe('the Links route’s search', () => {
  it('reads filters, a sort and columns on a link’s own columns and on either end’s', () => {
    expect(
      validateLinksSearch({
        filter: [
          { column: 'to.statusClass', op: 'in', values: ['4xx', '5xx'] },
          { column: 'from.depth', op: 'gte', value: 2 },
          { column: 'kind', op: 'in', values: ['hyperlink'] },
          { column: 'to.id', op: 'eq', value: 42 },
        ],
        sort: [{ column: 'to.statusCode', desc: true }],
        cols: ['from.url', 'to.url', 'follow', 'to.statusCode'],
        q: 'rattan',
        inspect: 7,
      }),
    ).toEqual({
      view: undefined,
      filter: [
        { column: 'to.statusClass', op: 'in', values: ['4xx', '5xx'] },
        { column: 'from.depth', op: 'gte', value: 2 },
        { column: 'kind', op: 'in', values: ['hyperlink'] },
        { column: 'to.id', op: 'eq', value: 42 },
      ],
      sort: [{ column: 'to.statusCode', desc: true }],
      cols: ['from.url', 'to.url', 'follow', 'to.statusCode'],
      q: 'rattan',
      inspect: 7,
    });
  });

  it('sorts by one key, as the links grid does', () => {
    expect(validateLinksSearch({ sort: '-to.statusCode,to.url,id' }).sort).toEqual([
      { column: 'to.statusCode', desc: true },
    ]);
  });

  it('drops what isn’t a links column, instead of breaking the view', () => {
    const search = validateLinksSearch({
      filter: [
        { column: 'via.url', op: 'contains', value: 'x' },
        { column: 'to.', op: 'isEmpty' },
        { column: 'to.a.b', op: 'isEmpty' },
        { column: 'to.statusCode; DROP', op: 'isEmpty' },
        { column: 'text', op: 'isEmpty' },
      ],
      sort: [{ column: 'to..url' }],
      cols: ['to.url', 'to.url', 'from/url', 'kind'],
    });
    expect(search.filter).toEqual([{ column: 'text', op: 'isEmpty' }]);
    expect(search.sort).toBeUndefined();
    expect(search.cols).toEqual(['to.url', 'kind']);
  });

  it('never takes a check’s group: that is the URL grid’s', () => {
    expect(
      validateLinksSearch({
        filter: [{ column: 'to.check', op: 'in', values: ['x'], group: { detail: null, relatedUrlId: 1 } }],
      }).filter,
    ).toBeUndefined();
  });

  it('keeps which end of its rows the inspector is on, with the URL it’s on', () => {
    expect(validateLinksSearch({ inspect: 7, end: 'from' })).toMatchObject({ inspect: 7, end: 'from' });
    // The target is the default: it has no word in the address.
    expect(asRouted({ inspect: 7, end: 'to' }, validateLinksSearch)).toEqual({ inspect: 7 });
    expect(validateLinksSearch({ inspect: 7 }).end).toBeUndefined();
    // Nothing inspected, nothing to be an end of; and nothing raw gets through.
    expect(asRouted({ end: 'from' }, validateLinksSearch)).toEqual({});
    expect(asRouted({ inspect: 'x', end: 'from' }, validateLinksSearch)).toEqual({});
    expect(asRouted({ inspect: 7, end: ['from'] }, validateLinksSearch)).toEqual({ inspect: 7 });
  });

  it('lets nothing raw through', () => {
    const raw = { filter: 'kind', cols: [{ a: 1 }], inspect: 'x', other: '1' };
    expect(asRouted(raw, validateLinksSearch)).toEqual({});
  });

  it('leaves the URL grid’s search as it was: a dotted column isn’t one of its', () => {
    const search = validateGridSearch({
      filter: [{ column: 'to.statusClass', op: 'in', values: ['4xx'] }],
      sort: [{ column: 'to.url' }, { column: 'url' }],
      cols: ['to.url', 'url'],
    });
    expect(search.filter).toBeUndefined();
    expect(search.sort).toEqual([{ column: 'url', desc: false }]);
    expect(search.cols).toEqual(['url']);
  });
});
