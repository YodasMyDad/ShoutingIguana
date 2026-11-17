import { describe, expect, it } from 'vitest';
import type { RelatedFacts } from '../api/grids';
import type { IssueTarget, LinkRow, UrlFoundOn } from '../api/inspector';
import {
  plainAlt,
  plainCutOff,
  plainDetail,
  plainDetailColumn,
  plainEvidence,
  plainFinal,
  plainImageFile,
  plainImageInsight,
  plainLinkedFrom,
  plainOthers,
  plainRelated,
  plainShownAt,
  plainStatus,
  plainTargetLine,
} from './plainEvidence';

const ORIGIN = 'https://example.com';

const FILE: RelatedFacts = {
  statusCode: 200,
  outcome: 'success',
  contentType: 'image/jpeg',
  sizeBytes: 1_887_436,
  pixelWidth: 4000,
  pixelHeight: 3000,
  final: null,
  indexability: null,
  canonical: null,
};

const target = (overrides: Partial<IssueTarget>): IssueTarget => ({
  id: 812,
  url: `${ORIGIN}/img/hero.jpg`,
  statusCode: 200,
  fetched: true,
  outcome: 'success',
  kind: 'hyperlink',
  text: null,
  position: 'content',
  flags: [],
  links: 1,
  img: null,
  ...overrides,
});

const IMAGE = { id: 'image-too-large', targetFacts: ['sizeBytes', 'pixelWidth', 'pixelHeight', 'img', 'position'] };

describe('what a cause is, in plain words', () => {
  it('says an image’s file, its size and its pixels', () => {
    expect(plainImageFile(FILE)).toBe('The image file is 1.8 MB, 4000×3000 pixels');
    expect(plainImageFile({ ...FILE, pixelWidth: null })).toBe('The image file is 1.8 MB');
    expect(plainImageFile({ sizeBytes: null, pixelWidth: null, pixelHeight: null })).toBeNull();
    expect(plainRelated(FILE, IMAGE, ORIGIN)).toBe('The image file is 1.8 MB, 4000×3000 pixels.');
  });

  it('says where a forwarding address ends, and how an address that isn’t working answered', () => {
    const final = { id: 2, url: `${ORIGIN}/new/`, statusCode: 200, outcome: 'success' as const, hops: 1, loop: false };
    expect(plainFinal(final, ORIGIN)).toBe('Forwards to /new/, which works');
    expect(plainFinal({ ...final, hops: 3, statusCode: 404, outcome: 'clientError' }, ORIGIN)).toBe(
      'Forwards to /new/ in 3 steps, which is missing (404 Not Found)',
    );
    expect(plainFinal({ ...final, loop: true }, ORIGIN)).toBe('Forwards round in a loop, back to /new/');
    const facts = { id: 'links-to-redirects', targetFacts: ['statusCode', 'final', 'text', 'position', 'links'] };
    expect(plainRelated({ ...FILE, statusCode: 301, final }, facts, ORIGIN)).toBe('Forwards to /new/, which works.');
    const broken = { id: 'broken-internal-links', targetFacts: ['statusCode', 'final', 'text', 'position'] };
    expect(plainRelated({ ...FILE, statusCode: 404, outcome: 'clientError' }, broken, ORIGIN)).toBe(
      'It is missing (404 Not Found).',
    );
    // A page that works says nothing of it: the check's fault is elsewhere.
    expect(plainRelated(FILE, broken, ORIGIN)).toBeNull();
  });

  it('says how long a page elsewhere took to open', () => {
    const slow = { id: 'slow-external-links', targetFacts: ['statusCode', 'responseMs', 'text', 'position'] };
    expect(plainRelated({ ...FILE, responseMs: 7_400 }, slow, ORIGIN)).toBe(
      'It took 7.4 seconds to open when it was checked.',
    );
    expect(plainRelated(FILE, slow, ORIGIN)).toBeNull();
  });

  it('says why a main address can’t appear in search results', () => {
    const canonical = { id: 'canonical-to-non-indexable', targetFacts: ['statusCode', 'indexability', 'canonical'] };
    expect(plainRelated({ ...FILE, indexability: 'noindex' }, canonical, ORIGIN)).toBe(
      'It can’t appear in search results: it says not to show it (noindex).',
    );
  });

  it('says how an address answered, as a clause', () => {
    expect(plainStatus(200)).toBe('works');
    expect(plainStatus(301)).toBe('forwards (301 Moved Permanently)');
    expect(plainStatus(500)).toBe('has a server error (500 Internal Server Error)');
    expect(plainStatus(null, 'timeout')).toBe('didn’t answer');
    expect(plainStatus(null, 'robotsDisallowed')).toBe('is kept out by the rules file');
  });
});

