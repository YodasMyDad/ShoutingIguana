// The reads behind "where it's found": each URL shown asks for its own, and the asks of one moment go as one request
// (api/whereFoundBatch.ts). The words are whereFound.ts's.
import { useQuery } from '@tanstack/react-query';
import { foundOnQuery, issueGroupRowsQuery, issueTargetsQuery } from '../../api/queries';
import type { IssueTarget, IssueTargetsOfUrl, UrlFoundOn } from '../../api/inspector';
import type { Check } from '../../api/types';
import { FIRST_GROUP_ROWS, groupRefOf } from './groupTree';
import { SOURCES_SHOWN, referencesOf } from './whereFound';

/** The group asked for while none is: the query doesn't run then. */
const NO_GROUP = { detail: null, relatedUrlId: null };

export interface WhereFoundOf {
  references: ReturnType<typeof referencesOf>;
  foundOn: UrlFoundOn | null;
  targets: IssueTargetsOfUrl | null;
  /** Still being read. */
  pending: boolean;
  error: unknown;
}

/**
 * Where one flagged URL is found (a `sources` check), or what one flagged page names (a `targets` check); nothing for
 * any other check, or no URL.
 */
export function useWhereFoundOf(
  crawlId: string,
  check: Pick<Check, 'id' | 'references'>,
  urlId: number | null,
  limit: number = SOURCES_SHOWN,
): WhereFoundOf {
  const references = referencesOf(check);
  const foundOn = useQuery({
    ...foundOnQuery(crawlId, urlId ?? 0, limit),
    enabled: references === 'sources' && urlId !== null,
  });
  const targets = useQuery({
    ...issueTargetsQuery(crawlId, check.id, urlId ?? 0, limit),
    enabled: references === 'targets' && urlId !== null,
  });
  const query = references === 'targets' ? targets : foundOn;
  return {
    references,
    foundOn: foundOn.data ?? null,
    targets: targets.data ?? null,
    pending: references !== 'none' && urlId !== null && query.isPending,
    error: query.error,
  };
}

/**
 * The link a group of a `targets` check shares (every page linking to the same broken URL): the first page's link to
 * the group's URL, with its kind and position, so its callout can say where on the pages it is. Null while it's read,
 * for any other check or group, and where the page has no stored link to it.
 */
export function useTemplateLink(
  crawlId: string,
  check: Pick<Check, 'id' | 'references'>,
  group: { detail: string | null; relatedUrlId: number | null } | null,
): IssueTarget | null {
  const wanted = referencesOf(check) === 'targets' && group?.relatedUrlId != null ? group : null;
  // The group's first pages, as its rows under it read them when it's open: the same read, so one at most.
  const first = useQuery({
    ...issueGroupRowsQuery(crawlId, check.id, groupRefOf(wanted ?? NO_GROUP), FIRST_GROUP_ROWS),
    enabled: wanted !== null,
  });
  const pageId = first.data?.rows[0]?.id ?? null;
  const named = useQuery({
    ...issueTargetsQuery(crawlId, check.id, pageId ?? 0, MAX_TARGETS_READ),
    enabled: wanted !== null && pageId !== null,
  });
  return named.data?.targets.find((target) => target.id === wanted?.relatedUrlId) ?? null;
}

/** The targets read of a page to find the one a group shares: the engine lists at most 50 of a page's. */
const MAX_TARGETS_READ = 50;
