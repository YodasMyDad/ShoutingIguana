import type { UseQueryResult } from '@tanstack/react-query';
import { useState } from 'react';
import { isProblem } from '../../api/errors';
import type { Analysis } from '../../api/types';

export interface AnalysisAnswer {
  /** The crawl has no analysis: the read said so, or said so when last asked and is being asked again. */
  neverAnalysed: boolean;
  /** "Not analysed" was the last answer, and the read is on its way again: what's on screen stays, as refreshing. */
  rereading: boolean;
  /**
   * The crawl's analysis has just ended here and its result is on its way: to the eye it's still finishing, not "never
   * analysed, analyse it" for the moment the read takes.
   */
  finishing: boolean;
}

/**
 * What the analysis's read says of a crawl, remembered across the read being asked again.
 *
 * "Not analysed" is an answer, but it comes as an error (404 `not-analyzed`), and TanStack Query forgets an error the
 * moment the read is asked again: the query is `pending`, as on a first load. A screen that goes by that swaps itself
 * for its loading frame whenever a never-analysed crawl's analysis is refetched (when its analysis ends, when the
 * stream reconnects), unmounting what it was showing. So this remembers that the crawl wasn't analysed when last
 * asked: until the read says otherwise it still isn't, and is being read again.
 *
 * `crawling`: the screen doesn't read the analysis while the crawl runs (Overview). A crawl that has just been
 * crawling has no analysis of what it is now, so its first read after it ends is treated the same way.
 */
export function useAnalysisAnswer(
  crawlId: string,
  analysis: UseQueryResult<Analysis>,
  { crawling = false, analysing }: { crawling?: boolean; analysing: boolean },
): AnalysisAnswer {
  const notAnalysed = isProblem(analysis.error, 'not-analyzed');
  // The crawl whose last answer was "not analysed". State, adjusted as the answers come, so it's per crawl.
  const [unanalysed, setUnanalysed] = useState<string | null>(null);
  if ((notAnalysed || crawling) && unanalysed !== crawlId) setUnanalysed(crawlId);
  if (unanalysed === crawlId && (analysis.data !== undefined || (analysis.error !== null && !notAnalysed))) {
    setUnanalysed(null);
  }
  const rereading = unanalysed === crawlId && analysis.isPending && !analysis.error;

  const [analysedHere, setAnalysedHere] = useState<string | null>(null);
  if (analysing && analysedHere !== crawlId) setAnalysedHere(crawlId);
  if (!analysing && analysedHere === crawlId && !analysis.isFetching) setAnalysedHere(null);
  const finishing = !analysing && analysedHere === crawlId && rereading && analysis.isFetching;

  return { neverAnalysed: notAnalysed || rereading, rereading, finishing };
}
