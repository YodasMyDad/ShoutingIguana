import clsx from 'clsx';
import type { HTMLAttributes, ReactNode } from 'react';
import { IconSlot, type IconLike } from './Icon';

export interface CardProps extends HTMLAttributes<HTMLElement> {
  /** `section` when it has a title (it's then a region named by it); `div` otherwise. */
  as?: 'section' | 'div' | 'article' | undefined;
  /** Clips its content to its corners: for a grid or a table flush to its edges. */
  flush?: boolean | undefined;
}

/** Content on a sheet: the surface, a hairline edge, the large radius and the first elevation. */
export function Card({ as: Element = 'div', flush = false, className, children, ...rest }: CardProps) {
  return (
    <Element {...rest} className={clsx('si-card', flush && 'si-card--flush', className)}>
      {children}
    </Element>
  );
}

export interface CardHeaderProps {
  title: ReactNode;
  /** Give the heading an id to name the card's region with it (`aria-labelledby`). */
  titleId?: string | undefined;
  /** Quiet words after the title: "2,713 URLs". */
  meta?: ReactNode;
  /** Buttons at the end. */
  actions?: ReactNode;
  /** The heading level in the page's outline. */
  level?: 2 | 3 | undefined;
  className?: string | undefined;
}

export function CardHeader({ title, titleId, meta, actions, level = 2, className }: CardHeaderProps) {
  const Heading = level === 3 ? 'h3' : 'h2';
  return (
    <div className={clsx('si-card__header', className)}>
      <Heading id={titleId} className="si-card__title">
        {title}
      </Heading>
      {meta ? <span className="si-card__meta">{meta}</span> : null}
      {actions ? <div className="si-card__actions">{actions}</div> : null}
    </div>
  );
}

export function CardBody({
  tight = false,
  className,
  children,
}: {
  tight?: boolean | undefined;
  className?: string | undefined;
  children: ReactNode;
}) {
  return <div className={clsx('si-card__body', tight && 'si-card__body--tight', className)}>{children}</div>;
}

/** A footnote under a rule: what the card can't show yet, or what happens next. */
export function CardFooter({
  icon,
  className,
  children,
}: {
  icon?: IconLike;
  className?: string | undefined;
  children: ReactNode;
}) {
  return (
    <div className={clsx('si-card__foot', className)}>
      <IconSlot icon={icon} size="sm" />
      <span>{children}</span>
    </div>
  );
}
