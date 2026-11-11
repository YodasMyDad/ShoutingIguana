import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { GridPage } from '../api/grids';
import { onAnnounce } from '../feedback/announce';
import { COPY_ALL_PAGE_SIZE, copyAll, readRows, type PageReader } from './copyAll';
import { MAX_COPY_ROWS } from './tsv';

interface Row {
  id: number;
}

/** A view of `total` rows, read by cursor: the cursor is the id of the last row read. */
function view(total: number, calls: { after: string | undefined; limit: number }[] = []): PageReader<Row> {
  return (after, limit) => {
    calls.push({ after, limit });
    const start = after === undefined ? 0 : Number(after);
    const rows = Array.from({ length: Math.max(0, Math.min(limit, total - start)) }, (_, index) => ({
      id: start + index + 1,
    }));
    const end = start + rows.length;
    const page: GridPage<Row> = {
      columns: ['id'],
      rows,
      offset: after === undefined ? 0 : null,
      matching: total,
      total,
      next: end < total ? String(end) : null,
      summary: { available: true, reason: null },
      unavailable: [],
    };
    return Promise.resolve(page);
  };
}

const lines = (rows: readonly Row[]) => ({ text: rows.map((row) => row.id).join('\n'), count: rows.length });

describe('reading a whole view', () => {
  it('reads page after page by the cursor, to the end', async () => {
    const calls: { after: string | undefined; limit: number }[] = [];
    const read = await readRows(view(2_345, calls), new AbortController().signal);
    expect(read.rows).toHaveLength(2_345);
    expect(read.matching).toBe(2_345);
    expect(calls.map((call) => call.after)).toEqual([undefined, '1000', '2000']);
    expect(calls.every((call) => call.limit === COPY_ALL_PAGE_SIZE)).toBe(true);
  });

  it('stops at the most a copy takes, asking for no more rows than that', async () => {
    const calls: { after: string | undefined; limit: number }[] = [];
    const read = await readRows(view(5_000_000, calls), new AbortController().signal, 2_500);
    expect(read.rows).toHaveLength(2_500);
    expect(read.matching).toBe(5_000_000);
    expect(calls.map((call) => call.limit)).toEqual([1_000, 1_000, 500]);
  });

  it('ends on an empty view', async () => {
    expect((await readRows(view(0), new AbortController().signal)).rows).toEqual([]);
  });
});

describe('copying a whole view', () => {
  let said: string[] = [];
  let stop: () => void = () => undefined;
  beforeEach(() => {
    said = [];
    stop = onAnnounce((message) => said.push(message));
  });
  afterEach(() => stop());

  it('copies what the rows make, and says how many', async () => {
    const copy = vi.fn(() => Promise.resolve(true));
    const copied = await copyAll({
      read: view(3),
      text: lines,
      noun: ['target URL', 'target URLs'],
      failed: 'The target URLs couldn’t be copied.',
      copy,
    });
    expect(copied).toBe(3);
    expect(copy).toHaveBeenCalledWith('1\n2\n3');
    expect(said).toEqual(['Copied 3 target URLs.']);
  });

  it('says it is of the first rows when the view has more than a copy takes', async () => {
    const copy = vi.fn(() => Promise.resolve(true));
    const copied = await copyAll({
      read: view(MAX_COPY_ROWS + 5),
      text: () => ({ text: 'x', count: 1 }),
      noun: ['target URL', 'target URLs'],
      failed: 'The target URLs couldn’t be copied.',
      copy,
    });
    expect(copied).toBe(1);
    expect(said).toEqual(['Copied 1 target URL. From the first 10,000 of 10,005 rows: a copy takes no more.']);
  });

  it('says a part of the view in the copy’s own words, where its count isn’t a count of rows', async () => {
    const copy = vi.fn(() => Promise.resolve(true));
    const truncated = vi.fn((read: number, matching: number, count: number) => ({
      title: `Copied the first ${read} links’ target URLs`,
      description: `${count} URL, each once. The view has ${matching} links.`,
    }));
    const options = {
      text: () => ({ text: 'x', count: 1 }),
      noun: ['target URL', 'target URLs'] as const,
      failed: 'The target URLs couldn’t be copied.',
      truncated,
      copy,
    };
    await copyAll({ ...options, read: view(MAX_COPY_ROWS + 5) });
    expect(truncated).toHaveBeenCalledWith(MAX_COPY_ROWS, MAX_COPY_ROWS + 5, 1);
    expect(said).toEqual(['Copied the first 10000 links’ target URLs. 1 URL, each once. The view has 10005 links.']);
    // A view read whole says the plain count: nothing was left out.
    said = [];
    truncated.mockClear();
    await copyAll({ ...options, read: view(MAX_COPY_ROWS) });
    expect(truncated).not.toHaveBeenCalled();
    expect(said).toEqual(['Copied 1 target URL.']);
  });

  it('says so, and copies nothing, when the clipboard refuses or a read fails', async () => {
    const refused = await copyAll({
      read: view(3),
      text: lines,
      noun: ['row', 'rows'],
      failed: 'The column couldn’t be copied.',
      copy: () => Promise.resolve(false),
    });
    expect(refused).toBeNull();
    const copy = vi.fn(() => Promise.resolve(true));
    const failed = await copyAll({
      read: () => Promise.reject(new Error('The engine has gone.')),
      text: lines,
      noun: ['row', 'rows'],
      failed: 'The column couldn’t be copied.',
      copy,
    });
    expect(failed).toBeNull();
    expect(copy).not.toHaveBeenCalled();
    expect(said).toEqual([]);
  });
});
