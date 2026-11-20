// The checks the user hid leave the report and the workbook (docs/v2-ui-discovery.md, decision 2), and an Issues
// view's workbook takes the view's checks (gap 19 of the closing audit, and the parity list's "the workbook doesn't
// take filters").
import { afterEach, describe, expect, it } from 'vitest';
import type { CrawlListItem } from '../api/types';
import { replaceSettings } from '../settings/settingsStore';
import type { ExportBuildOptions } from './exportOffer';
import { crawlFileChoices, issuesOffer, urlLinksOffer } from './offers';

const item = {
  id: '0123456789abcdef',
  site: 'example.com',
  startedAt: '2026-10-31T10:15:00Z',
  analyzedAt: '2026-10-31T11:00:00Z',
} as CrawlListItem;
const as = (patch: Partial<ExportBuildOptions> = {}): ExportBuildOptions => ({
  format: 'xlsx',
  path: 'C:/x/a.xlsx',
  cut: 'full',
  crawlOrder: false,
  ...patch,
});

afterEach(() => replaceSettings({ hiddenChecks: [], hiddenChecksByCrawl: {} }));

describe('the report and the workbook', () => {
  it('ask for nothing more while no check is hidden', () => {
    const [report, workbook] = crawlFileChoices(item, false);
    // The report is in the words of the mode it's saved from: Advanced's here.
    expect(report?.build(as({ format: 'html' }))).toEqual({
      kind: 'report',
      format: 'html',
      path: 'C:/x/a.xlsx',
      words: 'technical',
    });
    expect(workbook?.build(as())).toEqual({ kind: 'workbook', format: 'xlsx', path: 'C:/x/a.xlsx' });
  });

  it('leave out the checks hidden in every crawl and in this one, as they are when Export is pressed', () => {
    const [report, workbook] = crawlFileChoices(item, false);
    replaceSettings({
      hiddenChecks: ['title-missing'],
      hiddenChecksByCrawl: { [item.id]: ['thin-content'], fedcba9876543210: ['h1-missing'] },
    });
    expect(report?.build(as({ format: 'html' }))).toMatchObject({ hiddenChecks: ['title-missing', 'thin-content'] });
    expect(workbook?.build(as())).toMatchObject({ hiddenChecks: ['title-missing', 'thin-content'] });
  });
});

describe('“This check” of a check in groups (round 5 review, R2-9)', () => {
  const offer = () =>
    issuesOffer({
      item,
      crawling: false,
      simple: false,
      check: { id: 'h1-missing', title: 'Missing H1', urls: 12 },
      group: null,
    });
  const thisCheck = () => offer().choices.find((choice) => choice.id === 'check');

  it('asks for nothing more while no check is hidden', () => {
    expect(thisCheck()?.build(as())).toEqual({
      kind: 'check',
      format: 'xlsx',
      path: 'C:/x/a.xlsx',
      checkId: 'h1-missing',
    });
  });

  it('sends the checks hidden in every crawl and in this one, so its file is numbered as the workbook’s sheet', () => {
    const choice = thisCheck();
    replaceSettings({
      hiddenChecks: ['title-missing'],
      hiddenChecksByCrawl: { [item.id]: ['thin-content'], fedcba9876543210: ['meta-description-missing'] },
    });
    // As they are when Export is pressed, not when the offer was made.
    expect(choice?.build(as({ format: 'csv' }))).toEqual({
      kind: 'check',
      format: 'csv',
      path: 'C:/x/a.xlsx',
      checkId: 'h1-missing',
      hiddenChecks: ['title-missing', 'thin-content'],
    });
  });
});

describe('an Issues view’s workbook', () => {
  const base = { item, crawling: false, simple: false, check: null, group: null };

  it('is offered beside the workbook of every check when the list is filtered, with the view’s checks', () => {
    const offer = issuesOffer({ ...base, listed: { ids: ['title-missing', 'h1-missing'], words: 'errors' } });
    const ids = offer.choices.map((choice) => choice.id);
    expect(ids).toEqual(['report', 'workbookListed', 'workbook']);
    const listed = offer.choices.find((choice) => choice.id === 'workbookListed');
    expect(listed?.description).toMatch(/^2 checks \(errors\)/);
    expect(listed?.build(as())).toEqual({
      kind: 'workbook',
      format: 'xlsx',
      path: 'C:/x/a.xlsx',
      checks: ['title-missing', 'h1-missing'],
    });
  });

  it('isn’t offered for a list that isn’t filtered', () => {
    expect(issuesOffer(base).choices.map((choice) => choice.id)).toEqual(['report', 'workbook']);
  });
});

describe('the inspector’s links of a URL (gap 22)', () => {
  it('export as the links grid filtered as “All N in Links” is, the other end first', () => {
    const filters = [
      { column: 'to.id', op: 'eq' as const, value: 7 },
      { column: 'selfLink', op: 'is' as const, value: false },
    ];
    const offer = urlLinksOffer({
      item,
      crawling: false,
      direction: 'inlinks',
      filters,
      rows: 12,
      url: '/about/',
      followParts: ['followed', 'noFollow'],
    });
    expect(offer.title).toBe('Export inlinks');
    expect(offer.choices[0]?.description).toBe('12 inlinks of /about/, in crawl order');
    expect(offer.choices[0]?.fileView).toBe('inlinks of /about/');
    expect(offer.choices[0]?.build(as({ format: 'csv' }))).toEqual({
      kind: 'links',
      format: 'csv',
      path: 'C:/x/a.xlsx',
      columns: ['from.url', 'from.statusCode', 'from.outcome', 'text', 'kind', 'position', 'followed', 'noFollow'],
      filters,
    });
  });
});

describe('“This check” of a check whose URLs are referred to', () => {
  const described = (references: 'sources' | 'targets' | 'none') =>
    issuesOffer({
      item,
      crawling: false,
      simple: false,
      check: { id: 'internal-client-error', title: 'Internal URL returns 4xx', urls: 8, references },
      group: null,
    }).choices.find((choice) => choice.id === 'check')?.description;

  it('says its file has where each is found, or what each page links to', () => {
    expect(described('sources')).toBe(
      'Internal URL returns 4xx: 8 pages, with what was found on each and the pages that link to it',
    );
    expect(described('targets')).toMatch(/with what was found on each and the URLs it links to$/);
    expect(described('none')).toMatch(/with what was found on each$/);
  });
});
