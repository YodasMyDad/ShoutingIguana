// Review proof: "Show the page in URLs" / "Show the URL in URLs" build `url eq <url>` (oneUrlFilter), but the URL
// route's validator trims every text value to 1,000 characters (routing/search.ts MAX_TEXT, parseFilters' default
// case). A crawled URL longer than that lands on a filter for a URL that doesn't exist: an empty grid.
import { describe, expect, it } from 'vitest';
import { validateGridSearch } from '../routing/search';
import { oneUrlFilter } from './urls/urlPlaces';

describe('review: one URL in the URL grid, by its text', () => {
  it('keeps a long URL whole, so `url eq` still matches it', () => {
    const url = `https://example.com/search?${'facet=colour-red&'.repeat(70)}page=2`;
    expect(url.length).toBeGreaterThan(1_000);
    const search = validateGridSearch({ filter: oneUrlFilter(url), inspect: 7 });
    expect(search.filter).toEqual([{ column: 'url', op: 'eq', value: url }]);
  });
});
