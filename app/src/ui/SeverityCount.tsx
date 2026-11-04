import clsx from 'clsx';
import { formatCount } from '../lib/format';
import { SeverityMark } from './SeverityMark';
import { SEVERITIES, severityNoun, type Severity } from './severity';

export interface SeverityCountProps {
  severity: Severity;
  count: number;
  /**
   * Dense places (the sidebar, a check's row, the Issues header) show the mark and the number; the word is still
   * there for screen readers.
   */
  compact?: boolean | undefined;
  className?: string | undefined;
}

/** "▲ 6 warnings": a count of one severity. A count of 0 is muted, and its mark fades, so the others stand out. */
export function SeverityCount({ severity, count, compact = false, className }: SeverityCountProps) {
  const noun = severityNoun(severity, count);
  return (
    <span className={clsx('si-sevcount', count === 0 && 'si-sevcount--zero', className)}>
      <SeverityMark severity={severity} decorative />
      {formatCount(count)}{' '}
      {compact ? <span className="sr-only">{noun}</span> : <span className="si-sevcount__word">{noun}</span>}
    </span>
  );
}

export interface SeverityCountsProps {
  counts: Readonly<Record<Severity, number>>;
  compact?: boolean | undefined;
  /** Leave out the severities with nothing to count. */
  hideZero?: boolean | undefined;
  className?: string | undefined;
}

/** Errors, warnings and notices, in that order: the library's "Checks with issues", the sidebar's counts. */
export function SeverityCounts({ counts, compact = false, hideZero = false, className }: SeverityCountsProps) {
  return (
    <span className={clsx('si-sevcounts', className)}>
      {SEVERITIES.filter((severity) => !hideZero || counts[severity] > 0).map((severity) => (
        <SeverityCount key={severity} severity={severity} count={counts[severity]} compact={compact} />
      ))}
    </span>
  );
}
