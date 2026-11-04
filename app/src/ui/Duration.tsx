import clsx from 'clsx';
import { formatDuration, formatMilliseconds } from '../lib/format';
import { FigureParts } from './Num';

/**
 * A duration as the brief reads it, "1 min 12 s", its units smaller and muted. `precise` keeps milliseconds under a
 * second ("312 ms"), for response times. A missing one is an en dash.
 */
export function Duration({
  ms,
  precise = false,
  className,
}: {
  ms: number | null | undefined;
  precise?: boolean | undefined;
  className?: string | undefined;
}) {
  return (
    <span className={clsx('figures', className)}>
      <FigureParts text={precise ? formatMilliseconds(ms) : formatDuration(ms)} />
    </span>
  );
}
