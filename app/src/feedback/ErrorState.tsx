import { useId, useState, type ReactNode } from 'react';
import { canRetry, errorDetail, errorTitle } from '../api/errors';
import { copyText } from '../lib/clipboard';
import { announce } from './announce';

/**
 * The error state every view uses: the plain cause, a retry when trying again could work, and the technical detail
 * behind "Details", copyable. Never a raw exception as the headline.
 */
export function ErrorState({
  error,
  title,
  onRetry,
  retryLabel = 'Try again',
  actions,
  compact = false,
}: {
  error: unknown;
  /** Overrides the error's own plain title, when the view can say it better. */
  title?: string;
  /** Offered only when trying again could work: never for a problem the engine meant, such as a 404 or a 422. */
  onRetry?: () => void;
  retryLabel?: string;
  /** What else the view offers: what to do about it ("Analyse it"), or a way out ("Go to Home"). */
  actions?: ReactNode;
  compact?: boolean;
}) {
  const detail = errorDetail(error);
  const [open, setOpen] = useState(false);
  const [copied, setCopied] = useState(false);
  const detailId = useId();
  const retry = onRetry && canRetry(error) ? onRetry : undefined;

  const copy = async () => {
    const done = await copyText(detail ?? '');
    setCopied(done);
    announce(done ? 'Details copied.' : 'The details couldn’t be copied.');
  };

  return (
    <div role="alert" className={compact ? 'flex flex-col gap-2 p-4' : 'flex flex-col items-start gap-3 p-8'}>
      <p className="text-14 font-medium text-text">{title ?? errorTitle(error)}</p>
      {title ? <p className="text-13 text-text-secondary">{errorTitle(error)}</p> : null}
      <div className="flex flex-wrap gap-2">
        {retry ? (
          <button
            type="button"
            onClick={retry}
            className="h-(--control-height-sm) rounded-sm bg-accent-fill px-3 text-13 font-medium text-text-on-accent hover:bg-accent-fill-hover"
          >
            {retryLabel}
          </button>
        ) : null}
        {actions}
        {detail ? (
          <button
            type="button"
            aria-expanded={open}
            aria-controls={detailId}
            onClick={() => setOpen((value) => !value)}
            className="h-(--control-height-sm) rounded-sm border border-border px-3 text-13 text-text-secondary hover:bg-surface-hover"
          >
            Details
          </button>
        ) : null}
      </div>
      {detail && open ? (
        <div id={detailId} className="flex w-full max-w-(--container-content) flex-col gap-2">
          <pre className="max-h-(--detail-max-height) overflow-auto rounded-sm border border-border-subtle bg-surface-sunken p-3 font-mono text-12 whitespace-pre-wrap text-text-secondary">
            {detail}
          </pre>
          <button
            type="button"
            onClick={() => void copy()}
            className="self-start text-12 text-accent-text underline underline-offset-(--underline-offset)"
          >
            {copied ? 'Copied' : 'Copy details'}
          </button>
        </div>
      ) : null}
    </div>
  );
}