describe('how a page has a cause, in plain words', () => {
  it('says the size a page shows an image at, and where', () => {
    const img = {
      width: '400',
      height: '300',
      alt: 'Sofa',
      hasAlt: true,
      altLength: 4,
      srcset: false,
      loading: 'lazy',
    };
    expect(plainTargetLine(target({ kind: 'image', img }), IMAGE)).toBe(
      'Shown at 400×300 pixels, loads when scrolled to, in the main content',
    );
    expect(plainShownAt({ width: '100%', height: null })).toBe('Shown 100% wide, with no height set');
    expect(plainShownAt({ width: null, height: null })).toBe('No width or height set');
    const missing = { id: 'image-missing-alt', targetFacts: ['img', 'position', 'links'] };
    expect(plainTargetLine(target({ kind: 'image', img: { ...img, alt: null, hasAlt: false } }), missing)).toBe(
      'No alt text, in the main content',
    );
    expect(plainAlt({ alt: 'A long description', hasAlt: true, altLength: 140 }, true)).toBe(
      'Alt text: “A long description” (140 characters)',
    );
  });

  it('says where a link is on the page and what it says, or that it has no text', () => {
    const facts = { id: 'links-to-redirects', targetFacts: ['statusCode', 'final', 'text', 'position', 'links'] };
    expect(plainTargetLine(target({ text: 'Old range', position: 'footer', links: 2 }), facts)).toBe(
      'The link is in the footer: “Old range” · 2 links',
    );
    const empty = { id: 'empty-anchor-text', targetFacts: ['statusCode', 'position', 'flags', 'links'] };
    expect(plainTargetLine(target({ position: 'navigation' }), empty)).toBe('The link is in the menu, with no text');
    // A page whose cause is its own address: a home page's logo linking home.
    expect(plainTargetLine(target({ position: 'header' }), empty, true)).toBe(
      'The link to this same page is in the header, with no text',
    );
    expect(plainTargetLine(target({ position: null }), empty, true)).toBe('A link to this same page');
    expect(plainTargetLine(target({ position: null }), empty)).toBeNull();
    const fragment = { id: 'broken-fragment-links', targetFacts: ['fragment', 'text', 'position'] };
    expect(plainTargetLine(target({ fragment: 'specs', text: 'Specs' }), fragment)).toBe(
      'It links to the section #specs · The link is in the main content: “Specs”',
    );
    expect(plainTargetLine(target({}), { id: 'x', targetFacts: [] })).toBeNull();
  });

  it('says which pages link to a broken address, the first with where its link is and what it says', () => {
    const link = (id: number, path: string, overrides: Partial<LinkRow> = {}): LinkRow => ({
      id,
      url: `${ORIGIN}${path}`,
      kind: 'hyperlink',
      text: null,
      position: 'content',
      flags: [],
      followed: true,
      fragment: null,
      statusCode: 200,
      indexable: true,
      fetched: true,
      outcome: 'success',
      ...overrides,
    });
    const found = (overrides: Partial<UrlFoundOn> = {}): UrlFoundOn => ({
      urlId: 10,
      links: 0,
      pages: 0,
      pagesExact: true,
      sources: [],
      sitemaps: { total: 0, sitemaps: [] },
      start: false,
      ...overrides,
    });
    const sources = [
      link(1, '/garden/', { position: 'footer', text: 'Old range' }),
      link(2, '/sofas/'),
      link(3, '/blog/'),
      link(4, '/x/'),
    ];
    expect(plainLinkedFrom(found({ links: 8, pages: 8, sources }), ORIGIN)).toBe(
      'Linked from /garden/ (in the footer: “Old range”), /sofas/, /blog/ and 5 more',
    );
    expect(plainLinkedFrom(found({ links: 2, pages: 2, sources: sources.slice(1, 3) }), ORIGIN)).toBe(
      'Linked from /sofas/ (in the main content) and /blog/',
    );
    expect(plainLinkedFrom(found({ sitemaps: { total: 1, sitemaps: [{ id: 1, url: 'x' }] } }), ORIGIN)).toBe(
      'Listed in your sitemap',
    );
    expect(plainLinkedFrom(found({ start: true }), ORIGIN)).toBe('It’s the address the check started from');
    expect(plainLinkedFrom(found(), ORIGIN)).toBeNull();
  });
});

