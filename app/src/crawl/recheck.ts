// A recheck, as both modes start and follow it (docs/v2-fix-loop.md, section 3, "How a recheck runs"; the sidecar's
// "Rechecks (fix loop)"): starting one, with what its refusals and the crawl file's missing secrets need said plainly,
// and its run as the live store hears it: the analysis first when the crawl's is out of date, the fetching, the
// analysis after, and the result once that analysis has settled it.
import { useSyncExternalStore } from 'react';
import type { QueryClient } from '@tanstack/react-query';
import { api } from '../api/client';
import { isProblem } from '../api/errors';
import type { RecheckAsk, RecheckEvent, RecheckResult, RecheckStatus, StartedRecheck } from '../api/fixLoop';
import type { CrawlSignature } from '../api/types';
import { queryKeys } from '../api/queries';
import { isLive } from '../api/types';
import { crawlToast, notifyError, notifyResult } from '../feedback/notify';
import { EMPTY_TRAIL, liveStore, useLiveCrawl, type LiveCrawl, type LiveStore } from '../live/liveStore';
import { openSettingsAction } from './actions';
import { keptSecrets } from './secrets';

// ---------------------------------------------------------------------------------------------------------- Starting

/** A recheck this window started: its number from the 202, until its own events take over. */
export interface PendingRecheck {
  recheckId: number;
  ask: RecheckAsk;
  /** The crawl came to rest without the run's events: refused once its analysis first was done, or Stop then. */
  ended: boolean;
  /** Why, when the crawl's event said. */
  error: string | null;
}

const pending = new Map<string, PendingRecheck>();
const pendingListeners = new Set<() => void>();

function setPending(crawlId: string, next: PendingRecheck | null): void {
  if (next) pending.set(crawlId, next);
  else pending.delete(crawlId);
  pendingListeners.forEach((listener) => listener());
}

/** The recheck this window started on a crawl, if any. */
export function pendingRecheck(crawlId: string): PendingRecheck | undefined {
  return pending.get(crawlId);
}

/** For tests: forgets every recheck started. */
export function resetPendingRechecks(): void {
  pending.clear();
  pendingListeners.forEach((listener) => listener());
}

/**
 * Listens for a crawl that comes to rest after a recheck was started on it and before any of the run's own events: the
 * sidecar refused it once its analysis first was done (the crawl event's `error` says why), or Stop came then. Its
 * pending recheck then reads as ended, never as running for ever.
 */
export function watchPendingRechecks(store: LiveStore = liveStore): () => void {
  return store.onEvent((event, previous) => {
    if (event.type !== 'crawl') return;
    const { crawlId, status, error } = event.data;
    const started = pending.get(crawlId);
    if (!started || started.ended || isLive(status)) return;
    const heard = previous.crawls[crawlId]?.recheck;
    if (heard && heard.recheckId >= started.recheckId) return;
    setPending(crawlId, { ...started, ended: true, error: error ?? null });
  });
}

watchPendingRechecks();

/**
 * Starts a recheck. When the crawl file doesn't keep what it needs (header values, a proxy's password), says so as
 * Continue does, with "Recheck without" for headers alone, or the crawl's settings; any other refusal (`site-busy`,
 * `recheck-not-latest`...) in the sidecar's plain words, with Retry where trying again can work. The started recheck,
 * or null when it didn't start. A Shopify store's signature goes with it: the one given, else the one kept for the
 * crawl (a crawl file keeps none), so a recheck reads the pages the store refuses an unsigned crawler.
 */
export async function startRecheck(
  crawlId: string,
  ask: RecheckAsk,
  options: { skipMissingHeaders?: boolean; signatures?: Record<string, CrawlSignature> } = {},
): Promise<StartedRecheck | null> {
  try {
    const signatures = options.signatures ?? (await keptSecrets(`crawl:${crawlId}`))?.signatures;
    const started = await api.startRecheck(crawlId, {
      ...ask,
      ...(options.skipMissingHeaders ? { skipMissingHeaders: true } : {}),
      ...(signatures && Object.keys(signatures).length > 0 ? { signatures } : {}),
    });
    setPending(crawlId, { recheckId: started.recheckId, ask, ended: false, error: null });
    return started;
  } catch (error) {
    if (isProblem(error, 'needs-secrets')) {
      const headersOnly = !error.proxyCredentials && error.missingHeaders.length > 0;
      const settings = openSettingsAction(crawlId);
      notifyResult(error.title, {
        id: crawlToast(crawlId, 'recheck'),
        description: headersOnly
          ? `Its headers (${error.missingHeaders.join(', ')}) aren’t kept in the crawl file. Enter them again in its settings, or recheck without them.`
          : 'Its proxy password isn’t kept in the crawl file. Enter it again in the crawl’s settings to recheck.',
        ...(headersOnly
          ? {
              action: {
                label: 'Recheck without',
                onClick: () => void startRecheck(crawlId, ask, { skipMissingHeaders: true }),
              },
            }
          : settings
            ? { action: settings }
            : {}),
      });
      return null;
    }
    notifyError(error, { id: crawlToast(crawlId, 'recheck'), retry: () => void startRecheck(crawlId, ask, options) });
    return null;
  }
}

