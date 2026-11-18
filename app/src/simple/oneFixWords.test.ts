import { describe, expect, it } from 'vitest';
import { flatOneFixSentences, oneFixSentences, oneFixTitle, type OneFixCause } from './oneFixWords';

const cause = (overrides: Partial<OneFixCause>): OneFixCause => ({
  check: { id: 'image-too-large', evidence: [] },
  on: 12,
  of: 14,
  noun: 'pages',
  path: '/img/hero.jpg',
  sharedText: null,
  link: { kind: 'image', position: 'content' },
  finalPath: null,
  ...overrides,
});

describe('the One fix, in plain words', () => {
  // The fixit audit: a shared image on 195 of 225 pages was "One fix", as if one change cleared the problem.
  it('says One fix clears them all only when the cause is on every page, else how many it covers', () => {
    expect(oneFixTitle(14, 14, 'pages')).toBe('One fix clears all 14 of these pages');
    expect(oneFixTitle(195, 225, 'pages')).toBe('One fix covers 195 of 225 pages');
    expect(oneFixTitle(1_200, 1_250, 'addresses')).toBe('One fix covers 1,200 of 1,250 addresses');
  });

  it('says exactly what the pages share and on how many, then the one change', () => {
    expect(oneFixSentences(cause({}))).toEqual({
      lead: 'The same image, /img/hero.jpg, is on 12 of these 14 pages.',
      fix: 'Make it smaller once and all 12 are fixed.',
    });
    expect(
      oneFixSentences(
        cause({ check: { id: 'image-missing-alt' }, on: 14, link: { kind: 'image', position: 'header' } }),
      ),
    ).toEqual({
      lead: 'The same image, /img/hero.jpg, is in the header on all 14 of these pages.',
      fix: 'Give it alt text once and all 14 are fixed.',
    });
  });

  it('says a shared link where it is, and what to point it at', () => {
    expect(
      oneFixSentences(
        cause({
          check: { id: 'links-to-redirects' },
          path: '/old',
          link: { kind: 'hyperlink', position: 'footer' },
          finalPath: '/new/',
        }),
      ),
    ).toEqual({
      lead: 'The same link, to /old, is in the footer on 12 of these 14 pages.',
      fix: 'Point it at /new/ once there and all 12 are fixed.',
    });
    expect(
      oneFixSentences(
        cause({
          check: { id: 'broken-internal-links' },
          path: '/gone/',
          link: { kind: 'hyperlink', position: 'navigation' },
        }),
      ).lead,
    ).toBe('The same link, to /gone/, is in the menu on 12 of these 14 pages.');
  });

  it('says a shared main address, a shared title, and a fault without an address', () => {
    expect(
      oneFixSentences(
        cause({ check: { id: 'canonical-broken' }, path: '/', link: { kind: 'canonical', position: 'head' } }),
      ).lead,
    ).toBe('12 of these 14 pages name the same main address, /.');
    expect(
      oneFixSentences(
        cause({ check: { id: 'title-duplicate', evidence: ['title'] }, sharedText: 'Shop | Example', link: null }),
      ),
    ).toEqual({
      lead: '12 of these 14 pages share the same title, “Shop | Example”.',
      fix: 'It’s probably set in one place for all of them: change it there and all 12 are fixed.',
    });
    expect(oneFixSentences(cause({ check: { id: 'open-graph-missing' }, path: null, link: null }))).toEqual({
      lead: '12 of these 14 pages have the same fault, in the part of the site every page shares.',
      fix: 'Fix it there once and all 12 are fixed.',
    });
    expect(
      oneFixSentences(cause({ check: { id: 'blocked-by-robots' }, noun: 'addresses', path: null, link: null })).lead,
    ).toBe('12 of these 14 addresses have the same cause.');
    expect(flatOneFixSentences(13, 14)).toEqual({
      lead: 'It’s on 13 of the 14 pages checked.',
      fix: 'Fix it there once and all 13 are fixed.',
    });
  });
});
