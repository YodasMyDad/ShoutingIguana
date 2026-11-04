import { useMinuteClock } from '../lib/clock';
import { formatExactTime, formatRelativeTime } from '../lib/format';
import { Tooltip } from './Tooltip';

/**
 * A time as "3 minutes ago", kept up to date by the app's minute clock, with the exact time ("1 Nov 2026, 10:15") in
 * its tooltip and its `dateTime`, and read after it by screen readers, as the tooltip can't be reached by the keyboard.
 * `now` is for stories and tests; the app's clock otherwise.
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
  const clock = useMinuteClock();
  const relative = formatRelativeTime(iso, now ?? clock);
  if (!iso || relative === '–') return <span className={className}>–</span>;
  const exact = formatExactTime(iso);
  return (
    <Tooltip content={exact} triggerName={`${relative}, ${exact}`}>
      <time dateTime={iso} className={className}>
        {relative}
        <span className="sr-only">, {exact}</span>
      </time>
    </Tooltip>
  );
}
