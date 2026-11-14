// The grids' issue counts leave the hidden checks out (api/client.ts sends them with every grid read): once they change,
// every grid read, and the URL facets (Overview's severity counts among them), is read again (round 5 review, also
// noted). Nothing else is.
import type { QueryClient } from '@tanstack/react-query';
import { getSettings, subscribeSettings } from '../settings/settingsStore';

const hiddenKey = () => {
  const { hiddenChecks, hiddenChecksByCrawl } = getSettings();
  return JSON.stringify([hiddenChecks, hiddenChecksByCrawl]);
};

/** Reads the grids again whenever the hidden checks change; gives the function that stops it. */
export function watchHiddenChecks(queryClient: QueryClient): () => void {
  let last = hiddenKey();
  return subscribeSettings(() => {
    const next = hiddenKey();
    if (next === last) return;
    last = next;
    void queryClient.invalidateQueries({
      predicate: (query) =>
        query.queryKey[0] === 'grid' || (query.queryKey[0] === 'crawl' && query.queryKey[2] === 'facets'),
    });
  });
}
