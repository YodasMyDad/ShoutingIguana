import { describe, expect, it } from 'vitest';
import type { ExportJob } from '../api/exports';
import {
  doneLines,
  engineMilliseconds,
  exportFileName,
  exportPhase,
  exportProblemWords,
  fileDate,
  fileNameOf,
  folderOf,
  inFolder,
  isNetworkPath,
  nextPeak,
  progressWords,
  runningLine,
  shareWritten,
} from './exportWords';

function job(patch: Partial<ExportJob> = {}): ExportJob {
  return {
    exportId: '3f9a61c2d0b4e87a',
    crawlId: 'c',
    kind: 'urls',
    format: 'xlsx',
    path: 'C:\\Users\\me\\Documents\\example.com 2026-10-31 URLs.xlsx',
    status: 'running',
    rowsWritten: 0,
    rowsExpected: null,
    startedAt: '2026-11-01T17:42:00.118Z',
    finishedAt: null,
    result: null,
    error: null,
    ...patch,
  };
}

describe('the default file name', () => {
  it('is the site, the crawl’s date and the view', () => {
    expect(exportFileName('example.com', '2026-10-31', 'URLs', 'xlsx')).toBe('example.com 2026-10-31 URLs.xlsx');
    expect(exportFileName('gardenfurnituresets.co.uk', '2026-10-31', 'report', 'html')).toBe(
      'gardenfurnituresets.co.uk 2026-10-31 report.html',
    );
  });

  it('takes the host of a URL, and keeps no character a file’s name can’t have', () => {
    expect(exportFileName('https://www.example.com/shop/', '2026-10-31', 'links', 'csv')).toBe(
      'www.example.com 2026-10-31 links.csv',
    );
    expect(exportFileName('example.com', '2026-10-31', 'Titles: "long" / short?', 'csv')).toBe(
      'example.com 2026-10-31 Titles- -long- - short-.csv',
    );
    expect(exportFileName('example.com', '2026-10-31', 'ends in a dot.', 'csv')).toBe(
      'example.com 2026-10-31 ends in a dot.csv',
    );
  });

  it('shortens a long view, keeping its extension', () => {
    const name = exportFileName('example.com', '2026-10-31', 'x'.repeat(400), 'xlsx');
    expect(name.length).toBeLessThanOrEqual(126);
    expect(name.endsWith('….xlsx')).toBe(true);
  });

  it('dates by the computer’s calendar, today when the crawl’s isn’t known', () => {
    expect(fileDate('2026-10-31T10:15:00')).toBe('2026-10-31');
    expect(fileDate(null, new Date(2026, 10, 13))).toBe('2026-11-13');
    expect(fileDate('not a date', new Date(2026, 0, 2))).toBe('2026-01-02');
  });
});

describe('paths', () => {
  it('splits a folder from a name, and puts a name in a folder with its own separator', () => {
    expect(folderOf('C:\\Users\\me\\a.csv')).toBe('C:\\Users\\me');
    expect(folderOf('/home/me/a.csv')).toBe('/home/me');
    expect(folderOf('a.csv')).toBeNull();
    expect(inFolder('C:\\Users\\me', 'a.csv')).toBe('C:\\Users\\me\\a.csv');
    expect(inFolder('/home/me/', 'a.csv')).toBe('/home/me/a.csv');
    expect(inFolder(null, 'a.csv')).toBe('a.csv');
    expect(fileNameOf('C:\\Users\\me\\a b.xlsx')).toBe('a b.xlsx');
  });

  it('knows a network share and a device path from a mapped drive', () => {
    expect(isNetworkPath('\\\\server\\share\\a.csv')).toBe(true);
    expect(isNetworkPath('\\\\?\\C:\\a.csv')).toBe(true);
    expect(isNetworkPath('Z:\\a.csv')).toBe(false);
  });
});

describe('progress', () => {
  it('goes from starting to counting to writing', () => {
    expect(exportPhase(job())).toBe('starting');
    expect(exportPhase(job({ rowsExpected: 100 }))).toBe('counting');
    expect(exportPhase(job({ rowsExpected: 100, rowsWritten: 5 }))).toBe('writing');
    expect(exportPhase(job({ kind: 'report' }))).toBe('writing');
    expect(exportPhase(job({ status: 'failed' }))).toBe('failed');
    expect(exportPhase(job(), true)).toBe('lost');
    expect(runningLine(job())).toBe('Starting…');
    expect(runningLine(job({ rowsExpected: 9 }))).toBe('Counting the URLs…');
    expect(runningLine(job({ kind: 'report' }))).toBe('Writing the report…');
  });

  it('says how far it has got in rows, and nothing for the report', () => {
    expect(progressWords(job({ rowsWritten: 41_000, rowsExpected: 112_590 }))).toBe('41,000 of 112,590 URLs');
    expect(progressWords(job({ kind: 'links', rowsWritten: 1, rowsExpected: 1 }))).toBe('1 of 1 link');
    // The two lists count what they list.
    expect(progressWords(job({ kind: 'sitemaps', rowsWritten: 3, rowsExpected: 12 }))).toBe('3 of 12 sitemaps');
    expect(progressWords(job({ kind: 'robotsOrigins', rowsWritten: 0, rowsExpected: 1 }))).toBe('0 of 1 origin');
    expect(progressWords(job())).toBeNull();
    expect(progressWords(job({ kind: 'report' }))).toBeNull();
  });

  it('never moves the bar back when a running crawl’s count rises with the rows', () => {
    let peak = 0;
    peak = nextPeak(peak, job({ rowsWritten: 300, rowsExpected: 400 }));
    expect(peak).toBe(0.75);
    // The count was older than the rows: it rises, and the share with it falls.
    peak = nextPeak(peak, job({ rowsWritten: 310, rowsExpected: 600 }));
    expect(peak).toBe(0.75);
    peak = nextPeak(peak, job({ rowsWritten: 590, rowsExpected: 600 }));
    expect(peak).toBeCloseTo(590 / 600);
    expect(shareWritten(job({ rowsWritten: 31_000, rowsExpected: 30_757 }))).toBe(1);
    expect(shareWritten(job())).toBeNull();
  });
});

