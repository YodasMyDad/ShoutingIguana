// "Where to fix it" (the UX audit of 2026-11-19, S-02 to S-05): a fault that is a link, by the page to edit.
import { describe, expect, it } from 'vitest';
import type { IssueGroup, IssueRow, LinkRow as GridLink } from '../api/grids';
import type { IssueTarget, IssueTargetsOfUrl, LinkRow, UrlFoundOn } from '../api/inspector';
import { SIMPLE_CHECKS } from '../test/simpleSidecar';
import {
  addressesModel,
  deadEndWrong,
  findableWords,
  fixListText,
  fixShape,
  fixSummary,
  fixedOnce,
  linkWords,
  linksToChange,
  pagesModel,
  placeWords,
  plainWrong,
  sharedWhere,
  unlinkedWords,
} from './fixPages';

const SITE = 'https://www.example.com';
const check = (id: string) => SIMPLE_CHECKS.checks.find((candidate) => candidate.id === id)!;

const source = (id: number, path: string, text: string | null, position: LinkRow['position']): LinkRow => ({
  id,
  url: `${SITE}${path}`,
  kind: 'hyperlink',
  text,
  position,
  flags: [],
  followed: true,
  fragment: null,
  statusCode: 200,
  indexable: true,
  fetched: true,
});

const found = (urlId: number, pages: number, sources: LinkRow[], extra: Partial<UrlFoundOn> = {}): UrlFoundOn => ({
  urlId,
  links: pages,
  pages,
  pagesExact: true,
  sources,
  sitemaps: { total: 0, sitemaps: [] },
  start: false,
  ...extra,
});

const link = (
  from: number,
  fromPath: string,
  title: string | null,
  to: number,
  text: string | null,
  position: GridLink['position'] = 'content',
): GridLink => ({
  id: from * 1_000 + to,
  'from.id': from,
  'from.url': `${SITE}${fromPath}`,
  'from.title': title,
  'to.id': to,
  'to.url': `${SITE}/dead-${to}/`,
  kind: 'hyperlink',
  text,
  position,
});

const dead = (id: number) => ({ id, url: `${SITE}/dead-${id}/`, wrong: 'missing (404 Not Found)' });

describe('which checks are listed by the page to edit', () => {
  it('lists dead addresses and faulty links page first, and nothing else', () => {
    expect(fixShape(check('internal-client-error'))).toBe('addresses');
    expect(fixShape(check('redirect-broken'))).toBe('addresses');
    expect(fixShape(check('broken-internal-links'))).toBe('pages');
    expect(fixShape(check('broken-external-links'))).toBe('pages');
    expect(fixShape(check('empty-anchor-text'))).toBe('pages');
    // A server error is mended at the address, a missing file isn't a link, an image isn't either.
    expect(fixShape(check('internal-server-error'))).toBeNull();
    expect(fixShape(check('broken-resources'))).toBeNull();
    expect(fixShape(check('image-broken'))).toBeNull();
    expect(fixShape(check('title-missing'))).toBeNull();
  });
});

