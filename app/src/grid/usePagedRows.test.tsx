import { QueryClientProvider } from '@tanstack/react-query';
import { act, renderHook } from '@testing-library/react';
import type { ReactNode } from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { GridPage } from '../api/grids';
import { createQueryClient } from '../app/queryClient';
import { SCRUB_INTERVAL_MS, usePagedRows } from './usePagedRows';

const PAGE = 200;
const ROWS = 1_000_000;
const ROW_HEIGHT = 32;

interface Row {
  id: number;
}

interface Request {
  page: number;
  signal: AbortSignal;
  answer: () => void;
}

/** A sidecar whose pages arrive when the test says, recording each request with its abort signal. */
function pagedSidecar() {
  const requests: Request[] = [];
  const fetchPage = (offset: number, limit: number, signal: AbortSignal) =>
    new Promise<GridPage<Row>>((resolve, reject) => {
      signal.addEventListener('abort', () => reject(new DOMException('Aborted', 'AbortError')));
      requests.push({
        page: offset / PAGE,
        signal,
        answer: () =>
          resolve({
            columns: ['id'],
            rows: Array.from({ length: limit }, (_, index) => ({ id: offset + index })),
            offset,
            matching: ROWS,
            total: ROWS + 5,
            next: null,
            summary: { available: true, reason: null },
            unavailable: [],
          }),
      });
    });
  const pagesAsked = () => requests.map((request) => request.page);
  const answer = async (page: number) => {
    await act(async () => {
      requests.filter((request) => request.page === page).forEach((request) => request.answer());
      // TanStack Query tells observers on its next tick.
      await vi.advanceTimersByTimeAsync(0);
    });
  };
  return { requests, fetchPage, pagesAsked, answer };
}

function setup() {
  const sidecar = pagedSidecar();
  const queryClient = createQueryClient();
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  );
  const queryKey = ['grid', 'urls', '0123456789abcdef', {}] as const;
  const hook = renderHook(() => usePagedRows<Row>({ queryKey, fetchPage: sidecar.fetchPage, rowHeight: ROW_HEIGHT }), {
    wrapper,
  });
  /** The rows in view now, as the grid reports them. */
  const view = (first: number, last: number, scrubbing = false) =>
    act(async () => {
      hook.result.current.onRangeChange({ first, last, scrubbing });
      // The pages in view are queries: they fetch once React has rendered them.
      await vi.advanceTimersByTimeAsync(0);
    });
  /** A scroll to this row, at this many rows a second: how the grid reports its offsets. */
  const scroll = (fromRow: number, toRow: number) =>
    act(() => {
      hook.result.current.onScrollOffset(fromRow * ROW_HEIGHT);
      vi.advanceTimersByTime(50);
      hook.result.current.onScrollOffset(toRow * ROW_HEIGHT);
    });
  return { ...sidecar, hook, view, scroll, queryClient };
}

beforeEach(() => {
  vi.useFakeTimers();
});

afterEach(() => {
  vi.useRealTimers();
});

describe('usePagedRows', () => {
  it('counts the rows from the first page, and reads rows from whatever pages are cached', async () => {
    const { hook, answer } = setup();
    expect(hook.result.current.rowCount).toBeNull();
    expect(hook.result.current.isLoading).toBe(true);
    await answer(0);
    expect(hook.result.current.rowCount).toBe(ROWS);
    expect(hook.result.current.unfilteredTotal).toBe(ROWS + 5);
    expect(hook.result.current.getRow(199)).toEqual({ id: 199 });
    expect(hook.result.current.getRow(200)).toBeUndefined();
  });

  it('asks for the pages in view before any page around them', async () => {
    const { view, answer, pagesAsked } = setup();
    await answer(0);
    // A jump to row 500,000: page 2,500.
    await view(500_000, 500_030);
    expect(pagesAsked().slice(1)).toEqual([2_500, 2_501, 2_499]);
  });

  it('prefetches in the direction of travel, further the faster it goes', async () => {
    const { view, scroll, answer, pagesAsked } = setup();
    await answer(0);
    scroll(10_000, 9_900); // upwards, 100 rows in 50 ms
    await view(9_900, 9_930);
    // The page in view, then ahead, which is up: 48, 47, 46; then one page behind, below.
    expect(pagesAsked().slice(1)).toEqual([49, 48, 47, 46, 50]);
    scroll(20_000, 20_100); // downwards
    await view(20_100, 20_130);
    expect(pagesAsked().slice(6)).toEqual([100, 101, 102, 103, 99]);
  });

  it('keeps a prefetch within reach when the lookahead shrinks, so a steady scroll asks for each page once', async () => {
    const { view, scroll, answer, requests } = setup();
    await answer(0);
    scroll(10_000, 10_150); // quick: a few pages ahead
    await view(10_150, 10_180);
    const farthest = Math.max(...requests.map((request) => request.page));
    expect(farthest).toBeGreaterThan(51);
    scroll(10_150, 10_151); // slower: the lookahead shrinks
    await view(10_151, 10_181);
    const prefetch = requests.find((request) => request.page === farthest);
    expect(prefetch?.signal.aborted).toBe(false);
    expect(requests.filter((request) => request.page === farthest)).toHaveLength(1);
  });

  it('cancels a page nobody wants any more, so its request is aborted', async () => {
    const { view, answer, requests } = setup();
    await answer(0);
    await view(100_000, 100_030); // pages 500, 501 and 499
    const stale = requests.find((request) => request.page === 501);
    expect(stale?.signal.aborted).toBe(false);
    await view(900_000, 900_030); // gone far past them before they came
    expect(stale?.signal.aborted).toBe(true);
    expect(requests.find((request) => request.page === 4_500)?.signal.aborted).toBe(false);
  });

  it('while the thumb is dragged, prefetches nothing and asks for the pages in view at most every so often', async () => {
    const { view, answer, pagesAsked } = setup();
    await answer(0);
    const before = pagesAsked().length;
    await view(100_000, 100_030, true); // shown at once: page 500
    await view(200_000, 200_030, true);
    await view(300_000, 300_030, true);
    expect(pagesAsked().slice(before)).toEqual([500]);
    await act(() => vi.advanceTimersByTimeAsync(SCRUB_INTERVAL_MS));
    // Only where it was then: 1,500.
    expect(pagesAsked().slice(before)).toEqual([500, 1_500]);
    // Let go: where it ends is fetched at once, with the pages around it.
    await view(400_000, 400_030, false);
    expect(pagesAsked().slice(before + 2)).toEqual([2_000, 2_001, 1_999]);
  });

  it('drops what’s still coming for rows the thumb has flown past', async () => {
    const { view, answer, requests } = setup();
    await answer(0);
    await view(100_000, 100_030); // prefetches 501 and 499
    const prefetch = requests.find((request) => request.page === 501);
    await view(500_000, 500_030, true);
    expect(prefetch?.signal.aborted).toBe(true);
  });

  it('never cancels a page a copy is waiting for, wherever the grid scrolls meanwhile', async () => {
    const { hook, view, answer, requests } = setup();
    await answer(0);
    let copied: readonly (Row | undefined)[] = [];
    await act(async () => {
      void hook.result.current.fetchRows([1_000, 1_001]).then((rows) => (copied = rows));
      await Promise.resolve();
    });
    const copying = requests.find((request) => request.page === 5);
    await view(900_000, 900_030);
    await view(950_000, 950_030, true);
    expect(copying?.signal.aborted).toBe(false);
    await answer(5);
    expect(copied).toEqual([{ id: 1_000 }, { id: 1_001 }]);
  });
});
