import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { ExportJob } from '../../api/exports';
import { liveStore } from '../../live/liveStore';
import { message } from '../../test/overviewSidecar';

const os = vi.hoisted(() => ({ pickSavePath: vi.fn(), showExportInFolder: vi.fn() }));
const client = vi.hoisted(() => ({
  api: { startExport: vi.fn(), exportJob: vi.fn(), cancelExport: vi.fn() },
}));
const toast = vi.hoisted(() => ({
  update: vi.fn<(text: string) => void>(),
  done: vi.fn<(title: string, action?: { label: string; onClick: () => void }) => void>(),
  fail: vi.fn<(error: unknown) => void>(),
  dismiss: vi.fn(),
}));
const notify = vi.hoisted(() => ({
  notifyProgress: vi.fn((_title: string, _options: { onCancel?: () => void }) => toast),
  notifyResult: vi.fn(),
}));

vi.mock('../../lib/platform', () => ({ isTauri: true, isMac: false }));
vi.mock('../../shell/os', () => os);
vi.mock('../../api/client', () => client);
vi.mock('../../feedback/notify', () => notify);

const { exportProgressText, fileNameOf, followExport, saveReportFile } = await import('./saveReportFile');

const PATH = 'C:\\Users\\me\\Documents\\gardenfurnituresets.co.uk 2026-10-31 report.html';

function job(overrides: Partial<ExportJob> = {}): ExportJob {
  return {
    exportId: '3f9a61c2d0b4e87a',
    crawlId: '0123456789abcdef',
    kind: 'report',
    format: 'html',
    path: PATH,
    status: 'running',
    rowsWritten: 0,
    rowsExpected: null,
    startedAt: '2026-11-13T00:00:00Z',
    finishedAt: null,
    result: null,
    error: null,
    ...overrides,
  };
}

const SAVE = {
  crawlId: '0123456789abcdef',
  kind: 'report' as const,
  cut: 'full' as const,
  fileName: 'gardenfurnituresets.co.uk 2026-10-31 report.html',
};

const exportEvent = (data: ExportJob) => liveStore.ingest(message('export', data));

beforeEach(() => {
  liveStore.reset();
  vi.useFakeTimers();
  // Running, until an event says otherwise.
  client.api.exportJob.mockImplementation(() => new Promise(() => undefined));
  client.api.cancelExport.mockResolvedValue(undefined);
});

afterEach(() => {
  vi.useRealTimers();
  vi.clearAllMocks();
});

describe('saving the report', () => {
  it('asks where with the default name, writes the cut shown there, and ends in a toast with Open folder', async () => {
    os.pickSavePath.mockResolvedValue(PATH);
    client.api.startExport.mockResolvedValue(job());
    expect(await saveReportFile({ ...SAVE, cut: 'summary' })).toBe(true);
    expect(os.pickSavePath).toHaveBeenCalledWith(expect.objectContaining({ defaultPath: SAVE.fileName }));
    expect(client.api.startExport).toHaveBeenCalledWith(SAVE.crawlId, {
      kind: 'report',
      format: 'html',
      path: PATH,
      cut: 'summary',
    });
    expect(notify.notifyProgress).toHaveBeenCalledWith('Saving the report…', expect.anything());

    exportEvent(job({ status: 'done', finishedAt: '2026-11-13T00:00:01Z' }));
    expect(toast.done).toHaveBeenCalledWith('Report saved: gardenfurnituresets.co.uk 2026-10-31 report.html', {
      label: 'Open folder',
      onClick: expect.any(Function) as unknown,
    });
    const action = toast.done.mock.calls[0]?.[1];
    action?.onClick();
    expect(os.showExportInFolder).toHaveBeenCalledWith('3f9a61c2d0b4e87a');
  });

  it('starts nothing when the dialog is cancelled', async () => {
    os.pickSavePath.mockResolvedValue(null);
    expect(await saveReportFile(SAVE)).toBe(false);
    expect(client.api.startExport).not.toHaveBeenCalled();
    expect(notify.notifyProgress).not.toHaveBeenCalled();
  });

  it('says a refused start in the engine’s words, in the same toast', async () => {
    os.pickSavePath.mockResolvedValue(PATH);
    const refused = new Error('export-file-in-use');
    client.api.startExport.mockRejectedValue(refused);
    expect(await saveReportFile(SAVE)).toBe(false);
    expect(toast.fail).toHaveBeenCalledWith(refused);
  });

  it('says a failed export with the problem it ended with', async () => {
    os.pickSavePath.mockResolvedValue(PATH);
    client.api.startExport.mockResolvedValue(job());
    await saveReportFile(SAVE);
    exportEvent(
      job({ status: 'failed', error: { type: 'export-disk-full', title: 'The disk is full.', status: 507 } }),
    );
    expect(toast.fail).toHaveBeenCalledWith(expect.objectContaining({ title: 'The disk is full.' }));
    expect(toast.done).not.toHaveBeenCalled();
  });

  it('cancels an export Cancel was pressed for before the engine answered, and says nothing was saved', async () => {
    os.pickSavePath.mockResolvedValue(PATH);
    let answer: (value: ExportJob) => void = () => undefined;
    client.api.startExport.mockReturnValue(new Promise<ExportJob>((resolve) => (answer = resolve)));
    const saving = saveReportFile(SAVE);
    await vi.waitFor(() => expect(notify.notifyProgress).toHaveBeenCalled());
    const [, options] = notify.notifyProgress.mock.calls[0]!;
    options.onCancel?.();
    expect(client.api.cancelExport).not.toHaveBeenCalled();
    answer(job());
    await saving;
    expect(client.api.cancelExport).toHaveBeenCalledWith('3f9a61c2d0b4e87a');
    exportEvent(job({ status: 'cancelled' }));
    expect(toast.dismiss).toHaveBeenCalled();
    expect(notify.notifyResult).toHaveBeenCalledWith(
      'The report wasn’t saved: saving it was cancelled.',
      expect.anything(),
    );
  });
});