describe('dead addresses, by the page to edit', () => {
  const rows = [dead(1), dead(2), dead(3), dead(4), dead(5)];
  const everywhere = Array.from({ length: 5 }, (_, index) => source(100 + index, `/p${index}/`, 'Old range', 'footer'));
  const where = new Map<number, UrlFoundOn>([
    [1, found(1, 1, [source(10, '/blog/', 'Buy Now', 'content')])],
    [2, found(2, 2, [source(10, '/blog/', 'Buy Now', 'content'), source(11, '/about/', null, 'content')])],
    // In the footer of 212 pages: one row, never 212.
    [3, found(3, 212, everywhere)],
    // Only a sitemap lists it.
    [4, found(4, 0, [], { sitemaps: { total: 1, sitemaps: [{ id: 7, url: `${SITE}/sitemap.xml` }] } })],
  ]);
  const links = [
    link(10, '/blog/', 'The blog', 1, 'Buy Now'),
    link(10, '/blog/', 'The blog', 2, 'Buy Now'),
    link(11, '/about/', null, 2, null),
    // The same link twice on a page is one row with a count.
    link(11, '/about/', null, 2, null),
  ];
  const model = addressesModel(rows, where, links);

  it('lists each page once, the page with the most links to change first, its links under it', () => {
    expect(model.pages.map((page) => [page.url, page.title, page.links.length])).toEqual([
      [`${SITE}/blog/`, 'The blog', 2],
      [`${SITE}/about/`, null, 1],
    ]);
    expect(model.pages[0]!.links.map((each) => [each.text, each.target.url, each.target.wrong])).toEqual([
      ['Buy Now', `${SITE}/dead-1/`, 'missing (404 Not Found)'],
      ['Buy Now', `${SITE}/dead-2/`, 'missing (404 Not Found)'],
    ]);
    expect(model.pages[1]!.links[0]).toMatchObject({ text: null, count: 2 });
  });

  it('makes a link many pages share one row, with where on them it is', () => {
    expect(model.shared).toHaveLength(1);
    expect(model.shared[0]).toMatchObject({ pages: 212, position: 'footer', text: 'Old range' });
    expect(sharedWhere(model.shared[0]!)).toBe('In the footer of 212 pages');
    expect(fixedOnce(model.shared[0]!)).toBe(true);
    expect(sharedWhere({ pages: 14, pagesExact: true, position: 'content' })).toBe('On 14 pages');
    expect(sharedWhere({ pages: 5_000, pagesExact: false, position: null })).toBe('On 5,000+ pages');
    expect(fixedOnce({ position: 'content' })).toBe(false);
  });

  it('keeps what no page links to in a list of its own, saying where it came from, and never drops an address', () => {
    expect(model.unlinked.map((each) => [each.target.url, each.where])).toEqual([
      [`${SITE}/dead-4/`, 'sitemap'],
      // Where it's found couldn't be read: still listed.
      [`${SITE}/dead-5/`, 'unread'],
    ]);
    expect(unlinkedWords(model.unlinked[0]!)).toBe(
      'No page links to it. It’s listed in your sitemap: take it out of there.',
    );
    expect(unlinkedWords({ where: 'nowhere', sitemaps: [] })).toBe('No page that was checked links to it now.');
  });

  it('counts what it lists', () => {
    expect(fixSummary(model)).toBe(
      '3 links to change on 2 pages · 1 link many pages share · 2 addresses no page links to',
    );
    expect(linksToChange(1)).toBe('1 link to change');
    expect(linksToChange(5)).toBe('5 links to change');
  });

  it('marks an address that is new since the last check', () => {
    const marked = addressesModel(
      [
        { ...dead(1), change: { state: 'new', detailThen: null } },
        { ...dead(2), change: { state: 'still', detailThen: '404 Not Found' } },
      ],
      where,
      links,
    );
    expect(marked.pages[0]!.links.map((each) => each.target.isNew)).toEqual([true, false]);
  });

  it('hands the list on as text: the page first, then each link, where it leads and what’s wrong (S-05)', () => {
    expect(fixListText(model).join('\n')).toBe(
      [
        `Page: ${SITE}/blog/ (The blog)`,
        '  Link: “Buy Now” (main content)',
        `    leads to: ${SITE}/dead-1/ (missing (404 Not Found))`,
        '  Link: “Buy Now” (main content)',
        `    leads to: ${SITE}/dead-2/ (missing (404 Not Found))`,
        '',
        `Page: ${SITE}/about/`,
        '  Link: A link with no words (main content, 2 times)',
        `    leads to: ${SITE}/dead-2/ (missing (404 Not Found))`,
        '',
        'Links many pages share:',
        '  In the footer of 212 pages: “Old range”',
        `    leads to: ${SITE}/dead-3/ (missing (404 Not Found))`,
        '',
        'Addresses no page links to:',
        `  ${SITE}/dead-4/ (missing (404 Not Found))`,
        '    No page links to it. It’s listed in your sitemap: take it out of there.',
        `    Sitemap: ${SITE}/sitemap.xml`,
        `  ${SITE}/dead-5/ (missing (404 Not Found))`,
        '    Where it’s linked from couldn’t be read.',
      ].join('\n'),
    );
  });
});

