// What only the site's platform can change, as the Shopify lane's phase B sidecar answers it: each such check's count
// carries its `platform` (who sets it and why, in words to show as they are), and the check is out of the score's lines,
// its five and Fix first. The sidecar on this branch doesn't send it yet: the tests make it from the garden crawl.
import type { Analysis, PlatformOwner } from '../api/types';

/** Shopify, as the sidecar names it, with its reason word for word. */
export const SHOPIFY_OWNER: PlatformOwner = {
  name: 'Shopify',
  reason: "Set by Shopify. You can't change this on a Shopify store.",
};

/** The checks Shopify sets on the garden crawl: its two security settings checks and its cookies (all notices). */
export const SHOPIFY_SET: readonly string[] = ['security-headers-missing', 'security-headers-weak', 'cookies-insecure'];

/** The analysis with `checkIds` set by `owner`, as the sidecar gives it: out of the score's lines and Fix first. */
export function platformOwned(
  analysis: Analysis,
  checkIds: readonly string[] = SHOPIFY_SET,
  owner: PlatformOwner = SHOPIFY_OWNER,
): Analysis {
  const owned = new Set(checkIds);
  return {
    ...analysis,
    counts: analysis.counts.map((count) => ({
      ...count,
      platform: owned.has(count.checkId) && count.count > 0 ? owner : null,
    })),
    ...(analysis.fixFirst ? { fixFirst: analysis.fixFirst.filter((id) => !owned.has(id)) } : {}),
    score: analysis.score
      ? { ...analysis.score, lines: analysis.score.lines.filter((line) => !owned.has(line.checkId)) }
      : null,
  };
}
