import { describe, expect, it } from 'vitest';
import type { RelatedFacts } from '../api/grids';
import type { IssueTarget, RedirectChain } from '../api/inspector';
import {
  altWords,
  evidenceValueWords,
  fileTypeWords,
  finalWords,
  imageFileWords,
  objectCellWords,
  pixelsWords,
  redirectChainWords,
  relatedFactParts,
  robotsRuleWords,
  shownAtCellWords,
  shownAtInsight,
  shownAtOf,
  shownAtWords,
  statusWords,
  targetFactParts,
  widerWords,
} from './evidence';

const SITE = 'https://www.example.com';

/** A target as the evidence engine names it, with the fields a test cares about. */
const target = (overrides: Partial<IssueTarget>): IssueTarget => ({
  id: 812,
  url: `${SITE}/img/hero.jpg`,
  statusCode: 200,
  fetched: true,
  outcome: 'success',
  kind: 'image',
  text: null,
  position: 'content',
  contentType: null,
  sizeBytes: null,
  pixelWidth: null,
  pixelHeight: null,
  final: null,
  indexability: null,
  canonical: null,
  fragment: null,
  flags: [],
  links: 1,
  img: null,
  ...overrides,
});

const IMAGE_TOO_LARGE = [
  'sizeBytes',
  'contentType',
  'pixelWidth',
  'pixelHeight',
  'img',
  'statusCode',
  'final',
  'position',
  'links',
];

describe('a file as a fact', () => {
  it('names a file type as a person does, and keeps one it has no name for', () => {
    expect(fileTypeWords('image/jpeg')).toBe('JPEG');
    expect(fileTypeWords('image/webp; charset=binary')).toBe('WebP');
    expect(fileTypeWords('image/svg+xml')).toBe('SVG');
    expect(fileTypeWords('application/x-thing')).toBe('application/x-thing');
    expect(fileTypeWords(null)).toBeNull();
    expect(fileTypeWords('')).toBeNull();
  });

  it('says an image’s size, type and pixels, what of them is known', () => {
    expect(
      imageFileWords({ sizeBytes: 1_887_436, contentType: 'image/jpeg', pixelWidth: 4000, pixelHeight: 3000 }),
    ).toBe('1.8 MB · JPEG · 4000×3000 px');
    expect(
      imageFileWords({ sizeBytes: 250_000, contentType: 'image/svg+xml', pixelWidth: null, pixelHeight: null }),
    ).toBe('244.1 KB · SVG');
    expect(imageFileWords({ sizeBytes: null, contentType: null, pixelWidth: null, pixelHeight: null })).toBe('');
    expect(pixelsWords(4000, null)).toBeNull();
  });

  it('says the size a page shows an image at, from its attributes as written', () => {
    expect(shownAtWords({ width: '400', height: '300' })).toBe('shown at 400×300');
    expect(shownAtWords({ width: '100%', height: null })).toBe('shown 100% wide, no height');
    expect(shownAtWords({ width: null, height: '300' })).toBe('shown 300 high, no width');
    expect(shownAtWords({ width: null, height: ' ' })).toBe('no width or height');
    expect(shownAtWords(null)).toBeNull();
  });

  it('says an image’s alt text, or that it has none, with its length where that is the fault', () => {
    expect(altWords({ alt: 'Garden sofa', hasAlt: true, altLength: 11 })).toBe('alt “Garden sofa”');
    expect(altWords({ alt: 'A'.repeat(140), hasAlt: true, altLength: 140 }, true)).toBe(
      `alt “${'A'.repeat(59)}…” (140 characters)`,
    );
    expect(altWords({ alt: null, hasAlt: false, altLength: null })).toBe('no alt');
    expect(altWords({ alt: '', hasAlt: true, altLength: 0 })).toBe('empty alt');
  });
});

