import { describe, expect, it } from 'vitest';
import type { CrawlListItem } from '../api/types';
import type { ExportBuildOptions } from './exportOffer';
import {
  crawlFileChoices,
  issuesOffer,
  linkExportColumns,
  linkGroupsOffer,
  linksOffer,
  robotsBlockedOffer,
  robotsOriginsOffer,
  sitemapsOffer,
  sitemapUrlsOffer,
  urlExportColumns,
  urlGridOffer,
  viewWords,
} from './offers';

const item = {
  id: 'c1',
  site: 'example.com',
  startedAt: '2026-10-31T10:15:00Z',
  analyzedAt: '2026-10-31T11:00:00Z',
} as CrawlListItem;

const as = (patch: Partial<ExportBuildOptions> = {}): ExportBuildOptions => ({
  format: 'xlsx',
  path: 'C:\\x\\a.xlsx',
  cut: 'full',
  crawlOrder: false,
  ...patch,
});

const view = {
  item,
  crawling: false,
  columns: ['url', 'statusCode', 'outcome', 'title'],
  filters: [{ column: 'type', op: 'in' as const, values: ['page'] }],
  sort: [{ column: 'inlinks', desc: true }],
  search: 'rattan',
  rows: 264,
};

describe('a grid’s export is the grid’s own read', () => {
  it('URLs: its columns, filters, search and sort, and no page', () => {
    const offer = urlGridOffer({ ...view, viewName: null, linkEnds: undefined });
    expect(offer.choices[0]?.build(as())).toEqual({
      kind: 'urls',
      format: 'xlsx',
      path: 'C:\\x\\a.xlsx',
      columns: ['url', 'statusCode', 'outcome', 'title'],
      filters: [{ column: 'type', op: 'in', values: ['page'] }],
      search: 'rattan',
      sort: [{ column: 'inlinks', desc: true }],
    });
    expect(offer.choices[0]?.description).toBe('264 URLs: this filter, this search, this sort, these 4 columns');
    expect(offer.choices[0]?.fileView).toBe('URLs');
  });

  it('in crawl order when asked, and while crawling says a sort by what the crawl changes is refused', () => {
    const offer = urlGridOffer({ ...view, crawling: true, viewName: 'Thin pages', linkEnds: undefined });
    expect(offer.choices[0]?.build(as({ crawlOrder: true }))).not.toHaveProperty('sort');
    expect(offer.choices[0]?.sortedWhileCrawling).toBe(true);
    expect(offer.note).toMatch(/as it is when the export begins/);
    expect(offer.choices[0]?.fileView).toBe('URLs Thin pages');
    const byUrl = urlGridOffer({
      ...view,
      sort: [{ column: 'url', desc: false }],
      crawling: true,
      viewName: null,
      linkEnds: undefined,
    });
    expect(byUrl.choices[0]?.sortedWhileCrawling).toBe(false);
  });

  it('a view narrowed to a links view’s pages or targets is exported narrowed to them, and its name says which', () => {
    const ends = {
      end: 'to' as const,
      filters: [{ column: 'kind', op: 'in' as const, values: ['hyperlink'] }],
      search: '',
    };
    const targets = urlGridOffer({ ...view, viewName: null, linkEnds: ends });
    expect(targets.choices[0]?.unavailable).toBeUndefined();
    expect(targets.choices[0]?.build(as())).toEqual({
      kind: 'urls',
      format: 'xlsx',
      path: 'C:\\x\\a.xlsx',
      columns: ['url', 'statusCode', 'outcome', 'title'],
      filters: [{ column: 'type', op: 'in', values: ['page'] }],
      search: 'rattan',
      sort: [{ column: 'inlinks', desc: true }],
      // Only the links' fields given: an empty search is every link, and isn't sent.
      linkEnds: { end: 'to', filters: [{ column: 'kind', op: 'in', values: ['hyperlink'] }] },
    });
    expect(targets.choices[0]?.fileView).toBe('URLs link targets');
    expect(targets.choices[0]?.description).toBe(
      '264 URLs: this filter, this search, this sort, these 4 columns, only the URLs a links view’s links lead to',
    );
    const pages = urlGridOffer({ ...view, viewName: 'Thin pages', linkEnds: { end: 'from', search: 'sale' } });
    expect(pages.choices[0]?.build(as())).toMatchObject({ linkEnds: { end: 'from', search: 'sale' } });
    expect(pages.choices[0]?.fileView).toBe('URLs Thin pages pages with the links');
    expect(urlGridOffer({ ...view, viewName: null, linkEnds: undefined }).choices[0]?.build(as())).not.toHaveProperty(
      'linkEnds',
    );
  });

  it('writes a status shown as words beside its code, and the follow column as its four flags', () => {
    expect(urlExportColumns(['url', 'statusCode', 'title'])).toEqual(['url', 'statusCode', 'outcome', 'title']);
    expect(urlExportColumns(['title'])).toEqual(['url', 'title']);
    expect(linkExportColumns(['from.url', 'follow', 'to.statusCode'], 'follow', ['followed', 'noFollow'])).toEqual([
      'from.url',
      'followed',
      'noFollow',
      'to.statusCode',
      'to.outcome',
    ]);
  });

  it('Links, one sitemap’s URLs and an origin’s blocked URLs take their own fields', () => {
    expect(linksOffer({ ...view, viewName: 'Broken links' }).choices[0]?.build(as({ format: 'csv' }))).toMatchObject({
      kind: 'links',
      format: 'csv',
    });
    expect(
      sitemapUrlsOffer({ ...view, sitemapId: 7, sitemapName: 'sitemap.xml' }).choices[0]?.build(as()),
    ).toMatchObject({ kind: 'sitemapUrls', sitemapId: 7, search: 'rattan' });
    const blocked = robotsBlockedOffer({ item, crawling: false, origin: 'https://example.com', line: 4, rows: null });
    expect(blocked.choices[0]?.build(as())).toEqual({
      kind: 'robotsBlocked',
      format: 'xlsx',
      path: 'C:\\x\\a.xlsx',
      origin: 'https://example.com',
      line: 4,
    });
    expect(blocked.choices[0]?.fileView).toBe('blocked by example.com robots.txt line 4');
  });
});

