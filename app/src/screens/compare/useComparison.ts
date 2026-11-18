// Advanced's comparison of a crawl (docs/v2-fix-loop.md, section 4, Advanced, and "The states, at a glance"): what it
// is compared with (the preference, against the library), the comparison the sidecar gives for it, read forward when
// that crawl is a later one, and the stage it's at, for Overview's card, Issues' column and the picker alike.
import { useQuery } from '@tanstack/react-query';
import { useMemo } from 'react';
import { comparisonOf, type CrawlComparison } from '../../api/fixLoop';
import { changesQuery, crawlQuery, libraryQuery } from '../../api/queries';
import { isLive, type CrawlListItem, type CrawlStatus } from '../../api/types';
import { useLive } from '../../live/liveStore';
import { useCompareChoice } from '../../settings/compareWith';
import {
  compareCandidates,
  compareWithParam,
  comparingOf,
  forwardComparison,
  type CompareCandidate,
  type Comparing,
} from './comparing';

/**
 * Where Advanced's comparison stands:
 * - `none`: nothing to compare (a site's first crawl, Don't compare, or the sidecar has none): no card, no column;
 * - `live`: this crawl is being crawled, analysed or rechecked here: nothing until its results are worked out;
 * - `loading`: the card's skeleton, the column's cells blank;
 * - `failed`: the comparison couldn't be read (the crawl picked can't be compared now): the card says so;
 * - `needsAnalysis`: the other crawl was analysed by other rules: counts only, with Analyse it again;
 * - `shown`: `full`, `partial` or `countsOnly`.
 */
export type AdvancedStage =
  | { kind: 'none' }
  | { kind: 'live' }
  | { kind: 'loading' }
  | { kind: 'failed'; error: unknown }
  | { kind: 'needsAnalysis'; comparison: CrawlComparison }
  | { kind: 'shown'; comparison: CrawlComparison };

/** The stage, from what the crawl is compared with, its status, and the sidecar's answer (pure). */
export function advancedStage(
  comparing: Comparing,
  status: CrawlStatus | null,
  answer: { comparison: CrawlComparison | null | undefined; error: unknown },
): AdvancedStage {
  if (status !== null && isLive(status)) return { kind: 'live' };
  if (comparing.kind === 'none') return { kind: 'none' };
  if (answer.error) return { kind: 'failed', error: answer.error };
  if (answer.comparison === undefined) return { kind: 'loading' };
  if (answer.comparison === null) return { kind: 'none' };
  if (answer.comparison.previousNeedsAnalysis.length > 0)
    return { kind: 'needsAnalysis', comparison: answer.comparison };
  return { kind: 'shown', comparison: answer.comparison };
}

export interface AdvancedComparison {
  item: CrawlListItem | undefined;
  /** What it's compared with now. */
  comparing: Comparing;
  /** The crawls the picker offers. */
  candidates: CompareCandidate[];
  /** `compareWith` for the grids' reads: null for none. */
  compareWith: string | null;
  stage: AdvancedStage;
}

/** A crawl's comparison as Advanced shows it. The comparison is read only while the crawl is at rest here. */
export function useComparison(crawlId: string): AdvancedComparison {
  const item = useQuery(crawlQuery(crawlId)).data;
  const library = useQuery(libraryQuery()).data;
  const choice = useCompareChoice(crawlId);
  const liveStatus = useLive((state) => state.crawls[crawlId]?.status ?? null);
  const status = liveStatus !== null && isLive(liveStatus) ? liveStatus : (item?.status ?? null);

  const candidates = useMemo(
    () => (item ? compareCandidates(item, library ?? [item], (other) => other.status) : []),
    [item, library],
  );
  const comparing = useMemo<Comparing>(
    () => (item ? comparingOf(item, choice, candidates) : { kind: 'none', why: 'first' }),
    [item, choice, candidates],
  );
  const compareWith = compareWithParam(comparing);
  const atRest = status !== null && !isLive(status);
  const changes = useQuery({
    ...changesQuery(crawlId, compareWith === 'previous' ? undefined : (compareWith ?? undefined)),
    enabled: compareWith !== null && atRest,
  });
  const comparison = useMemo(() => {
    const answer = changes.data === undefined ? undefined : comparisonOf(changes.data);
    if (!answer || comparing.kind === 'none' || !comparing.later) return answer;
    return forwardComparison(answer);
  }, [changes.data, comparing]);

  return {
    item,
    comparing,
    candidates,
    compareWith,
    stage: advancedStage(comparing, status, { comparison, error: changes.error }),
  };
}
