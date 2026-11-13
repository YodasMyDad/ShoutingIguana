import clsx from 'clsx';
import { SeverityBadge } from './SeverityBadge';
import type { IconSize } from './Icon';
import { SeverityMark } from './SeverityMark';
import { plainOutcomeWord, type CheckOutcome } from './severity';

export interface SeverityWordProps {
  severity: CheckOutcome;
  /**
   * `badge`, on its tint (a group's or a problem's head); `large`, the badge at a head's size (one problem); `inline`,
   * the mark and the word in its colour (a line of facts); `mark`, the mark alone with the word as its name (a dense
   * row).
   */
  variant?: 'badge' | 'large' | 'inline' | 'mark' | undefined;
  /** The mark's size, for `mark`: 20 px by default, a problem's row. */
  size?: IconSize | undefined;
  className?: string | undefined;
}

/**
 * Severity in Simple's words (docs/v2-ui-simple.md, "Simple's words"): Serious, Worth fixing and Worth a look, with
 * Error's, Warning's and Notice's shapes and colours, one to one; Couldn't check and Nothing wrong for a skipped or a
 * passed check. Advanced keeps `SeverityBadge` and its own words.
 */
export function SeverityWord({ severity, variant = 'badge', size = 'lg', className }: SeverityWordProps) {
  const word = plainOutcomeWord(severity);
  if (variant === 'mark') {
    return <SeverityMark severity={severity} size={size} label={word} className={className} />;
  }
  return (
    <SeverityBadge
      severity={severity}
      variant={variant === 'inline' ? 'inline' : 'badge'}
      className={clsx(variant === 'large' && 'si-sev-badge--lg', className)}
    >
      {word}
    </SeverityBadge>
  );
}