describe('what a URL a page names is', () => {
  it('says an oversized image whole: its file, the size the page shows it at and how it loads', () => {
    const hero = target({
      contentType: 'image/jpeg',
      sizeBytes: 1_887_436,
      pixelWidth: 4000,
      pixelHeight: 3000,
      img: {
        width: '400',
        height: '300',
        alt: 'Garden sofa',
        hasAlt: true,
        altLength: 11,
        srcset: true,
        loading: 'lazy',
      },
    });
    expect(targetFactParts(hero, IMAGE_TOO_LARGE, SITE, 'image-too-large')).toEqual([
      '1.8 MB · JPEG · 4000×3000 px',
      'shown at 400×300',
      'srcset',
      'lazy',
      'in the content',
    ]);
    // An image without alt text says that, and one without dimensions which it lacks.
    const img = { width: '400', height: null, alt: null, hasAlt: false, altLength: null, srcset: false, loading: null };
    expect(targetFactParts(target({ img }), ['img', 'position', 'links'], SITE, 'image-missing-alt')).toEqual([
      'no alt',
      'in the content',
    ]);
    expect(
      targetFactParts(
        target({ img, pixelWidth: 1200, pixelHeight: 800 }),
        ['img', 'pixelWidth', 'pixelHeight'],
        SITE,
        'image-missing-dimensions',
      ),
    ).toEqual(['shown 400 wide, no height', '1200×800 px']);
  });

  it('says where a redirecting link should point instead, where on the page it is and what it says', () => {
    const old = target({
      url: `${SITE}/old`,
      statusCode: 301,
      outcome: 'redirect',
      kind: 'hyperlink',
      text: 'Shop',
      position: 'footer',
      final: { id: 91, url: `${SITE}/new/`, statusCode: 200, outcome: 'success', hops: 1, loop: false },
      links: 2,
    });
    expect(targetFactParts(old, ['statusCode', 'final', 'text', 'position', 'links'], SITE)).toEqual([
      '→ /new/ 200',
      'in the footer',
      '“Shop”',
      '2 links',
    ]);
  });

  it('says how long a slow link’s page took to answer, then where the link is and what it says', () => {
    const slow = target({
      url: 'https://elsewhere.example/slow',
      kind: 'hyperlink',
      text: 'Report',
      position: 'content',
      responseMs: 7_400,
    });
    expect(
      targetFactParts(slow, ['statusCode', 'responseMs', 'text', 'position'], SITE, 'slow-external-links'),
    ).toEqual(['answered in 7.4 s', 'in the content', '“Report”']);
    // A group's related URL says it too; an older sidecar's, without it, says nothing of it.
    const page: RelatedFacts = {
      statusCode: 200,
      outcome: 'success',
      contentType: 'text/html',
      sizeBytes: 20_000,
      pixelWidth: null,
      pixelHeight: null,
      final: null,
      indexability: null,
      canonical: null,
    };
    const facts = ['statusCode', 'responseMs', 'text', 'position'];
    expect(relatedFactParts({ ...page, responseMs: 3_200 }, facts, SITE)).toEqual(['answered in 3.2 s']);
    expect(relatedFactParts(page, facts, SITE)).toEqual([]);
  });

  it('names a missing section and a nofollow link, and an image link without alt text', () => {
    expect(
      targetFactParts(
        target({ kind: 'hyperlink', text: 'Specs', fragment: 'specs', position: 'content' }),
        ['statusCode', 'fragment', 'text', 'position', 'links'],
        SITE,
      ),
    ).toEqual(['#specs', 'in the content', '“Specs”']);
    expect(
      targetFactParts(
        target({ kind: 'hyperlink', text: null, position: 'header', flags: ['imageLink', 'noFollow'] }),
        ['statusCode', 'position', 'flags', 'links'],
        SITE,
        'empty-anchor-text',
      ),
    ).toEqual(['in the header', 'image link, nofollow']);
  });

  it('says why a canonical or an alternate can’t be indexed, and where it points', () => {
    const facts = ['statusCode', 'indexability', 'canonical', 'final', 'flags'];
    expect(
      targetFactParts(
        target({
          kind: 'canonical',
          position: 'head',
          indexability: 'canonicalised',
          canonical: { id: 3, url: `${SITE}/b/`, statusCode: 200, outcome: 'success' },
          flags: ['fromHttpHeader'],
        }),
        facts,
        SITE,
      ),
    ).toEqual(['canonicalised', 'canonical → /b/ 200', 'from the HTTP header']);
    // An indexable target says nothing of it: the fact is what's wrong.
    expect(targetFactParts(target({ indexability: 'none' }), ['indexability'], SITE)).toEqual([]);
  });

  it('says a redirect loop and a long chain', () => {
    expect(
      finalWords({ id: 4, url: `${SITE}/a/`, statusCode: 301, outcome: 'redirect', hops: 2, loop: true }, SITE),
    ).toBe('a redirect loop, back to /a/');
    expect(
      finalWords(
        { id: 4, url: 'https://other.example/x', statusCode: 200, outcome: 'success', hops: 3, loop: false },
        SITE,
      ),
    ).toBe('→ https://other.example/x 200 after 3 redirects');
  });

  it('says a status as its code, or what happened instead', () => {
    expect(statusWords(404, 'clientError')).toBe('404');
    expect(statusWords(null, 'robotsDisallowed')).toBe('blocked by robots.txt');
    expect(statusWords(null, 'timeout')).toBe('timed out');
    expect(statusWords(null, null)).toBe('not crawled');
  });

  it('says a group’s related URL without the page’s link to it', () => {
    const related: RelatedFacts = {
      statusCode: 200,
      outcome: 'success',
      contentType: 'image/jpeg',
      sizeBytes: 1_887_436,
      pixelWidth: 4000,
      pixelHeight: 3000,
      final: null,
      indexability: 'none',
      canonical: null,
    };
    expect(relatedFactParts(related, IMAGE_TOO_LARGE, SITE)).toEqual(['1.8 MB · JPEG · 4000×3000 px']);
    expect(relatedFactParts({ ...related, indexability: 'noindex' }, ['statusCode', 'indexability'], SITE)).toEqual([
      'noindex',
    ]);
  });
});