describe('a finished export', () => {
  it('says its rows and the engine’s time, by the engine’s own clock', () => {
    const done = job({
      status: 'done',
      rowsWritten: 112_590,
      rowsExpected: 112_590,
      finishedAt: '2026-11-01T17:42:21.118Z',
      result: { rows: 112_590, bytes: 9, sheets: 1, unavailableColumns: [], truncatedCells: 0, crawlWasRunning: false },
    });
    expect(engineMilliseconds(done)).toBe(21_000);
    expect(doneLines(done)).toEqual(['112,590 URLs in 21 s.']);
  });

  it('says what the file holds that the grid doesn’t: cut cells, empty columns, a running crawl, more sheets', () => {
    const done = job({
      status: 'done',
      format: 'csv',
      finishedAt: '2026-11-01T17:42:00.418Z',
      result: {
        rows: 2_000_000,
        bytes: 9,
        sheets: 2,
        unavailableColumns: ['inlinks', 'issues'],
        truncatedCells: 3,
        crawlWasRunning: true,
      },
    });
    const titles: Record<string, string> = { inlinks: 'Inlinks', issues: 'Issues' };
    expect(doneLines(done, (id) => titles[id] ?? id)).toEqual([
      '2,000,000 URLs, on 2 sheets in 300 ms.',
      'The crawl was running: the file holds what it had when the export began.',
      '3 cells were cut to the 32,758 characters a spreadsheet reads in one.',
      'Inlinks and Issues are empty: they need a current analysis of the crawl.',
    ]);
  });

  it('says the report was written, with no rows', () => {
    const report = job({
      kind: 'report',
      format: 'html',
      status: 'done',
      finishedAt: '2026-11-01T17:42:01.118Z',
      result: { rows: 0, bytes: 84_000, sheets: 0, unavailableColumns: [], truncatedCells: 0, crawlWasRunning: false },
    });
    expect(doneLines(report)).toEqual(['Written in 1 s.']);
  });
});

describe('each problem', () => {
  const words = (type: string, extra: { reason?: string; errors?: Record<string, string[]> } = {}) =>
    exportProblemWords({ type, title: 'The engine’s title.', ...extra });

  it('has a sentence of the app’s and the action that helps', () => {
    expect(words('export-not-writable').remedies).toEqual(['chooseAnother']);
    expect(words('export-file-in-use').sentence).toMatch(/Another program has that file open/);
    expect(words('export-disk-full').remedies[0]).toBe('chooseAnother');
    expect(words('export-crawl-changed').remedies).toEqual(['exportAgain']);
    expect(words('export-too-large').remedies).toEqual(['asCsv']);
    expect(words('not-analyzed').remedies).toEqual(['analyse']);
    expect(words('check-skipped').remedies).toEqual(['analyse']);
    expect(words('needs-analysis', { reason: 'crawling' }).remedies).toEqual([]);
    expect(words('needs-analysis', { reason: 'notAnalyzed' }).remedies).toEqual(['analyse']);
  });

  it('tells the three causes of export-busy apart by the engine’s title (the review’s finding 9)', () => {
    const busy = (title: string) => exportProblemWords({ type: 'export-busy', title });
    expect(busy('Another export is writing this file. Wait for it to finish, or cancel it.')).toEqual({
      sentence: 'Another export is writing that file now. Wait for it to finish, or save to another file.',
      remedies: ['tryAgain', 'chooseAnother'],
    });
    expect(busy('4 exports are running already. Wait for one to finish, or cancel one.').remedies).toEqual([
      'tryAgain',
    ]);
    expect(busy('The engine is shutting down.')).toEqual({ sentence: 'The engine is shutting down.', remedies: [] });
  });

  it('says a refused sort of a running crawl, with the export in crawl order', () => {
    const refused = words('invalid-query', { errors: { sort: ['A running crawl…'] } });
    expect(refused.remedies).toEqual(['crawlOrder']);
    expect(refused.sentence).toMatch(/crawl order or by URL/);
  });

  it('says a network share in its own words, and another refused path in the engine’s', () => {
    const share = exportProblemWords(
      { type: 'invalid-export', title: 'x', errors: { path: ['Another computer’s path…'] } },
      { path: '\\\\server\\share\\a.csv' },
    );
    expect(share.sentence).toMatch(/network folder/);
    const name = exportProblemWords(
      { type: 'invalid-export', title: 'x', errors: { path: ['The name holds a character no file’s name may have.'] } },
      { path: 'C:\\d.txt:x.csv' },
    );
    expect(name.sentence).toBe('That file can’t be saved: the name holds a character no file’s name may have.');
    expect(name.remedies).toEqual(['chooseAnother']);
  });

  it('falls back to the engine’s own sentence for one it has no words for', () => {
    expect(words('something-new').sentence).toBe('The engine’s title.');
  });
});
