import { describe, expect, it } from 'vitest';
import { fileDate, reportFileName, saveDialogOptions } from './reportFile';

const GARDEN = {
  site: 'gardenfurnituresets.co.uk',
  fileName: 'www.gardenfurnituresets.co.uk-20261031.sicrawl',
  startedAt: '2026-10-31T14:13:00Z',
  analyzedAt: '2026-11-12T23:58:00Z',
};

describe('the report’s file names', () => {
  it('names the report and the spreadsheet as the design does: the site, the crawl’s date, then what it is', () => {
    expect(reportFileName(GARDEN, 'report')).toBe('gardenfurnituresets.co.uk 2026-10-31 report.html');
    expect(reportFileName(GARDEN, 'workbook')).toBe('gardenfurnituresets.co.uk 2026-10-31 report.xlsx');
  });

  it('says which cut a saved report is', () => {
    expect(reportFileName(GARDEN, 'report', 'summary')).toBe(
      'gardenfurnituresets.co.uk 2026-10-31 report summary.html',
    );
    // The spreadsheet is everything, whatever the screen shows.
    expect(reportFileName(GARDEN, 'workbook', 'summary')).toBe('gardenfurnituresets.co.uk 2026-10-31 report.xlsx');
  });

  it('goes by the crawl’s date, then its analysis’s, and leaves the date out when it has neither', () => {
    expect(reportFileName({ ...GARDEN, startedAt: null }, 'report')).toBe(
      'gardenfurnituresets.co.uk 2026-11-12 report.html',
    );
    expect(reportFileName({ ...GARDEN, startedAt: null, analyzedAt: null }, 'report')).toBe(
      'gardenfurnituresets.co.uk report.html',
    );
  });

  it('names a crawl with no site by its file, and never names a file Windows refuses', () => {
    expect(reportFileName({ ...GARDEN, site: null, startedAt: null, analyzedAt: null }, 'report')).toBe(
      'www.gardenfurnituresets.co.uk-20261031.sicrawl report.html',
    );
    expect(reportFileName({ ...GARDEN, site: 'a:b/c*d?"e<f>g|h\u0001' }, 'report')).toBe(
      'a b c d e f g h 2026-10-31 report.html',
    );
  });

  it('reads the date on the computer’s clock, and nothing from a date that isn’t one', () => {
    expect(fileDate('2026-01-05T12:00:00')).toBe('2026-01-05');
    expect(fileDate('not a date')).toBeNull();
    expect(fileDate(null)).toBeNull();
  });

  it('offers the one kind of file each saves', () => {
    expect(saveDialogOptions('report', 'x.html').filters).toEqual([{ name: 'Web page (HTML)', extensions: ['html'] }]);
    expect(saveDialogOptions('workbook', 'x.xlsx').filters).toEqual([{ name: 'Excel workbook', extensions: ['xlsx'] }]);
  });
});
