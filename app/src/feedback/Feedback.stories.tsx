import type { Meta, StoryObj } from '@storybook/react-vite';
import { useState } from 'react';
import { ProblemError, SidecarUnreachableError } from '../api/errors';
import { Button } from '../ui/Button';
import { AppToaster } from './AppToaster';
import { ErrorState } from './ErrorState';
import { notifyError, notifyProgress, notifyResult, notifyUndo } from './notify';
import { SkeletonRows } from './Skeleton';
import { useDelayedLoading } from './useDelayedLoading';

const meta = { title: 'Feedback/States' } satisfies Meta;
export default meta;
type Story = StoryObj<typeof meta>;

/** The error state: the plain cause, Try again, and the detail behind "Details". */
export const ProblemErrorState: Story = {
  render: () => (
    <ErrorState
      error={
        new ProblemError({
          type: 'crawl-busy',
          title: 'This crawl is open in another process.',
          status: 409,
          detail: 'The file’s lock is held.',
        })
      }
      onRetry={() => undefined}
    />
  ),
};

export const EngineUnreachable: Story = {
  render: () => (
    <ErrorState error={new SidecarUnreachableError(new TypeError('Failed to fetch'))} onRetry={() => undefined} />
  ),
};

export const UnknownError: Story = {
  render: () => (
    <ErrorState error={new Error('Cannot read properties of undefined')} title="This view couldn’t be shown." />
  ),
};

function LoadingRule() {
  const [loading, setLoading] = useState(false);
  const showSkeleton = useDelayedLoading(loading);
  const load = (ms: number) => {
    setLoading(true);
    setTimeout(() => setLoading(false), ms);
  };
  return (
    <div className="flex flex-col gap-4 p-6">
      <div className="flex gap-2">
        <Button onClick={() => load(100)}>Fast load (100 ms)</Button>
        <Button onClick={() => load(200)}>Slow load (200 ms)</Button>
        <Button onClick={() => load(2_000)}>Very slow load (2 s)</Button>
      </div>
      <p className="text-13 text-text-secondary">
        A fast load shows no skeleton. A slow one shows it after 150 ms and keeps it at least 300 ms.
      </p>
      {showSkeleton ? (
        <SkeletonRows rows={5} label="Loading the library" />
      ) : (
        <p className="text-14">{loading ? '' : 'Loaded.'}</p>
      )}
    </div>
  );
}

export const TheLoadingRule: Story = { render: () => <LoadingRule /> };

function Toasts() {
  return (
    <div className="flex flex-wrap gap-2 p-6">
      <Button
        onClick={() =>
          notifyResult('Crawl finished', {
            description: 'gardenfurnituresets.co.uk: 4,521 URLs, 19 checks found issues',
            action: { label: 'View issues', onClick: () => undefined },
          })
        }
      >
        Result
      </Button>
      <Button
        onClick={() =>
          notifyUndo(
            'gardenfurnituresets.co.uk was removed from the library. The file is still on disk.',
            () => undefined,
          )
        }
      >
        Undo
      </Button>
      <Button
        onClick={() =>
          notifyError(
            new ProblemError({
              type: 'internal',
              title: 'Something went wrong in the engine.',
              status: 500,
              detail: 'Disk full',
            }),
            { retry: () => undefined },
          )
        }
      >
        Error with retry
      </Button>
      <Button
        onClick={() => {
          const progress = notifyProgress('Exporting 4,521 URLs', { onCancel: () => undefined });
          setTimeout(() => progress.update('Writing the Excel file'), 800);
          setTimeout(
            () => progress.done('Exported 4,521 URLs', { label: 'Open folder', onClick: () => undefined }),
            1_800,
          );
        }}
      >
        Progress
      </Button>
      <AppToaster />
    </div>
  );
}

export const ToastsWithTheirRules: Story = { render: () => <Toasts /> };
