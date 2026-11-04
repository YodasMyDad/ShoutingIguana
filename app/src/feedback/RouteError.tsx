import { useQueryErrorResetBoundary } from '@tanstack/react-query';
import { Link, useRouter, type ErrorComponentProps } from '@tanstack/react-router';
import { Button } from '../ui/Button';
import { ErrorState } from './ErrorState';

/**
 * Every screen's error boundary: the plain cause and a retry, inside the shell, which keeps working around it. Going
 * Home is always there too, for a view that fails again however often it's tried.
 */
export function RouteError({ error, reset }: ErrorComponentProps) {
  const router = useRouter();
  const queryReset = useQueryErrorResetBoundary();
  return (
    <ErrorState
      error={error}
      title="This view couldn’t be shown."
      onRetry={() => {
        queryReset.reset();
        reset();
        void router.invalidate();
      }}
      actions={
        <Button asChild size="sm">
          <Link to="/" search={{}}>
            Go to Home
          </Link>
        </Button>
      }
    />
  );
}

/**
 * The shell's own boundary, for a failure in the frame around every view (its parts have their own boundaries, so this
 * is rare): the window can't be drawn, so offer to load it again.
 */
export function AppError({ error }: ErrorComponentProps) {
  return (
    <main className="flex h-full items-center justify-center bg-canvas text-text">
      <ErrorState
        error={error}
        title="Shouting Iguana couldn’t show this window."
        onRetry={() => window.location.reload()}
        retryLabel="Reload the window"
      />
    </main>
  );
}
