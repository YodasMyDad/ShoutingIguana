// The checks the user hid leave the report and the workbook (docs/v2-ui-discovery.md, decision 2), and an Issues
// view's workbook takes the view's checks (gap 19 of the closing audit, and the parity list's "the workbook doesn't
// take filters").
import { afterEach, describe, expect, it } from 'vitest';
import type { CrawlListItem } from '../api/types';
import { replaceSettings } from '../settings/settingsStore';
import type { ExportBuildOptions } from './exportOffer';
import { crawlFileChoices, issuesOffer } from './offers';

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
    expect(report?.build(as({ format: 'html' }))).toEqual({ kind: 'report', format: 'html', path: 'C:/x/a.xlsx' });
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
