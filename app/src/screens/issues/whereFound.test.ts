import { describe, expect, it } from 'vitest';
import type { IssueTargetsOfUrl, LinkRow, UrlFoundOn } from '../../api/inspector';
import type { TargetFact } from '../../api/types';
import { childPlace, pageChildren } from './groupTree';
import {
  foundOnRows,
  foundOnSummary,
  linkPlaceWords,
  referencesOf,
  sharedSource,
  targetLineWords,
  targetRows,
  targetsSummary,
  templateLinkWords,
  whereRowText,
} from './whereFound';

const ORIGIN = 'https://example.com';

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

describe('where a URL is found, in words', () => {
  it('says how many pages link to it and the first, where on it and what it says, then the sitemap, the start URL, or nothing', () => {
    const three = found({
      links: 4,
      pages: 3,
      sources: [
        link(1, '/garden-furniture/', { position: 'footer', text: 'Old range' }),
        link(1, '/garden-furniture/'),
        link(2, '/sofas/'),
      ],
    });
    expect(foundOnSummary(three, ORIGIN)).toBe('Found on 3 pages · /garden-furniture/ (Footer · “Old range”) +2');
    expect(foundOnSummary(found({ links: 1, pages: 1, sources: [link(1, '/a/')] }), ORIGIN)).toBe(
      'Found on 1 page · /a/ (Content)',
    );
    expect(
      foundOnSummary(
        found({ links: 9_000, pages: 5_000, pagesExact: false, sources: [link(1, '/a/', { position: null })] }),
        ORIGIN,
      ),
    ).toBe('Found on 5,000+ pages · /a/ +4,999');
    const listed = found({ sitemaps: { total: 2, sitemaps: [{ id: 1, url: `${ORIGIN}/sitemap.xml` }] } });
    expect(foundOnSummary(listed, ORIGIN)).toBe('In a sitemap · /sitemap.xml +1');
    expect(foundOnSummary(found({ start: true }), ORIGIN)).toBe('The start URL');
    expect(foundOnSummary(found(), ORIGIN)).toBe('Not linked from any page crawled');
    // Another host shows whole.
    expect(
      foundOnSummary(
        found({ links: 1, pages: 1, sources: [link(1, '/a/', { url: 'https://other.example/a/', position: null })] }),
        ORIGIN,
      ),
    ).toBe('Found on 1 page · https://other.example/a/');
  });

  it('says what a page names, by what the check means: links to, canonical, shows', () => {
    const named: IssueTargetsOfUrl = {
      urlId: 2,
      total: 3,
      targets: [
        {
          id: 9,
          url: `${ORIGIN}/old/`,
          statusCode: 404,
          fetched: true,
          outcome: 'clientError',
          kind: 'hyperlink',
          text: 'Old',
          position: 'footer',
        },
      ],
    };
    expect(targetsSummary(named, ORIGIN, { id: 'broken-internal-links' })).toBe('Links to /old/ (404) +2');
    expect(targetsSummary({ ...named, total: 1 }, ORIGIN, { id: 'canonical-broken' })).toBe('Canonical /old/ (404)');
    expect(targetsSummary({ ...named, total: 1 }, ORIGIN, { id: 'image-broken' })).toBe('Shows /old/ (404)');
    const refused = {
      ...named,
      total: 1,
      targets: [{ ...named.targets[0]!, statusCode: null, outcome: 'robotsDisallowed' as const }],
    };
    expect(targetsSummary(refused, ORIGIN, { id: 'broken-internal-links' })).toBe(
      'Links to /old/ (blocked by robots.txt)',
    );
  });

  it('says where on its page a link is and what it says', () => {
    expect(linkPlaceWords({ kind: 'hyperlink', position: 'footer', text: ' Old range ' })).toBe('Footer · “Old range”');
    expect(linkPlaceWords({ kind: 'image', position: 'content', text: 'Sofa' })).toBe('Image in the content · “Sofa”');
    expect(linkPlaceWords({ kind: 'canonical', position: null, text: null })).toBe('Canonical');
    expect(linkPlaceWords({ kind: null, position: null, text: null })).toBe('');
  });

  it('lists the rows under an expanded URL, and says when there are none', () => {
    const rows = foundOnRows(
      found({
        links: 7,
        pages: 6,
        sources: [1, 2, 3, 4, 5, 6].map((id) => link(id, `/p${id}/`)),
        sitemaps: { total: 3, sitemaps: [1, 2, 3].map((id) => ({ id, url: `${ORIGIN}/s${id}.xml` })) },
        start: true,
      }),
    );
    expect(rows.map((row) => row.kind)).toEqual([
      'source',
      'source',
      'source',
      'source',
      'source',
      'sitemap',
      'sitemap',
      'start',
      'all',
    ]);
    expect(whereRowText(rows[rows.length - 1]!)).toBe('Show all 7 links in Links');
    expect(foundOnRows(found())).toEqual([
      { kind: 'nothing', words: 'No page crawled links to it, and no sitemap lists it' },
    ]);
    expect(targetRows({ urlId: 1, total: 0, targets: [] })).toEqual([
      { kind: 'nothing', words: 'It names no other URL' },
    ]);
    const many = targetRows({
      urlId: 1,
      total: 9,
      targets: [
        {
          id: 3,
          url: `${ORIGIN}/x/`,
          statusCode: 404,
          fetched: true,
          outcome: null,
          kind: null,
          text: null,
          position: null,
        },
      ],
    });
    // The way on to the rest is to Links, where they all are.
    expect(many.map(whereRowText)).toEqual([`${ORIGIN}/x/ (404)`, 'Show all 9 in Links']);
  });

  it('finds the one page most of the URLs are linked from, with where on it when they share it', () => {
    const footer = (urlId: number) =>
      found({ urlId, links: 1, pages: 1, sources: [link(1, '/garden-furniture/', { position: 'footer' })] });
    expect(
      sharedSource([footer(10), footer(11), found({ urlId: 12, links: 1, pages: 1, sources: [link(2, '/b/')] })]),
    ).toEqual({
      // Its id too: the callout's link inspects it, and its links are shown in Links.
      id: 1,
      url: `${ORIGIN}/garden-furniture/`,
      urls: 2,
      of: 3,
      position: 'footer',
    });
    expect(sharedSource([footer(10)])).toBeNull();
    expect(sharedSource([footer(10), found({ urlId: 11, links: 1, pages: 1, sources: [link(2, '/b/')] })])).toBeNull();
  });

  it('says where a template’s shared link is', () => {
    expect(templateLinkWords({ kind: 'hyperlink', position: 'footer' })).toBe('It’s a link in the footer');
    expect(templateLinkWords({ kind: 'image', position: 'header' })).toBe('It’s an image in the header');
    expect(templateLinkWords({ kind: 'canonical', position: 'head' })).toBe(
      'It’s their canonical, in each page’s head',
    );
    expect(templateLinkWords({ kind: 'hyperlink', position: null })).toBeNull();
    expect(templateLinkWords(null)).toBeNull();
  });

  it('reads a sidecar from before the field as referring to nothing', () => {
    expect(referencesOf({})).toBe('none');
    expect(referencesOf({ references: 'sources' })).toBe('sources');
  });
});

