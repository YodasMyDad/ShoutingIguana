import type { HiddenScope } from '../../settings/hiddenChecks';
/**
 * The words that show a hidden check again, by where it's hidden: in every crawl, it's shown in every crawl, and the
 * button says so (round 5 review R1-12).
 */
export function showCheckWords(scope: HiddenScope): string {
  if (scope === 'all') return 'Show this check in every crawl';
  if (scope === 'crawl') return 'Show this check in this crawl';
  return 'Show this check in the list';
}
