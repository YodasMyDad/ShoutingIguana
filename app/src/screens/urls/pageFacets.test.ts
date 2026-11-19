// The URL grid's facets about pages (the UX audit of 2026-11-19, D-10 and D-11).
import { describe, expect, it } from 'vitest';
import type { GridFilter } from '../../api/grids';
import {
  CONTENT_FACETS,
  PAGE_TYPE,
  checkFilter,
  contentFacetValues,
  indexabilityCountFilters,
  isNotIndexable,
  notIndexableValues,
  tickedIndexability,
  withIndexability,
} from './pageFacets';

const DEPTH: GridFilter = { column: 'depth', op: 'eq', value: 2 };
const IMAGES: GridFilter = { column: 'type', op: 'in', values: ['image'] };
const indexability = (...values: string[]): GridFilter => ({ column: 'indexability', op: 'in', values });

describe('the Indexability facet', () => {
  it('takes several values in one filter, and answers with pages where the view names no type', () => {
    const one = withIndexability([DEPTH], ['noindex']);
    expect(one).toEqual([DEPTH, PAGE_TYPE, indexability('noindex')]);
    expect(tickedIndexability(one)).toEqual(['noindex']);
    // A second reason joins the first: one list, never a trip a reason.
    const two = withIndexability(one, ['noindex', 'canonicalised']);
    expect(two).toEqual([DEPTH, PAGE_TYPE, indexability('noindex', 'canonicalised')]);
    // The last one unticked: the filter goes, and the view's other filters stay.
    expect(withIndexability(two, [])).toEqual([DEPTH, PAGE_TYPE]);
  });

  it('leaves the types the view already names: its own choice of what to list', () => {
    expect(withIndexability([IMAGES], ['clientError'])).toEqual([IMAGES, indexability('clientError')]);
    expect(indexabilityCountFilters([IMAGES, indexability('none')])).toEqual([IMAGES]);
  });

  it('counts its values without its own filter, among pages, so a second value can be added', () => {
    expect(indexabilityCountFilters([DEPTH, indexability('noindex')])).toEqual([DEPTH, PAGE_TYPE]);
    expect(indexabilityCountFilters([])).toEqual([PAGE_TYPE]);
  });

  it('offers every reason at once: “Not indexable” is all the values but Indexable', () => {
    const values = ['none', 'noindex', 'canonicalised', 'redirected', 'null'];
    expect(notIndexableValues(values)).toEqual(['noindex', 'canonicalised', 'redirected']);
    expect(isNotIndexable(['redirected', 'noindex', 'canonicalised'], values)).toBe(true);
    expect(isNotIndexable(['noindex'], values)).toBe(false);
    expect(isNotIndexable([], ['none'])).toBe(false);
  });
});

describe('the Title, Description and H1 facets', () => {
  it('list what the checks found, by the analysis’s counts, a zero included', () => {
    const title = CONTENT_FACETS.find((facet) => facet.id === 'title')!;
    const values = contentFacetValues(
      title,
      [
        { checkId: 'title-duplicate', count: 12 },
        { checkId: 'title-too-long', count: 40 },
      ],
      [checkFilter('title-duplicate')],
    );
    expect(values?.map((value) => [value.words, value.count, value.applied])).toEqual([
      ['Missing', 0, false],
      ['Duplicate', 12, true],
      ['Too long', 40, false],
      ['Too short', 0, false],
      ['More than one', 0, false],
    ]);
    expect(values?.[1]?.filter).toEqual({ column: 'check', op: 'in', values: ['title-duplicate'] });
    // Until the counts are read: counting, not an empty list.
    expect(contentFacetValues(title, undefined, [])).toBeNull();
  });

  it('cover missing and duplicate for all three, by the checks’ own ids', () => {
    const checks = CONTENT_FACETS.flatMap((facet) => facet.values.map((value) => value.check));
    expect(checks).toEqual(
      expect.arrayContaining([
        'title-missing',
        'title-duplicate',
        'description-missing',
        'description-duplicate',
        'h1-missing',
        'h1-duplicate',
        'h1-multiple',
      ]),
    );
  });
});
