// Where a check's URLs are found, in Advanced's words (fix-it pass, links lane, item 4): a redirect hop is said as a
// redirect, a canonical as a canonical, a URL without a status one way, and a row's detail kept where it says more.
import { describe, expect, it } from 'vitest';
import type { IssueTargetsOfUrl, LinkRow, UrlFoundOn } from '../../api/inspector';
import {
  detailSaysMore,
  foundOnSummary,
  linkPlaceWords,
  sharedSourceCallout,
  targetVerb,
  targetsSummary,
} from './whereFound';

const ORIGIN = 'https://example.com';

const link = (overrides: Partial<LinkRow>): LinkRow => ({
  id: 1,
  url: `${ORIGIN}/plants/loop-a`,
  kind: 'hyperlink',
  text: null,
  position: 'content',
  flags: [],
  followed: true,
  fragment: null,
  statusCode: 301,
  indexable: false,
  fetched: true,
  outcome: 'redirect',
  ...overrides,
});

describe('a redirect as where a URL is found', () => {
  it('says the URL redirects here, with no place on a page', () => {
    expect(linkPlaceWords(link({ kind: 'redirect', position: 'head' }))).toBe('Redirects here');
    const found: UrlFoundOn = {
      urlId: 2,
      links: 1,
      pages: 1,
      pagesExact: true,
      sources: [link({ kind: 'redirect', position: 'head' })],
      sitemaps: { total: 0, sitemaps: [] },
      start: false,
    };
    expect(foundOnSummary(found, ORIGIN)).toBe('Found on 1 page · /plants/loop-a (Redirects here)');
  });
});

describe('how a page refers to the URL its issue names', () => {
  it('says a canonical for every check of canonicals, whatever its id', () => {
    expect(targetVerb({ id: 'noindex-with-canonical', category: 'canonicals' }, { kind: 'canonical' })).toBe(
      'Canonical',
    );
    expect(targetVerb({ id: 'canonical-loop', category: 'canonicals' }, { kind: 'canonical' })).toBe('Canonical');
    expect(targetVerb({ id: 'broken-internal-links', category: 'links' }, { kind: 'hyperlink' })).toBe('Links to');
  });

  it('says a URL that didn’t answer as “no response”', () => {
    const named: IssueTargetsOfUrl = {
      urlId: 5,
      total: 1,
      targets: [
        {
          id: 9,
          url: 'http://cdn.example.net/a.js',
          statusCode: null,
          fetched: true,
          outcome: 'networkError',
          kind: 'script',
          text: null,
          position: 'head',
        },
      ],
    };
    expect(targetsSummary(named, ORIGIN, { id: 'mixed-content', category: 'security' })).toBe(
      'Loads http://cdn.example.net/a.js (no response)',
    );
  });
});

describe('a row’s detail beside what its line says', () => {
  it('is left out where it only counts what the line names, and kept where it says more', () => {
    expect(detailSaysMore('Links to 2 URLs')).toBe(false);
    expect(detailSaysMore('2 images over 200 KB, the largest 1.8 MB')).toBe(false);
    expect(detailSaysMore('3 alternates')).toBe(false);
    expect(detailSaysMore('Names /fr/ as "fr-CA"; /fr/ calls itself "fr"')).toBe(true);
    expect(detailSaysMore('The canonical URL redirects back to this page')).toBe(true);
    expect(detailSaysMore('The canonical URL 301 → /a/ 200')).toBe(true);
  });
});

describe('the one page most of a check’s URLs are linked from, in Advanced', () => {
  const shared = { url: `${ORIGIN}/blog/x/`, urls: 5, of: 8, position: 'footer' as const };

  it('states the fact, and asks to fix the links there only for a check a link’s fix clears', () => {
    expect(
      sharedSourceCallout(
        shared,
        { id: 'internal-client-error', category: 'responseCodes', subject: 'url' },
        8,
        ORIGIN,
      ),
    ).toEqual({
      title: '/blog/x/ links to 5 of these 8 URLs',
      body: 'Most of them are linked from its footer: fix or remove the links there first.',
    });
    expect(
      sharedSourceCallout(shared, { id: 'redirect-chain', category: 'redirects', subject: 'url' }, 8, ORIGIN).body,
    ).toBe('Most of them are linked from its footer: fix or remove the links there first.');
    expect(
      sharedSourceCallout(
        { ...shared, position: 'content' },
        { id: 'blocked-by-robots', category: 'indexability', subject: 'page' },
        8,
        ORIGIN,
      ),
    ).toEqual({ title: '/blog/x/ links to 5 of these 8 pages', body: 'Most of them are linked from there.' });
  });

  it('says “the first” where it looked at fewer than the check flags', () => {
    expect(
      sharedSourceCallout(
        { ...shared, of: 25 },
        { id: 'internal-client-error', category: 'responseCodes', subject: 'url' },
        300,
        ORIGIN,
      ).title,
    ).toBe('/blog/x/ links to 5 of the first 25 URLs');
  });
});
