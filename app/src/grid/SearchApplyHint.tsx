// A grid's search applies on Enter (URLs, Links, one sitemap's URLs): each search is a read of the whole view and a new
// place in the history, so it isn't run on every key. What's typed and not yet applied says so: the Enter key at the
// field's end, and in words for a screen reader, read with the field (gap 23 of the closing audit).
import { useId, type ReactNode } from 'react';
import { Kbd } from '../ui/Kbd';

export interface SearchApplyHint {
  /** For the field's `aria-describedby`. */
  describedBy: string;
  /** For the field's `end`: the Enter key while what's typed isn't the view's search. */
  end: ReactNode;
  /** Put beside the field: the words the field is described by. */
  note: ReactNode;
}

/** `pending`: the field's text isn't the search the view has (typed, not applied yet). */
export function useSearchApplyHint(pending: boolean): SearchApplyHint {
  const id = useId();
  return {
    describedBy: id,
    end: pending ? (
      <span className="si-field__hint si-field__enter" title="Press Enter to search">
        <Kbd chord="enter" size="sm" aria-hidden />
      </span>
    ) : null,
    note: (
      <span id={id} hidden>
        Press Enter to search.
      </span>
    ),
  };
}
