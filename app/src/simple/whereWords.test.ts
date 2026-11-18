import { describe, expect, it } from 'vitest';
import { SIMPLE_CHECKS } from '../test/simpleSidecar';
import { causeName, causeWords, causesWords, pathOrigin, shownWords } from './whereWords';

const pages = (count: number) => (count === 1 ? 'page' : 'pages');
const garden = { site: 'gardenfurnituresets.co.uk', startUrl: 'https://gardenfurnituresets.co.uk/' };
const check = (id: string) => SIMPLE_CHECKS.checks.find((candidate) => candidate.id === id)!;

// The fixit audit's item 8: "129 causes, on 60 pages" left an owner asking "129 what?".
describe('what Where it is counts', () => {
  it('names a check’s causes by what they are', () => {
    const nouns = (id: string) => {
      const words = causeWords(check(id));
      return [words.one, words.many];
    };
    expect(nouns('image-too-large')).toEqual(['image', 'images']);
    expect(nouns('image-missing-dimensions')).toEqual(['image', 'images']);
    expect(nouns('social-image-too-small')).toEqual(['image', 'images']);
    expect(nouns('broken-internal-links')).toEqual(['address', 'addresses']);
    expect(nouns('empty-anchor-text')).toEqual(['address', 'addresses']);
    expect(nouns('broken-resources')).toEqual(['file', 'files']);
    expect(nouns('mixed-content')).toEqual(['file', 'files']);
    expect(nouns('title-duplicate')).toEqual(['title', 'titles']);
    expect(nouns('og-title-duplicate')).toEqual(['title', 'titles']);
    expect(nouns('description-duplicate')).toEqual(['description', 'descriptions']);
    expect(nouns('h1-duplicate')).toEqual(['heading', 'headings']);
    expect(nouns('duplicate-content')).toEqual(['set of matching pages', 'sets of matching pages']);
    expect(nouns('hreflang-missing-return')).toEqual(['language version', 'language versions']);
    expect(nouns('canonicalised-page')).toEqual(['main address', 'main addresses']);
    expect(nouns('canonical-to-first-page')).toEqual(['list', 'lists']);
    expect(nouns('redirect-chain')).toEqual(['address', 'addresses']);
    // A check grouped by what was found: each group a kind of problem.
    expect(nouns('h1-missing')).toEqual(['kind of problem', 'kinds of problem']);
    expect(nouns('internal-client-error')).toEqual(['kind of problem', 'kinds of problem']);
  });

  it('counts the causes against the pages the headline counts', () => {
    expect(causesWords(check('image-too-large'), 129, 60)).toBe('129 images on 60 pages');
    expect(causesWords(check('image-missing-dimensions'), 3_661, 225)).toBe('3,661 images on 225 pages');
    expect(causesWords(check('h1-missing'), 1, 2)).toBe('1 kind of problem on 2 pages');
    expect(causesWords(check('internal-client-error'), 2, 1)).toBe('2 kinds of problem on 1 address');
    expect(causesWords(check('title-duplicate'), 1, 1)).toBe('1 title on 1 page');
  });

  it('heads the causes with what they hold, and the pages with how they’re linked to it where that’s the fix', () => {
    expect(causeWords(check('image-too-large')).heading).toBe('Image');
    expect(causeWords(check('h1-missing')).heading).toBe('What was found');
    expect(causeWords(check('title-duplicate')).heading).toBe('The title they share');
    expect(causeWords(check('broken-internal-links')).heading).toBe('Address linked to');
    // The fixit index audit: "What it’s about" named neither what the column holds nor where the fix is.
    expect(causeWords(check('hreflang-missing-return'))).toMatchObject({
      heading: 'Version that doesn’t link back',
      pagesHeading: 'Pages it should link back to',
    });
    expect(causeWords(check('canonical-to-first-page'))).toMatchObject({
      heading: 'The list’s first page',
      pagesHeading: 'Later pages that point Google at it',
    });
    expect(causeWords(check('image-too-large')).pagesHeading).toBe('On these pages');
    expect(causeWords(check('internal-client-error')).pagesHeading).toBe('At these addresses');
  });
});

// The fixit audit's item 10: h1-missing's group had no name in Simple.
describe('a cause found by its detail, named', () => {
  it('names a detail the engine leaves empty, and says a heading’s plainly', () => {
    expect(causeName(check('h1-missing'), null)).toBe('No main heading');
    expect(causeName(check('h1-missing'), 'The H1 has no text')).toBe('The main heading has no text');
    expect(causeName(check('blocked-by-robots'), null)).toBe('Kept out by the rules file');
    expect(causeName(check('broken-fragment-links'), null)).toBe('Links to a part of the same page that isn’t there');
  });

  it('names any other empty detail by the check, and keeps a detail that says something', () => {
    expect(causeName(check('open-graph-missing'), null)).toBe(check('open-graph-missing').plainTitle);
    expect(causeName(check('open-graph-missing'), '')).toBe(check('open-graph-missing').plainTitle);
    expect(causeName(check('internal-client-error'), '404 Not Found')).toBe('404 Not Found');
  });
});

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

  it('counts a check’s causes by what they are, where it says', () => {
    const images = (count: number) => (count === 1 ? 'image' : 'images');
    const base = { noun: pages, cause: images, origin: null, shortened: false, grouped: true };
    expect(shownWords({ ...base, listed: 1, all: 1 })).toBe('The one image is shown.');
    expect(shownWords({ ...base, listed: 3, all: 3 })).toBe('All 3 images are shown.');
    expect(shownWords({ ...base, listed: 25, all: 129 })).toBe('Showing 25 of 129 images.');
    expect(shownWords({ ...base, grouped: false, listed: 25, all: 60 })).toBe('Showing 25 of 60 pages.');
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
