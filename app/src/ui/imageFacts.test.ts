import { describe, expect, it } from 'vitest';
import { isBrokenImage } from './imageFacts';

describe('an image known to be broken', () => {
  it('answered 4xx or 5xx, or didn’t answer, after any redirects', () => {
    expect(isBrokenImage({ statusCode: 404, outcome: 'clientError' })).toBe(true);
    expect(isBrokenImage({ statusCode: 503, outcome: 'serverError' })).toBe(true);
    expect(isBrokenImage({ statusCode: null, outcome: 'timeout' })).toBe(true);
    expect(isBrokenImage({ statusCode: null, outcome: 'networkError' })).toBe(true);
    expect(
      isBrokenImage({ statusCode: 301, outcome: 'redirect', final: { statusCode: 404, outcome: 'clientError' } }),
    ).toBe(true);
  });

  it('isn’t one that answered, nor one the crawl never asked for', () => {
    expect(isBrokenImage({ statusCode: 200, outcome: 'success' })).toBe(false);
    expect(
      isBrokenImage({ statusCode: 301, outcome: 'redirect', final: { statusCode: 200, outcome: 'success' } }),
    ).toBe(false);
    expect(isBrokenImage({ statusCode: null, outcome: 'robotsDisallowed' })).toBe(false);
    expect(isBrokenImage({ statusCode: null, outcome: 'notChecked' })).toBe(false);
    expect(isBrokenImage({ statusCode: null, outcome: null })).toBe(false);
    expect(isBrokenImage({ statusCode: undefined })).toBe(false);
  });
});
