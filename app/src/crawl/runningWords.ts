// The sidebar's heading over the crawls running in the background, by what each is doing now: a crawl that is paused
// or being analysed isn't "crawling", nor "checking" (the closing audit's gap 29, and its minor "1 crawling" for a
// paused crawl). The one heading for both modes (round 5's parts A and B each wrote one; integration kept this). Pure.
import type { CrawlStatus } from '../api/types';
import type { Mode } from '../mode/mode';

type Doing = 'crawling' | 'paused' | 'stopping' | 'analysing' | 'rechecking';

/** A crawl whose status hasn't arrived yet counts as crawling: it's live because it was started. */
function doing(status: CrawlStatus | null | undefined): Doing {
  if (status === 'paused') return 'paused';
  if (status === 'stopping') return 'stopping';
  if (status === 'analyzing') return 'analysing';
  if (status === 'rechecking') return 'rechecking';
  return 'crawling';
}

const SIMPLE: Record<Doing, string> = {
  crawling: 'Checking now',
  paused: 'Paused',
  stopping: 'Stopping',
  analysing: 'Working out the results',
  // A recheck checks some of the site's pages again: its row says "Rechecking" itself (the fix loop).
  rechecking: 'Checking now',
};

/**
 * Advanced: "1 crawling", "2 crawling, 1 paused", "1 analysing". Simple: what they all do, "Checking now", "Paused",
 * "Stopping" or "Working out the results", or "Checking now" when they differ (each row says its own).
 */
export function runningTitle(statuses: readonly (CrawlStatus | null | undefined)[], mode: Mode): string {
  const counts = new Map<Doing, number>();
  for (const status of statuses) counts.set(doing(status), (counts.get(doing(status)) ?? 0) + 1);
  if (mode === 'simple') {
    const [only] = counts.keys();
    return counts.size === 1 && only ? SIMPLE[only] : SIMPLE.crawling;
  }
  return (['crawling', 'paused', 'stopping', 'analysing', 'rechecking'] as const)
    .filter((state) => counts.has(state))
    .map((state) => `${counts.get(state)} ${state}`)
    .join(', ');
}
