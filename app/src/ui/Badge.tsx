import clsx from 'clsx';
import type { ReactNode } from 'react';
import { IconSlot, type IconLike } from './Icon';

export interface BadgeProps {
  /**
   * `neutral` for a fact ("Links"), `accent` for something live or new, `outline` for a quiet label, `dashed` for
   * what didn't happen ("Skipped").
   */
  variant?: 'neutral' | 'accent' | 'outline' | 'dashed' | undefined;
  icon?: IconLike | undefined;
  children: ReactNode;
  className?: string | undefined;
}

/** A small label beside a title or in a row. Severity has its own badge (`SeverityBadge`); this one has no colour. */
export function Badge({ variant = 'neutral', icon, children, className }: BadgeProps) {
  return (
    <span className={clsx('si-badge', variant !== 'neutral' && `si-badge--${variant}`, className)}>
      <IconSlot icon={icon} size="xs" />
      {children}
    </span>
  );
}
