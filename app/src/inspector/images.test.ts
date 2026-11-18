import { describe, expect, it } from 'vitest';
import type { PageImage } from '../api/inspector';
import { imageFileWords, imageUsesFilters, imagesSummaryWords, isLargeImage, shownWords } from './images';

const image = (sizeBytes: number | null, facts: Partial<PageImage> = {}): PageImage => ({
  id: 9,
  url: 'https://example.com/img/hero.jpg',
  links: 1,
  text: 'A teak bench',
  positions: ['content'],
  target: {
    statusCode: 200,
    outcome: 'success',
    contentType: 'image/jpeg',
    sizeBytes,
    pixelWidth: 4000,
    pixelHeight: 3000,
    final: null,
    indexability: null,
    canonical: null,
  },
  ...facts,
});

const LIMIT = 200 * 1024;

describe('a page’s images', () => {
  it('flag a file over the analysis’s limit for a large image, after any redirects', () => {
    expect(isLargeImage(image(LIMIT + 1), LIMIT)).toBe(true);
    expect(isLargeImage(image(LIMIT), LIMIT)).toBe(false);
    expect(isLargeImage(image(null), LIMIT)).toBe(false);
  });

  it('say how many there are and how many are large', () => {
    expect(imagesSummaryWords({ total: 0, rows: [] }, LIMIT)).toBe('No images');
    expect(imagesSummaryWords({ total: 1, rows: [image(1_000)] }, LIMIT)).toBe('1 image');
    expect(imagesSummaryWords({ total: 3, rows: [image(2_202_009), image(LIMIT + 5), image(1_000)] }, LIMIT)).toBe(
      '3 images, 2 over 200 KB',
    );
  });

  it('say how the page shows each, and what its file is', () => {
    expect(shownWords(image(1))).toBe('Content');
    expect(shownWords(image(1, { links: 2, positions: ['header', 'footer'] }))).toBe('2 times · Header, Footer');
    expect(shownWords(image(1, { positions: [] }))).toBe('');
    expect(imageFileWords(image(2_202_009))).toBe('2.1 MB · 4000×3000 px');
  });

  it('lead to every page that shows one, in Links', () => {
    expect(imageUsesFilters(9)).toEqual([
      { column: 'to.id', op: 'eq', value: 9 },
      { column: 'selfLink', op: 'is', value: false },
      { column: 'kind', op: 'in', values: ['image'] },
    ]);
  });
});
