import clsx from 'clsx';
import { Info, Layers, MessageSquareWarning, MessageSquareX } from 'lucide-react';
import type { ReactNode } from 'react';
import { IconSlot, type IconLike } from './Icon';

export type CalloutTone = 'insight' | 'info' | 'warning' | 'error';

export interface CalloutProps {
  /**
   * `insight` (the default): what the data says, quietly ("one fix clears the group"). `info`: something to know.
   * `warning` and `error`: a problem with what's shown (checks skipped, robots.txt unread), in the message tones and
   * Lucide's message icons: the app's message, never a finding's severity.
   */
  tone?: CalloutTone | undefined;
  title?: ReactNode;
  children: ReactNode;
  /** In place of the tone's icon. */
  icon?: IconLike;
  /** What to do about it, on the right. */
  actions?: ReactNode;
  /** `status` for news that arrives while the view is open, so screen readers hear it. */
  role?: 'status' | 'alert' | 'note' | undefined;
  className?: string | undefined;
}

const TONE_ICONS = { insight: Layers, info: Info, warning: MessageSquareWarning, error: MessageSquareX } as const;

/** A note in a panel: an insight from the data, information, or a problem with what's shown. */
export function Callout({ tone = 'insight', title, children, icon, actions, role, className }: CalloutProps) {
  return (
    <div role={role} className={clsx('si-callout', tone !== 'insight' && `si-callout--${tone}`, className)}>
      <span className="si-callout__icon">
        <IconSlot icon={icon ?? TONE_ICONS[tone]} size="md" />
      </span>
      <div className="si-callout__body">
        {title ? <p className="si-callout__title">{title}</p> : null}
        <div>{children}</div>
      </div>
      {actions ? <div className="si-callout__actions">{actions}</div> : null}
    </div>
  );
}
