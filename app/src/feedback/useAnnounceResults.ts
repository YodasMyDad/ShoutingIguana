import { useEffect, useRef } from 'react';
import { announce } from './announce';

/**
 * Says a view's result count once after the user changed what it shows: a filter applied or removed, a search, a
 * kind of link chosen. A count on screen is never a live region of its own: while a crawl runs it changes every
 * second, and a screen reader would read counts for as long as the crawl lasts.
 *
 * - `scope` is where the user is (the crawl; the URL inspected and the direction of its links). Arriving somewhere
 *   says nothing: focus goes there, and what has it is read.
 * - `view` is what they chose there (the filters and the search; the kind).
 * - `words` is the count in words ("264 of 4,521 URLs") once it's known for this view, null until then.
 *
 * The count is said when it's first known for a view other than the one last spoken for, and not again while the
 * view stays the same, however the count moves.
 */
export function useAnnounceResults(scope: string, view: string, words: string | null): void {
  /** The view whose count needn't be said: the one arrived on, then each one said. */
  const said = useRef({ scope, view });
  useEffect(() => {
    if (said.current.scope !== scope) {
      said.current = { scope, view };
      return;
    }
    if (words === null || said.current.view === view) return;
    said.current = { scope, view };
    announce(words);
  }, [scope, view, words]);
}
