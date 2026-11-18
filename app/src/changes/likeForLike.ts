// Like for like (docs/v2-fix-loop.md, section 1): when a crawl's previous crawl was analysed by other rules (an engine
// update, other checks or limits), Simple analyses it again by itself before it compares, behind the comparison's
// skeleton, so a rule change doesn't read as dozens of problems new or fixed. Once per pair a session: if it can't be
// done (in use, read-only, failed), the comparison stays counts only.
import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { api } from '../api/client';
import { queryKeys } from '../api/queries';
import { isLive } from '../api/types';
import { forgetQuietAnalysis, noteQuietAnalysis } from '../crawl/quietAnalyses';
import { liveStore } from '../live/liveStore';
import type { ComparisonStage } from './changes';

/** Pairs (`crawl>previous`) already asked for this session. */
const attempted = new Set<string>();
/** Pairs whose previous crawl's analysis couldn't start: counts only. */
const refused = new Set<string>();
/** Previous crawls worked out again here, this session: the line's tooltip says so. */
const againHere = new Set<string>();

/** For tests. */
export function resetLikeForLikeForTests(): void {
  attempted.clear();
  refused.clear();
  againHere.clear();
}

/** Whether `previousId` was worked out again here with today's checks. */
export function analysedAgainHere(previousId: string): boolean {
  return againHere.has(previousId);
}

export interface LikeForLike {
  /** The previous crawl is being worked out again, or about to be. */
  working: boolean;
  /** It couldn't be: the comparison is counts only. */
  gaveUp: boolean;
}

/**
 * Analyses the previous crawl again when the stage asks for it (`needsAnalysis`), once, quietly; and reads the
 * comparison again when that analysis ends, whoever ran it. `enabled` is false in Advanced, which asks first.
 */
export function useLikeForLike(crawlId: string, stage: ComparisonStage, enabled: boolean): LikeForLike {
  const queryClient = useQueryClient();
  const previousId = stage.kind === 'needsAnalysis' ? stage.previousId : null;
  const key = previousId ? `${crawlId}>${previousId}` : null;
  const [, setTick] = useState(0);

  useEffect(() => {
    if (!enabled || !previousId || !key || attempted.has(key)) return;
    attempted.add(key);
    noteQuietAnalysis(previousId);
    api.analyze(previousId).then(
      () => againHere.add(previousId),
      () => {
        forgetQuietAnalysis(previousId);
        refused.add(key);
        setTick((tick) => tick + 1);
      },
    );
  }, [enabled, previousId, key]);

  // The previous crawl's analysis ending (this one, or one started anywhere else) changes the comparison. Heard from
  // the stream's events as they come: a quick analysis's two statuses can reach the screen as one.
  useEffect(() => {
    if (!previousId) return undefined;
    return liveStore.onEvent((event, before) => {
      if (event.type !== 'crawl' || event.data.crawlId !== previousId) return;
      if (before.crawls[previousId]?.status !== 'analyzing' || isLive(event.data.status)) return;
      void queryClient.invalidateQueries({ queryKey: queryKeys.changes(crawlId) });
      void queryClient.invalidateQueries({ queryKey: queryKeys.crawlItem(crawlId) });
    });
  }, [previousId, queryClient, crawlId]);

  const gaveUp = key !== null && refused.has(key);
  return { working: enabled && previousId !== null && !gaveUp, gaveUp };
}
