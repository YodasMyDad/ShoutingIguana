import clsx from 'clsx';
import type { Check } from '../../api/types';

export interface HowToFixProps {
  /** The check: its fix in words. */
  check: Pick<Check, 'fix'>;
  className?: string | undefined;
}

/**
 * A check's "How to fix it": the catalog's words, in the head with what's wrong. A check with nothing to say shows
 * nothing.
 */
export function HowToFix({ check, className }: HowToFixProps) {
  if (check.fix.trim() === '') return null;
  return (
    <div className={clsx('si-how-to-fix', className)}>
      <h3 className="mb-1.5 text-13 font-semibold">How to fix it</h3>
      <p className="max-w-(--measure-text) text-14 leading-body text-text-secondary">{check.fix}</p>
    </div>
  );
}
