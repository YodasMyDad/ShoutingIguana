import clsx from 'clsx';
import { SeverityMark } from './SeverityMark';
import { outcomeWord, type CheckOutcome } from './severity';

export interface SeverityBadgeProps {
  severity: CheckOutcome;
  /** `badge`, on its tint (a selected check's header); `inline`, the mark and the word in its colour (lists, facts). */
  variant?: 'badge' | 'inline' | undefined;
  /** Another word than the severity's own: "Skipped: no sitemap". The mark keeps the severity. */
  children?: string | undefined;
  className?: string | undefined;
}

/** Severity as shape, colour and word, wherever there's room for the word. */
export function SeverityBadge({ severity, variant = 'badge', children, className }: SeverityBadgeProps) {
  return (
    <span
      className={clsx(
        variant === 'badge' ? ['si-sev-badge', `si-sev-badge--${severity}`] : ['si-sev', `si-sev--${severity}`],
        className,
      )}
    >
      <SeverityMark severity={severity} size={variant === 'badge' ? 'sm' : 'md'} decorative />
      {children ?? outcomeWord(severity)}
    </span>
  );
}
