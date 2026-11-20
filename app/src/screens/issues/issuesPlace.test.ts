// Where a check was left (the 2026-11-19 UX round, rule 9): kept for the session by crawl and check.
import { afterEach, describe, expect, it } from 'vitest';
import { checkPlace, forgetCheckPlaces, keepCheckPlace } from './issuesPlace';

const CRAWL = '0123456789abcdef';
const OTHER_CRAWL = 'fedcba9876543210';

afterEach(() => forgetCheckPlaces());

describe('where a check was left', () => {
  it('is nowhere for a check not opened this session: its own default shows', () => {
    expect(checkPlace(CRAWL, 'internal-client-error')).toEqual({});
  });

  it('keeps more of it each time, leaving what was kept before', () => {
    keepCheckPlace(CRAWL, 'internal-client-error', { view: 'groups' });
    keepCheckPlace(CRAWL, 'internal-client-error', { cursor: { grid: 'g', row: 3, key: 'p|10' } });
    expect(checkPlace(CRAWL, 'internal-client-error')).toEqual({
      view: 'groups',
      cursor: { grid: 'g', row: 3, key: 'p|10' },
    });
    keepCheckPlace(CRAWL, 'internal-client-error', { view: 'list' });
    expect(checkPlace(CRAWL, 'internal-client-error')).toEqual({
      view: 'list',
      cursor: { grid: 'g', row: 3, key: 'p|10' },
    });
  });

  it('is each check’s own, and each crawl’s', () => {
    keepCheckPlace(CRAWL, 'internal-client-error', { view: 'groups' });
    expect(checkPlace(CRAWL, 'empty-anchor-text')).toEqual({});
    expect(checkPlace(OTHER_CRAWL, 'internal-client-error')).toEqual({});
  });

  it('keeps a page closed again as closed, not as it was', () => {
    keepCheckPlace(CRAWL, 'internal-client-error', { expanded: { group: '404 Not Found|', page: 10 } });
    keepCheckPlace(CRAWL, 'internal-client-error', { expanded: null });
    expect(checkPlace(CRAWL, 'internal-client-error').expanded).toBeNull();
  });

  it('forgets every check for the tests', () => {
    keepCheckPlace(CRAWL, 'internal-client-error', { view: 'groups' });
    keepCheckPlace(OTHER_CRAWL, 'title-too-long', { toggled: new Map([[0, true]]), toggledOf: 'k' });
    forgetCheckPlaces();
    expect(checkPlace(CRAWL, 'internal-client-error')).toEqual({});
    expect(checkPlace(OTHER_CRAWL, 'title-too-long')).toEqual({});
  });
});
