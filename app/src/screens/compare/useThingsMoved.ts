// What moved since the crawl compared with, in the headline's unit (things), for Overview's comparison card and the
// Issues head: both analyses read (each once, as the screens read their own), the counted checks the headline counts.
import { useQuery } from '@tanstack/react-query';
import { useMemo } from 'react';
import { analysisQuery, checksQuery } from '../../api/queries';
import { useHiddenChecks } from '../../settings/hiddenChecks';
import { thingsMoved, type ThingsMoved } from './thingsMoved';
import type { AdvancedComparison } from './useComparison';

/**
 * The things moved, or null: nothing compared, the comparison counts only (the other crawl analysed by other rules, whose
 * things can't be set against these), or either analysis without its things counted. Against a later crawl, read
 * forward: from this crawl's to that one's.
 */
export function useThingsMoved(crawlId: string, comparison: AdvancedComparison): ThingsMoved | null {
  const { stage, comparing } = comparison;
  const otherId = stage.kind === 'shown' ? stage.comparison.with.id : null;
  const now = useQuery({ ...analysisQuery(crawlId), enabled: otherId !== null }).data;
  const then = useQuery({ ...analysisQuery(otherId ?? ''), enabled: otherId !== null }).data;
  const catalog = useQuery(checksQuery()).data;
  const hiddenIds = useHiddenChecks(crawlId);
  const later = comparing.kind !== 'none' && comparing.later;
  return useMemo(() => {
    if (!now || !then || !catalog || otherId === null) return null;
    const hidden = new Set(hiddenIds);
    return later ? thingsMoved(catalog, then, now, hidden) : thingsMoved(catalog, now, then, hidden);
  }, [now, then, catalog, otherId, hiddenIds, later]);
}
