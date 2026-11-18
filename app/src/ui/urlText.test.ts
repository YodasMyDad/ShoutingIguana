import { describe, expect, it } from 'vitest';
import { ELLIPSIS, fileLabelOf, isResourceType, middleTruncate, originLength, urlDisplay } from './urlText';

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

describe('fileLabelOf', () => {
  const SITE_ORIGIN = 'https://www.example.com';

  it('leads with the file’s name, without its query, the query or the folder after it', () => {
    expect(fileLabelOf(`${SITE_ORIGIN}/media/5265/harpswell-metal-patio-set-9.jpg?width=896`, SITE_ORIGIN)).toEqual({
      name: 'harpswell-metal-patio-set-9.jpg',
      rest: '?width=896',
    });
    expect(fileLabelOf(`${SITE_ORIGIN}/wp-content/uploads/2024/05/hero.webp`, SITE_ORIGIN)).toEqual({
      name: 'hero.webp',
      rest: '/wp-content/uploads/2024/05/',
    });
    expect(fileLabelOf(`${SITE_ORIGIN}/favicon.ico`, SITE_ORIGIN)).toEqual({ name: 'favicon.ico', rest: null });
  });

  it('decodes an encoded name, and keeps one that won’t decode as it is', () => {
    expect(fileLabelOf(`${SITE_ORIGIN}/img/caf%C3%A9%20menu.png`, SITE_ORIGIN).name).toBe('café menu.png');
    expect(fileLabelOf(`${SITE_ORIGIN}/img/100%25-cotton.jpg`, SITE_ORIGIN).name).toBe('100%-cotton.jpg');
    expect(fileLabelOf(`${SITE_ORIGIN}/img/bad%E0%A4%A.png`, SITE_ORIGIN).name).toBe('bad%E0%A4%A.png');
  });

  it('names a path that ends in a slash by its last folder, and one with no path by its host', () => {
    expect(fileLabelOf(`${SITE_ORIGIN}/garden/sofas/`, SITE_ORIGIN)).toEqual({ name: 'sofas/', rest: '/garden/' });
    expect(fileLabelOf(`${SITE_ORIGIN}/`, SITE_ORIGIN)).toEqual({ name: 'www.example.com', rest: null });
    expect(fileLabelOf('https://cdn.example.net', SITE_ORIGIN)).toEqual({ name: 'cdn.example.net', rest: null });
  });

  it('says another host’s file is on it, and cuts a long query', () => {
    expect(fileLabelOf('https://cdn.example.net/assets/site.css?v=3', SITE_ORIGIN)).toEqual({
      name: 'site.css',
      rest: 'cdn.example.net ?v=3',
    });
    const long = `?mode=crop&center=0.50,0.50&width=1200&height=800&format=webp&quality=80&rnd=133784512345678901`;
    const label = fileLabelOf(`${SITE_ORIGIN}/media/4364/rattan.jpg${long}`, SITE_ORIGIN);
    expect(label.name).toBe('rattan.jpg');
    expect(label.rest?.length).toBeLessThanOrEqual(61);
    expect(label.rest?.startsWith('?mode=crop&center=0.50,0.50')).toBe(true);
    expect(label.rest?.endsWith(ELLIPSIS)).toBe(true);
  });

  it('names an image written into the page, and keeps text that isn’t a URL as it is', () => {
    expect(fileLabelOf('data:image/png;base64,iVBORw0KGgo=')).toEqual({ name: 'Image in the page', rest: 'image/png' });
    expect(fileLabelOf('data:,Hello')).toEqual({ name: 'Data in the page', rest: null });
    expect(fileLabelOf('not a url')).toEqual({ name: 'not a url', rest: null });
  });
});

describe('isResourceType', () => {
  it('is a file a page loads, not a page', () => {
    expect(
      ['image/jpeg', 'image/svg+xml', 'text/css', 'application/javascript', 'text/javascript', 'font/woff2'].map(
        isResourceType,
      ),
    ).toEqual([true, true, true, true, true, true]);
    expect(['text/html', 'application/xhtml+xml', null, undefined].map(isResourceType)).toEqual([
      false,
      false,
      false,
      false,
    ]);
  });
});
