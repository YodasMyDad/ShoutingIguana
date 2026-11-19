import { describe, expect, it } from 'vitest';
import { detailParts } from './detailParts';

describe('a row’s detail, its addresses told from its words', () => {
  it('is its words alone where it names no address', () => {
    expect(detailParts('404 Not Found')).toEqual([{ kind: 'text', text: '404 Not Found' }]);
    expect(detailParts('')).toEqual([]);
  });

  it('names the file its row is about by its name, and makes the other address a link', () => {
    const robots = 'https://example.com/robots.txt';
    expect(
      detailParts(
        `${robots} is empty, so it has no Sitemap: line; the sitemap was found at https://example.com/sitemap.xml`,
        [robots],
      ),
    ).toEqual([
      { kind: 'file', url: robots, name: 'robots.txt' },
      { kind: 'text', text: ' is empty, so it has no Sitemap: line; the sitemap was found at ' },
      { kind: 'url', url: 'https://example.com/sitemap.xml' },
    ]);
  });

  it('leaves a sentence’s punctuation out of an address', () => {
    expect(detailParts('Redirects to https://example.com/a/, then (https://example.com/b).')).toEqual([
      { kind: 'text', text: 'Redirects to ' },
      { kind: 'url', url: 'https://example.com/a/' },
      { kind: 'text', text: ', then (' },
      { kind: 'url', url: 'https://example.com/b' },
      { kind: 'text', text: ').' },
    ]);
  });

  it('leaves what isn’t an address a browser could open as words', () => {
    expect(detailParts('The canonical is “http://[not”, which isn’t a URL')).toEqual([
      { kind: 'text', text: 'The canonical is “http://[not”, which isn’t a URL' },
    ]);
  });
});
