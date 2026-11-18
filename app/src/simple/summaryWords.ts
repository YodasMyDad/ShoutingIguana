// The Summary's head (docs/v2-ui-simple.md, "The Summary"; summary.html, summary-stopped.html): when the check was, and
// how long it read for, in one line. Pure.
import type { CrawlListItem, CrawlStatus } from '../api/types';
import { formatCount, formatDuration } from '../lib/format';
import { longDate } from './dates';

const DAY_MS = 24 * 60 * 60 * 1_000;

/**
 * How long the check read for: the time it spent crawling, pauses left out (`crawlingMs`). A check made by an engine
 * from before that figure has only its start and end: their span, said as such, when it's under a day (a longer one has
 * a stop and a continue in it, and says nothing of the reading).
 */
function tookWords(item: Pick<CrawlListItem, 'crawlingMs' | 'startedAt' | 'finishedAt'>): {
  took: string;
  clock: string;
} | null {
  if (item.crawlingMs) return { took: formatDuration(item.crawlingMs), clock: '' };
  const start = item.startedAt ? Date.parse(item.startedAt) : Number.NaN;
  const end = item.finishedAt ? Date.parse(item.finishedAt) : Number.NaN;
  const span = end - start;
  if (!Number.isFinite(span) || span <= 0 || span >= DAY_MS) return null;
  return { took: formatDuration(span), clock: ', start to finish' };
}

/**
 * "Checked on 31 October 2026. 265 pages read in 37 min 25 s." A check stopped early: "Stopped on 15 October 2026 after
 * 6 min 40 s. 312 pages read so far." No pages read: the date alone. `pages` is the figure the score covers.
 */
export function summarySubtitle(
  item: Pick<CrawlListItem, 'crawlingMs' | 'startedAt' | 'finishedAt' | 'pageCount'>,
  status: CrawlStatus | null,
  pages: number,
): string {
  const day = item.finishedAt ?? item.startedAt;
  const date = day ? longDate(day) : null;
  const took = tookWords(item);
  const read = `${pages === 1 ? '1 page' : `${formatCount(pages)} pages`} read`;
  if (status === 'stopped' || status === 'interrupted') {
    const when = date ? `Stopped on ${date}${took ? ` after ${took.took}${took.clock}` : ''}. ` : '';
    return `${when}${item.pageCount > 0 ? `${read} so far.` : ''}`.trim();
  }
  const when = date ? `Checked on ${date}. ` : '';
  return `${when}${item.pageCount > 0 ? `${read}${took ? ` in ${took.took}${took.clock}` : ''}.` : ''}`.trim();
}

/**
 * What the site's platform sets (the Shopify lane, phase B), in one quiet line a platform, apart from the problems to
 * fix: "2 set by Shopify: nothing for you to change." `owners` is each such check's platform name, in any order.
 */
export function platformLines(owners: readonly string[]): string[] {
  const counted = new Map<string, number>();
  for (const name of owners) counted.set(name, (counted.get(name) ?? 0) + 1);
  return [...counted].map(([name, count]) => `${formatCount(count)} set by ${name}: nothing for you to change.`);
}
