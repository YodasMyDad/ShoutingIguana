// A crawl's checks with issues by severity, as a library row says them, the checks the user has hidden (in this crawl
// or in every crawl) left out, as Issues, What to fix and the report leave them out (round 5: part C's hidden checks
// in part B's Advanced Home and part A's Your sites, one way).
import { useQuery } from '@tanstack/react-query';
import { useMemo } from 'react';
import { analysisQuery, checksQuery } from '../api/queries';
import type { CrawlListItem, IssueCounts } from '../api/types';
import { checkEntries, foundBySeverity } from '../screens/issues/checkList';
import { useHiddenChecks } from '../settings/hiddenChecks';

/**
 * With nothing hidden, the library's own counts; with some, the crawl's analysis, read for the row. Null while that's
 * read, and for a crawl never analysed.
 */
export function useVisibleIssueCounts(item: Pick<CrawlListItem, 'id' | 'issues'>): IssueCounts | null {
  const hiddenIds = useHiddenChecks(item.id);
  const needed = hiddenIds.length > 0 && item.issues !== null;
  const checks = useQuery({ ...checksQuery(), enabled: needed }).data;
  const analysis = useQuery({ ...analysisQuery(item.id), enabled: needed }).data;
  return useMemo(() => {
    if (!item.issues) return null;
    if (!needed) return item.issues;
    if (!checks || !analysis) return null;
    const { error, warning, notice } = foundBySeverity(checkEntries(checks, analysis, new Set(hiddenIds)));
    return { error, warning, notice };
  }, [item.issues, needed, checks, analysis, hiddenIds]);
}
