import { describe, expect, it } from 'vitest';
import { textWidth, twoLineWidth } from './textWidth';

// jsdom has no canvas: every character is 8 px, so the widths are the lines' lengths.
describe('a name on at most two lines', () => {
  it('breaks where its wider line is narrowest: "Blocked by" over "robots.txt"', () => {
    expect(textWidth('robots.txt', '12px sans-serif')).toBe(80);
    expect(twoLineWidth('Blocked by robots.txt', '12px sans-serif')).toBe(80);
  });

  it('breaks after a hyphen, keeping it on the first line', () => {
    expect(twoLineWidth('Non-indexable', '12px sans-serif')).toBe(9 * 8);
  });

  it('is one line for one word', () => {
    expect(twoLineWidth('Listed', '12px sans-serif')).toBe(6 * 8);
    expect(twoLineWidth('Not crawled', '12px sans-serif')).toBe(7 * 8);
  });
});
