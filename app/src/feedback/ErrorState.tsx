import { Check, ChevronDown, Copy } from 'lucide-react';
import { useId, useState, type ReactNode } from 'react';
import { canRetry, errorDetail, errorTitle } from '../api/errors';
import { copyText } from '../lib/clipboard';
import { Button } from '../ui/Button';
import { SeverityMark } from '../ui/SeverityMark';
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
    <div role="alert" className={compact ? 'flex gap-3 p-4' : 'flex gap-3 p-8'}>
      <span className="pt-0.5">
        <SeverityMark severity="error" size="md" decorative />
      </span>
      <div className="flex min-w-0 flex-1 flex-col items-start gap-3">
        <div className="flex flex-col gap-0.5">
          <p className="text-14 font-semibold text-text">{title ?? errorTitle(error)}</p>
          {title ? <p className="text-13 text-text-secondary">{errorTitle(error)}</p> : null}
        </div>
        <div className="flex flex-wrap gap-2">
          {retry ? (
            <Button variant="primary" size="sm" onClick={retry}>
              {retryLabel}
            </Button>
          ) : null}
          {actions}
          {detail ? (
            <Button
              size="sm"
              variant="ghost"
              iconEnd={ChevronDown}
              aria-expanded={open}
              aria-controls={detailId}
              onClick={() => setOpen((value) => !value)}
            >
              Details
            </Button>
          ) : null}
        </div>
        {detail && open ? (
          <div id={detailId} className="flex w-full max-w-(--container-content) flex-col items-start gap-2">
            <pre className="max-h-(--detail-max-height) w-full overflow-auto rounded-md border border-border-subtle bg-surface-sunken p-3 font-mono text-12 whitespace-pre-wrap text-text-secondary">
              {detail}
            </pre>
            <Button size="sm" variant="ghost" icon={copied ? Check : Copy} onClick={() => void copy()}>
              {copied ? 'Copied' : 'Copy details'}
            </Button>
          </div>
        ) : null}
      </div>
    </div>
  );
}
