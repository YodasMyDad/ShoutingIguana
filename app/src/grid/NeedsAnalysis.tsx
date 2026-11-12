import type { ReactNode } from 'react';
import type { ProblemError } from '../api/errors';
import { ErrorState } from '../feedback/ErrorState';
import { Button } from '../ui/Button';
import { analyseWords, type AnalyseWords } from './summaryWords';

/**
 * A sort or filter on a column the analysis fills, with no analysis to read: the engine's plain sentence says why, and
 * the view offers what can work: analysing the crawl, or dropping the sort and filters. Trying again can't. The
 * button is worded by the answer's reason: "Analyse it" for a crawl never analysed, "Analyse again" for one analysed
 * by another version or continued since, as the crawl's own controls say it.
 */
export function NeedsAnalysis({
  error,
  action,
  onClear,
}: {
  error: ProblemError;
  /**
   * The button that analyses the crawl, given its words (or a button already worded): left out while the crawl runs,
   * when it can't be analysed.
   */
  action: ReactNode | ((words: AnalyseWords) => ReactNode);
  onClear: () => void;
}) {
  return (
    <ErrorState
      error={error}
      actions={
        <>
          {error.reason === 'crawling'
            ? null
            : typeof action === 'function'
              ? action(analyseWords(error.reason))
              : action}
          <Button size="sm" onClick={onClear}>
            Clear the sort and filters
          </Button>
        </>
      }
    />
  );
}
