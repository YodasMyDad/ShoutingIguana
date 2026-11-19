// Where a check's URLs are found, as the parts its tables draw (the 2026-11-19 UX round, A-05, A-06, A-15): where on
// its page a link is, without what it says; how a page stands to the URL its issue names; the columns a check's
// references take; what heads the column of the URL a check's groups are of; what a check says of a URL beyond those
// columns; and how many references a row has past the one it shows.
import { describe, expect, it } from 'vitest';
import type { IssueTarget, IssueTargetsOfUrl, UrlFoundOn } from '../../api/inspector';
import type { TargetFact } from '../../api/types';
import {
  groupUrlHeader,
  linkWhereWords,
  moreReferences,
  referenceColumns,
  targetLead,
  targetRestWords,
} from './whereFound';

const ORIGIN = 'https://example.com';

const target = (overrides: Partial<IssueTarget> = {}): IssueTarget => ({
  id: 9,
  url: `${ORIGIN}/old`,
  statusCode: 301,
  fetched: true,
  outcome: 'redirect',
  kind: 'hyperlink',
  text: 'Old range',
  position: 'footer',
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

describe('where on its page a link is, without what it says', () => {
  it('says the place alone for a hyperlink, and the kind with it for anything else', () => {
    expect(linkWhereWords({ kind: 'hyperlink', position: 'footer' })).toBe('Footer');
    expect(linkWhereWords({ kind: 'hyperlink', position: 'content' })).toBe('Content');
    expect(linkWhereWords({ kind: 'image', position: 'content' })).toBe('Image in the content');
    expect(linkWhereWords({ kind: 'script', position: 'head' })).toBe('Script in the head');
    // A kind the crawl stored no place for is said alone.
    expect(linkWhereWords({ kind: 'canonical', position: null })).toBe('Canonical');
    // A link of no known kind is where it is.
    expect(linkWhereWords({ kind: null, position: 'header' })).toBe('Header');
  });

  it('says a redirect redirects here: it is on no page', () => {
    expect(linkWhereWords({ kind: 'redirect', position: 'head' })).toBe('Redirects here');
    expect(linkWhereWords({ kind: 'redirect', position: null })).toBe('Redirects here');
  });

  it('says nothing where the crawl stored neither', () => {
    expect(linkWhereWords({ kind: 'hyperlink', position: null })).toBe('');
    expect(linkWhereWords({ kind: null, position: null })).toBe('');
  });
});

describe('how a page stands to a URL its issue names', () => {
  it('leads with what the check means: links to, embeds, canonical to, alternate, loads', () => {
    expect(targetLead({ id: 'broken-internal-links', category: 'links' }, { kind: 'hyperlink' })).toBe('Links to');
    expect(targetLead({ id: 'image-too-large', category: 'images' }, { kind: 'image' })).toBe('Embeds');
    expect(targetLead({ id: 'canonical-broken' }, { kind: 'canonical' })).toBe('Canonical to');
    expect(targetLead({ id: 'hreflang-no-return' }, { kind: 'hreflang' })).toBe('Alternate');
    expect(targetLead({ id: 'mixed-content', category: 'security' }, { kind: 'script' })).toBe('Loads');
  });

  it('says a canonical for every check of canonicals, whatever its id or the link’s kind', () => {
    expect(targetLead({ id: 'noindex-with-canonical', category: 'canonicals' }, { kind: null })).toBe('Canonical to');
    expect(targetLead({ id: 'canonical-loop' }, { kind: null })).toBe('Canonical to');
  });

  it('goes by the link’s own kind where the check doesn’t say', () => {
    expect(targetLead({ id: 'some-check' }, { kind: 'canonical' })).toBe('Canonical to');
    expect(targetLead({ id: 'some-check' }, { kind: 'hreflang' })).toBe('Alternate');
    expect(targetLead({ id: 'some-check' }, { kind: 'image' })).toBe('Embeds');
    // An image a page loads over http is an image it embeds.
    expect(targetLead({ id: 'mixed-content' }, { kind: 'image' })).toBe('Embeds');
    expect(targetLead({ id: 'some-check' }, { kind: null })).toBe('Links to');
  });

  it('never says “Names”', () => {
    const kinds = ['hyperlink', 'image', 'script', 'stylesheet', 'canonical', 'hreflang', 'iframe', null] as const;
    const checks = ['broken-internal-links', 'image-broken', 'canonical-broken', 'hreflang-x', 'mixed-content', 'x'];
    for (const id of checks) for (const kind of kinds) expect(targetLead({ id }, { kind })).not.toMatch(/Names/);
  });
});

describe('the columns a check’s references take in its list', () => {
  it('has none for a check that refers to nothing, or doesn’t say', () => {
    expect(referenceColumns({ id: 'title-too-long', references: 'none' })).toBeNull();
    expect(referenceColumns({ id: 'title-too-long' })).toBeNull();
  });

  it('says where a URL other pages refer to is linked from, what the link says and where on the page it is', () => {
    expect(referenceColumns({ id: 'internal-client-error', references: 'sources' })).toEqual({
      other: 'Linked from',
      text: 'Link text',
      place: true,
    });
    // Whatever the check is of: the pages that refer to it link to it.
    expect(referenceColumns({ id: 'image-broken', references: 'sources', category: 'images' })?.other).toBe(
      'Linked from',
    );
  });

  it('heads what a page names by its kind, with only the columns that kind has', () => {
    expect(referenceColumns({ id: 'broken-internal-links', references: 'targets', category: 'links' })).toEqual({
      other: 'Links to',
      text: 'Link text',
      place: true,
    });
    // An image's alt text is among its facts, not a column of quotes.
    expect(referenceColumns({ id: 'image-too-large', references: 'targets', category: 'images' })).toEqual({
      other: 'Image',
      text: null,
      place: true,
    });
    // A canonical is in the head and says nothing.
    expect(referenceColumns({ id: 'canonical-broken', references: 'targets' })).toEqual({
      other: 'Canonical to',
      text: null,
      place: false,
    });
    expect(referenceColumns({ id: 'noindex-with-canonical', references: 'targets', category: 'canonicals' })).toEqual({
      other: 'Canonical to',
      text: null,
      place: false,
    });
    // A hreflang link's text is its language.
    expect(referenceColumns({ id: 'hreflang-no-return', references: 'targets' })).toEqual({
      other: 'Alternate',
      text: 'Language',
      place: false,
    });
    expect(referenceColumns({ id: 'mixed-content', references: 'targets', category: 'security' })).toEqual({
      other: 'Loads',
      text: null,
      place: true,
    });
  });
});

describe('what heads the column of the URL a check’s groups are of', () => {
  it('names it by what it holds', () => {
    expect(groupUrlHeader({ id: 'image-too-large', category: 'images', references: 'targets' })).toBe('Image');
    expect(groupUrlHeader({ id: 'canonical-broken' })).toBe('Canonical target');
    expect(groupUrlHeader({ id: 'noindex-with-canonical', category: 'canonicals' })).toBe('Canonical target');
    expect(groupUrlHeader({ id: 'hreflang-no-return' })).toBe('Alternate URL');
    // A chain's group is of the URL its redirects end at.
    expect(groupUrlHeader({ id: 'redirect-chain', category: 'redirects', references: 'sources' })).toBe('Redirects to');
    expect(groupUrlHeader({ id: 'broken-internal-links', category: 'links', references: 'targets' })).toBe(
      'Linked URL',
    );
    // By what the check says of its pages, where its category doesn't: they hold the links.
    expect(groupUrlHeader({ id: 'empty-anchor-text', references: 'targets' })).toBe('Linked URL');
  });

  it('says “Related URL” only where the check says nothing of what it is', () => {
    expect(groupUrlHeader({ id: 'title-duplicate', category: 'titles', references: 'none' })).toBe('Related URL');
    expect(groupUrlHeader({ id: 'internal-client-error', category: 'responseCodes', references: 'sources' })).toBe(
      'Related URL',
    );
    expect(groupUrlHeader({ id: 'some-check' })).toBe('Related URL');
  });
});

describe('what a check says of a URL its page names beyond the list’s own columns', () => {
  const facts = (...list: TargetFact[]) => list;

  it('leaves out the status, the link’s text and its place, which have columns, and says the rest', () => {
    const redirect = target({
      final: { id: 91, url: `${ORIGIN}/new/`, statusCode: 200, outcome: 'success', hops: 1, loop: false },
      links: 2,
    });
    const check = {
      id: 'links-to-redirects',
      references: 'targets' as const,
      category: 'links',
      targetFacts: facts('statusCode', 'final', 'text', 'position', 'links'),
    };
    expect(targetRestWords(redirect, check, ORIGIN)).toBe('→ /new/ 200 · 2 links');
    // One link is what a row is: nothing to add.
    expect(targetRestWords({ ...redirect, links: 1 }, check, ORIGIN)).toBe('→ /new/ 200');
    // The URL in full where it's off the site.
    expect(targetRestWords({ ...redirect, links: 1 }, check, 'https://other.example')).toBe(`→ ${ORIGIN}/new/ 200`);
  });

  it('says an image’s file and how the page shows it, its place being a column', () => {
    const image = target({
      url: `${ORIGIN}/img/hero.jpg`,
      statusCode: 200,
      outcome: 'success',
      kind: 'image',
      text: 'Garden sofa',
      position: 'content',
      contentType: 'image/jpeg',
      sizeBytes: 1_887_436,
      pixelWidth: 4000,
      pixelHeight: 3000,
      links: 1,
      img: {
        width: '400',
        height: '300',
        alt: 'Garden sofa',
        hasAlt: true,
        altLength: 11,
        srcset: false,
        loading: 'lazy',
      },
    });
    const check = {
      id: 'image-too-large',
      references: 'targets' as const,
      category: 'images',
      targetFacts: facts(
        'sizeBytes',
        'contentType',
        'pixelWidth',
        'pixelHeight',
        'img',
        'statusCode',
        'final',
        'position',
        'links',
      ),
    };
    expect(targetRestWords(image, check, ORIGIN)).toBe('1.8 MB · JPEG · 4000×3000 px · shown at 400×300 · lazy');
  });

  it('says the text and the place too for a check whose list has no column for them', () => {
    // A canonical's list is the URL and its status: whatever else the check says of it is said here.
    const check = {
      id: 'canonical-broken',
      references: 'targets' as const,
      targetFacts: facts('statusCode', 'text', 'position', 'links'),
    };
    const canonical = target({ kind: 'hyperlink', text: 'Old range', position: 'footer', links: 3 });
    const rest = targetRestWords(canonical, check, ORIGIN);
    expect(rest).toContain('“Old range”');
    expect(rest).toContain('footer');
    expect(rest).toContain('3 links');
    // Its status never: the list draws it beside the URL.
    expect(rest).not.toContain('301');
  });

  it('says nothing where the check says nothing more', () => {
    expect(targetRestWords(target(), { id: 'broken-internal-links', references: 'targets' }, ORIGIN)).toBe('');
    expect(
      targetRestWords(
        target(),
        { id: 'broken-internal-links', references: 'targets', targetFacts: facts('statusCode', 'text', 'position') },
        ORIGIN,
      ),
    ).toBe('');
  });
});

describe('how many references a row has beyond the one it shows', () => {
  const named = (total: number): IssueTargetsOfUrl => ({ urlId: 2, total, targets: [] });

  it('counts a URL’s links, the sitemaps listing it and its being the start URL, less the first', () => {
    expect(moreReferences({ foundOn: found({ links: 3, pages: 3 }), targets: null })).toBe(2);
    // By its links, not its pages: two links on one page are two rows.
    expect(moreReferences({ foundOn: found({ links: 4, pages: 3 }), targets: null })).toBe(3);
    expect(
      moreReferences({
        foundOn: found({ links: 1, pages: 1, sitemaps: { total: 1, sitemaps: [{ id: 1, url: `${ORIGIN}/s.xml` }] } }),
        targets: null,
      }),
    ).toBe(1);
    expect(
      moreReferences({
        foundOn: found({ links: 2, pages: 2, sitemaps: { total: 1, sitemaps: [] }, start: true }),
        targets: null,
      }),
    ).toBe(3);
  });

  it('is none for a URL with one reference, or with none', () => {
    expect(moreReferences({ foundOn: found({ links: 1, pages: 1 }), targets: null })).toBe(0);
    expect(moreReferences({ foundOn: found({ start: true }), targets: null })).toBe(0);
    expect(moreReferences({ foundOn: found(), targets: null })).toBe(0);
  });

  it('counts the URLs a page’s issue names, less the first', () => {
    expect(moreReferences({ foundOn: null, targets: named(5) })).toBe(4);
    expect(moreReferences({ foundOn: null, targets: named(1) })).toBe(0);
    expect(moreReferences({ foundOn: null, targets: named(0) })).toBe(0);
  });

  it('is none while neither is read', () => {
    expect(moreReferences({ foundOn: null, targets: null })).toBe(0);
  });
});
