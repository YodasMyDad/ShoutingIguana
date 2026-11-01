import clsx from 'clsx';
import type { ButtonHTMLAttributes, ReactNode } from 'react';
import { useAriaShortcut, useShortcut } from '../keyboard/registry';

type Variant = 'primary' | 'secondary' | 'ghost';

const VARIANTS: Record<Variant, string> = {
  primary: 'bg-accent-fill text-text-on-accent hover:bg-accent-fill-hover shadow-control',
  secondary: 'border border-border bg-surface text-text hover:bg-surface-hover shadow-control',
  ghost: 'text-text-secondary hover:bg-surface-hover hover:text-text',
};

export interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: Variant;
  size?: 'sm' | 'md';
  /** The command this button runs: its shortcut joins the tooltip ("Pause (F7)"). */
  command?: string;
  icon?: ReactNode;
}

/**
 * The one button. It responds on press; a spinner inside it, never a full-screen one, is for work over 300 ms.
 * Placeholder look: the design-system pass gives it every state's final style.
 */
export function Button({
  variant = 'secondary',
  size = 'md',
  command,
  icon,
  title,
  className,
  children,
  type,
  ...rest
}: ButtonProps) {
  const shortcut = useShortcut(command ?? '');
  const ariaShortcut = useAriaShortcut(command ?? '');
  const label = title ?? (typeof children === 'string' ? children : undefined);
  return (
    <button
      type={type === 'submit' ? 'submit' : 'button'}
      title={label && shortcut ? `${label} (${shortcut})` : label}
      aria-keyshortcuts={ariaShortcut ?? undefined}
      className={clsx(
        'inline-flex items-center justify-center gap-1.5 rounded-sm font-medium whitespace-nowrap transition-colors duration-(--duration-instant) ease-out',
        'disabled:cursor-not-allowed disabled:opacity-50',
        size === 'sm' ? 'h-(--control-height-sm) px-2.5 text-12' : 'h-(--control-height) px-3 text-13',
        VARIANTS[variant],
        className,
      )}
      {...rest}
    >
      {icon}
      {children}
    </button>
  );
}