describe('an open group with a page expanded', () => {
  it('puts the expanded page’s rows after it, and the link to all its pages last', () => {
    const open = { page: 1, rows: 3 };
    expect(pageChildren(4, open)).toBe(8);
    expect(pageChildren(4, null)).toBe(5);
    expect([0, 1, 2, 3, 4, 5, 6, 7].map((child) => childPlace(child, 4, open))).toEqual([
      { kind: 'page', page: 0 },
      { kind: 'page', page: 1 },
      { kind: 'where', page: 1, row: 0 },
      { kind: 'where', page: 1, row: 1 },
      { kind: 'where', page: 1, row: 2 },
      { kind: 'page', page: 2 },
      { kind: 'page', page: 3 },
      { kind: 'more' },
    ]);
    // A page past the ones that came is no page to expand.
    expect(pageChildren(2, { page: 5, rows: 3 })).toBe(3);
  });
});

describe('a page under a group of one URL it names', () => {
  it('names the group’s URL first', () => {
    const target = (id: number, path: string, statusCode: number) => ({
      id,
      url: `${ORIGIN}${path}`,
      statusCode,
      fetched: true,
      outcome: null,
      kind: 'hyperlink' as const,
      text: null,
      position: null,
    });
    const named: IssueTargetsOfUrl = { urlId: 1, total: 2, targets: [target(3, '/a/', 404), target(9, '/b/', 301)] };
    expect(targetsSummary(named, ORIGIN, { id: 'broken-internal-links' }, 9)).toBe('Links to /b/ (301) +1');
    expect(targetsSummary(named, ORIGIN, { id: 'broken-internal-links' }, 77)).toBe('Links to /a/ (404) +1');
  });

  it('says a link to the page itself is to this same page, not its own address again', () => {
    // A home page's logo linking home with nothing to read: the issue names the page itself.
    const logo = {
      id: 1,
      url: `${ORIGIN}/`,
      statusCode: 200,
      fetched: true,
      outcome: 'success' as const,
      kind: 'hyperlink' as const,
      text: null,
      position: 'header' as const,
      flags: ['imageLink' as const, 'samePage' as const],
      links: 1,
    };
    const home: IssueTargetsOfUrl = { urlId: 1, total: 1, targets: [logo] };
    const check = {
      id: 'empty-anchor-text',
      targetFacts: ['statusCode', 'position', 'flags', 'links'] as TargetFact[],
    };
    expect(targetsSummary(home, ORIGIN, check)).toBe('Links to this same page');
    expect(targetsSummary({ ...home, total: 3 }, ORIGIN, check, 1)).toBe('Links to this same page +2');
    expect(targetLineWords(home, 1, check, ORIGIN)).toBe('A link to this same page · in the header · image link');
    expect(targetRows(home, check, ORIGIN).map(whereRowText)).toEqual([
      `${ORIGIN}/ (200) · this same page · in the header · image link`,
    ]);

    // Another page's link to the home page names it as ever.
    const page: IssueTargetsOfUrl = { urlId: 5, total: 1, targets: [logo] };
    expect(targetsSummary(page, ORIGIN, check)).toBe('Links to / (200)');
    expect(targetLineWords(page, 1, check, ORIGIN)).toBe('in the header · image link');
  });
});
