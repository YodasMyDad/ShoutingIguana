import { describe, expect, it } from 'vitest';
import type { GridFilter, LinkGroupsPage } from '../../api/grids';
import {
  DEFAULT_GROUP_SORT,
  groupSortOf,
  groupTextLine,
  groupedReadWords,
  groupedTotalsWords,
  groupsAsGridPage,
  imageFileLine,
  isImageView,
  nextGroupSort,
  otherTextsWords,
  positionsWords,
  showAllLinksWords,
  sortForMode,
  targetLinksFilters,
} from './linkGroups';

const FACTS = {
  statusCode: 200,
  outcome: 'success' as const,
  contentType: 'image/jpeg',
  sizeBytes: 1_887_437,
  pixelWidth: 4000,
  pixelHeight: 3000,
  final: null,
  indexability: null,
  canonical: null,
};

describe('a grouped view’s order', () => {
  it('is the most linked from pages first unless the route names a key the grouped view takes', () => {
    expect(groupSortOf(undefined)).toEqual(DEFAULT_GROUP_SORT);
    expect(DEFAULT_GROUP_SORT).toEqual({ column: 'pages', desc: true });
    expect(groupSortOf([{ column: 'id', desc: true }])).toEqual(DEFAULT_GROUP_SORT);
    expect(groupSortOf([{ column: 'links', desc: false }])).toEqual({ column: 'links', desc: false });
    expect(groupSortOf([{ column: 'to.sizeBytes', desc: true }])).toEqual({ column: 'to.sizeBytes', desc: true });
  });

  it('keeps a sort by the target across the modes and drops one the other mode doesn’t have', () => {
    expect(sortForMode([{ column: 'to.url', desc: false }], true)).toEqual([{ column: 'to.url', desc: false }]);
    expect(sortForMode([{ column: 'to.url', desc: true }], false)).toEqual([{ column: 'to.url', desc: true }]);
    expect(sortForMode([{ column: 'id', desc: true }], true)).toBeUndefined();
    expect(sortForMode([{ column: 'pages', desc: true }], false)).toBeUndefined();
    expect(sortForMode([{ column: 'links', desc: false }], true)).toEqual([{ column: 'links', desc: false }]);
    expect(sortForMode(undefined, true)).toBeUndefined();
  });

  it('turns a count or a size the largest first, then the other way, then back to the default', () => {
    const pages = DEFAULT_GROUP_SORT;
    expect(nextGroupSort(pages, 'pages')).toEqual([{ column: 'pages', desc: false }]);
    expect(nextGroupSort({ column: 'pages', desc: false }, 'pages')).toEqual([]);
    expect(nextGroupSort(pages, 'links')).toEqual([{ column: 'links', desc: true }]);
    expect(nextGroupSort(pages, 'to.sizeBytes')).toEqual([{ column: 'to.sizeBytes', desc: true }]);
    // A to Z first for the rest.
    expect(nextGroupSort(pages, 'to.url')).toEqual([{ column: 'to.url', desc: false }]);
    expect(nextGroupSort({ column: 'to.url', desc: false }, 'to.url')).toEqual([{ column: 'to.url', desc: true }]);
    expect(nextGroupSort({ column: 'to.url', desc: true }, 'to.url')).toEqual([]);
  });
});

describe('a grouped row in words', () => {
  it('says where the links sit, and the texts besides the one shown', () => {
    expect(positionsWords(['navigation', 'footer'])).toBe('Navigation, Footer');
    expect(positionsWords([])).toBe('');
    expect(otherTextsWords({ text: 'Shop now', texts: 1 })).toBeNull();
    expect(otherTextsWords({ text: 'Shop now', texts: 3 })).toBe('+2 other texts');
    expect(otherTextsWords({ text: 'Shop now', texts: 2 })).toBe('+1 other text');
    // None shown means none of its links has a text: "no text" is all they have, never another text.
    expect(otherTextsWords({ text: null, texts: 1 })).toBeNull();
    expect(groupTextLine({ text: null, texts: 1 })).toBe('None');
    expect(groupTextLine({ text: 'Shop now', texts: 3 })).toBe('Shop now +2 other texts');
    expect(groupTextLine({ text: null, texts: 0 })).toBe('None');
  });

  it('counts a view as its links on its pages, and says an image’s file in one line', () => {
    expect(groupedTotalsWords(312, 265)).toBe('312 links on 265 pages');
    expect(groupedTotalsWords(1, 1)).toBe('1 link on 1 page');
    expect(imageFileLine({ target: FACTS })).toBe('1.8 MB · 4000×3000 px');
    expect(imageFileLine({ target: { ...FACTS, sizeBytes: null, pixelHeight: null } })).toBe('');
    expect(showAllLinksWords(265, false)).toBe('Show all 265 links to this target, one a row');
    expect(showAllLinksWords(3, true)).toBe('Show all 3 uses of this image, one a row');
    expect(showAllLinksWords(1, false)).toBe('Show the one link to this target, as a row');
    expect(showAllLinksWords(1, true)).toBe('Show the one use of this image, as a row');
    expect(groupedReadWords(5_053_909)).toBe(
      'Reading all 5,053,909 links to group them by target: each one is read once.',
    );
    expect(groupedReadWords(null)).toBe('Reading every link to group them by target: each one is read once.');
  });
});

describe('a view of images', () => {
  it('is one whose links are images alone', () => {
    expect(isImageView([{ column: 'kind', op: 'in', values: ['image'] }])).toBe(true);
    expect(
      isImageView([
        { column: 'from.id', op: 'eq', value: 4 },
        { column: 'kind', op: 'in', values: ['image'] },
      ]),
    ).toBe(true);
    expect(isImageView([{ column: 'kind', op: 'in', values: ['image', 'hyperlink'] }])).toBe(false);
    expect(isImageView([{ column: 'kind', op: 'notIn', values: ['image'] }])).toBe(false);
    expect(isImageView([])).toBe(false);
  });
});

describe('one target’s links', () => {
  it('are the view’s filters with the target named once', () => {
    const view: GridFilter[] = [{ column: 'to.statusClass', op: 'in', values: ['4xx'] }];
    expect(targetLinksFilters(view, 9)).toEqual([...view, { column: 'to.id', op: 'eq', value: 9 }]);
    expect(targetLinksFilters(targetLinksFilters(view, 9), 9)).toEqual([
      ...view,
      { column: 'to.id', op: 'eq', value: 9 },
    ]);
  });

  it('come as grid pages that keep what the view holds in all', () => {
    const page: LinkGroupsPage = {
      total: 9,
      links: 312,
      pages: 265,
      offset: 0,
      sort: DEFAULT_GROUP_SORT,
      rows: [],
      summary: { available: true, reason: null },
    };
    expect(groupsAsGridPage(page)).toMatchObject({
      matching: 9,
      total: 9,
      offset: 0,
      links: 312,
      pages: 265,
      next: null,
    });
  });
});
