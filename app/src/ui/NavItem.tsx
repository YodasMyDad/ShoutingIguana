import { createLink } from '@tanstack/react-router';
import clsx from 'clsx';
import type { AnchorHTMLAttributes, ReactNode, Ref } from 'react';
import { formatCount } from '../lib/format';
import { IconSlot, type IconLike } from './Icon';
import { Tooltip } from './Tooltip';

type NavAnchorProps = Omit<AnchorHTMLAttributes<HTMLAnchorElement>, 'children'> & {
  ref?: Ref<HTMLAnchorElement> | undefined;
  label: string;
  icon?: IconLike;
  /** A count after the label: Issues 19, URLs 4,521. Null shows nothing. */
  count?: number | null | undefined;
  /** Anything after the label instead of a count: a status dot, severity counts. */
  end?: ReactNode;
  /** The sidebar collapsed to its icons: the label is the accessible name and the tooltip. */
  collapsed?: boolean | undefined;
  /** The command that goes here (Ctrl+1 to Ctrl+7): its keys join the tooltip. */
  command?: string | undefined;
  /**
   * Draws the current place's look while it's the page shown (the default). False for a second way to a place that
   * has its own item, as a running crawl's link to its Overview: one item looks current, and the link stays
   * `aria-current`.
   */
  markCurrent?: boolean | undefined;
};

function NavAnchor({
  ref,
  label,
  icon,
  count,
  end,
  collapsed = false,
  command,
  markCurrent = true,
  className,
  ...rest
}: NavAnchorProps) {
  // The router marks the page shown with aria-current="page": that's the current place, drawn with `.si-current`.
  const current = markCurrent && rest['aria-current'] === 'page';
  const anchor = (
    <a
      ref={ref}
      {...rest}
      className={clsx('si-nav-item', current && 'si-current', collapsed && 'si-nav-item--collapsed', className)}
    >
      {icon ? (
        <span className="si-nav-item__icon">
          <IconSlot icon={icon} size="md" />
        </span>
      ) : null}
      <span className={collapsed ? 'sr-only' : 'si-nav-item__label'}>{label}</span>
      {collapsed ? null : end}
      {collapsed || end || count === null || count === undefined ? null : (
        <span className="si-nav-item__count">{formatCount(count)}</span>
      )}
    </a>
  );
  // Collapsed, the tooltip is the label; open, only the keys add anything, and they're worth a tooltip too.
  return (
    <Tooltip
      content={collapsed || command ? label : undefined}
      command={command}
      side={collapsed ? 'right' : 'bottom'}
      triggerName={label}
    >
      {anchor}
    </Tooltip>
  );
}

/**
 * A place in a list of places (the sidebar, Settings' sections): an icon, a label and a count. The current one has
 * the tint, its text in the accent and the accent rule along its edge (`.si-current`), never the tint alone.
 */
export const NavItem = createLink(NavAnchor);
