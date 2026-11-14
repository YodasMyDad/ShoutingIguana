import { describe, expect, it } from 'vitest';
import {
  MAX_COLUMN_WIDTH,
  MIN_COLUMN_WIDTH,
  applyColumnLayout,
  isEmptyLayout,
  movedOrder,
  readColumnLayout,
  readColumnLayouts,
  withOrder,
  withPinned,
  withWidth,
} from './columnLayout';
import { footWords } from './gridFoot';
import type { GridColumn } from './types';

const column = (id: string, width = 100, minWidth?: number): GridColumn<unknown> => ({
  id,
  header: id.toUpperCase(),
  width,
  ...(minWidth === undefined ? {} : { minWidth }),
  cell: () => id,
  text: () => id,
});
const ids = (columns: readonly GridColumn<unknown>[]) => columns.map((entry) => entry.id);

describe('a column layout', () => {
  it('changes nothing until the user lays a column out', () => {
    const columns = [column('a'), column('b')];
    expect(applyColumnLayout(columns, undefined)).toEqual(columns);
    expect(isEmptyLayout({ widths: {}, pinned: [] })).toBe(true);
  });

  it('keeps a width within the bounds and the column’s own least', () => {
    expect(withWidth(undefined, 'a', 10).widths).toEqual({ a: MIN_COLUMN_WIDTH });
    expect(withWidth(undefined, 'a', 99_999).widths).toEqual({ a: MAX_COLUMN_WIDTH });
    const laid = applyColumnLayout([column('url', 300, 240)], withWidth(undefined, 'url', 100));
    expect(laid[0]?.width).toBe(240);
  });

  it('puts a width back, and says nothing once nothing is set', () => {
    const set = withWidth(undefined, 'a', 180);
    expect(set).toEqual({ widths: { a: 180 } });
    expect(withWidth(set, 'a', null)).toEqual({});
  });

  it('puts pinned columns first, in the order they were pinned, and marks them', () => {
    const layout = withPinned(withPinned(undefined, 'c', true), 'b', true);
    const laid = applyColumnLayout([column('a'), column('b'), column('c')], layout);
    expect(ids(laid)).toEqual(['c', 'b', 'a']);
    expect(laid.map((entry) => entry.pinned === true)).toEqual([true, true, false]);
    expect(ids(applyColumnLayout([column('a'), column('b'), column('c')], withPinned(layout, 'c', false)))).toEqual([
      'b',
      'a',
      'c',
    ]);
  });

  it('keeps an order of its own only where the grid keeps it here', () => {
    const layout = withOrder(undefined, ['c', 'a', 'b']);
    const columns = [column('a'), column('b'), column('c'), column('d')];
    expect(ids(applyColumnLayout(columns, layout, true))).toEqual(['c', 'a', 'b', 'd']);
    // The URL grid's order is its route's: the layout's is ignored.
    expect(ids(applyColumnLayout(columns, layout, false))).toEqual(['a', 'b', 'c', 'd']);
  });

  it('places a column the order doesn’t name where it was among the others', () => {
    const layout = withOrder(undefined, ['b', 'a']);
    expect(ids(applyColumnLayout([column('a'), column('new'), column('b')], layout, true))).toEqual(['b', 'new', 'a']);
  });

  it('moves a column, never across the pinned columns’ edge', () => {
    expect(movedOrder(['a', 'b', 'c', 'd'], 'a', 2)).toEqual(['b', 'c', 'a', 'd']);
    expect(movedOrder(['a', 'b', 'c', 'd'], 'd', 0)).toEqual(['d', 'a', 'b', 'c']);
    // a and b are pinned: c can't go before them, and b can't go after c.
    expect(movedOrder(['a', 'b', 'c', 'd'], 'c', 0, ['a', 'b'])).toEqual(['a', 'b', 'c', 'd']);
    expect(movedOrder(['a', 'b', 'c', 'd'], 'b', 3, ['a', 'b'])).toEqual(['a', 'b', 'c', 'd']);
    expect(movedOrder(['a', 'b', 'c', 'd'], 'b', 0, ['a', 'b'])).toEqual(['b', 'a', 'c', 'd']);
    expect(movedOrder(['a', 'b'], 'x', 0)).toEqual(['a', 'b']);
  });

  it('reads back only what a layout can hold', () => {
    expect(readColumnLayout(null)).toBeUndefined();
    expect(
      readColumnLayout({ widths: { a: 120, 'b c': 90, d: 'wide', e: 3 }, pinned: ['a', 'a', 7], order: [] }),
    ).toEqual({ widths: { a: 120, e: MIN_COLUMN_WIDTH }, pinned: ['a'] });
    expect(
      readColumnLayouts({ urls: { widths: { url: 400 } }, 'BAD ID': { widths: { url: 400 } }, links: {}, list: [] }),
    ).toEqual({ urls: { widths: { url: 400 } } });
    expect(readColumnLayouts([])).toEqual({});
  });
});

describe('the footer’s place', () => {
  it('says where the cursor is, or what is selected', () => {
    expect(footWords(11, 264, 1, false, ['URL', 'URLs'])).toBe('URL 12 of 264');
    expect(footWords(null, 4_521, 0, false, ['URL', 'URLs'])).toBe('4,521 URLs');
    expect(footWords(2, 264, 3, false, ['row', 'rows'])).toBe('3 rows selected');
    expect(footWords(0, 112_577, 112_577, false, ['URL', 'URLs'])).toBe('112,577 URLs selected');
    expect(footWords(null, 0, 0, false, ['link', 'links'])).toBe('');
    expect(footWords(null, 0, 0, true, ['link', 'links'])).toBe('');
  });
});
