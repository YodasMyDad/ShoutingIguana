// The size a group's pages show its image at (crawl/evidence.ts, `shownAtOf`): read from what each page's issue names,
// in the same batched reads as the pages' own lines (`/targets`, five a page), so it costs nothing they don't.
import { useQueries, useQuery } from '@tanstack/react-query';
import { useCallback } from 'react';
import type { IssueGroup } from '../../api/grids';
import type { IssueTargetsOfUrl } from '../../api/inspector';
import { issueGroupRowsQuery, issueTargetsQuery } from '../../api/queries';
import type { Check } from '../../api/types';
import { shownAtOf, type ShownAt } from '../../crawl/evidence';
import { groupRowColumns } from './evidenceColumns';
import { FIRST_GROUP_ROWS, groupRefOf } from './groupTree';
import { SOURCES_SHOWN } from './whereFound';

/** Whether a check says how pages show its images, and how big their files are: the insight is then its to say. */
export function saysShownAt(check: Pick<Check, 'targetFacts'>): boolean {
  const facts = check.targetFacts ?? [];
  return facts.includes('img') && facts.includes('pixelWidth') && facts.includes('sizeBytes');
}

/**
 * The size the given pages show one image at: undefined while it's read, null where none gives a size in pixels (or
 * the check says nothing of how pages show its images).
 */
export function useShownAt(
  crawlId: string,
  check: Pick<Check, 'id' | 'targetFacts'>,
  imageId: number | null,
  pageIds: readonly number[],
): ShownAt | null | undefined {
  const wanted = imageId !== null && saysShownAt(check);
  const combine = useCallback(
    (results: { data: IssueTargetsOfUrl | null | undefined; isPending: boolean }[]) =>
      results.some((result) => result.isPending)
        ? undefined
        : imageId === null
          ? null
          : shownAtOf(
              results.map((result) => result.data),
              imageId,
            ),
    [imageId],
  );
  return useQueries({
    queries: (wanted ? pageIds : []).map((id) => issueTargetsQuery(crawlId, check.id, id, SOURCES_SHOWN)),
    combine,
  });
}

/** The same for a group, from its first pages (the read its rows under it share). */
export function useGroupShownAt(
  crawlId: string,
  check: Check,
  group: Pick<IssueGroup, 'detail' | 'relatedUrlId'> | null,
): ShownAt | null | undefined {
  const wanted = group?.relatedUrlId != null && saysShownAt(check);
  const rows = useQuery({
    ...issueGroupRowsQuery(
      crawlId,
      check.id,
      groupRefOf(group ?? { detail: null, relatedUrlId: null }),
      FIRST_GROUP_ROWS,
      groupRowColumns(check),
    ),
    enabled: wanted,
  });
  const shown = useShownAt(crawlId, check, group?.relatedUrlId ?? null, rows.data?.rows.map((row) => row.id) ?? []);
  if (!wanted) return null;
  return rows.data ? shown : undefined;
}
