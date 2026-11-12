import { describe, expect, it } from 'vitest';
import { advancedOnlyScreen, simpleLanding } from './modeRoutes';

const CRAWL = '/crawls/0123456789abcdef';

describe('which screens are Advanced only', () => {
  it.each([
    [`${CRAWL}/urls`, 'URLs'],
    [`${CRAWL}/links`, 'Links'],
    [`${CRAWL}/sitemaps`, 'Sitemaps'],
    [`${CRAWL}/sitemaps/3`, 'Sitemaps'],
    [`${CRAWL}/robots/https%3A%2F%2Fexample.com`, 'robots.txt'],
    [`${CRAWL}/settings`, 'the crawl’s settings'],
    ['/settings/crawl-defaults', 'Crawl defaults'],
    ['/settings/analysis', 'Analysis'],
    ['/settings/shortcuts', 'Shortcuts'],
    ['/settings/diagnostics', 'Diagnostics'],
  ])('%s is %s', (path, name) => {
    expect(advancedOnlyScreen(path)).toBe(name);
  });

  it.each([
    '/',
    '/new',
    CRAWL,
    `${CRAWL}/overview`,
    `${CRAWL}/issues`,
    `${CRAWL}/issues/title-too-long`,
    `${CRAWL}/report`,
    '/settings',
    '/settings/data',
    '/settings/updates',
    '/settings/about',
  ])('%s is in both modes', (path) => {
    expect(advancedOnlyScreen(path)).toBeNull();
  });
});

describe('where switching to Simple lands', () => {
  it('stays on a screen Simple has', () => {
    expect(simpleLanding(`${CRAWL}/issues/title-too-long`)).toBeNull();
    expect(simpleLanding('/new')).toBeNull();
    expect(simpleLanding('/settings/about')).toBeNull();
  });

  it('goes to the crawl’s Summary from its Advanced-only sections, and to General from Settings’', () => {
    expect(simpleLanding(`${CRAWL}/links`)).toEqual({
      to: '/crawls/$crawl/overview',
      params: { crawl: CRAWL.slice(8) },
    });
    expect(simpleLanding('/settings/shortcuts')).toEqual({
      to: '/settings/{-$section}',
      params: { section: undefined },
    });
  });
});
