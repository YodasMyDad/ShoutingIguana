import { Slot, Slottable } from '@radix-ui/react-slot';
import clsx from 'clsx';
import type { ButtonHTMLAttributes, MouseEvent, Ref } from 'react';
import { useDelayedLoading } from '../feedback/useDelayedLoading';
import { useCommandKeys } from '../keyboard/registry';
import { IconSlot, type IconLike, type IconSize } from './Icon';
import { Kbd } from './Kbd';
import { Spinner } from './Spinner';
import { Tooltip } from './Tooltip';

type Variant = 'primary' | 'secondary' | 'ghost' | 'danger';

/** Work shorter than this shows no spinner: the button just responds (docs/v2-ui-brief.md, Feedback and motion). */
export const BUTTON_SPINNER_DELAY_MS = 300;

const ICON_SIZES: Record<NonNullable<ButtonProps['size']>, IconSize> = { sm: 'sm', md: 'md', lg: 'md', xl: 'lg' };

export interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  ref?: Ref<HTMLButtonElement> | undefined;
  /** `primary` for the one main action in view; `danger` only for an action that loses data ("Delete crawl file"). */
  variant?: Variant | undefined;
  /** 28, 32 (the default), 40 and 52 px: `xl` sits beside the URL field on Home. */
  size?: 'sm' | 'md' | 'lg' | 'xl' | undefined;
  /** The command this button runs: its shortcut joins the tooltip ("Pause" with F7) and `aria-keyshortcuts`. */
  command?: string | undefined;
  /** Before the label: a Lucide icon, drawn at the button's size, or an element. */
  icon?: IconLike | undefined;
  /** After the label: a menu's chevron. */
  iconEnd?: IconLike | undefined;
  /** Shows the command's keys inside the button too ("Pause F7"), where the keyboard is the point. */
  showShortcut?: boolean | undefined;
  /**
   * Work under way. The button keeps its place and its width, ignores more presses, and after 300 ms shows a
   * spinner inside itself: in place of its icon, or over its label.
   */
  loading?: boolean | undefined;
  /** A toggle: on or off, read as pressed. */
  pressed?: boolean | undefined;
  /** Renders its one child (a router `Link`) with the button's look and behaviour. */
  asChild?: boolean | undefined;
}

/**
 * The one button. It responds on press; a spinner inside it, never a full-screen one, is for work over 300 ms. Its
 * tooltip is its `title`, or its label with the command's shortcut, shown on hover and on keyboard focus.
 */
export function Button({
  variant = 'secondary',
  size = 'md',
  command,
  icon,
  iconEnd,
  showShortcut = false,
  loading = false,
  pressed,
  asChild = false,
  title,
  className,
  children,
  type,
  onClick,
  ...rest
}: ButtonProps) {
  // One lookup, and none without a command: grids put buttons in their cells.
  const { shortcut, chord, ariaShortcut } = useCommandKeys(command);
  // After 300 ms of work, and gone the moment it ends: a skeleton's minimum time would cover the label of a button that
  // already takes presses again.
  const spinner = useDelayedLoading(loading, { delayMs: BUTTON_SPINNER_DELAY_MS, minMs: 0 }) && loading;
  const label = title ?? (typeof children === 'string' ? children : undefined);
  // What a screen reader calls it: its aria-label, or its words. The tooltip leaves that out of the description.
  const name = rest['aria-label'] ?? (typeof children === 'string' ? children : undefined);
  // A tooltip when it adds something: a title, or the shortcut beside the label.
  const tip = title ?? (shortcut ? label : undefined);
  const iconSize = ICON_SIZES[size];

  const press = (event: MouseEvent<HTMLButtonElement>) => {
    if (loading) {
      event.preventDefault();
      return;
    }
    onClick?.(event);
  };

  const common = {
    'aria-keyshortcuts': ariaShortcut ?? undefined,
    'aria-pressed': pressed,
    'aria-busy': loading || undefined,
    ...(loading ? { 'aria-disabled': true, 'data-loading': '' } : {}),
    className: clsx('si-btn', `si-btn--${variant}`, size !== 'md' && `si-btn--${size}`, className),
    onClick: press,
    ...rest,
  };
  // The slot's children are its own (no fragment round them), so it finds the child to render as the button.
  const button = asChild ? (
    <Slot {...common}>
      <IconSlot icon={icon} size={iconSize} />
      <Slottable>{children}</Slottable>
      <IconSlot icon={iconEnd} size={iconSize} />
    </Slot>
  ) : (
    <button
      {...common}
      type={type === 'submit' ? 'submit' : type === 'reset' ? 'reset' : 'button'}
      data-spinner={spinner && !icon ? 'cover' : undefined}
    >
      <span className="si-btn__content">
        {spinner && icon ? <Spinner size={iconSize} /> : <IconSlot icon={icon} size={iconSize} />}
        {children}
        {/* Shown, not read: aria-keyshortcuts gives the keys, and the name stays the words ("Pause", not "Pause F7"). */}
        {showShortcut && chord ? <Kbd chord={chord} size="sm" aria-hidden /> : null}
        <IconSlot icon={iconEnd} size={iconSize} />
      </span>
      {spinner && !icon ? (
        <span className="si-btn__spinner">
          <Spinner size={iconSize} />
        </span>
      ) : null}
    </button>
  );
  return (
    <Tooltip content={tip} chord={chord ?? undefined} triggerName={name}>
      {button}
    </Tooltip>
  );
}