describe('a column whose value is an object', () => {
  const chain: RedirectChain = {
    hops: [
      { id: 2, url: `${SITE}/b/`, statusCode: 301, outcome: 'redirect', via: 'http' },
      { id: 3, url: `${SITE}/c/`, statusCode: 200, outcome: 'success', via: 'metaRefresh' },
    ],
    final: { id: 3, url: `${SITE}/c/`, statusCode: 200, outcome: 'success' },
    loop: false,
    tooLong: false,
  };

  it('writes a redirect chain hop by hop, each with its status, and says a loop', () => {
    expect(redirectChainWords(chain, SITE)).toBe('→ /b/ 301 → (meta refresh) /c/ 200');
    expect(redirectChainWords({ ...chain, loop: true }, null)).toBe(
      `→ ${SITE}/b/ 301 → (meta refresh) ${SITE}/c/ 200 · a loop`,
    );
    expect(redirectChainWords({ ...chain, tooLong: true }, SITE)).toBe(
      '→ /b/ 301 → (meta refresh) /c/ 200 · more than 20 hops',
    );
  });

  it('writes the robots.txt rule with its line, or why there is none', () => {
    expect(robotsRuleWords({ origin: SITE, reason: 'rule', line: 12, rule: 'Disallow: /cart/' })).toBe(
      'Disallow: /cart/ · line 12',
    );
    expect(robotsRuleWords({ origin: SITE, reason: 'unreachable', line: null, rule: null })).toBe(
      'robots.txt couldn’t be read',
    );
    expect(robotsRuleWords({ origin: SITE, reason: 'unknown', line: null, rule: null })).toBe('No matching rule');
  });

  it('reads either by its column, an end’s too, and says one it doesn’t know as JSON', () => {
    expect(objectCellWords('redirectChain', chain, SITE)).toBe('→ /b/ 301 → (meta refresh) /c/ 200');
    expect(objectCellWords('to.robotsRule', { origin: SITE, reason: 'rule', line: 3, rule: 'Disallow: /' }, SITE)).toBe(
      'Disallow: / · line 3',
    );
    expect(objectCellWords('somethingNew', { a: 1 }, SITE)).toBe('{"a":1}');
    expect(objectCellWords('redirectChain', null, SITE)).toBe('');
  });
});

describe('the size pages show an image at', () => {
  const page = (width: string | null, height: string | null, id = 812) => ({
    targets: [{ id, img: { width, height, alt: null, hasAlt: false, altLength: null, srcset: false, loading: null } }],
  });

  it('takes the largest size the pages read give in pixels, and says when they differ', () => {
    expect(shownAtOf([page('400', '300'), page('400px', '300'), page('100%', null), null], 812)).toEqual({
      width: 400,
      height: 300,
      pages: 2,
      sampled: 3,
      varies: false,
    });
    expect(shownAtOf([page('400', '300'), page('800', '600')], 812)).toMatchObject({ width: 800, varies: true });
    expect(shownAtOf([page('100%', 'auto'), page('400', '300', 9)], 812)).toBeNull();
  });

  it('says how many times wider the file is, and the insight in words', () => {
    expect(widerWords(4000, 400)).toBe('10');
    expect(widerWords(1000, 400)).toBe('2.5');
    expect(widerWords(410, 400)).toBeNull();
    const shown = { width: 400, height: 300, pages: 12, sampled: 12, varies: false };
    expect(shownAtCellWords(shown, 4000)).toBe('400×300 (10×)');
    expect(shownAtCellWords({ ...shown, varies: true }, null)).toBe('≤400×300');
    const file = { pixelWidth: 4000, pixelHeight: 3000 };
    expect(shownAtInsight(shown, file, 12)).toBe('shown at 400×300 on 12 pages; the file is 4000×3000 (10× wider)');
    expect(shownAtInsight({ ...shown, sampled: 10, pages: 10, varies: true }, file, 205)).toBe(
      'shown at up to 400×300 on the first 10 pages read; the file is 4000×3000 (10× wider)',
    );
  });
});

describe('a row’s evidence as facts', () => {
  it('says a title’s width in pixels, its length in characters and how results show it', () => {
    expect(evidenceValueWords('titleWidth', 742, null)).toBe('742 px');
    expect(evidenceValueWords('descriptionWidth', 1_204, null)).toBe('1,204 px');
    expect(evidenceValueWords('titleLength', 80, null)).toBe('80 characters');
    expect(evidenceValueWords('language', 'english', null)).toBe('lang “english”');
    expect(
      evidenceValueWords('titleInResults', 'Garden sofas and chairs for every season, delivered free…', null),
    ).toBe('in results “Garden sofas and chairs for every season, delivered free…”');
    expect(evidenceValueWords('descriptionInResults', null, null)).toBeNull();
  });
});
