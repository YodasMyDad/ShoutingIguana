import clsx from 'clsx';
import { Info } from 'lucide-react';
import type { ReactNode } from 'react';
import { IconSlot, type IconLike } from './Icon';
import { SeverityMark } from './SeverityMark';

export interface BannerProps {
  /**
   * `info`: the view is out of date ("This crawl changed after its last analysis") or something is on its way.
   * `warning`: the app is working round a problem (the engine restarting). `error`: it can't.
   */
  tone?: 'info' | 'warning' | 'error' | undefined;
  children: ReactNode;
  /** A second, quieter line: the cause in the engine's words. */
  detail?: ReactNode;
  /** What to do: "Analyse again", "Try again". */
  actions?: ReactNode;
  icon?: IconLike;
  /** Inside a panel, as a rounded strip, rather than across the window under the top bar. */
  inset?: boolean | undefined;
  /** `alert` for an error that stops the app; `status` (the default) otherwise. */
  role?: 'status' | 'alert' | undefined;
  className?: string | undefined;
}

/**
 * A strip across a view (or, `inset`, inside a panel) for a state of the whole view: out of date, the engine lost,
 * reconnecting. It says why in plain words and offers the one action that helps. Warnings and errors carry the
 * severity's shape, so the strip isn't told by colour alone.
 */
export function Banner({
  tone = 'info',
  children,
  detail,
  actions,
  icon,
  inset = false,
  role,
  className,
}: BannerProps) {
  return (
    <div
      role={role ?? (tone === 'error' ? 'alert' : 'status')}
      className={clsx('si-banner', `si-banner--${tone}`, inset && 'si-banner--inset', className)}
    >
      <span className="si-banner__icon">
        {icon ? (
          <IconSlot icon={icon} size="md" />
        ) : tone === 'info' ? (
          <IconSlot icon={Info} size="md" />
        ) : (
          <SeverityMark severity={tone} size="md" decorative />
        )}
      </span>
      <div className="si-banner__body">
        <div>{children}</div>
        {detail ? <div className="si-banner__detail">{detail}</div> : null}
      </div>
      {actions ? <div className="si-banner__actions">{actions}</div> : null}
    </div>
  );
}
