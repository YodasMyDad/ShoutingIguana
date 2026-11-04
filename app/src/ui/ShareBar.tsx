import clsx from 'clsx';
import { formatPercent } from '../lib/format';

/**
 * A part's share of a whole, as a bar and its percentage: "99.6%" of a check's issues in one group. A share above
 * nothing keeps a sliver of bar, and its words never read 0%. The percentage is the text; the bar draws it.
 */
export function ShareBar({
  value,
  total,
  label,
  className,
}: {
  value: number;
  total: number;
  /** Read before the percentage: "Share of issues". */
  label?: string | undefined;
  className?: string | undefined;
}) {
  const share = total > 0 ? Math.min(1, Math.max(0, value / total)) : 0;
  const text = formatPercent(value, total);
  return (
    <span className={clsx('si-share', className)}>
      <span className="si-bar-track si-bar-track--meter" aria-hidden="true">
        <span className={clsx('si-bar-fill', share > 0 && 'si-bar-fill--some')} style={{ width: `${share * 100}%` }} />
      </span>
      <span className="si-share__pct">
        {label ? <span className="sr-only">{label}: </span> : null}
        {text}
      </span>
    </span>
  );
}
