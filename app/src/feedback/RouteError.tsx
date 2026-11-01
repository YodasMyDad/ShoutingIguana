import { useQueryErrorResetBoundary } from '@tanstack/react-query';
import { useRouter, type ErrorComponentProps } from '@tanstack/react-router';
import { ErrorState } from './ErrorState';

/** Every route's error boundary: the plain cause and a retry, inside the shell, which keeps working around it. */
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
    />
  );
}
