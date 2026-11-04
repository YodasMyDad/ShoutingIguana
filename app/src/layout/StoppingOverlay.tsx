import { useEffect, useRef } from 'react';
import { useModal } from '../app/overlays';
import { useSidecarStatus } from '../shell/connection';

/**
 * The window's "Stopping crawls…" state while the app quits: the shell has asked the engine to stop every crawl
 * (each stays resumable) and waits for it before closing. Nothing behind it can be used meanwhile.
 */
export function StoppingOverlay() {
  const status = useSidecarStatus();
  const ref = useRef<HTMLDivElement>(null);
  const stopping = status.state === 'stopping';
  // The app behind it is inert, and its keys do nothing, until the window closes.
  useModal(stopping);

  useEffect(() => {
    if (stopping) ref.current?.focus();
  }, [stopping]);

  if (!stopping) return null;
  return (
    <div
      ref={ref}
      role="alertdialog"
      aria-modal="true"
      aria-labelledby="stopping-title"
      aria-describedby="stopping-detail"
      tabIndex={-1}
      className="fixed inset-0 z-50 flex items-center justify-center bg-scrim outline-none"
    >
      <div className="flex w-(--dialog-width) max-w-full flex-col gap-2 rounded-lg border border-border bg-surface-overlay p-6 shadow-4">
        <p id="stopping-title" className="flex items-center gap-2 text-16 font-semibold">
          <span aria-hidden="true" className="size-(--mark-size) rounded-full bg-accent motion-safe:animate-pulse" />
          Stopping crawls…
        </p>
        <p id="stopping-detail" className="text-13 text-text-secondary">
          Each crawl is saved as it stops, and can be continued from the library next time.
        </p>
      </div>
    </div>
  );
}