describe('pages that hold a faulty link, by the page to edit', () => {
  const group = (id: number, path: string, pages: number, extra: Partial<IssueGroup> = {}): IssueGroup => ({
    detail: null,
    relatedUrlId: id,
    relatedUrl: `${SITE}${path}`,
    pages,
    urls: pages,
    share: 0,
    likelyTemplate: false,
    related: {
      statusCode: 404,
      outcome: 'clientError',
      contentType: null,
      sizeBytes: null,
      pixelWidth: null,
      pixelHeight: null,
      final: null,
      indexability: null,
      canonical: null,
    },
    ...extra,
  });
  const page = (id: number, path: string, title: string | null): IssueRow => ({ id, url: `${SITE}${path}`, title });
  const target = (id: number, text: string | null, position: IssueTarget['position'], links = 1): IssueTarget => ({
    id,
    url: `${SITE}/t${id}/`,
    statusCode: 404,
    fetched: true,
    outcome: 'clientError',
    kind: 'hyperlink',
    text,
    position,
    links,
  });
  const names = (urlId: number, targets: IssueTarget[]): [number, IssueTargetsOfUrl] => [
    urlId,
    { urlId, total: targets.length, targets },
  ];
  const model = pagesModel(
    [
      { group: group(50, '/gone/', 2), rows: [page(1, '/', 'Home'), page(2, '/blog/', 'Blog')] },
      { group: group(51, '/also-gone/', 1), rows: [page(2, '/blog/', 'Blog')] },
      // The logo's link, on every page: the engine takes it for the template's.
      {
        group: group(52, '/', 260, { likelyTemplate: true, related: null }),
        rows: [page(1, '/', 'Home'), page(2, '/blog/', 'Blog')],
      },
    ],
    new Map([
      names(1, [target(50, 'Sale', 'navigation'), target(52, null, 'header')]),
      names(2, [target(50, 'the sale', 'content', 3), target(51, 'Read more', 'content'), target(52, null, 'header')]),
    ]),
    SITE,
  );

  it('lists each page once with every faulty link on it, in its own words and place', () => {
    expect(
      model.pages.map((each) => [each.title, each.links.map((one) => [one.text, one.position, one.count])]),
    ).toEqual([
      [
        'Blog',
        [
          ['Read more', 'content', 1],
          ['the sale', 'content', 3],
        ],
      ],
      ['Home', [['Sale', 'navigation', 1]]],
    ]);
    expect(model.pages[0]!.links[0]!.target).toMatchObject({
      url: `${SITE}/also-gone/`,
      wrong: 'missing (404 Not Found)',
    });
  });

  it('makes the template’s link one row', () => {
    expect(model.shared).toHaveLength(1);
    expect(model.shared[0]).toMatchObject({ pages: 260, position: 'header', text: null });
    expect(model.shared[0]!.target.wrong).toBeNull();
  });
});

describe('the words of a link to change', () => {
  it('identifies a link by its words, and one without by what it is', () => {
    expect(linkWords({ kind: 'hyperlink', text: ' Buy  Now ' })).toBe('“Buy Now”');
    expect(linkWords({ kind: 'hyperlink', text: null })).toBe('A link with no words');
    expect(linkWords({ kind: 'image', text: 'Garden sofa' })).toBe('The image “Garden sofa”');
    expect(linkWords({ kind: 'image', text: null })).toBe('An image with no description');
    expect(linkWords({ kind: 'script', text: null })).toBe('A file the page loads');
  });

  it('opens a page at a link’s own words only: a browser can’t find an image’s description', () => {
    expect(findableWords({ kind: 'hyperlink', text: ' Buy Now ' })).toBe('Buy Now');
    expect(findableWords({ kind: 'image', text: 'Garden sofa' })).toBeNull();
    expect(findableWords({ kind: 'hyperlink', text: '  ' })).toBeNull();
  });

  it('says where on the page in a word or two', () => {
    expect(placeWords('content')).toBe('main content');
    expect(placeWords('navigation')).toBe('menu');
    expect(placeWords(null)).toBeNull();
  });

  it('says what’s wrong where the link leads, and nothing where the address works', () => {
    expect(plainWrong({ statusCode: 404 }, SITE)).toBe('missing (404 Not Found)');
    expect(plainWrong({ statusCode: 500 }, SITE)).toBe('has a server error (500 Internal Server Error)');
    expect(plainWrong({ statusCode: null, outcome: 'timeout' }, SITE)).toBe('didn’t answer');
    expect(plainWrong({ statusCode: 200 }, SITE)).toBeNull();
    expect(
      plainWrong(
        {
          statusCode: 301,
          final: { id: 2, url: `${SITE}/new/`, statusCode: 200, outcome: 'success', hops: 1, loop: false },
        },
        SITE,
      ),
    ).toBe('forwards to /new/');
    expect(
      plainWrong(
        {
          statusCode: 301,
          final: { id: 2, url: `${SITE}/gone/`, statusCode: 404, outcome: 'clientError', hops: 1, loop: false },
        },
        SITE,
      ),
    ).toBe('forwards to /gone/, which is missing (404 Not Found)');
  });

  it('says how a dead address answered from its status, else from its detail in plain words', () => {
    const row = { id: 1, url: `${SITE}/a/`, detail: '404 Not Found' };
    expect(deadEndWrong(check('internal-client-error'), { ...row, statusCode: 410 }, SITE)).toBe(
      'missing (410 Gone)',
    );
    expect(deadEndWrong(check('internal-client-error'), row, SITE)).toBe('missing (404 Not Found)');
  });
});