// ---------------------------------------------------------------------------------------------------------- Its run

/**
 * Where a recheck stands:
 * - `starting`: asked, and nothing heard yet;
 * - `analysingFirst`: the crawl's analysis was out of date, so it's worked out first (the "before" is today's);
 * - `fetching`: the run fetches the URLs again (`progress` says how far);
 * - `stopping`: Stop was pressed; what was fetched stays;
 * - `analysing`: the run ended (`status`), and the analysis after it works out the result;
 * - `settled`: that analysis settled it: `result`;
 * - `ended`: the crawl came to rest without a run (refused once the analysis first was done, or stopped then).
 */
export type RecheckStage = 'starting' | 'analysingFirst' | 'fetching' | 'stopping' | 'analysing' | 'settled' | 'ended';

export interface RecheckRun {
  crawlId: string;
  /** Its number in the crawl file. */
  recheckId: number;
  stage: RecheckStage;
  /** The run's own status once it has one: `running`, then how it ended. */
  status: RecheckStatus | null;
  /** Its fetches in the phase, while fetching: null before the first count. */
  progress: Pick<RecheckEvent, 'phase' | 'done' | 'total'> | null;
  result: RecheckResult | null;
  /** Why it ended without a run, when the sidecar said. */
  error: string | null;
}

/**
 * A crawl's recheck, from its live state and the recheck this window started on it (pure): its own events once they
 * come, the crawl's live status before then. Null when there's no recheck to speak of.
 */
export function recheckRunOf(live: LiveCrawl | undefined, started: PendingRecheck | undefined): RecheckRun | null {
  const event = live?.recheck ?? null;
  const status = live?.status ?? null;
  const crawlId = live?.id ?? null;
  if (event && (!started || event.recheckId >= started.recheckId)) {
    const progress =
      event.total > 0 || event.phase !== null ? { phase: event.phase, done: event.done, total: event.total } : null;
    const base = { crawlId: event.crawlId, recheckId: event.recheckId, status: event.status, error: null };
    if (event.result) return { ...base, stage: 'settled', progress: null, result: event.result };
    if (event.status === 'running') {
      return { ...base, stage: status === 'stopping' ? 'stopping' : 'fetching', progress, result: null };
    }
    return { ...base, stage: 'analysing', progress: null, result: null };
  }
  if (!started || crawlId === null) return null;
  const base = { crawlId, recheckId: started.recheckId, status: null, progress: null, result: null };
  if (status === 'analyzing') return { ...base, stage: 'analysingFirst', error: null };
  if (status === 'rechecking') return { ...base, stage: 'fetching', error: null };
  if (status === 'stopping') return { ...base, stage: 'stopping', error: null };
  if (started.ended) return { ...base, stage: 'ended', error: started.error };
  return { ...base, stage: 'starting', error: null };
}

const subscribePending = (listener: () => void) => {
  pendingListeners.add(listener);
  return () => pendingListeners.delete(listener);
};

/** A crawl's recheck as it runs here, or null: what Simple's card and Advanced's banner show. */
export function useRecheckRun(crawlId: string): RecheckRun | null {
  const live = useLiveCrawl(crawlId);
  const started = useSyncExternalStore(subscribePending, () => pending.get(crawlId));
  // Before the crawl's first event the live store may not know it: the run is still this crawl's.
  return recheckRunOf(live ?? (started ? { ...BLANK, id: crawlId } : undefined), started);
}

const BLANK: Omit<LiveCrawl, 'id'> = {
  status: null,
  progress: null,
  history: [],
  trail: EMPTY_TRAIL,
  clock: null,
  pace: [],
  alerts: [],
  analysis: null,
  recheck: null,
  error: null,
  errorType: null,
};

/** Whether a recheck is under way: started and not yet settled or ended. */
export function isRecheckRunning(run: RecheckRun | null): boolean {
  return run !== null && run.stage !== 'settled' && run.stage !== 'ended';
}

/**
 * What to read again once a recheck is settled: its list and the plans (whether one can run moved). The crawl's other
 * reads (its analysis, groups, rows, comparison) the analysis's own events refresh, as after any analysis.
 */
export function refreshAfterRecheck(queryClient: QueryClient, crawlId: string): Promise<unknown> {
  return Promise.all([
    queryClient.invalidateQueries({ queryKey: queryKeys.rechecks(crawlId) }),
    queryClient.invalidateQueries({ queryKey: ['crawl', crawlId, 'recheck-plan'] }),
  ]);
}
