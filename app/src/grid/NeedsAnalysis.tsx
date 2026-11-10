import type { ReactNode } from 'react';
import type { ProblemError } from '../api/errors';
import { ErrorState } from '../feedback/ErrorState';
import { Button } from '../ui/Button';

/**
 * A sort or filter on a column the analysis fills, with no analysis to read: the engine's plain sentence says why, and
 * the view offers what can work: analysing the crawl, or dropping the sort and filters. Trying again can't.
 */
export function NeedsAnalysis({
  error,
  action,
  onClear,
}: {
  error: ProblemError;
  /** Analyse it, or Analyse again: left out while the crawl runs, when neither can be done. */
  action: ReactNode;
  onClear: () => void;
}) {
  return (
    <ErrorState
      error={error}
      actions={
        <>
          {error.reason === 'crawling' ? null : action}
          <Button size="sm" onClick={onClear}>
            Clear the sort and filters
          </Button>
        </>
      }
    />
  );
}
