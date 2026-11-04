import clsx from 'clsx';

/** The live dot: the accent, pulsing while motion is allowed. Decorative: what's beside it says what's live. */
export function LiveDot({ pulse = true, className }: { pulse?: boolean; className?: string }) {
  return <span aria-hidden="true" className={clsx('si-live-dot', pulse && 'si-live-dot--pulse', className)} />;
}

/** "Live": beside a title whose figures update in place while a crawl runs. */
export function LiveBadge({ children = 'Live', className }: { children?: string; className?: string }) {
  return (
    <span className={clsx('si-live-badge', className)}>
      <LiveDot />
      {children}
    </span>
  );
}
