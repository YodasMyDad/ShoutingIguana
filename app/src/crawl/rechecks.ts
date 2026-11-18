// Rechecks asked from this app (the fix loop: docs/v2-fix-loop.md, "How a recheck runs"): starting one, with
// Continue's secrets, and knowing that one is on its way. A recheck takes the crawl through `analyzing` (when its
// analysis was out of date), `rechecking`, `analyzing` again and back to its status; the screens keep showing the crawl
// through all of it (the problem page's card follows it), never "the check is still running". The live store says
// what the sidecar does; what's kept here covers the moment before its first event, which the sidecar's status
// doesn't (an analysis first looks like any analysis).
import { useSyncExternalStore } from 'react';
import { api } from '../api/client';
import { isProblem } from '../api/errors';
import type { RecheckAsk, RecheckRefusal, StartedRecheck } from '../api/fixLoop';
import type { CrawlStatus } from '../api/types';
import { useLive, type LiveCrawl } from '../live/liveStore';

/** A recheck this app asked for, until its crawl's run and analysis are over. */
export interface AskedRecheck {
  recheckId: number;
  checkId: string | null;
  judged: number;
}

const asked = new Map<string, AskedRecheck>();
const listeners = new Set<() => void>();
let version = 0;

function changed() {
  version += 1;
  listeners.forEach((listener) => listener());
}

/** The recheck this app asked for on a crawl, while its run and its analyses go on; undefined otherwise. */
export function askedRecheck(crawlId: string): AskedRecheck | undefined {
  return asked.get(crawlId);
}

/** Notes a recheck started here: the screens follow it from now, before its first event. */
export function noteRecheckAsked(crawlId: string, recheck: AskedRecheck): void {
  asked.set(crawlId, recheck);
  changed();
}

/** The crawl's recheck is over (settled, refused, or its crawl at rest again): the screens go back to its status. */
export function forgetRecheckAsked(crawlId: string): void {
  if (asked.delete(crawlId)) changed();
}

/** For tests: nothing asked. */
export function resetRechecksAskedForTests(): void {
  asked.clear();
  changed();
}

function subscribe(listener: () => void): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

/** The recheck this app asked for on a crawl, live. */
export function useAskedRecheck(crawlId: string): AskedRecheck | undefined {
  useSyncExternalStore(
    subscribe,
    () => version,
    () => version,
  );
  return asked.get(crawlId);
}

/**
 * Whether a recheck of the crawl is on its way, from what this app asked and what the sidecar says: rechecking, or an
 * analysis that comes before or after one (its `recheck` event not yet settled), or stopping it.
 */
export function recheckUnderway(
  live: Pick<LiveCrawl, 'status' | 'recheck'> | undefined,
  asking: AskedRecheck | undefined,
): boolean {
  const status: CrawlStatus | null = live?.status ?? null;
  if (status === 'rechecking') return true;
  const busy = status === 'analyzing' || status === 'stopping';
  if (!busy) return false;
  if (asking) return true;
  return live?.recheck ? live.recheck.result === null : false;
}

/** `recheckUnderway`, for a screen: re-drawn only when its answer changes. */
export function useRecheckUnderway(crawlId: string): boolean {
  const asking = useAskedRecheck(crawlId);
  return useLive((state) => recheckUnderway(state.crawls[crawlId], asking));
}

/** How asking for a recheck went. */
export type RecheckStart =
  | { kind: 'started'; started: StartedRecheck }
  /** Header values or proxy credentials the crawl file doesn't keep: give them in its settings, or go without the headers. */
  | { kind: 'needsSecrets'; title: string; missingHeaders: readonly string[]; proxyCredentials: boolean }
  /** It can't run now: the sidecar's reason (`site-busy`, `recheck-not-latest`...). */
  | { kind: 'refused'; refused: RecheckRefusal }
  | { kind: 'failed'; error: unknown };

const REFUSALS: readonly string[] = [
  'crawl-busy',
  'site-busy',
  'recheck-not-latest',
  'recheck-unsupported',
  'not-analyzed',
  'not-analysable',
];

/**
 * Starts a recheck. What the crawl was last given (headers, a proxy's password) goes with it from the credential store,
 * as Continue's does, so it asks only when nothing was kept. Never throws: the card says each outcome its own way.
 */
export async function startRecheck(
  crawlId: string,
  ask: RecheckAsk,
  options: { skipMissingHeaders?: boolean } = {},
): Promise<RecheckStart> {
  try {
    const started = await api.startRecheck(crawlId, {
      ...ask,
      ...(options.skipMissingHeaders ? { skipMissingHeaders: true } : {}),
    });
    noteRecheckAsked(crawlId, { recheckId: started.recheckId, checkId: started.checkId, judged: started.judged });
    return { kind: 'started', started };
  } catch (error) {
    if (isProblem(error, 'needs-secrets')) {
      return {
        kind: 'needsSecrets',
        title: error.title,
        missingHeaders: error.missingHeaders,
        proxyCredentials: error.proxyCredentials,
      };
    }
    if (isProblem(error) && REFUSALS.includes(error.type)) {
      return { kind: 'refused', refused: { type: error.type, title: error.title } };
    }
    return { kind: 'failed', error };
  }
}
