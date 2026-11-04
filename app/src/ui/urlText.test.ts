import { describe, expect, it } from 'vitest';
import { ELLIPSIS, middleTruncate, originLength, urlDisplay } from './urlText';

/** Monospace: every character the same width. */
const mono = (text: string) => text.length * 8;
/** Proportional: narrow and wide characters, as Inter sets them. */
const WIDTHS: Record<string, number> = { i: 3, l: 3, '/': 4, '.': 3, m: 12, w: 12, W: 14, M: 14, [ELLIPSIS]: 10 };
const proportional = (text: string) => {
  let width = 0;
  for (const char of text) width += WIDTHS[char] ?? 7;
  return width;
};

const SITE = 'https://www.gardenfurnituresets.co.uk';
const LONG = `${SITE}/harpswell-metal-patio-set-with-6-chairs-and-parasol/?colour=graphite`;

describe('urlDisplay', () => {
  it('shows the path of a URL on the crawl’s own origin', () => {
    expect(urlDisplay(`${SITE}/contact-us/`, SITE)).toEqual({
      full: `${SITE}/contact-us/`,
      shown: '/contact-us/',
      origin: '',
    });
    expect(urlDisplay(SITE, SITE).shown).toBe('/');
    expect(urlDisplay(`${SITE}?q=1`, SITE).shown).toBe('?q=1');
    expect(urlDisplay(`${SITE}/a#top`, `${SITE}/`).shown).toBe('/a#top');
  });

  it('shows the site’s other versions and other hosts in full, the origin set apart', () => {
    expect(urlDisplay('http://www.gardenfurnituresets.co.uk/a/', SITE)).toEqual({
      full: 'http://www.gardenfurnituresets.co.uk/a/',
      shown: 'http://www.gardenfurnituresets.co.uk/a/',
      origin: 'http://www.gardenfurnituresets.co.uk',
    });
    expect(urlDisplay('https://gardenfurnituresets.co.uk/', SITE).origin).toBe('https://gardenfurnituresets.co.uk');
    expect(urlDisplay('https://cdn.example/x.jpg', SITE).shown).toBe('https://cdn.example/x.jpg');
  });

  it('copes with what isn’t a URL', () => {
    expect(urlDisplay('not a url', SITE)).toEqual({ full: 'not a url', shown: 'not a url', origin: '' });
    expect(urlDisplay(`${SITE}/a`, 'nonsense').shown).toBe(`${SITE}/a`);
  });
});

describe('middleTruncate', () => {
  it('leaves what fits alone, and anything when there’s no room to measure', () => {
    expect(middleTruncate('/a/', 100, mono)).toBe('/a/');
    expect(middleTruncate(LONG, 0, mono)).toBe(LONG);
  });

  it('keeps the whole host and as much of the end as fits', () => {
    const shown = middleTruncate(LONG, 60 * 8, mono, { keepStart: SITE.length });
    expect(shown.startsWith(SITE)).toBe(true);
    expect(shown.endsWith('colour=graphite')).toBe(true);
    expect(shown).toContain(ELLIPSIS);
    expect(shown).toHaveLength(60);
  });

  it('shares the room when keeping the host would leave too little of the end', () => {
    const shown = middleTruncate(LONG, 30 * 8, mono, { keepStart: SITE.length });
    expect(shown).toHaveLength(30);
    expect(shown.startsWith('https://www.g')).toBe(true);
    expect(shown.endsWith('graphite')).toBe(true);
  });

  it('gives a path’s end two thirds of the room', () => {
    const path = '/harpswell-metal-patio-set-with-6-chairs-and-parasol/';
    const shown = middleTruncate(path, 31 * 8, mono);
    const [start = '', end = ''] = shown.split(ELLIPSIS);
    expect(start.length).toBe(10);
    expect(end.length).toBe(20);
    expect(path.startsWith(start) && path.endsWith(end)).toBe(true);
  });

  it('fits proportional text by its widths, not its length', () => {
    for (const width of [80, 140, 230, 333, 480]) {
      const shown = middleTruncate(LONG, width, proportional, { keepStart: SITE.length });
      expect(proportional(shown)).toBeLessThanOrEqual(width);
      // One more character of the end wouldn't have fitted.
      const [start = '', end = ''] = shown.split(ELLIPSIS);
      const longer = `${start}${ELLIPSIS}${LONG.slice(LONG.length - end.length - 1)}`;
      if (shown !== LONG && start === SITE) expect(proportional(longer)).toBeGreaterThan(width);
    }
  });

  it('still says something was cut in the narrowest room', () => {
    expect(middleTruncate(LONG, 1, mono)).toBe(ELLIPSIS);
  });
});

describe('originLength', () => {
  it('counts the characters of the origin at the start of what’s shown', () => {
    expect(originLength(`${SITE}/a`, SITE)).toBe(SITE.length);
    expect(originLength(`https://www.g${ELLIPSIS}graphite`, SITE)).toBe('https://www.g'.length);
    expect(originLength('/a', '')).toBe(0);
  });
});