describe('Links grouped by target', () => {
  it('writes what’s shown: one row a target, with the view’s filters, search and order, and no columns to choose', () => {
    const offer = linkGroupsOffer({
      item,
      crawling: false,
      filters: [{ column: 'to.statusClass', op: 'in', values: ['4xx', '5xx'] }],
      sort: { column: 'pages', desc: true },
      search: 'rattan',
      targets: 9,
      viewName: 'Broken links',
    });
    expect(offer.title).toBe('Export links by target');
    const [choice] = offer.choices;
    expect(choice?.description).toBe(
      '9 targets, one row a target: this filter, this search, the most linked from pages first',
    );
    expect(choice?.fileView).toBe('Broken links by target');
    expect(choice?.formats).toEqual(['csv', 'xlsx']);
    expect(choice?.build(as({ format: 'csv' }))).toEqual({
      kind: 'linkGroups',
      format: 'csv',
      path: 'C:\\x\\a.xlsx',
      filters: [{ column: 'to.statusClass', op: 'in', values: ['4xx', '5xx'] }],
      search: 'rattan',
      sort: [{ column: 'pages', desc: true }],
    });
    // Images, by size: named for them; no view name, the links.
    const images = linkGroupsOffer({
      item,
      crawling: false,
      filters: [{ column: 'kind', op: 'in', values: ['image'] }],
      sort: { column: 'to.sizeBytes', desc: true },
      search: undefined,
      targets: 1,
      viewName: null,
      images: true,
    });
    expect(images.choices[0]?.description).toBe('1 image, one row an image: this filter, by this sort');
    expect(images.choices[0]?.fileView).toBe('images');
  });
});

describe('the two lists', () => {
  it('Sitemaps: the list, as CSV or Excel, with no field of a grid’s', () => {
    const offer = sitemapsOffer({ item, crawling: false, rows: 12 });
    expect(offer.title).toBe('Export sitemaps');
    expect(offer.choices.map((choice) => [choice.id, choice.formats, choice.unavailable])).toEqual([
      ['view', ['csv', 'xlsx'], undefined],
    ]);
    expect(offer.choices[0]?.build(as())).toEqual({ kind: 'sitemaps', format: 'xlsx', path: 'C:\\x\\a.xlsx' });
    expect(offer.choices[0]?.build(as({ format: 'csv' }))).toMatchObject({ format: 'csv' });
    expect(offer.choices[0]?.description).toBe('12 sitemaps: what became of each, and its URLs counted');
    expect(offer.choices[0]?.fileView).toBe('sitemaps');
    expect(sitemapsOffer({ item, crawling: true, rows: null }).note).toMatch(/as it is when the export begins/);
  });

  it('robots.txt: the origins, with the list’s search and filter when it has them', () => {
    const all = robotsOriginsOffer({ item, crawling: false, rows: 17 });
    expect(all.title).toBe('Export robots.txt');
    expect(all.choices[0]?.build(as({ format: 'csv' }))).toEqual({
      kind: 'robotsOrigins',
      format: 'csv',
      path: 'C:\\x\\a.xlsx',
    });
    expect(all.choices[0]?.description).toBe('17 origins: what each answer meant, and the URLs it blocked');
    expect(all.choices[0]?.fileView).toBe('robots.txt origins');

    const narrowed = robotsOriginsOffer({ item, crawling: false, rows: 1, search: 'example', blocked: true });
    expect(narrowed.choices[0]?.build(as())).toEqual({
      kind: 'robotsOrigins',
      format: 'xlsx',
      path: 'C:\\x\\a.xlsx',
      search: 'example',
      blocked: true,
    });
    expect(narrowed.choices[0]?.description).toBe(
      '1 origin that blocked a URL, whose name has “example”: what each answer meant, and the URLs it blocked',
    );
    expect(narrowed.choices[0]?.fileView).toBe('robots.txt origins that blocked a URL');
    expect(
      robotsOriginsOffer({ item, crawling: false, rows: null, blocked: false }).choices[0]?.build(as()),
    ).toMatchObject({ blocked: false });
  });
});

