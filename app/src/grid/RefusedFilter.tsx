import type { ProblemError } from '../api/errors';
import { ErrorState } from '../feedback/ErrorState';
import { Button } from '../ui/Button';

/**
 * A filter the engine wouldn't run (a pattern it refuses, a value it can't take), in place of the rows: which filter,
 * the engine's own sentence for why, and the two ways on. "Change the filter" opens the filter's editor from its chip,
 * with the sentence under the value; "Remove the filter" takes that one filter out. The view's other filters, its
 * search, its sort and its columns stay as they are. Trying again can't work, so it isn't offered.
 */
export function RefusedFilter({
  error,
  sentence,
  message,
  onChange,
  onRemove,
}: {
  error: ProblemError;
  /** The filter as its chip reads: "Anchor or alt text matches (?=x)y". */
  sentence: string;
  /** The engine's sentence for why it won't run it. */
  message: string;
  /** Opens the filter's editor; left out for a filter the editor has no fields for, which can only be removed. */
  onChange?: (() => void) | undefined;
  onRemove: () => void;
}) {
  return (
    <ErrorState
      error={error}
      title={`This filter can’t be run: ${sentence}`}
      description={message}
      actions={
        <>
          {onChange ? (
            <Button variant="primary" size="sm" onClick={onChange}>
              Change the filter
            </Button>
          ) : null}
          <Button size="sm" onClick={onRemove}>
            Remove the filter
          </Button>
        </>
      }
    />
  );
}
