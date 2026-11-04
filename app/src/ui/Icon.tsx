import clsx from 'clsx';
import type { LucideIcon } from 'lucide-react';
import { isValidElement, type ReactNode } from 'react';

export type IconSize = 'xs' | 'sm' | 'md' | 'lg';

/** What a component's `icon` prop takes: a Lucide icon, drawn at the component's size, or anything already drawn. */
export type IconLike = LucideIcon | ReactNode;

const isIconComponent = (icon: IconLike): icon is LucideIcon =>
  typeof icon === 'function' ||
  (typeof icon === 'object' && icon !== null && !isValidElement(icon) && 'render' in icon);

/** Draws an `IconLike` at a size: a Lucide icon through `Icon`, anything else as it is. */
export function IconSlot({ icon, size = 'md' }: { icon: IconLike; size?: IconSize }) {
  if (icon === null || icon === undefined || icon === false) return null;
  return isIconComponent(icon) ? <Icon icon={icon} size={size} /> : <>{icon}</>;
}

export interface IconProps {
  /** A Lucide icon (https://lucide.dev), imported by name: `import { Search } from 'lucide-react'`. */
  icon: LucideIcon;
  /** The token sizes: 12, 14, 16 (the default) and 20 px. */
  size?: IconSize | undefined;
  /** An icon that means something on its own. Without one it's decorative, and hidden from screen readers. */
  label?: string | undefined;
  className?: string | undefined;
}

/**
 * The one icon wrapper: Lucide's set at the token sizes, stroked in the text colour around it. Decorative unless it
 * has a label: next to a word, the word is the name.
 */
export function Icon({ icon: Glyph, size = 'md', label, className }: IconProps) {
  return (
    <Glyph
      className={clsx('si-icon', `si-icon--${size}`, className)}
      strokeWidth={2}
      focusable="false"
      {...(label ? { role: 'img', 'aria-label': label } : { 'aria-hidden': true })}
    />
  );
}
