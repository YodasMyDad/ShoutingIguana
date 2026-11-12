import { describe, expect, it } from 'vitest';
import { createMemoryHistory } from '@tanstack/react-router';
import { validateReportSearch } from '../routing/search';
import { advancedOnlyScreen, simpleLanding } from './modeRoutes';
import { createQueryClient } from './queryClient';
import { createAppRouter } from './router';

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

describe('the search params round 4’s screens read', () => {
  it.each([
    [`${CRAWL}/overview?score=1`, { score: 1 }],
    [`${CRAWL}/issues/title-too-long?severity=error&q=title`, { severity: ['error'], q: 'title' }],
  ])('%s keeps them, before any Simple screen exists', async (path, search) => {
    const router = createAppRouter(createQueryClient(), createMemoryHistory({ initialEntries: [path] }));
    await router.load();
    // The search as the route validated it: what its screen reads.
    expect(router.state.matches.at(-1)?.search).toMatchObject(search);
  });

  it('keeps the Report’s cut, which its route checks while the Report has no screen yet', () => {
    expect(validateReportSearch({ cut: 'summary' })).toMatchObject({ cut: 'summary' });
    expect(validateReportSearch({ cut: 'everything' }).cut).toBeUndefined();
  });
});
