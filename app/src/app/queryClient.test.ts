import { QueryClient } from '@tanstack/react-query';
import { describe, expect, it } from 'vitest';
import { ProblemError, SidecarUnreachableError } from '../api/errors';
import { capGridPages, shouldRetry } from './queryClient';

describe('the grid page cache', () => {
  it('keeps at most the limit of grid pages, dropping the oldest unused first', async () => {
    const client = new QueryClient();
    capGridPages(client.getQueryCache(), 3);
    for (let page = 0; page < 5; page++) {
      client.setQueryData(['grid', 'urls', 'crawl', {}, page], { rows: [page] });
      await Promise.resolve();
    }
    const kept = client
      .getQueryCache()
      .findAll({ queryKey: ['grid'] })
      .map((query) => query.queryKey.at(-1));
    expect(kept).toEqual([2, 3, 4]);
  });

  it('trims back to the limit when pages that were still fetching land', async () => {
    const client = new QueryClient();
    capGridPages(client.getQueryCache(), 2);
    const answers: (() => void)[] = [];
    const fetches = [0, 1, 2, 3].map((page) =>
      client.query({
        queryKey: ['grid', 'urls', 'crawl', {}, page],
        queryFn: () => new Promise<{ rows: number[] }>((resolve) => answers.push(() => resolve({ rows: [page] }))),
        gcTime: 60_000,
      }),
    );
    await Promise.resolve();
    // Fetching, so nothing could go yet.
    expect(client.getQueryCache().findAll({ queryKey: ['grid'] })).toHaveLength(4);
    answers.forEach((answer) => answer());
    await Promise.all(fetches);
    await Promise.resolve();
    expect(client.getQueryCache().findAll({ queryKey: ['grid'] })).toHaveLength(2);
  });

  it('never touches other queries', async () => {
    const client = new QueryClient();
    capGridPages(client.getQueryCache(), 1);
    client.setQueryData(['library'], []);
    client.setQueryData(['grid', 'urls', 'c', {}, 0], {});
    client.setQueryData(['grid', 'urls', 'c', {}, 1], {});
    await Promise.resolve();
    expect(client.getQueryData(['library'])).toEqual([]);
  });
});

describe('retries', () => {
  it('retries what might work a moment later, and never a problem the engine meant', () => {
    expect(shouldRetry(0, new SidecarUnreachableError(new TypeError('x')))).toBe(true);
    expect(shouldRetry(2, new SidecarUnreachableError(new TypeError('x')))).toBe(false);
    expect(shouldRetry(0, new ProblemError({ type: 'crawl-not-found', title: 'Gone.', status: 404 }))).toBe(false);
    expect(shouldRetry(0, new ProblemError({ type: 'crawl-busy', title: 'Busy.', status: 409 }))).toBe(true);
    expect(shouldRetry(0, new Error('a bug'))).toBe(false);
  });
});
