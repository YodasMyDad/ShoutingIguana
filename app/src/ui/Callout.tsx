import clsx from 'clsx';
import { Info, Layers } from 'lucide-react';
import type { ReactNode } from 'react';
import { IconSlot, type IconLike } from './Icon';
import { SeverityMark } from './SeverityMark';

export type CalloutTone = 'insight' | 'info' | 'warning' | 'error';

export interface CalloutProps {
  /**
   * `insight` (the default): what the data says, quietly ("one fix clears the group"). `info`: something to know.
   * `warning` and `error`: a problem with what's shown, with the severity's shape.
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

function ToneIcon({ tone }: { tone: CalloutTone }) {
  if (tone === 'warning' || tone === 'error') return <SeverityMark severity={tone} size="md" decorative />;
  return <IconSlot icon={tone === 'insight' ? Layers : Info} size="md" />;
}

/** A note in a panel: an insight from the data, information, or a problem with what's shown. */
export function Callout({ tone = 'insight', title, children, icon, actions, role, className }: CalloutProps) {
  return (
    <div role={role} className={clsx('si-callout', tone !== 'insight' && `si-callout--${tone}`, className)}>
      <span className="si-callout__icon">{icon ? <IconSlot icon={icon} size="md" /> : <ToneIcon tone={tone} />}</span>
      <div className="si-callout__body">
        {title ? <p className="si-callout__title">{title}</p> : null}
        <div>{children}</div>
      </div>
      {actions ? <div className="si-callout__actions">{actions}</div> : null}
    </div>
  );
}
