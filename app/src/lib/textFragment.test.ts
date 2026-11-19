import { describe, expect, it } from 'vitest';
import { urlAtText } from './textFragment';

describe('urlAtText', () => {
  it('adds the words as a text fragment', () => {
    expect(urlAtText('https://example.com/blog/post/', 'Buy Now')).toBe(
      'https://example.com/blog/post/#:~:text=Buy%20Now',
    );
  });

  it('encodes what the directive would read as syntax', () => {
    expect(urlAtText('https://example.com/', 'deluxe 7-piece set, & more')).toBe(
      'https://example.com/#:~:text=deluxe%207%2Dpiece%20set%2C%20%26%20more',
    );
  });

  it('keeps a fragment the URL already has', () => {
    expect(urlAtText('https://example.com/page#reviews', 'Read more')).toBe(
      'https://example.com/page#reviews:~:text=Read%20more',
    );
  });

  it('leaves the URL alone when there are no words', () => {
    expect(urlAtText('https://example.com/', '')).toBe('https://example.com/');
    expect(urlAtText('https://example.com/', '   ')).toBe('https://example.com/');
    expect(urlAtText('https://example.com/', null)).toBe('https://example.com/');
  });

  it('leaves a URL that is not http(s), or already has a directive, alone', () => {
    expect(urlAtText('mailto:someone@example.com', 'Email us')).toBe('mailto:someone@example.com');
    expect(urlAtText('https://example.com/#:~:text=first', 'second')).toBe('https://example.com/#:~:text=first');
  });

  it('puts the words on one line and cuts a long run at a word', () => {
    expect(urlAtText('https://example.com/', '  Buy\n   Now ')).toBe('https://example.com/#:~:text=Buy%20Now');
    const long = 'The quick brown fox jumps over the lazy dog and keeps on running through the field until night falls';
    const fragment = decodeURIComponent(urlAtText('https://example.com/', long).split(':~:text=')[1] ?? '');
    expect(fragment.length).toBeLessThanOrEqual(80);
    expect(long.startsWith(fragment)).toBe(true);
    expect(long[fragment.length]).toBe(' ');
  });
});
