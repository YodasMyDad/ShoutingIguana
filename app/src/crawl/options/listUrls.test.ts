import { describe, expect, it } from 'vitest';
import { readList } from './listUrls';

describe('a list of addresses', () => {
  it('reads a text list in order and keeps one of each', () => {
    expect(readList('https://example.com/b\n\nhttps://example.com/a\nhttps://example.com/a\n').urls).toEqual([
      'https://example.com/b',
      'https://example.com/a',
    ]);
  });

  it('crawls a higher priority first', () => {
    const text = 'Priority,Url\n1,https://example.com/low\n9,"https://example.com/high"\n5,https://Example.com/low\n';
    expect(readList(text)).toEqual({
      urls: ['https://example.com/high', 'https://Example.com/low'],
      error: null,
    });
  });

  it('refuses a priority that is not a number', () => {
    expect(readList('Url,Priority\nhttps://example.com/a,soon\n').error).toBe(
      "Priority 'soon' in the list isn't a whole number.",
    );
  });

  it('is nothing when the box is empty', () => {
    expect(readList('  \n')).toEqual({ urls: [], error: null });
  });
});
