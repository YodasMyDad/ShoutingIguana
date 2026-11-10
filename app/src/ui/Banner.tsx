import clsx from 'clsx';
import { Info, MessageSquareWarning, MessageSquareX } from 'lucide-react';
import type { ReactNode } from 'react';
import { IconSlot, type IconLike } from './Icon';

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
  /**
   * `alert` for an error that stops the app; `status` (the default) otherwise. `none` for a banner that states what
   * something else has already announced (a crawl's status, which the live bridge announces as it changes, with a
   * toast for some): it isn't a live region then, so the change isn't read out two or three times over.
   */
  role?: 'status' | 'alert' | 'none' | undefined;
  className?: string | undefined;
}

/**
 * A strip across a view (or, `inset`, inside a panel) for a state of the whole view: out of date, the engine lost,
 * reconnecting. It says why in plain words and offers the one action that helps. It's the app's message, never a
 * finding: warnings and errors take the message tones and Lucide's message icons, not severity's colours and shapes,
 * and the words say what happened, so the strip isn't told by colour alone.
 */
const TONE_ICONS = { info: Info, warning: MessageSquareWarning, error: MessageSquareX } as const;

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
      role={role === 'none' ? undefined : (role ?? (tone === 'error' ? 'alert' : 'status'))}
      className={clsx('si-banner', `si-banner--${tone}`, inset && 'si-banner--inset', className)}
    >
      <span className="si-banner__icon">
        <IconSlot icon={icon ?? TONE_ICONS[tone]} size="md" />
      </span>
      <div className="si-banner__body">
        <div>{children}</div>
        {detail ? <div className="si-banner__detail">{detail}</div> : null}
      </div>
      {actions ? <div className="si-banner__actions">{actions}</div> : null}
    </div>
  );
}
