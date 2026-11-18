import { describe, expect, it } from 'vitest';
import type { LinkRow } from '../../api/grids';
import { LINK_COLUMNS } from '../../api/mock/linkGrid';
import { validateLinksSearch } from '../../routing/search';
import {
  DEFAULT_LINK_COLUMNS,
  isLinkColumn,
  linkChooserGroups,
  linkExportShown,
  linkGridColumns,
  linkQueryColumns,
  pixelsCellWords,
  shownLinkColumns,
} from './linkColumns';
import { PIXELS_COLUMN, linkColumnName, linkColumnShortName } from './linkWords';
import { IMAGE_COLUMNS, IMAGE_SORT, LINK_PRESETS, activePreset, imagesOfPage, presetSearch } from './presets';

const IMAGES = LINK_PRESETS.find((preset) => preset.id === 'images');
const BROKEN = LINK_PRESETS.find((preset) => preset.id === 'broken');

describe('the Images preset', () => {
  it('is the image links, the largest file first, with the image’s own columns', () => {
    expect(IMAGES).toMatchObject({
      title: 'Images',
      filters: [{ column: 'kind', op: 'in', values: ['image'] }],
      columns: ['to.url', 'to.sizeBytes', PIXELS_COLUMN, 'text', 'from.url', 'position'],
      sort: [{ column: 'to.sizeBytes', desc: true }],
    });
    expect(activePreset([{ column: 'kind', op: 'in', values: ['image'] }], undefined)?.id).toBe('images');
    // Every column it shows is one the engine has.
    expect(IMAGE_COLUMNS.every((id) => isLinkColumn(id, LINK_COLUMNS))).toBe(true);
    expect(shownLinkColumns(IMAGE_COLUMNS as string[], LINK_COLUMNS)).toEqual(IMAGE_COLUMNS);
  });

  it('puts its columns and order in the route, and another preset after it puts the default columns back', () => {
    if (!IMAGES || !BROKEN) throw new Error('A preset is missing');
    const images = presetSearch(IMAGES, { cols: ['from.url', 'to.url'] });
    expect(images).toEqual({ cols: [...IMAGE_COLUMNS], filter: [...IMAGES.filters], sort: [...IMAGE_SORT] });
    // From the Images view, the columns go with it; columns the user chose stay with any preset without its own.
    expect(presetSearch(BROKEN, { cols: [...IMAGE_COLUMNS] })).toEqual({ filter: [...BROKEN.filters] });
    expect(presetSearch(BROKEN, { cols: ['from.url', 'to.url'] })).toEqual({
      cols: ['from.url', 'to.url'],
      filter: [...BROKEN.filters],
    });
    // The mode stays.
    expect(presetSearch(IMAGES, { by: 'target' }).by).toBe('target');
    expect(presetSearch(BROKEN, { by: 'target' })).toEqual({ filter: [...BROKEN.filters], by: 'target' });
  });

  it('opens one page’s images from an image check, as the inspector lists them', () => {
    expect(imagesOfPage(12)).toEqual({
      cols: [...IMAGE_COLUMNS],
      filter: [
        { column: 'from.id', op: 'eq', value: 12 },
        { column: 'selfLink', op: 'is', value: false },
        { column: 'kind', op: 'in', values: ['image'] },
      ],
      sort: [{ column: 'to.sizeBytes', desc: true }],
    });
  });
});

describe('the pixel size column', () => {
  const row = (width: number | null, height: number | null) =>
    ({ id: 1, 'to.pixelWidth': width, 'to.pixelHeight': height }) as LinkRow;

  it('is one column of the target’s width and height, asked for and exported as the two', () => {
    expect(isLinkColumn(PIXELS_COLUMN, LINK_COLUMNS)).toBe(true);
    expect(isLinkColumn('to.pixelWidth', LINK_COLUMNS)).toBe(false);
    expect(linkQueryColumns([PIXELS_COLUMN])).toContain('to.pixelWidth');
    expect(linkQueryColumns([PIXELS_COLUMN])).toContain('to.pixelHeight');
    expect(linkQueryColumns([PIXELS_COLUMN])).not.toContain(PIXELS_COLUMN);
    // An image view asks how each image answered, so only a broken one's thumbnail is drawn broken.
    expect(linkQueryColumns(IMAGE_COLUMNS, true)).toEqual(expect.arrayContaining(['to.statusCode', 'to.outcome']));
    expect(linkQueryColumns(IMAGE_COLUMNS)).not.toContain('to.outcome');
    expect(linkExportShown(['to.url', PIXELS_COLUMN, 'text'])).toEqual([
      'to.url',
      'to.pixelWidth',
      'to.pixelHeight',
      'text',
    ]);
    expect(pixelsCellWords(row(896, 600))).toBe('896×600 px');
    expect(pixelsCellWords(row(896, null))).toBe('');
    expect(linkColumnName(PIXELS_COLUMN)).toBe('Target pixel size');
    expect(linkColumnShortName(PIXELS_COLUMN)).toBe('Pixel size');
  });

  it('is in the chooser among the target’s columns, where its width would be', () => {
    const target = linkChooserGroups(LINK_COLUMNS, true).find((group) => group.id === 'to');
    const ids = target?.columns.map((column) => column.id) ?? [];
    expect(ids).toContain(PIXELS_COLUMN);
    expect(ids).not.toContain('to.pixelWidth');
    expect(ids).not.toContain('to.pixelHeight');
  });

  it('names an image view’s columns for the image, not the target', () => {
    const context = { siteOrigin: 'https://example.com', onOpen: () => undefined, sort: [] };
    const plain = linkGridColumns(IMAGE_COLUMNS, LINK_COLUMNS, context).map((column) => column.header);
    const images = linkGridColumns(IMAGE_COLUMNS, LINK_COLUMNS, { ...context, images: true }).map(
      (column) => column.header,
    );
    expect(plain).toEqual([
      'Target URL',
      'Target size',
      'Target pixel size',
      'Anchor or alt text',
      'Source URL',
      'Position',
    ]);
    expect(images).toEqual(['Image', 'Size', 'Pixel size', 'Alt text', 'Shown on', 'Position']);
    expect(DEFAULT_LINK_COLUMNS).not.toContain(PIXELS_COLUMN);
  });
});

describe('the Links route', () => {
  it('keeps one row a target, and nothing else for its mode', () => {
    expect(validateLinksSearch({ by: 'target' }).by).toBe('target');
    expect(validateLinksSearch({ by: 'source' }).by).toBeUndefined();
    expect(validateLinksSearch({}).by).toBeUndefined();
    expect(validateLinksSearch({ cols: [PIXELS_COLUMN] }).cols).toEqual([PIXELS_COLUMN]);
  });
});