describe('saving the spreadsheet of everything', () => {
  it('writes the workbook and counts its rows, never backwards', async () => {
    os.pickSavePath.mockResolvedValue('C:\\x\\a report.xlsx');
    client.api.startExport.mockResolvedValue(job({ kind: 'workbook', format: 'xlsx', path: 'C:\\x\\a report.xlsx' }));
    await saveReportFile({ ...SAVE, kind: 'workbook', fileName: 'a report.xlsx' });
    expect(client.api.startExport).toHaveBeenCalledWith(SAVE.crawlId, {
      kind: 'workbook',
      format: 'xlsx',
      path: 'C:\\x\\a report.xlsx',
    });
    const workbook = (rows: number) =>
      job({ kind: 'workbook', format: 'xlsx', rowsWritten: rows, rowsExpected: 4_521 });
    exportEvent(workbook(1_200));
    exportEvent(workbook(900));
    exportEvent(workbook(4_000));
    expect(toast.update.mock.calls.map(([text]) => text)).toEqual(['1,200 of 4,521 rows', '4,000 of 4,521 rows']);
  });
});

describe('following an export', () => {
  it('reads the job at once, so one that ended before its events were heard still ends', async () => {
    client.api.exportJob.mockResolvedValue(job({ status: 'done' }));
    const heard = vi.fn();
    followExport('3f9a61c2d0b4e87a', heard);
    await vi.waitFor(() => expect(heard).toHaveBeenCalledWith(expect.objectContaining({ status: 'done' })));
    // Over: neither the poll nor an event is heard again.
    exportEvent(job({ status: 'done' }));
    await vi.advanceTimersByTimeAsync(5_000);
    expect(heard).toHaveBeenCalledTimes(1);
  });

  it('hears only its own export', () => {
    const heard = vi.fn();
    const stop = followExport('3f9a61c2d0b4e87a', heard);
    exportEvent(job({ exportId: '0000000000000000' }));
    expect(heard).not.toHaveBeenCalled();
    stop();
  });
});

describe('the words', () => {
  it('gives a file’s own name from either kind of path', () => {
    expect(fileNameOf(PATH)).toBe('gardenfurnituresets.co.uk 2026-10-31 report.html');
    expect(fileNameOf('/home/me/a report.xlsx')).toBe('a report.xlsx');
  });

  it('says how far a spreadsheet has got, and nothing for the report', () => {
    expect(exportProgressText(job())).toBeNull();
    expect(exportProgressText(job({ kind: 'workbook', rowsWritten: 5, rowsExpected: null }))).toBe('5 rows written');
  });
});
