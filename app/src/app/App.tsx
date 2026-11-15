import { QueryClientProvider, type QueryClient } from '@tanstack/react-query';
import { RouterProvider } from '@tanstack/react-router';
import { useEffect } from 'react';
import { watchHiddenChecks } from '../api/hiddenChecksRefresh';
import { TooltipProvider } from '../ui/Tooltip';
import type { AppRouter } from './router';

export function App({ router, queryClient }: { router: AppRouter; queryClient: QueryClient }) {
  // A check hidden or shown again: the grids' issue counts are read again without it, or with it.
  useEffect(() => watchHiddenChecks(queryClient), [queryClient]);
  return (
    <QueryClientProvider client={queryClient}>
      <TooltipProvider>
        <RouterProvider router={router} />
      </TooltipProvider>
    </QueryClientProvider>
  );
}
