// Where the Shopify prompt goes on a problem page (owner's ruling, 2026-11-18): on the Summary, once at the top of What
// to fix, and on a problem page only where it's about those pages: the problem is about pages that weren't checked, or
// its own addresses include pages Shopify refused. Pages Shopify refused were never read, so no page's content flagged
// them: only a check of addresses can name one.
import { useQuery } from '@tanstack/react-query';
import { api } from '../api/client';
import { shopifyAccessQuery } from '../api/queries';
import type { Check } from '../api/types';

/** The checks about pages that weren't checked: the prompt explains some of them. */
const NOT_CHECKED_CHECKS: ReadonlySet<string> = new Set(['internal-blocked']);

/** The most of a check's addresses read to look for refused pages: a check of addresses rarely has more. */
const MAX_ROWS = 1_000;

export function useShopifyPromptOnProblem(crawlId: string, check: Pick<Check, 'id' | 'subject'>): boolean {
  const refused = useQuery(shopifyAccessQuery(crawlId)).data?.urlIds ?? [];
  const about = NOT_CHECKED_CHECKS.has(check.id);
  const reads = refused.length > 0 && !about && check.subject === 'url';
  const flagged = useQuery({
    queryKey: ['crawl', crawlId, 'shopify-prompt-rows', check.id] as const,
    queryFn: ({ signal }) =>
      api.issueRows(crawlId, check.id, { columns: ['url'], filters: [], sort: [], offset: 0, limit: MAX_ROWS }, signal),
    enabled: reads,
  }).data;
  if (refused.length === 0) return false;
  if (about) return true;
  if (!reads || !flagged) return false;
  const ids = new Set(refused);
  return flagged.rows.some((row) => ids.has(row.id));
}
