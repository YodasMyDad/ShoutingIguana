import { useState } from 'react';

/**
 * A search field's text, for a search the route keeps (`?q=`): what's typed, until the route's search changes, which
 * the field then shows (Enter applied it, Back, a view reset, a link). The field is controlled by this, never
 * remounted to follow the route: a field given a new `key` when its own search is applied is a new element, and the
 * one that had focus is gone.
 */
export function useSearchText(q: string | undefined): [text: string, setText: (text: string) => void] {
  const [text, setText] = useState(q ?? '');
  const [shown, setShown] = useState(q);
  if (shown !== q) {
    setShown(q);
    setText(q ?? '');
  }
  return [text, setText];
}
