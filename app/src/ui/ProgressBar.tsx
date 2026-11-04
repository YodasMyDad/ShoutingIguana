import clsx from 'clsx';
import { formatPercent } from '../lib/format';

export interface ProgressBarProps {
  /** The phase in words: "Analysing: links". Its name for screen readers too. */
  label: string;
  /** Done so far, of `max`. Leave it out while there's nothing to measure: the bar then moves on its own. */
  value?: number | null | undefined;
  max?: number | undefined;
  /** What's shown on the right instead of the percentage: "2,388 of 6,140 URLs". */
  valueText?: string | undefined;
  /** A 4 px bar without the label row: a running crawl in the sidebar. */
  thin?: boolean | undefined;
  className?: string | undefined;
}

/** Real progress, with its phase named: analysis, export, a crawl against its limit. */
export function ProgressBar({ label, value, max = 100, valueText, thin = false, className }: ProgressBarProps) {
  const known = value !== null && value !== undefined;
  const share = known && max > 0 ? Math.min(1, Math.max(0, value / max)) : 0;
  const text = valueText ?? (known ? formatPercent(value, max) : undefined);
  return (
    <div className={clsx('si-progress', className)}>
      {thin ? null : (
        <div className="si-progress__head" aria-hidden="true">
          <span>{label}</span>
          {text ? <span className="si-progress__value">{text}</span> : null}
        </div>
      )}
      <div
        role="progressbar"
        aria-label={label}
        aria-valuemin={known ? 0 : undefined}
        aria-valuemax={known ? max : undefined}
        aria-valuenow={known ? value : undefined}
        aria-valuetext={known ? text : undefined}
        className={clsx('si-bar-track', thin && 'si-bar-track--thin')}
      >
        <span
          className={clsx('si-bar-fill', known ? share > 0 && 'si-bar-fill--some' : 'si-bar-fill--indeterminate')}
          style={known ? { width: `${share * 100}%` } : undefined}
        />
      </div>
    </div>
  );
}

/**
 * Refreshing: the thin line at the top of a panel while what's shown stays (the brief's "Refreshing" state). Put it in
 * a positioned panel. Its parent should say `aria-busy`.
 */
export function RefreshLine({ label = 'Refreshing' }: { label?: string | undefined }) {
  return <div role="progressbar" aria-label={label} className="si-progress-line" />;
}
