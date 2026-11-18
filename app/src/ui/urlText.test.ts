import { describe, expect, it } from 'vitest';
import {
  ELLIPSIS,
  fileLabelOf,
  isResourceType,
  middleTruncate,
  originLength,
  splitExtension,
  urlDisplay,
} from './urlText';

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

  it('leads with the file’s name, without its query, its folder and query after it', () => {
    expect(fileLabelOf(`${SITE_ORIGIN}/media/5265/harpswell-metal-patio-set-9.jpg?width=896`, SITE_ORIGIN)).toEqual({
      name: 'harpswell-metal-patio-set-9.jpg',
      rest: '/media/5265/ ?width=896',
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

  it('says another host’s file is on it, in its folder there, and cuts a long query', () => {
    expect(fileLabelOf('https://cdn.example.net/assets/site.css?theme=dark', SITE_ORIGIN)).toEqual({
      name: 'site.css',
      rest: 'cdn.example.net/assets/ ?theme=dark',
    });
    expect(fileLabelOf('https://cdn.example.net/logo.png', SITE_ORIGIN)).toEqual({
      name: 'logo.png',
      rest: 'cdn.example.net',
    });
    const long = `?mode=crop&center=0.50,0.50&width=1200&height=800&format=webp&quality=80&anchor=topcenter`;
    const label = fileLabelOf(`${SITE_ORIGIN}/media/4364/rattan.jpg${long}`, SITE_ORIGIN);
    expect(label.name).toBe('rattan.jpg');
    // The folder whole, then the query's first 60 characters.
    expect(label.rest).toBe(`/media/4364/ ${long.slice(0, 60)}${ELLIPSIS}`);
  });

  // The fixit audit's item 4: the garden site's ten different files were all "2.png ?width=896".
  it('tells files of one name apart by their folder, though they have a query', () => {
    const labels = ['/media/6965/2.png?width=896', '/media/6790/2.png?width=896'].map((path) =>
      fileLabelOf(`${SITE_ORIGIN}${path}`, SITE_ORIGIN),
    );
    expect(labels).toEqual([
      { name: '2.png', rest: '/media/6965/ ?width=896' },
      { name: '2.png', rest: '/media/6790/ ?width=896' },
    ]);
  });

  it('leaves a cache-buster out of the label, never out of the address', () => {
    const shopify = 'https://jitsunami.com/cdn/shop/files/hero_1200x.png?v=1761041746';
    expect(fileLabelOf(shopify, 'https://jitsunami.com')).toEqual({ name: 'hero_1200x.png', rest: '/cdn/shop/files/' });
    // A number, a version or a hash is a cache-buster's value, under any of its usual names.
    for (const query of [
      '?ver=6.4.2',
      '?_=1700000000000',
      '?cb=a1b2c3d4',
      '?t=1731900000',
      '?ts=20261118',
      '?rnd=1337',
    ]) {
      expect(fileLabelOf(`${SITE_ORIGIN}/wp-content/themes/shop/style.css${query}`, SITE_ORIGIN).rest).toBe(
        '/wp-content/themes/shop/',
      );
    }
    // The rest of the query stays, and so does a parameter of the same name whose value says something.
    expect(fileLabelOf(`${SITE_ORIGIN}/img/a.jpg?width=896&v=3`, SITE_ORIGIN).rest).toBe('/img/ ?width=896');
    expect(fileLabelOf(`${SITE_ORIGIN}/img/a.jpg?v=3&width=896`, SITE_ORIGIN).rest).toBe('/img/ ?width=896');
    expect(fileLabelOf(`${SITE_ORIGIN}/img/a.jpg?t=dark`, SITE_ORIGIN).rest).toBe('/img/ ?t=dark');
    expect(fileLabelOf(`${SITE_ORIGIN}/a.jpg?v=3`, SITE_ORIGIN).rest).toBeNull();
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

describe('splitExtension', () => {
  it('parts a name from its extension, so a cut name keeps it', () => {
    expect(splitExtension('harpswell-metal-patio-set-9.jpg')).toEqual({
      stem: 'harpswell-metal-patio-set-9',
      ext: '.jpg',
    });
    expect(splitExtension('archive.tar.gz')).toEqual({ stem: 'archive.tar', ext: '.gz' });
    expect(splitExtension('hero.WEBP')).toEqual({ stem: 'hero', ext: '.WEBP' });
  });

  it('leaves a name without an extension whole', () => {
    expect(splitExtension('index')).toEqual({ stem: 'index', ext: '' });
    expect(splitExtension('folder/')).toEqual({ stem: 'folder/', ext: '' });
    expect(splitExtension('.htaccess')).toEqual({ stem: '.htaccess', ext: '' });
    expect(splitExtension('notes.markdown2')).toEqual({ stem: 'notes.markdown2', ext: '' });
  });
});
