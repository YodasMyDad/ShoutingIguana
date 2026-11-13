// "Show the page in URLs" / "Show the URL in URLs" build `url eq <url>` (oneUrlFilter), but the URL
// route's validator trims every text value to 1,000 characters (routing/search.ts MAX_TEXT, parseFilters' default
// case). A crawled URL longer than that lands on a filter for a URL that doesn't exist: an empty grid.
import { describe, expect, it } from 'vitest';
import { validateGridSearch } from '../../routing/search';
import { oneUrlFilter } from './urlPlaces';

describe('one URL in the URL grid', () => {
  it('names a URL longer than a filter’s text by its id, so the route keeps it whole and it still lands on the URL', () => {
    const url = `https://example.com/search?${'facet=colour-red&'.repeat(70)}page=2`;
    expect(url.length).toBeGreaterThan(1_000);
    const search = validateGridSearch({ filter: oneUrlFilter(url, 7), inspect: 7 });
    expect(search.filter).toEqual([{ column: 'id', op: 'eq', value: 7 }]);
  });

  it('names any other URL by its text, which its chip reads', () => {
    const search = validateGridSearch({ filter: oneUrlFilter('https://example.com/about/', 7), inspect: 7 });
    expect(search.filter).toEqual([{ column: 'url', op: 'eq', value: 'https://example.com/about/' }]);
  });
});
