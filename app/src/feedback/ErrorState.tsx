import { useId, useState } from 'react';
import { errorDetail, errorTitle } from '../api/errors';
import { copyText } from '../lib/clipboard';

/**
 * The error state every view uses: the plain cause, a retry when there's something to retry, and the technical
 * detail behind "Details", copyable. Never a raw exception as the headline.
 */
export function ErrorState({
  error,
  title,
  onRetry,
  compact = false,
}: {
  error: unknown;
  /** Overrides the error's own plain title, when the view can say it better. */
  title?: string;
  onRetry?: () => void;
  compact?: boolean;
}) {
  const detail = errorDetail(error);
  const [open, setOpen] = useState(false);
  const [copied, setCopied] = useState(false);
  const detailId = useId();

  return (
    <div role="alert" className={compact ? 'flex flex-col gap-2 p-4' : 'flex flex-col items-start gap-3 p-8'}>
      <p className="text-14 font-medium text-text">{title ?? errorTitle(error)}</p>
      {title ? <p className="text-13 text-text-secondary">{errorTitle(error)}</p> : null}
      <div className="flex gap-2">
        {onRetry ? (
          <button
            type="button"
            onClick={onRetry}
            className="h-(--control-height-sm) rounded-sm bg-accent-fill px-3 text-13 font-medium text-text-on-accent hover:bg-accent-fill-hover"
          >
            Try again
          </button>
        ) : null}
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
          <pre className="max-h-48 overflow-auto rounded-sm border border-border-subtle bg-surface-sunken p-3 font-mono text-12 whitespace-pre-wrap text-text-secondary">
            {detail}
          </pre>
          <button
            type="button"
            onClick={() => void copyText(detail).then(setCopied)}
            className="self-start text-12 text-accent-text underline underline-offset-(--underline-offset)"
          >
            {copied ? 'Copied' : 'Copy details'}
          </button>
        </div>
      ) : null}
    </div>
  );
}
