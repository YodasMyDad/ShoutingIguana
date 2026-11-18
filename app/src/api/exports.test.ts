import { describe, expect, it } from 'vitest';
import { isExportOver, parseExportJob, type ExportJob } from './exports';

/** An export as the sidecar serialises one (`ExportDto`), running. */
const RUNNING = {
  exportId: '3f9a61c2d0b4e87a',
  crawlId: 'c1',
  kind: 'links',
  format: 'xlsx',
  path: 'C:\\Users\\me\\Documents\\example.com links.xlsx',
  status: 'running',
  rowsWritten: 41000,
  rowsExpected: 5053909,
  startedAt: '2026-11-01T17:42:00.118Z',
  finishedAt: null,
  result: null,
  error: null,
};

describe('parseExportJob', () => {
  it('reads a running export as the stream sends it', () => {
    const job = parseExportJob(JSON.parse(JSON.stringify(RUNNING)));
    expect(job).toEqual(RUNNING);
    expect(job && isExportOver(job)).toBe(false);
  });

  it('reads an export of the links grouped by target (step 3b)', () => {
    expect(parseExportJob({ ...RUNNING, kind: 'linkGroups' })?.kind).toBe('linkGroups');
  });

  it('reads what a finished export wrote', () => {
    const result = {
      rows: 5053909,
      bytes: 166723584,
      sheets: 5,
      unavailableColumns: ['to.inlinks'],
      truncatedCells: 2,
      crawlWasRunning: true,
    };
    const job = parseExportJob({
      ...RUNNING,
      status: 'done',
      rowsWritten: 5053909,
      finishedAt: '2026-11-01T17:42:31.900Z',
      result,
    });
    expect(job?.status).toBe('done');
    expect(job?.finishedAt).toBe('2026-11-01T17:42:31.900Z');
    expect(job?.result).toEqual(result);
    expect(job && isExportOver(job)).toBe(true);
  });

  it('reads why a failed export failed, as the problem a call would have been answered with', () => {
    const job = parseExportJob({
      ...RUNNING,
      kind: 'urls',
      status: 'failed',
      rowsExpected: null,
      finishedAt: '2026-11-01T17:42:01.000Z',
      error: {
        type: 'needs-analysis',
        title: 'This crawl hasn’t been analysed.',
        status: 409,
        reason: 'notAnalyzed',
        columns: ['inlinks'],
      },
    });
    expect(job?.status).toBe('failed');
    expect(job?.rowsExpected).toBeNull();
    expect(job?.result).toBeNull();
    expect(job?.error).toMatchObject({
      type: 'needs-analysis',
      status: 409,
      reason: 'notAnalyzed',
      columns: ['inlinks'],
    });
  });

  it('reads a report’s export, which counts no rows', () => {
    const job = parseExportJob({
      ...RUNNING,
      kind: 'report',
      format: 'html',
      rowsWritten: 0,
      rowsExpected: null,
      status: 'done',
      result: { rows: 0, bytes: 96374, sheets: 0, unavailableColumns: [], truncatedCells: 0, crawlWasRunning: false },
    });
    expect(job?.kind).toBe('report');
    expect(job?.result?.bytes).toBe(96374);
  });

  it('drops what isn’t an export', () => {
    const broken: Record<string, unknown>[] = [
      { ...RUNNING, exportId: 7 },
      { ...RUNNING, crawlId: undefined },
      { ...RUNNING, kind: 'everything' },
      { ...RUNNING, format: 'pdf' },
      { ...RUNNING, status: 'exploded' },
      { ...RUNNING, rowsWritten: '41000' },
      { ...RUNNING, rowsWritten: -1 },
      { ...RUNNING, startedAt: null },
      { ...RUNNING, path: null },
    ];
    for (const value of broken) expect(parseExportJob(value)).toBeNull();
    expect(parseExportJob(null)).toBeNull();
    expect(parseExportJob('export')).toBeNull();
  });

  it('keeps the export when only its result or its error doesn’t read', () => {
    const job = parseExportJob({ ...RUNNING, status: 'done', result: { rows: 'many' }, error: 'no' });
    expect(job).toMatchObject({ status: 'done', result: null, error: null } satisfies Partial<ExportJob>);
    // A result short of its later fields reads with none of them claimed.
    const short = parseExportJob({ ...RUNNING, status: 'done', result: { rows: 3, bytes: 120 } });
    expect(short?.result).toEqual({
      rows: 3,
      bytes: 120,
      sheets: 0,
      unavailableColumns: [],
      truncatedCells: 0,
      crawlWasRunning: false,
    });
  });
});
