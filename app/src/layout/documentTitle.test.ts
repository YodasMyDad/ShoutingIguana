import { describe, expect, it } from 'vitest';
import { documentTitle } from './documentTitle';

const CRAWL = '/crawls/0123456789abcdef';

describe('documentTitle', () => {
  it('names the section, then the site, then the app (gap 26)', () => {
    expect(documentTitle({ pathname: `${CRAWL}/issues`, mode: 'advanced', site: 'garden.co.uk' })).toBe(
      'Issues · garden.co.uk · Shouting Iguana',
    );
    expect(documentTitle({ pathname: `${CRAWL}/urls`, mode: 'advanced', site: 'garden.co.uk' })).toBe(
      'URLs · garden.co.uk · Shouting Iguana',
    );
    expect(documentTitle({ pathname: `${CRAWL}/robots/x`, mode: 'advanced', site: 'garden.co.uk' })).toBe(
      'robots.txt · garden.co.uk · Shouting Iguana',
    );
    expect(documentTitle({ pathname: `${CRAWL}/report`, mode: 'advanced', site: 'garden.co.uk' })).toBe(
      'Report · garden.co.uk · Shouting Iguana',
    );
  });

  it("says Simple's names in Simple", () => {
    expect(documentTitle({ pathname: `${CRAWL}/issues/title-missing`, mode: 'simple', site: 'garden.co.uk' })).toBe(
      'What to fix · garden.co.uk · Shouting Iguana',
    );
    expect(documentTitle({ pathname: `${CRAWL}/overview`, mode: 'simple', site: 'garden.co.uk' })).toBe(
      'Summary · garden.co.uk · Shouting Iguana',
    );
    expect(documentTitle({ pathname: '/new', mode: 'simple', site: null })).toBe('Check a website · Shouting Iguana');
  });

  it('names the pages outside a crawl', () => {
    expect(documentTitle({ pathname: '/', mode: 'advanced', site: null })).toBe('Home · Shouting Iguana');
    expect(documentTitle({ pathname: '/new', mode: 'advanced', site: null })).toBe('New crawl · Shouting Iguana');
    expect(documentTitle({ pathname: '/settings/updates', mode: 'advanced', site: null })).toBe(
      'Settings · Shouting Iguana',
    );
    expect(documentTitle({ pathname: '/nowhere', mode: 'advanced', site: null })).toBe('Shouting Iguana');
  });

  it('leaves out the site until the crawl is known', () => {
    expect(documentTitle({ pathname: `${CRAWL}/links`, mode: 'advanced', site: null })).toBe('Links · Shouting Iguana');
    expect(documentTitle({ pathname: CRAWL, mode: 'advanced', site: 'garden.co.uk' })).toBe(
      'garden.co.uk · Shouting Iguana',
    );
  });
});
