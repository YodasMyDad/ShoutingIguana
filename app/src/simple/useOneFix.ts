import { useQueries } from '@tanstack/react-query';
import { api } from '../api/client';
import { queryKeys } from '../api/queries';
import type { IssueGroupPage } from '../api/grids';
import type { CheckEntry } from '../screens/issues/checkList';
import { oneFixHolds } from './oneFixWords';

/** A check's largest group, as the problem's "Where it is" and its "One fix" read it. */
export const firstGroupQuery = (crawlId: string, checkId: string) => ({
  queryKey: [...queryKeys.crawl(crawlId), 'issues', checkId, 'first-group'] as const,
  queryFn: ({ signal }: { signal: AbortSignal }): Promise<IssueGroupPage> =>
    api.issueGroups(crawlId, checkId, { offset: 0, limit: 1 }, signal),
  staleTime: Infinity,
});

/**
 * The checks of a list whose largest group is the engine's `likelyTemplate` and is on every page the check flagged: one
 * cause, in the part of the site every page shares, so one fix clears the whole problem. Their rows carry "One fix". A
 * shared cause on most of the pages (195 of 225) doesn't: the problem's page says how many it covers. A check that
 * doesn't group has no cause to name.
 */
export function useOneFix(crawlId: string, entries: readonly CheckEntry[]): ReadonlySet<string> {
  const grouped = entries.filter(
    (entry) => entry.check.grouping !== 'none' && (entry.count ?? 0) > 0 && oneFixHolds(entry.check),
  );
  return useQueries({
    queries: grouped.map((entry) => firstGroupQuery(crawlId, entry.check.id)),
    combine: (results) =>
      new Set(
        results.flatMap((result, index) => {
          const entry = grouped[index];
          const largest = result.data?.groups[0];
          if (!entry || !largest?.likelyTemplate) return [];
          return (largest.urls ?? largest.pages) >= (entry.count ?? 0) ? [entry.check.id] : [];
        }),
      ),
  });
}

/**
 * The checks of a list whose largest group is the engine's `likelyTemplate` but isn't on every page the check flagged,
 * with how many it's on: their rows say "One fix covers 195 of 225 pages", as the problem's page does. A shared cause on
 * every page is `useOneFix`'s "One fix" instead. Reads the same largest groups, so nothing more is asked of the sidecar.
 */
export function useOneFixCovers(crawlId: string, entries: readonly CheckEntry[]): ReadonlyMap<string, number> {
  const grouped = entries.filter(
    (entry) => entry.check.grouping !== 'none' && (entry.count ?? 0) > 0 && oneFixHolds(entry.check),
  );
  return useQueries({
    queries: grouped.map((entry) => firstGroupQuery(crawlId, entry.check.id)),
    combine: (results) =>
      new Map(
        results.flatMap((result, index) => {
          const entry = grouped[index];
          const largest = result.data?.groups[0];
          if (!entry || !largest?.likelyTemplate) return [];
          const on = largest.urls ?? largest.pages;
          return on < (entry.count ?? 0) ? [[entry.check.id, on] as const] : [];
        }),
      ),
  });
}
