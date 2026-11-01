import { useEffect, useState, type ReactNode } from 'react';
import { useLive } from '../live/liveStore';
import { restartSidecar, useSidecarStatus, type SidecarStatus } from '../shell/connection';
import { Button } from '../ui/Button';

/** How long the event stream may be reconnecting before the banner says so: a blip shouldn't flash a banner. */
const RECONNECT_GRACE_MS = 2_000;

const FAILURE_TITLES: Record<Extract<SidecarStatus, { state: 'failed' }>['reason'], string> = {
  'missing-binary': 'The engine isn’t installed with the app. Reinstall Shouting Iguana.',
  'bad-arguments': 'The engine wouldn’t start with the app’s settings.',
  'state-in-use': 'Another copy of the engine is still using the app’s data folder.',
  'start-failed': 'The engine didn’t start.',
  crashed: 'The engine keeps stopping.',
  'not-configured': 'No engine to connect to.',
};

/**
 * The "sidecar lost" state from the brief: a banner while the engine restarts by itself, and a plain cause with
 * Try again when it can't. A crawl that was running when the engine stopped is resumable from its file.
 */
export function SidecarBanner() {
  const status = useSidecarStatus();
  const stream = useLive((state) => state.stream);
  const reconnecting = useDelayedFlag(status.state === 'ready' && stream === 'reconnecting', RECONNECT_GRACE_MS);

  if (status.state === 'restarting') {
    return (
      <Banner tone="warning">
        The engine stopped unexpectedly and is restarting. A crawl that was running can be continued once it’s back.
      </Banner>
    );
  }
  if (status.state === 'failed') {
    return (
      <Banner tone="error">
        <span>
          {FAILURE_TITLES[status.reason]}
          {status.message ? <span className="block text-12 text-text-secondary">{status.message}</span> : null}
        </span>
        {status.reason === 'not-configured' ? null : (
          <Button size="sm" onClick={() => void restartSidecar()}>
            Try again
          </Button>
        )}
      </Banner>
    );
  }
  if (reconnecting) {
    return <Banner tone="warning">Reconnecting to the engine…</Banner>;
  }
  return null;
}

function Banner({ tone, children }: { tone: 'warning' | 'error'; children: ReactNode }) {
  return (
    <div
      role={tone === 'error' ? 'alert' : 'status'}
      className={
        tone === 'error'
          ? 'flex items-center gap-3 border-b border-border bg-error-subtle px-4 py-2 text-13 text-text'
          : 'flex items-center gap-3 border-b border-border bg-warning-subtle px-4 py-2 text-13 text-text'
      }
    >
      <span aria-hidden="true" className={tone === 'error' ? 'text-error-text' : 'text-warning-text'}>
        {tone === 'error' ? '■' : '▲'}
      </span>
      {children}
    </div>
  );
}

/** True once `value` has stayed true for `delayMs`. */
function useDelayedFlag(value: boolean, delayMs: number): boolean {
  const [delayed, setDelayed] = useState(false);
  useEffect(() => {
    const timer = setTimeout(() => setDelayed(value), value ? delayMs : 0);
    return () => clearTimeout(timer);
  }, [value, delayMs]);
  return value && delayed;
}
