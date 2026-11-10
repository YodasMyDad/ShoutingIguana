import type { QueryClient } from '@tanstack/react-query';
import { createQueryClient } from '../app/queryClient';

/**
 * The app's query client, with the back-off between a failed read's tries taken out: it's still tried as often
 * (`shouldRetry`), at once. For tests of what a view shows once a read has failed for good. The app waits 250 ms,
 * then 500 ms, between tries, so the view says so 750 ms after the read began: three quarters of the second
 * Testing Library waits for it, in real time, however busy the machine is. Under load that wait ran out before the
 * view had anything to say.
 */
export function createQueryClientWithoutBackoff(): QueryClient {
  const queryClient = createQueryClient();
  const defaults = queryClient.getDefaultOptions();
  queryClient.setDefaultOptions({ ...defaults, queries: { ...defaults.queries, retryDelay: 0 } });
  return queryClient;
}
