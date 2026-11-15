// The one page most of a problem's first addresses are linked from, for the problem page's "One fix" (docs/v2-ui-simple.md):
// a broken link in a shared menu or footer, or a page full of old links, is one place to fix. Read from where each of the
// first addresses is found, batched as every list's reads are.
import { useQueries, useQuery } from '@tanstack/react-query';
import { api } from '../api/client';
import { foundOnQuery, queryKeys } from '../api/queries';
import type { Check } from '../api/types';
import { SOURCES_SHOWN, referencesOf, sharedSource } from '../screens/issues/whereFound';

/** The addresses looked at: the first of the check's, in crawl order. */
export const SHARED_SOURCE_LOOKED_AT = 25;

export function useSharedSource(crawlId: string, check: Pick<Check, 'id' | 'references'>) {
  const wanted = referencesOf(check) === 'sources';
  const first = useQuery({
    queryKey: [...queryKeys.crawl(crawlId), 'issues', check.id, 'shared-source', SHARED_SOURCE_LOOKED_AT] as const,
    queryFn: ({ signal }) =>
      api.issueRows(
        crawlId,
        check.id,
        { columns: ['url'], filters: [], sort: [], offset: 0, limit: SHARED_SOURCE_LOOKED_AT },
        signal,
      ),
    enabled: wanted,
    staleTime: Infinity,
  });
  const ids = first.data?.rows.map((row) => row.id) ?? [];
  return useQueries({
    queries: ids.map((id) => foundOnQuery(crawlId, id, SOURCES_SHOWN)),
    combine: (results) =>
      results.length > 0 && results.every((result) => result.data !== undefined)
        ? sharedSource(results.flatMap((result) => (result.data ? [result.data] : [])))
        : null,
  });
}
