// The words of a reference of a check's row (the 2026-11-19 UX round, A-01, A-04): what its link says, where on the
// page it is, what else the check says of it, and the page's URL opened at the link.
import { describe, expect, it } from 'vitest';
import type { IssueTarget, LinkRow } from '../../api/inspector';
import type { Check } from '../../api/types';
import { urlAtText } from '../../lib/textFragment';
import { OPEN_AT_LINK, openAtLink, referencePlace, referenceRest, referenceText } from './referenceWords';
import type { WhereRow } from './whereFound';

const PAGE = 'https://example.com/garden-furniture/';

const link = (overrides: Partial<LinkRow> = {}): LinkRow => ({
  id: 20,
  url: PAGE,
  kind: 'hyperlink',
  text: 'Old range',
  position: 'footer',
  flags: [],
  followed: true,
  fragment: null,
  statusCode: 200,
  indexable: true,
  fetched: true,
  outcome: 'success',
  ...overrides,
});

const target = (overrides: Partial<IssueTarget> = {}): IssueTarget => ({
  id: 9,
  url: 'https://example.com/old',
  statusCode: 301,
  fetched: true,
  outcome: 'redirect',
  kind: 'hyperlink',
  text: 'Shop the range',
  position: 'content',
  ...overrides,
});

const source = (overrides: Partial<LinkRow> = {}): WhereRow => ({ kind: 'source', link: link(overrides) });
const named = (overrides: Partial<IssueTarget> = {}): WhereRow => ({ kind: 'target', target: target(overrides) });
/** The rows that are no link: a sitemap, the start URL, the way on, and nothing. */
const OTHERS: WhereRow[] = [
  { kind: 'sitemap', sitemap: { id: 1, url: 'https://example.com/sitemap.xml' } },
  { kind: 'start' },
  { kind: 'all', total: 12, of: 'links' },
  { kind: 'nothing', words: 'It names no other URL' },
];

describe('the page’s URL opened at a link', () => {
  it('opens the page at the link’s words, and says so', () => {
    expect(openAtLink(PAGE, 'Old range')).toEqual({
      openUrl: `${PAGE}#:~:text=Old%20range`,
      openHint: 'Open the page at this link',
    });
    expect(openAtLink(PAGE, 'Old range').openUrl).toBe(urlAtText(PAGE, 'Old range'));
    expect(OPEN_AT_LINK).toBe('Open the page at this link');
  });

  it('looks for the words as the page shows them: trimmed, on one line', () => {
    expect(openAtLink(PAGE, '  Old range\n')).toEqual({
      openUrl: urlAtText(PAGE, 'Old range'),
      openHint: OPEN_AT_LINK,
    });
  });

  it('is nothing for a link with no words: the page opens at its top, as any URL does', () => {
    expect(openAtLink(PAGE, null)).toEqual({});
    expect(openAtLink(PAGE, undefined)).toEqual({});
    expect(openAtLink(PAGE, '')).toEqual({});
    expect(openAtLink(PAGE, '   ')).toEqual({});
  });

  it('is nothing for an address a browser can’t open at its words', () => {
    expect(openAtLink('mailto:shop@example.com', 'Write to us')).toEqual({});
  });
});

describe('what a reference’s link says', () => {
  it('is the link’s text, trimmed, for a page linking to the URL and for a URL the page names', () => {
    expect(referenceText(source({ text: ' Old range ' }))).toBe('Old range');
    expect(referenceText(named())).toBe('Shop the range');
  });

  it('is null for a link that says nothing', () => {
    expect(referenceText(source({ text: null }))).toBeNull();
    expect(referenceText(source({ text: '  ' }))).toBeNull();
    expect(referenceText(named({ text: null }))).toBeNull();
  });

  it('is null for a row that is no link', () => {
    for (const row of OTHERS) expect(referenceText(row)).toBeNull();
  });
});

describe('where on its page a reference’s link is', () => {
  it('says the place of a link that’s followed, without saying it’s followed', () => {
    expect(referencePlace(source())).toBe('Footer');
    expect(referencePlace(source({ kind: 'image', position: 'content' }))).toBe('Image in the content');
    expect(referencePlace(source({ kind: 'redirect', position: null }))).toBe('Redirects here');
  });

  it('says so after the place when a link isn’t followed, and how', () => {
    expect(referencePlace(source({ followed: false, flags: ['noFollow'] }))).toBe('Footer · nofollow');
    expect(referencePlace(source({ followed: false, flags: [] }))).toBe('Footer · Not followed');
    // With nowhere stored, that alone.
    expect(referencePlace(source({ position: null, followed: false, flags: ['noFollow'] }))).toBe('nofollow');
  });

  it('says the place of a URL the page names', () => {
    expect(referencePlace(named())).toBe('Content');
    expect(referencePlace(named({ kind: 'image', position: 'header' }))).toBe('Image in the header');
    expect(referencePlace(named({ position: null }))).toBe('');
  });

  it('is empty for a row that is no link', () => {
    for (const row of OTHERS) expect(referencePlace(row)).toBe('');
  });
});

describe('what else the check says of a reference', () => {
  const check: Check = {
    id: 'links-to-redirects',
    category: 'links',
    severity: 'notice',
    title: 'Internal links to redirects',
    description: '',
    why: '',
    fix: '',
    learnMore: null,
    grouping: 'relatedUrl',
    references: 'targets',
    targetFacts: ['statusCode', 'final', 'text', 'position', 'links'],
  };
  const view = { check, siteOrigin: 'https://example.com' };

  it('says the check’s facts of a URL the page names, beyond its status, its text and its place', () => {
    const redirect = named({
      final: { id: 91, url: 'https://example.com/new/', statusCode: 200, outcome: 'success', hops: 1, loop: false },
      links: 2,
    });
    expect(referenceRest(redirect, view)).toBe('→ /new/ 200 · 2 links');
  });

  it('says nothing of a page linking to the URL, or of a row that is no link', () => {
    expect(referenceRest(source(), view)).toBe('');
    for (const row of OTHERS) expect(referenceRest(row, view)).toBe('');
  });
});
