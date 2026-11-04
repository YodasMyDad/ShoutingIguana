import clsx from 'clsx';
import type { ReactNode } from 'react';

/**
 * Facts as labels and values (a `dl`): the inspector's summary, Settings' About. Labels are muted and in a column of
 * their own; `auto` sizes that column to the longest label.
 */
export function Facts({
  children,
  auto = false,
  className,
}: {
  children: ReactNode;
  auto?: boolean | undefined;
  className?: string | undefined;
}) {
  return <dl className={clsx('si-facts', auto && 'si-facts--auto', className)}>{children}</dl>;
}

export function Fact({
  label,
  children,
  className,
}: {
  label: ReactNode;
  children: ReactNode;
  className?: string | undefined;
}) {
  return (
    <>
      <dt className="si-facts__label">{label}</dt>
      <dd className={clsx('si-facts__value', className)}>{children}</dd>
    </>
  );
}