describe('a page’s own evidence, in plain words', () => {
  it('says a chain, a rule, a heading and a directive, and leaves out what the line says another way', () => {
    const chain = {
      hops: [
        { id: 2, url: `${ORIGIN}/b/`, statusCode: 301, outcome: 'redirect', via: 'http' },
        { id: 3, url: `${ORIGIN}/new/`, statusCode: 200, outcome: 'success', via: 'http' },
      ],
      final: { id: 3, url: `${ORIGIN}/new/`, statusCode: 200, outcome: 'success' },
      loop: false,
      tooLong: false,
    };
    expect(plainEvidence('redirectChain', chain, ORIGIN)).toBe('Forwards to /b/, then /new/, which works');
    expect(
      plainEvidence('robotsRule', { origin: ORIGIN, reason: 'rule', line: 12, rule: 'Disallow: /cart/' }, ORIGIN),
    ).toBe('The rules file says “Disallow: /cart/”, on line 12');
    expect(plainEvidence('h1', 'Sofas', ORIGIN)).toBe('Main heading: “Sofas”');
    expect(plainEvidence('metaRobots', 'noindex, follow', ORIGIN)).toBe('The page says “noindex, follow”');
    expect(plainEvidence('language', 'en_GB', ORIGIN)).toBe('The page gives its language as “en_GB”');
    expect(plainEvidence('indexability', 'canonicalised', ORIGIN)).toBe(
      'It can’t appear in search results: it names another address as its main address',
    );
    expect(plainEvidence('canonicalUrl', `${ORIGIN}/a/`, ORIGIN)).toBe('Main address: /a/');
    expect(plainEvidence('wordCount', 120, ORIGIN)).toBe('120 words');
    expect(plainEvidence('title', 'Sofas', ORIGIN)).toBeNull();
    expect(plainEvidence('titleLength', 78, ORIGIN)).toBeNull();
    expect(plainEvidence('fetchError', null, ORIGIN)).toBeNull();
  });

  it('never says a width in pixels, nor the text up to the cut, which the page’s line says its own way', () => {
    for (const column of ['titleWidth', 'descriptionWidth', 'titleInResults', 'descriptionInResults']) {
      expect(
        plainEvidence(column, column.endsWith('Width') ? 612 : 'Garden sofas for every season…', ORIGIN),
      ).toBeNull();
    }
  });
});

describe('where Google will likely cut a title or description, in plain words', () => {
  it('never opens the quote on a separator left from the words before it', () => {
    expect(plainCutOff('title', 'Contact Us - GardenFurnitureSets | Phone, Email or Contract…')).toBe(
      'Google will likely cut this title off after “…Phone, Email or Contract”',
    );
  });
  const CUT = '…';

  it('quotes the last whole words that show, up to 30 characters, opening with “…” where more comes before', () => {
    expect(plainCutOff('title', `Garden sofas and chairs for every season, delivered free${CUT}`)).toBe(
      'Google will likely cut this title off after “…every season, delivered free”',
    );
    expect(
      plainCutOff('description', `Our rattan corner sofa seats six, fully weatherproofed, comes flat-packed for${CUT}`),
    ).toBe('Google will likely cut this description off after “…comes flat-packed for”');
  });

  it('quotes all of what shows, with no “…” before it, when it’s 30 characters or fewer', () => {
    expect(plainCutOff('title', `Rattan sofas for small gardens${CUT}`)).toBe(
      'Google will likely cut this title off after “Rattan sofas for small gardens”',
    );
    // Characters as a reader counts them: an emoji family is one, though JavaScript counts it as eight.
    const family = String.fromCodePoint(0x1f469, 0x200d, 0x1f469, 0x200d, 0x1f467);
    const shown = `Garden ${family} sofas for the family`;
    expect(shown.length).toBeGreaterThan(30);
    expect(plainCutOff('title', `${shown}${CUT}`)).toBe(`Google will likely cut this title off after “${shown}”`);
  });

  it('quotes the end of one word longer than 30 characters', () => {
    const word = 'Gartenmöbelsetbestellungsbestätigungsnummer';
    expect(plainCutOff('title', `Bestellen ${word}${CUT}`)).toBe(
      `Google will likely cut this title off after “…${word.slice(-30)}”`,
    );
  });

  it('says nothing where nothing is cut', () => {
    expect(plainCutOff('title', 'Garden sofas')).toBeNull();
    expect(plainCutOff('title', null)).toBeNull();
    expect(plainCutOff('title', 612)).toBeNull();
  });
});

