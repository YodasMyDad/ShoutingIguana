// Calls wait for the engine while the shell starts it (api/transport.ts), so a cold start shows skeletons, not
// errors. Calls made while it had failed did fail; once one is ready after that ("Try again"), they fetch again here.
// Later sidecars are the live bridge's: a new generation refreshes everything.
import type { QueryClient } from '@tanstack/react-query';
import { getConnection, subscribeSidecarStatus } from './connection';

/** On the page's first ready sidecar, fetches again every query that failed before it. Returns a function that stops. */
export function refetchFailedQueriesOnFirstReady(queryClient: QueryClient): () => void {
  let ready = getConnection() !== null;
  return subscribeSidecarStatus(() => {
    if (ready || getConnection() === null) return;
    ready = true;
    void queryClient.invalidateQueries({ predicate: (query) => query.state.status === 'error' });
  });
}
