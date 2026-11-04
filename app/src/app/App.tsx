import { QueryClientProvider, type QueryClient } from '@tanstack/react-query';
import { RouterProvider } from '@tanstack/react-router';
import { TooltipProvider } from '../ui/Tooltip';
import type { AppRouter } from './router';

export function App({ router, queryClient }: { router: AppRouter; queryClient: QueryClient }) {
  return (
    <QueryClientProvider client={queryClient}>
      <TooltipProvider>
        <RouterProvider router={router} />
      </TooltipProvider>
    </QueryClientProvider>
  );
}
