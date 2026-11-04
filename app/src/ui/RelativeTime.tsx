import { formatExactTime, formatRelativeTime } from '../lib/format';
import { Tooltip } from './Tooltip';

/**
 * A time as "3 minutes ago", with the exact time ("1 Nov 2026, 10:15") in its tooltip and its `dateTime`. `now` is for
 * stories and tests; the app's own clock otherwise.
 */
export function RelativeTime({
  iso,
  now,
  className,
}: {
  iso: string | null | undefined;
  now?: number | undefined;
  className?: string | undefined;
}) {
  const relative = formatRelativeTime(iso, now);
  if (!iso || relative === '–') return <span className={className}>–</span>;
  return (
    <Tooltip content={formatExactTime(iso)}>
      <time dateTime={iso} className={className}>
        {relative}
      </time>
    </Tooltip>
  );
}
