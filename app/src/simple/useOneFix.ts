import { useQueries } from '@tanstack/react-query';
import { api } from '../api/client';
import { queryKeys } from '../api/queries';
import type { IssueGroupPage } from '../api/grids';
import type { CheckEntry } from '../screens/issues/checkList';

/** A check's largest group, as the problem's "Where it is" and its "One fix" read it. */
export const firstGroupQuery = (crawlId: string, checkId: string) => ({
  queryKey: [...queryKeys.crawl(crawlId), 'issues', checkId, 'first-group'] as const,
  queryFn: ({ signal }: { signal: AbortSignal }): Promise<IssueGroupPage> =>
    api.issueGroups(crawlId, checkId, { offset: 0, limit: 1 }, signal),
  staleTime: Infinity,
});

/**
 * The checks of a list whose largest group is the engine's `likelyTemplate`: one cause, in the part of the site every
 * page shares, so one fix clears it. Their rows carry "One fix". A check that doesn't group has no cause to name.
 */
export function useOneFix(crawlId: string, entries: readonly CheckEntry[]): ReadonlySet<string> {
  const grouped = entries.filter((entry) => entry.check.grouping !== 'none' && (entry.count ?? 0) > 0);
  return useQueries({
    queries: grouped.map((entry) => firstGroupQuery(crawlId, entry.check.id)),
    combine: (results) =>
      new Set(
        results.flatMap((result, index) =>
          result.data?.groups[0]?.likelyTemplate ? [grouped[index]?.check.id ?? ''] : [],
        ),
      ),
  });
}
