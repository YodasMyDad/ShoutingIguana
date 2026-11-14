// The sidebar's heading over the crawls running in the background, by what each is doing now: a crawl that is paused
// or being analysed isn't "crawling", nor "checking" (the closing audit's gap 29, and its minor "1 crawling" for a
// paused crawl). Pure.
import type { CrawlStatus } from '../api/types';
import type { Mode } from '../mode/mode';

type Doing = 'crawling' | 'paused' | 'analysing';

function doing(status: CrawlStatus | undefined): Doing {
  if (status === 'paused') return 'paused';
  if (status === 'analyzing') return 'analysing';
  return 'crawling';
}

const SIMPLE: Record<Doing, string> = {
  crawling: 'Checking now',
  paused: 'Paused',
  analysing: 'Working out the results',
};

/**
 * Advanced: "1 crawling", "2 crawling, 1 paused", "1 analysing". Simple: what they all do, "Checking now", "Paused" or
 * "Working out the results", or "Checking now" when they differ (each row says its own).
 */
export function runningTitle(statuses: readonly (CrawlStatus | undefined)[], mode: Mode): string {
  const counts = new Map<Doing, number>();
  for (const status of statuses) counts.set(doing(status), (counts.get(doing(status)) ?? 0) + 1);
  if (mode === 'simple') {
    const [only] = counts.keys();
    return counts.size === 1 && only ? SIMPLE[only] : SIMPLE.crawling;
  }
  return (['crawling', 'paused', 'analysing'] as const)
    .filter((state) => counts.has(state))
    .map((state) => `${counts.get(state)} ${state}`)
    .join(', ');
}
