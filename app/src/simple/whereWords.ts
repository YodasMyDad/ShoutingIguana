// What "Where it is" says under its list (docs/v2-ui-simple.md, "One problem"), and the site's origin its addresses are
// shortened against. Pure. The closing audit's gap 12: it said "It is shown. Addresses are on gardenfurnituresets.co.uk."
// of one cause, while the list showed every address in full (the pages were on www., the start address wasn't).
import type { CrawlListItem } from '../api/types';
import { formatCount } from '../lib/format';

const bare = (host: string) => host.toLowerCase().replace(/^www\./, '');

function parsed(url: string | null | undefined): URL | null {
  if (!url) return null;
  try {
    return new URL(url);
  } catch {
    return null;
  }
}

/**
 * The origin the list's addresses are shortened against, so the site's own show as paths ("/blog/"): the origin its
 * first address is on, when that is the site's (its host, www. or not), else the start address's. Null when neither is
 * known.
 */
export function pathOrigin(
  firstUrl: string | null | undefined,
  item: Pick<CrawlListItem, 'site' | 'startUrl'>,
): string | null {
  const start = parsed(item.startUrl);
  const first = parsed(firstUrl);
  const site = item.site ?? (start ? bare(start.hostname) : null);
  if (first && site !== null && bare(first.hostname) === bare(site)) return first.origin;
  return start?.origin ?? null;
}

/** The origin as the footer names it: "www.example.co.uk" on https, "http://example.co.uk:8080" otherwise. */
function originName(origin: string): string {
  return origin.startsWith('https://') ? origin.slice('https://'.length) : origin;
}

/**
 * The footer's sentence: how many of what the list counts are shown (its causes, for a check that groups them; its
 * pages or addresses otherwise), then where the addresses shown as paths are.
 */
export function shownWords({
  grouped,
  listed,
  all,
  noun,
  origin,
  shortened,
}: {
  grouped: boolean;
  listed: number;
  all: number;
  /** The check's noun for a count of what it flags: "page", "pages", "address", "addresses". */
  noun: (count: number) => string;
  origin: string | null;
  /** Whether any address shown was shortened to its path. */
  shortened: boolean;
}): string {
  const what = (count: number) => (grouped ? (count === 1 ? 'cause' : 'causes') : noun(count));
  const count =
    listed >= all
      ? all === 1
        ? `The one ${what(1)} is shown.`
        : `All ${formatCount(all)} ${what(all)} are shown.`
      : `Showing ${formatCount(listed)} of ${formatCount(all)} ${what(all)}.`;
  return origin && shortened ? `${count} Addresses starting with “/” are on ${originName(origin)}.` : count;
}