describe('a length check’s page in plain words, in place of the detail', () => {
  const row = {
    title: 'Garden sofas and chairs for every season, delivered free across the UK | Example',
    titleInResults: 'Garden sofas and chairs for every season, delivered free…',
    titleWidth: 742,
    titleLength: 80,
    description: 'Sofas.',
    descriptionWidth: 46,
    descriptionLength: 6,
    detail: '742 px, over 580 (80 characters)',
  };

  it('says where a long one is cut, and how many characters a short one has, never pixels', () => {
    expect(plainDetail({ id: 'title-too-long' }, row)).toBe(
      'Google will likely cut this title off after “…every season, delivered free”',
    );
    expect(plainDetail({ id: 'description-too-short' }, row)).toBe('Only 6 characters long');
    expect(plainDetail({ id: 'title-too-short' }, { titleLength: 1 })).toBe('Only 1 character long');
    expect(plainDetail({ id: 'title-too-short' }, { titleLength: 1_234 })).toBe('Only 1,234 characters long');
    expect(
      plainDetail(
        { id: 'description-too-long' },
        { descriptionInResults: 'Our rattan corner sofa seats six and comes flat-packed for…' },
      ),
    ).toBe('Google will likely cut this description off after “…six and comes flat-packed for”');
  });

  it('leaves the detail to say it where it can’t: nothing cut, no length, or another check', () => {
    expect(plainDetail({ id: 'title-too-long' }, { ...row, titleInResults: row.title })).toBeNull();
    expect(plainDetail({ id: 'title-too-short' }, { titleLength: null })).toBeNull();
    expect(plainDetail({ id: 'title-duplicate' }, row)).toBeNull();
  });

  it('names the column each reads, for the reads that ask for it', () => {
    expect(plainDetailColumn({ id: 'title-too-long' })).toBe('titleInResults');
    expect(plainDetailColumn({ id: 'description-too-long' })).toBe('descriptionInResults');
    expect(plainDetailColumn({ id: 'title-too-short' })).toBe('titleLength');
    expect(plainDetailColumn({ id: 'description-too-short' })).toBe('descriptionLength');
    expect(plainDetailColumn({ id: 'title-duplicate' })).toBeNull();
  });
});

describe('an image bigger than its pages show it, in plain words', () => {
  const shown = { width: 400, height: 300, pages: 12, sampled: 12, varies: false };

  it('says how much bigger the file is than it needs to be', () => {
    expect(plainImageInsight(FILE, shown)).toBe(
      'The image file is 1.8 MB, 4000×3000 pixels, but the pages show it at 400×300, so it’s about 10 times bigger than it needs to be.',
    );
    expect(plainImageInsight(FILE, { ...shown, sampled: 1, pages: 1, width: 800, height: 600, varies: true })).toBe(
      'The image file is 1.8 MB, 4000×3000 pixels, but the page shows it at up to 800×600, so it’s about 5 times bigger than it needs to be.',
    );
    expect(plainImageInsight(FILE, { ...shown, width: 3600, height: 2700 })).toBe(
      'The image file is 1.8 MB, 4000×3000 pixels, and the pages show it at 3600×2700, about its full size, so it needs saving at a smaller file size rather than shrinking.',
    );
    expect(plainImageInsight(FILE, null)).toBe('The image file is 1.8 MB, 4000×3000 pixels.');
  });

  it('names the page’s other problems of the same kind', () => {
    expect(plainOthers({ id: 'image-too-large' }, 1)).toBe('and 1 other large image on this page');
    expect(plainOthers({ id: 'broken-internal-links' }, 3)).toBe('and 3 other broken links on this page');
    expect(plainOthers({ id: 'something-new' }, 2)).toBe('and 2 other problems like it on this page');
  });
});