it('names an origin’s blocked URLs narrowed by their reason (the review’s finding 8)', () => {
  const offer = robotsBlockedOffer({
    item,
    crawling: false,
    origin: 'https://example.com',
    reason: 'unreachable',
    rows: null,
  });
  const choice = offer.choices[0];
  expect(choice?.title).toBe('Refused as robots.txt couldn’t be read');
  expect(choice?.description).toMatch(/every URL refused because its robots\.txt couldn’t be read/);
  expect(choice?.fileView).toBe('refused by example.com robots.txt unread');
  expect(choice?.build(as())).toMatchObject({ kind: 'robotsBlocked', reason: 'unreachable' });
  expect(
    robotsBlockedOffer({ item, crawling: false, origin: 'https://example.com', reason: 'unknown', rows: 2 }).choices[0]
      ?.title,
  ).toBe('Blocked with no matching rule');
});

it('names an origin’s blocked URLs narrowed to the conflicts, and every file says what blocking each costs', () => {
  const offer = robotsBlockedOffer({
    item,
    crawling: false,
    origin: 'https://example.com',
    conflict: true,
    rows: null,
  });
  const choice = offer.choices[0];
  expect(choice?.title).toBe('In your sitemap or menu but blocked');
  expect(choice?.description).toBe(
    'example.com’s robots.txt: every URL it blocked that your sitemap or menu asks for, with the pages linking to each and its sitemap',
  );
  expect(choice?.fileView).toBe('blocked by example.com robots.txt in sitemap or menu');
  expect(choice?.build(as())).toEqual({
    kind: 'robotsBlocked',
    format: 'xlsx',
    path: 'C:\\x\\a.xlsx',
    origin: 'https://example.com',
    conflict: true,
  });
  // Not narrowed to them: no conflict field, and the costs are in the file all the same.
  const all = robotsBlockedOffer({ item, crawling: false, origin: 'https://example.com', conflict: false, rows: 4 });
  expect(all.choices[0]?.build(as())).not.toHaveProperty('conflict');
  expect(all.choices[0]?.description).toBe(
    '4 URLs: every URL it blocked, with the line that decided each, the pages linking to it and its sitemap',
  );
});

describe('Issues and Overview', () => {
  it('offer the check, the open group, the report and every check', () => {
    const offer = issuesOffer({
      item,
      crawling: false,
      simple: false,
      check: { id: 'missing-title', title: 'Missing title', urls: 12 },
      group: { ref: { detail: 'x', relatedUrlId: null }, pages: 3, words: '“x”' },
    });
    expect(offer.choices.map((choice) => choice.id)).toEqual(['check', 'group', 'report', 'workbook']);
    expect(offer.choices[0]?.build(as({ format: 'csv' }))).toEqual({
      kind: 'check',
      format: 'csv',
      path: 'C:\\x\\a.xlsx',
      checkId: 'missing-title',
    });
    expect(offer.choices[1]?.build(as())).toMatchObject({
      kind: 'issueRows',
      checkId: 'missing-title',
      group: { detail: 'x', relatedUrlId: null },
    });
    expect(offer.choices[2]?.build(as({ format: 'html', cut: 'summary' }))).toEqual({
      kind: 'report',
      format: 'html',
      path: 'C:\\x\\a.xlsx',
      cut: 'summary',
    });
    expect(offer.choices[3]?.build(as())).toEqual({ kind: 'workbook', format: 'xlsx', path: 'C:\\x\\a.xlsx' });
  });

  it('a check that doesn’t group exports its list as the grid shows it, in the grid’s sort (the review’s finding 2)', () => {
    const offer = issuesOffer({
      item,
      crawling: false,
      simple: false,
      check: {
        id: 'not-in-sitemap',
        title: 'Not in a sitemap',
        urls: 9,
        flat: true,
        sort: [{ column: 'url', desc: true }],
      },
      group: null,
    });
    expect(offer.choices[0]?.build(as({ format: 'csv' }))).toEqual({
      kind: 'issueRows',
      format: 'csv',
      path: 'C:\\x\\a.xlsx',
      checkId: 'not-in-sitemap',
      columns: ['url', 'detail'],
      filters: [],
      sort: [{ column: 'url', desc: true }],
    });
    expect(offer.choices[0]?.build(as({ format: 'csv', crawlOrder: true }))).not.toHaveProperty('sort');
  });

  it('a crawl never analysed offers the analysis in place of the report and the workbook', () => {
    const never = { ...item, analyzedAt: null };
    const [report, workbook] = crawlFileChoices(never, true);
    expect(report?.unavailable).toMatch(/Work out the results first/);
    expect(workbook?.remedy).toBe('analyse');
    expect(crawlFileChoices(item, false).every((choice) => !choice.unavailable)).toBe(true);
  });
});

it('says what the file keeps of the view', () => {
  expect(viewWords({ filters: 0, search: false, sort: 0, columns: 1 })).toBe('in crawl order, this column');
  expect(viewWords({ filters: 3, search: true, sort: 2, columns: 9 })).toBe(
    'these 3 filters, this search, this sort, these 9 columns',
  );
});
