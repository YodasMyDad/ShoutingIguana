import { describe, expect, it } from 'vitest';
import { preferredFixCase } from './fixCase';

describe('preferredFixCase', () => {
  it('takes a link around an image for the image case', () => {
    expect(preferredFixCase('empty-anchor-text', [['imageLink']])).toBe('image-link');
    expect(preferredFixCase('empty-anchor-text', [['samePage', 'imageLink']])).toBe('image-link');
  });

  it('takes any other link with nothing to read for an icon or an inline SVG', () => {
    expect(preferredFixCase('empty-anchor-text', [[]])).toBe('icon-link');
    expect(preferredFixCase('empty-anchor-text', [['samePage']])).toBe('icon-link');
    expect(preferredFixCase('empty-anchor-text', [undefined])).toBe('icon-link');
  });

  it('goes by what most of the links are, and by the icon when they tie', () => {
    expect(preferredFixCase('empty-anchor-text', [['imageLink'], ['imageLink'], []])).toBe('image-link');
    expect(preferredFixCase('empty-anchor-text', [['imageLink'], [], ['noFollow']])).toBe('icon-link');
    expect(preferredFixCase('empty-anchor-text', [['imageLink'], []])).toBe('icon-link');
  });

  it('chooses nothing with no link to go by, or for another check', () => {
    expect(preferredFixCase('empty-anchor-text', [])).toBeUndefined();
    expect(preferredFixCase('image-missing-alt', [['imageLink']])).toBeUndefined();
    expect(preferredFixCase('uncrawlable-links', [[]])).toBeUndefined();
  });
});
