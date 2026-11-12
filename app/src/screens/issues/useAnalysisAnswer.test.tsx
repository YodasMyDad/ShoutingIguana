import type { UseQueryResult } from '@tanstack/react-query';
import { renderHook } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { ProblemError } from '../../api/errors';
import type { Analysis } from '../../api/types';
import { useAnalysisAnswer } from './useAnalysisAnswer';

/** The read as the screens hand it over: only what the hook looks at. */
function read(parts: Partial<UseQueryResult<Analysis>>): UseQueryResult<Analysis> {
  return { data: undefined, error: null, isPending: false, isFetching: false, ...parts } as UseQueryResult<Analysis>;
}

const analysed = read({ data: {} as Analysis });
const notAnalysed = read({
  error: new ProblemError({ type: 'not-analyzed', title: 'This crawl hasn’t been analysed yet', status: 404 }),
});

describe('useAnalysisAnswer', () => {
  // Found in the real shell: back from the tray after a crawl ended there, the crawl still read as running for a
  // moment (its item hadn't been read again) while its analysis had already arrived, and Overview failed with
  // React's "Too many re-renders": the two adjustments undid each other for ever.
  it('settles when the crawl still reads as running and its analysis is already there', () => {
    const { result, rerender } = renderHook(
      ({ crawling }: { crawling: boolean }) => useAnalysisAnswer('crawl-1', analysed, { crawling, analysing: false }),
      { initialProps: { crawling: true } },
    );
    expect(result.current).toEqual({ neverAnalysed: false, rereading: false, finishing: false });

    // The crawl's status catches up: the analysis that is there is the answer.
    rerender({ crawling: false });
    expect(result.current).toEqual({ neverAnalysed: false, rereading: false, finishing: false });
  });

  it('keeps "not analysed" while the read is asked again, and lets it go when the analysis comes', () => {
    const { result, rerender } = renderHook(
      ({ analysis }: { analysis: UseQueryResult<Analysis> }) =>
        useAnalysisAnswer('crawl-1', analysis, { analysing: false }),
      { initialProps: { analysis: notAnalysed } },
    );
    expect(result.current.neverAnalysed).toBe(true);

    rerender({ analysis: read({ isPending: true, isFetching: true }) });
    expect(result.current).toMatchObject({ neverAnalysed: true, rereading: true });

    rerender({ analysis: analysed });
    expect(result.current).toMatchObject({ neverAnalysed: false, rereading: false });
  });

  it('treats the first read after a crawl ends as a reread, not as a first load', () => {
    const { result, rerender } = renderHook(
      ({ crawling, analysis }: { crawling: boolean; analysis: UseQueryResult<Analysis> }) =>
        useAnalysisAnswer('crawl-1', analysis, { crawling, analysing: false }),
      { initialProps: { crawling: true, analysis: read({ isPending: true }) } },
    );
    rerender({ crawling: false, analysis: read({ isPending: true, isFetching: true }) });
    expect(result.current).toMatchObject({ neverAnalysed: true, rereading: true });
  });
});
