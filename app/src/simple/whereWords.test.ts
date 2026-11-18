import { describe, expect, it } from 'vitest';
import { SIMPLE_CHECKS } from '../test/simpleSidecar';
import {
  causeName,
  causeWords,
  causesWords,
  pathOrigin,
  rowDetailSaysMore,
  sharedSourceAdvice,
  shownWords,
} from './whereWords';

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

// The fixit index audit's X2, and the links audit's item 5.
describe('what to do at the page linking to most of a check’s addresses', () => {
  it('fixes or removes the links to broken and forwarding addresses', () => {
    for (const id of ['internal-client-error', 'internal-unreachable', 'redirect-chain', 'redirect-loop']) {
      expect(sharedSourceAdvice(check(id))).toBe('Fix or remove the links there first.');
    }
  });

  it('adds links for pages that want more of them', () => {
    expect(sharedSourceAdvice(check('non-indexable-inlinks-only'))).toBe(
      'It’s kept out of search results itself, so link to these pages from one that shows in search too.',
    );
    expect(sharedSourceAdvice(check('orphan-page'))).toMatch(/^Search engines are told not to follow the links there/);
  });

  it('says nothing for an address whose fault isn’t its links', () => {
    for (const id of [
      'blocked-by-robots',
      'sitemap-non-indexable',
      'canonical-unlinked',
      'hreflang-unlinked',
      'url-uppercase',
    ]) {
      expect(sharedSourceAdvice(check(id))).toBeNull();
    }
  });
});

// The fixit index audit's X3: related-URL groups hid the page's detail, though it said more than the cause's facts.
describe('a page’s own detail under its cause', () => {
  const fr = 'https://example.com/fr/';
  const de = 'https://example.com/de/';
  const codes = 'Names /fr/ as "fr"; /fr/ calls itself "fr-CA"';

  it('goes under the cause it’s about where it says more than the facts', () => {
    const inconsistent = check('hreflang-inconsistent-code');
    expect(rowDetailSaysMore(inconsistent, { detail: null, relatedUrl: fr }, { detail: codes, relatedUrl: fr })).toBe(
      true,
    );
    // The page names /de/ too: its detail is about /fr/, so it isn't said under /de/.
    expect(rowDetailSaysMore(inconsistent, { detail: null, relatedUrl: de }, { detail: codes, relatedUrl: fr })).toBe(
      false,
    );
    const small = { detail: 'og:image is 120 × 80 px, under 200 × 200', relatedUrl: fr };
    expect(rowDetailSaysMore(check('social-image-too-small'), { detail: null, relatedUrl: fr }, small)).toBe(true);
  });

  it('stays out where it’s a count, or the facts again', () => {
    const large = { detail: '1 image over 200 KB: 1.8 MB', relatedUrl: fr };
    expect(rowDetailSaysMore(check('image-too-large'), { detail: null, relatedUrl: fr }, large)).toBe(false);
    const broken = { detail: '1 broken URL', relatedUrl: fr };
    expect(rowDetailSaysMore(check('broken-internal-links'), { detail: null, relatedUrl: fr }, broken)).toBe(false);
    expect(
      rowDetailSaysMore(check('hreflang-inconsistent-code'), { detail: null, relatedUrl: fr }, { detail: null }),
    ).toBe(false);
  });

  it('goes under a kind of problem where the page’s instance isn’t the kind', () => {
    const relative = check('canonical-relative');
    const kind = { detail: 'Root-relative: starts with /', relatedUrl: null };
    expect(rowDetailSaysMore(relative, kind, { detail: '/plants/', relatedUrl: null })).toBe(true);
    expect(
      rowDetailSaysMore(
        check('internal-client-error'),
        { detail: '404 Not Found', relatedUrl: null },
        {
          detail: '404 Not Found',
          relatedUrl: null,
        },
      ),
    ).toBe(false);
  });
});

// The fixit audit's item 10: h1-missing's group had no name in Simple.
describe('a cause found by its detail, named', () => {
  it('names a detail the engine leaves empty, and says a heading’s plainly', () => {
    expect(causeName(check('h1-missing'), null)).toBe('No main heading');
    expect(causeName(check('h1-missing'), 'The H1 has no text')).toBe('The main heading has no words');
    expect(causeName(check('blocked-by-robots'), null)).toBe('Kept out by the rules file');
    expect(causeName(check('broken-fragment-links'), null)).toBe('Links to a part of the same page that isn’t there');
  });

  it('names any other empty detail by the check, and says a detail in plain words', () => {
    expect(causeName(check('open-graph-missing'), null)).toBe(check('open-graph-missing').plainTitle);
    expect(causeName(check('open-graph-missing'), '')).toBe(check('open-graph-missing').plainTitle);
    // The fixit page's leftovers: a cause was headed by the engine's own words.
    expect(causeName(check('internal-client-error'), '404 Not Found')).toBe('It is missing (404 Not Found)');
    expect(causeName(check('heading-levels-skipped'), 'H1 → H3')).toBe(
      'It goes from the main heading to a level-3 heading, skipping level 2',
    );
    expect(causeName(check('open-graph-missing'), 'Empty og:title')).toBe('For sharing, its title is empty');
    expect(
      causeName(check('structured-data-invalid'), `'…' is invalid after a value. Expected either ',', '}', or ']'.`),
    ).toBe(
      'The details for search engines have a mistake: a comma or closing bracket is missing, so search engines can’t read them',
    );
  });

  it('keeps a detail in a form no check writes, never a blank', () => {
    expect(causeName(check('internal-client-error'), 'Something new')).toBe('Something new');
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
