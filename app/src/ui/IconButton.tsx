import clsx from 'clsx';
import { Button, type ButtonProps } from './Button';
import type { IconLike } from './Icon';

export interface IconButtonProps extends Omit<ButtonProps, 'children' | 'icon' | 'iconEnd' | 'title' | 'size'> {
  /** The accessible name, and the tooltip: an icon alone never explains itself. */
  label: string;
  icon: IconLike;
  /** 24, 28, 32 (the default) and 40 px squares. */
  size?: 'xs' | 'sm' | 'md' | 'lg' | undefined;
  /** Another tooltip than the label, such as the state it toggles to. */
  tooltip?: string | undefined;
}

/** A square button with only an icon: ghost by default, named by `label`, which is also its tooltip. */
export function IconButton({
  label,
  icon,
  size = 'md',
  tooltip,
  variant = 'ghost',
  className,
  ...rest
}: IconButtonProps) {
  return (
    <Button
      {...rest}
      variant={variant}
      size={size === 'xs' ? 'sm' : size}
      icon={icon}
      aria-label={label}
      title={tooltip ?? label}
      className={clsx('si-btn--icon', size === 'xs' && 'si-btn--xs', className)}
    />
  );
}
