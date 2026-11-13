import { describe, expect, it } from 'vitest';
import type { CrawlListItem } from '../api/types';
import type { ExportBuildOptions } from './exportOffer';
import {
  crawlFileChoices,
  issuesOffer,
  linkExportColumns,
  linksOffer,
  robotsBlockedOffer,
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
    const offer = urlGridOffer({ ...view, viewName: null, linkEnds: false });
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
    const offer = urlGridOffer({ ...view, crawling: true, viewName: 'Thin pages', linkEnds: false });
    expect(offer.choices[0]?.build(as({ crawlOrder: true }))).not.toHaveProperty('sort');
    expect(offer.choices[0]?.sortedWhileCrawling).toBe(true);
    expect(offer.note).toMatch(/as it is when the export begins/);
    expect(offer.choices[0]?.fileView).toBe('URLs Thin pages');
    const byUrl = urlGridOffer({
      ...view,
      sort: [{ column: 'url', desc: false }],
      crawling: true,
      viewName: null,
      linkEnds: false,
    });
    expect(byUrl.choices[0]?.sortedWhileCrawling).toBe(false);
  });

  it('a view narrowed to a links view’s pages can’t be exported, and says so', () => {
    expect(urlGridOffer({ ...view, viewName: null, linkEnds: true }).choices[0]?.unavailable).toMatch(/can’t export/);
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
  ).toBe('Blocked with no rule kept');
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
