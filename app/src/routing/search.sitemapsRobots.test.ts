import { describe, expect, it } from 'vitest';
import { validateRobotsSearch, validateSitemapsSearch } from './search';

describe('the Sitemaps route’s search', () => {
  it('is the URL grid’s own, checked the same way, with the sitemap a link names by its address', () => {
    const search = validateSitemapsSearch({
      filter: [{ column: 'fetched', op: 'is', value: false }],
      sort: '-statusCode',
      q: 'contact',
      inspect: '7',
      url: 'https://example.com/sitemap.xml',
    });
    expect(search).toMatchObject({
      filter: [{ column: 'fetched', op: 'is', value: false }],
      sort: [{ column: 'statusCode', desc: true }],
      q: 'contact',
      inspect: 7,
      url: 'https://example.com/sitemap.xml',
    });
  });

  it('drops what it can’t use, and anything raw the address carries besides', () => {
    const search = validateSitemapsSearch({ filter: 'nonsense', inspect: -1, url: ['x'], line: 3, other: 'kept?' });
    expect(search.filter).toBeUndefined();
    expect(search.inspect).toBeUndefined();
    expect(search.url).toBeUndefined();
    expect(search).toHaveProperty('line', undefined);
    expect(search).toHaveProperty('other', undefined);
  });
});

describe('the robots.txt route’s search', () => {
  it('takes the line chosen, or the reason, and the inspected URL', () => {
    expect(validateRobotsSearch({ line: '6', inspect: 21 })).toMatchObject({ line: 6, inspect: 21 });
    expect(validateRobotsSearch({ reason: 'unknown' })).toMatchObject({ reason: 'unknown' });
    expect(validateRobotsSearch({ reason: 'unreachable' })).toMatchObject({ reason: 'unreachable' });
  });

  it('lets a line win over a reason: the list can be narrowed by one of them', () => {
    expect(validateRobotsSearch({ line: 6, reason: 'unknown' })).toMatchObject({ line: 6, reason: undefined });
  });

  it('takes the conflicts, which a line or a reason wins over, and only as true', () => {
    expect(validateRobotsSearch({ conflict: true })).toMatchObject({ conflict: true });
    expect(validateRobotsSearch({ conflict: true, line: 6 })).toMatchObject({ line: 6, conflict: undefined });
    expect(validateRobotsSearch({ conflict: true, reason: 'unknown' })).toMatchObject({
      reason: 'unknown',
      conflict: undefined,
    });
    for (const conflict of [false, 'true', 1, null])
      expect(validateRobotsSearch({ conflict }).conflict).toBeUndefined();
  });

  it('drops a line that isn’t a positive whole number and a reason the engine doesn’t have', () => {
    for (const line of [0, -2, 1.5, 'six', null]) expect(validateRobotsSearch({ line }).line).toBeUndefined();
    // `rule` is the engine's too, but it's what every line's URLs are: a line narrows to them, never the word.
    for (const reason of ['rule', 'bogus', 3]) expect(validateRobotsSearch({ reason }).reason).toBeUndefined();
  });

  it('keeps nothing raw', () => {
    expect(validateRobotsSearch({ filter: [{ column: 'x', op: 'is', value: true }], q: 'a' })).toEqual({
      filter: undefined,
      q: undefined,
      line: undefined,
      reason: undefined,
      inspect: undefined,
    });
  });
});
