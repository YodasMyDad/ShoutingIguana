import { afterEach, describe, expect, it, vi } from 'vitest';
import type { ExportJob } from '../api/exports';
import { LiveStore } from '../live/liveStore';
import { ExportStore, isNewer } from './exportStore';
import { startExports } from './startExports';

vi.mock('../api/client', () => ({ api: { exportJob: vi.fn() } }));
const { api } = await import('../api/client');

const CLOCK = { now: () => 0, setTimeout: () => 0, clearTimeout: () => undefined, isHidden: () => false };

function job(patch: Partial<ExportJob> = {}): ExportJob {
  return {
    exportId: 'a1',
    crawlId: 'c',
    kind: 'urls',
    format: 'csv',
    path: 'C:\\x\\a.csv',
    status: 'running',
    rowsWritten: 0,
    rowsExpected: null,
    startedAt: '2026-11-01T17:42:00.000Z',
    finishedAt: null,
    result: null,
    error: null,
    ...patch,
  };
}

describe('which of two reads of an export is the newer', () => {
  it('never takes an ended export back to running, nor a running one back in rows', () => {
    expect(isNewer(job({ status: 'done' }), job())).toBe(false);
    expect(isNewer(job(), job({ status: 'cancelled' }))).toBe(true);
    expect(isNewer(job({ rowsWritten: 50 }), job({ rowsWritten: 10 }))).toBe(false);
    expect(isNewer(job({ rowsWritten: 10 }), job({ rowsWritten: 50 }))).toBe(true);
    expect(isNewer(job(), job({ rowsExpected: 9 }))).toBe(true);
    expect(isNewer(job({ status: 'done' }), job({ status: 'failed' }))).toBe(false);
  });
});

describe('the export store', () => {
  it('keeps what the screen said of an export, whichever arrives first: the call’s answer or its events', () => {
    const store = new ExportStore(() => 0);
    // The first event can reach the stream before the call that started the export is answered.
    store.accept(job({ rowsWritten: 200, rowsExpected: 400 }));
    const held = store.accept(job(), { label: 'This view' });
    expect(held.label).toBe('This view');
    expect(held.job.rowsWritten).toBe(200);
    expect(held.peak).toBe(0.5);
  });

  it('holds the bar where it was when the count rises, and says when it ended', () => {
    let now = 0;
    const store = new ExportStore(() => now);
    store.accept(job({ rowsWritten: 300, rowsExpected: 400 }));
    store.accept(job({ rowsWritten: 310, rowsExpected: 1_000 }));
    expect(store.get('a1')?.peak).toBe(0.75);
    now = 42;
    store.accept(job({ status: 'done', rowsWritten: 1_000, rowsExpected: 1_000 }));
    expect(store.get('a1')?.endedAt).toBe(42);
    expect(store.running()).toEqual([]);
  });

  it('marks a running export lost once, and nothing else', () => {
    const store = new ExportStore(() => 1);
    store.accept(job());
    store.accept(job({ exportId: 'b2', status: 'done' }));
    store.markLost('a1');
    store.markLost('b2');
    expect(store.get('a1')?.lost).toBe(true);
    expect(store.get('b2')?.lost).toBe(false);
  });
});

describe('the live stream’s part', () => {
  afterEach(() => vi.mocked(api.exportJob).mockReset());

  const hello = (exports: string[]) => ({
    event: 'hello',
    data: JSON.stringify({ live: [], exports, version: '1', generation: 1 }),
    id: '',
  });

  it('takes up an export running before a reload, and learns how one ended while no stream was connected', async () => {
    const live = new LiveStore(CLOCK);
    const store = new ExportStore(() => 0);
    vi.mocked(api.exportJob).mockImplementation((id: string) =>
      id === 'r1'
        ? Promise.resolve(job({ exportId: 'r1', rowsWritten: 5, rowsExpected: 10 }))
        : Promise.resolve(job({ exportId: id, status: 'done' })),
    );
    const stop = startExports({ liveStore: live, store, toasts: false });
    store.accept(job({ exportId: 'old' }));
    live.ingest(hello(['r1']));
    await vi.waitFor(() => expect(store.get('r1')?.job.rowsWritten).toBe(5));
    await vi.waitFor(() => expect(store.get('old')?.job.status).toBe('done'));
    stop();
  });

  it('says an export the new engine doesn’t know was lost with the old one', async () => {
    const live = new LiveStore(CLOCK);
    const store = new ExportStore(() => 0);
    const { ProblemError } = await import('../api/errors');
    vi.mocked(api.exportJob).mockRejectedValue(
      new ProblemError({ type: 'export-not-found', title: 'No such export.', status: 404 }),
    );
    const stop = startExports({ liveStore: live, store, toasts: false });
    store.accept(job({ exportId: 'gone' }));
    live.ingest(hello([]));
    await vi.waitFor(() => expect(store.get('gone')?.lost).toBe(true));
    stop();
  });

  it('takes each export event into the store', () => {
    const live = new LiveStore(CLOCK);
    const store = new ExportStore(() => 0);
    const stop = startExports({ liveStore: live, store, toasts: false });
    live.ingest({ event: 'export', data: JSON.stringify(job({ rowsWritten: 7, rowsExpected: 9 })), id: '' });
    expect(store.get('a1')?.job.rowsWritten).toBe(7);
    stop();
  });
});
