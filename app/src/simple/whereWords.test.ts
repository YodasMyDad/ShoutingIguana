import { describe, expect, it } from 'vitest';
import { pathOrigin, shownWords } from './whereWords';

const pages = (count: number) => (count === 1 ? 'page' : 'pages');
const garden = { site: 'gardenfurnituresets.co.uk', startUrl: 'https://gardenfurnituresets.co.uk/' };

// The closing audit's gap 12.
describe('Where it is, under its list', () => {
  it('counts what the list lists: causes for a check that groups them, never “it is shown”', () => {
    const base = { noun: pages, origin: null, shortened: false };
    expect(shownWords({ ...base, grouped: true, listed: 1, all: 1 })).toBe('The one cause is shown.');
    expect(shownWords({ ...base, grouped: true, listed: 3, all: 3 })).toBe('All 3 causes are shown.');
    expect(shownWords({ ...base, grouped: true, listed: 25, all: 126 })).toBe('Showing 25 of 126 causes.');
    expect(shownWords({ ...base, grouped: false, listed: 25, all: 1_080 })).toBe('Showing 25 of 1,080 pages.');
    expect(shownWords({ ...base, grouped: false, listed: 1, all: 1 })).toBe('The one page is shown.');
  });

  it('says where the addresses shown as paths are, and only when some are', () => {
    const said = { grouped: true, listed: 1, all: 1, noun: pages };
    expect(shownWords({ ...said, origin: 'https://www.gardenfurnituresets.co.uk', shortened: true })).toBe(
      'The one cause is shown. Addresses starting with “/” are on www.gardenfurnituresets.co.uk.',
    );
    expect(shownWords({ ...said, origin: 'http://127.0.0.1:8080', shortened: true })).toBe(
      'The one cause is shown. Addresses starting with “/” are on http://127.0.0.1:8080.',
    );
    expect(shownWords({ ...said, origin: 'https://www.gardenfurnituresets.co.uk', shortened: false })).toBe(
      'The one cause is shown.',
    );
  });

  it('shortens against the origin the site’s pages are on, www. or not, else the start address’s', () => {
    expect(pathOrigin('https://www.gardenfurnituresets.co.uk/sofas/', garden)).toBe(
      'https://www.gardenfurnituresets.co.uk',
    );
    expect(pathOrigin('https://cdn.example.net/photo.jpg', garden)).toBe('https://gardenfurnituresets.co.uk');
    expect(pathOrigin(null, garden)).toBe('https://gardenfurnituresets.co.uk');
    expect(pathOrigin(null, { site: null, startUrl: null })).toBeNull();
  });
});
