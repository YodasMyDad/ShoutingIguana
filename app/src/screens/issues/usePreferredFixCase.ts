// Which case of a check's fix the crawl points to ("On this site"), from what the pane has already read: the
// `IssueTarget`s its rows asked for (`/issues/{checkId}/targets`, through `useRowReferences`, `useWhereFoundOf` and
// `useTemplateLink`). It reads the query cache and never asks for anything itself.
import { useQueryClient } from '@tanstack/react-query';
import { useCallback, useSyncExternalStore } from 'react';
import type { IssueTargetsOfUrl } from '../../api/inspector';
import { queryKeys } from '../../api/queries';
import type { Check } from '../../api/types';
import { preferredFixCase } from './fixCase';

/** The start of every `issueTargetsQuery` key of one check: its URL and limit follow. */
const targetsKeyOf = (crawlId: string, checkId: string) => queryKeys.issueTargets(crawlId, checkId, 0, 0).slice(0, 5);

/**
 * The case (`FixExample.case`) most of the check's links read so far are, for `HowToFix` to show first; undefined
 * until some are read, and for a check with one fix or none.
 */
export function usePreferredFixCase(crawlId: string, check: Pick<Check, 'id' | 'fixExamples'>): string | undefined {
  const cache = useQueryClient().getQueryCache();
  const cases = check.fixExamples?.length ?? 0;
  const subscribe = useCallback(
    (onChange: () => void) => (cases > 1 ? cache.subscribe(onChange) : () => undefined),
    [cache, cases],
  );
  const read = () => {
    if (cases <= 1) return undefined;
    const targets = cache
      .findAll({ queryKey: targetsKeyOf(crawlId, check.id) })
      .flatMap((query) => (query.state.data as IssueTargetsOfUrl | null | undefined)?.targets ?? []);
    return preferredFixCase(
      check.id,
      targets.map((target) => target.flags),
    );
  };
  return useSyncExternalStore(subscribe, read, read);
}
