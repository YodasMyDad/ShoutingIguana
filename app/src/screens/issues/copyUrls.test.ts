import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { GridPage, IssueRow } from '../../api/grids';
import { announce } from '../../feedback/announce';
import { notifyError, notifyProgress, notifyResult } from '../../feedback/notify';
import { COPY_PAGE_SIZE, copyUrls, readAllUrls, type RowsPageReader } from './copyUrls';

vi.mock('../../feedback/announce', () => ({ announce: vi.fn() }));
vi.mock('../../feedback/notify', () => ({ notifyError: vi.fn(), notifyProgress: vi.fn(), notifyResult: vi.fn() }));

const progress = { update: vi.fn(), done: vi.fn(), fail: vi.fn(), dismiss: vi.fn() };

beforeEach(() => {
  vi.mocked(notifyProgress).mockReturnValue(progress);
});

afterEach(() => {
  vi.clearAllMocks();
  vi.useRealTimers();
});

/** A check of `total` URLs, read `per` at a time with a cursor, as `/rows` answers. */
function reader(total: number, per = COPY_PAGE_SIZE, wait?: () => Promise<void>) {
  const calls: (string | undefined)[] = [];
  const read: RowsPageReader = async (after, signal) => {
    calls.push(after);
    await wait?.();
    if (signal.aborted) throw new DOMException('Aborted', 'AbortError');
    const start = after ? Number(after) : 0;
    const rows: IssueRow[] = Array.from({ length: Math.min(per, total - start) }, (_, i) => ({
      id: start + i + 1,
      url: `https://example.com/${start + i + 1}`,
    }));
    const end = start + rows.length;
    return {
      columns: ['id', 'url'],
      rows,
      offset: after ? null : 0,
      matching: total,
      total,
      next: end < total ? String(end) : null,
      summary: { available: true, reason: null },
      unavailable: [],
    } satisfies GridPage<IssueRow>;
  };
  return { read, calls };
}

describe('reading a check’s URLs', () => {
  it('reads page after page with the cursor until it runs out', async () => {
    const { read, calls } = reader(2_500);
    const seen: number[] = [];
    const urls = await readAllUrls(read, new AbortController().signal, (count) => seen.push(count));
    expect(urls).toHaveLength(2_500);
    expect(urls[2_499]).toBe('https://example.com/2500');
    expect(calls).toEqual([undefined, '1000', '2000']);
    expect(seen).toEqual([1_000, 2_000, 2_500]);
  });
});

describe('Copy URLs', () => {
  it('copies one URL per line and says how many, without a progress toast when it’s quick', async () => {
    const copy = vi.fn().mockResolvedValue(true);
    const { read } = reader(3);
    await expect(copyUrls({ read, expected: 3, copy })).resolves.toBe(3);
    expect(copy).toHaveBeenCalledWith('https://example.com/1\nhttps://example.com/2\nhttps://example.com/3');
    expect(notifyProgress).not.toHaveBeenCalled();
    expect(notifyResult).toHaveBeenCalledWith('Copied 3 URLs');
    expect(announce).toHaveBeenCalledWith('Copied 3 URLs.');
  });

  it('shows its progress with Cancel when it takes longer, and finishes in the same toast', async () => {
    vi.useFakeTimers();
    const copy = vi.fn().mockResolvedValue(true);
    const { read } = reader(2_000, COPY_PAGE_SIZE, () => new Promise((resolve) => setTimeout(resolve, 200)));
    const done = copyUrls({ read, expected: 2_000, copy });
    await vi.advanceTimersByTimeAsync(300);
    expect(vi.mocked(notifyProgress).mock.calls[0]?.[0]).toBe('Copying 2,000 URLs');
    expect(vi.mocked(notifyProgress).mock.calls[0]?.[1]?.onCancel).toBeTypeOf('function');
    await vi.advanceTimersByTimeAsync(200);
    expect(progress.update).toHaveBeenCalledWith('2,000 of 2,000 read');
    await expect(done).resolves.toBe(2_000);
    expect(progress.done).toHaveBeenCalledWith('Copied 2,000 URLs');
    expect(notifyResult).not.toHaveBeenCalled();
  });

  it('stops when cancelled, copying nothing', async () => {
    vi.useFakeTimers();
    const copy = vi.fn().mockResolvedValue(true);
    const { read } = reader(5_000, COPY_PAGE_SIZE, () => new Promise((resolve) => setTimeout(resolve, 200)));
    const done = copyUrls({ read, expected: 5_000, copy });
    await vi.advanceTimersByTimeAsync(300);
    const options = vi.mocked(notifyProgress).mock.calls[0]?.[1];
    options?.onCancel?.();
    await vi.advanceTimersByTimeAsync(1_000);
    await expect(done).resolves.toBeNull();
    expect(copy).not.toHaveBeenCalled();
    expect(progress.dismiss).toHaveBeenCalled();
    expect(notifyError).not.toHaveBeenCalled();
  });

  it('says so when a page can’t be read or the clipboard refuses', async () => {
    const failing: RowsPageReader = () => Promise.reject(new Error('Gone'));
    await expect(copyUrls({ read: failing, expected: 3, copy: vi.fn() })).resolves.toBeNull();
    expect(notifyError).toHaveBeenCalledWith(expect.any(Error), { title: 'The URLs couldn’t be copied.' });
    const { read } = reader(2);
    await expect(copyUrls({ read, expected: 2, copy: vi.fn().mockResolvedValue(false) })).resolves.toBeNull();
    expect(vi.mocked(notifyError).mock.calls[1]?.[1]).toEqual({
      title: 'The URLs couldn’t be copied.',
      description: 'The clipboard didn’t take them. Try again.',
    });
  });
});
