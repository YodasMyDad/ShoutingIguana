// What changed since the site's last check, as Simple's lists read it (docs/v2-fix-loop.md, section 4, "What to fix"
// and the Summary's Fix these first): each check's change in words for its count's second line, the checks fixed
// since, and when that last check was. One read of `/changes`, shared with the Summary's line; the last check worked
// out again with today's checks first when it was judged by other rules (like for like).
import { useQuery } from '@tanstack/react-query';
import { useMemo } from 'react';
import { comparisonOf, type CheckChange, type CrawlComparison } from '../api/fixLoop';
import { changesQuery } from '../api/queries';
import { isLive, type CrawlListItem, type CrawlStatus } from '../api/types';
import { comparisonStage } from '../changes/changes';
import { checkChangeWords, copyChangeWords } from '../changes/changeWords';
import { useLikeForLike } from '../changes/likeForLike';

export interface SimpleChanges {
  /** The comparison shown, or null: a first check, while it's read, or until the last check is worked out again. */
  comparison: CrawlComparison | null;
  /** When the last check was: "since 17 Oct". */
  since: string | null;
  /** A check's change by its id. */
  changeOf: (checkId: string) => CheckChange | null;
  /** A check's count's second line: "9 on 17 Oct", "New since 17 Oct", "Not looked for last time"; null for none. */
  wordsOf: (checkId: string) => string | null;
  /** What Copy this list adds after a count: " (was 9)", " (new since 17 Oct)". */
  copyWordsOf: (checkId: string) => string;
}

const NONE: SimpleChanges = {
  comparison: null,
  since: null,
  changeOf: () => null,
  wordsOf: () => null,
  copyWordsOf: () => '',
};

export function useSimpleChanges(
  crawlId: string,
  item: CrawlListItem | undefined,
  status: CrawlStatus | null,
): SimpleChanges {
  const live = status !== null && isLive(status);
  const enabled = Boolean(item?.previous) && !live;
  const changes = useQuery({ ...changesQuery(crawlId), enabled });
  const comparison = changes.isError ? null : changes.data === undefined ? undefined : comparisonOf(changes.data);
  const stage = comparisonStage(item, status, comparison);
  useLikeForLike(crawlId, stage, true);
  const shown = stage.kind === 'shown' ? stage.comparison : null;
  return useMemo(() => {
    if (!shown) return NONE;
    const byId = new Map(shown.checks.map((check) => [check.checkId, check]));
    const since = shown.with.startedAt;
    return {
      comparison: shown,
      since,
      changeOf: (checkId) => byId.get(checkId) ?? null,
      wordsOf: (checkId) => {
        const change = byId.get(checkId);
        return change ? checkChangeWords(change, since, 'simple') : null;
      },
      copyWordsOf: (checkId) => {
        const change = byId.get(checkId);
        return change ? copyChangeWords(change, since) : '';
      },
    };
  }, [shown]);
}
