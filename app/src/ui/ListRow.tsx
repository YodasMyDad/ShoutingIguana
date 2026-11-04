import { Slot, Slottable } from '@radix-ui/react-slot';
import clsx from 'clsx';
import type { HTMLAttributes, ReactNode, Ref } from 'react';
import { IconSlot, type IconLike } from './Icon';

export interface ListRowProps extends Omit<HTMLAttributes<HTMLElement>, 'title'> {
  ref?: Ref<HTMLElement> | undefined;
  /** A severity mark, a status mark or an icon, in the first column. */
  icon?: IconLike;
  title: ReactNode;
  /** On the right: a count, a badge ("Skipped"). */
  meta?: ReactNode;
  /** A second line under the title: a skipped check's reason. */
  description?: ReactNode;
  /** Quieter title: a skipped or passed check. */
  muted?: boolean | undefined;
  /**
   * Renders its child (a `button`, a router `Link`, an element with `role="option"`) with the row's look. The child
   * says what's selected: `aria-selected`, or `aria-current` for a link to the place shown. Without it the row is a
   * plain `div`.
   */
  asChild?: boolean | undefined;
  children?: ReactNode;
}

/**
 * A row in a list (the checks list, the inspector's issues, the sitemaps): icon, title, a count at the end, and a
 * line of description. Selected is the tint with the accent rule along its edge (3:1), and a heavier title.
 */
export function ListRow({
  ref,
  icon,
  title,
  meta,
  description,
  muted = false,
  asChild = false,
  className,
  children,
  ...rest
}: ListRowProps) {
  const content = (
    <>
      {icon ? (
        <span className="si-list-row__icon">
          <IconSlot icon={icon} size="md" />
        </span>
      ) : null}
      <span className="si-list-row__title">{title}</span>
      {meta !== undefined && meta !== null ? <span className="si-list-row__meta">{meta}</span> : <span />}
      {description ? <span className="si-list-row__description">{description}</span> : null}
    </>
  );
  const classes = clsx('si-list-row', !icon && 'si-list-row--plain', muted && 'si-list-row--muted', className);
  if (asChild) {
    // The child keeps its element and props; the row's content goes inside it.
    return (
      <Slot ref={ref} {...rest} className={classes}>
        <Slottable>{children}</Slottable>
        {content}
      </Slot>
    );
  }
  return (
    <div ref={ref as Ref<HTMLDivElement>} {...rest} className={classes}>
      {content}
    </div>
  );
}

/** A heading over a group of rows: "Response codes" with "4 passed" on the right. */
export function ListHeading({
  children,
  meta,
  id,
  level = 3,
}: {
  children: ReactNode;
  meta?: ReactNode;
  id?: string | undefined;
  level?: 2 | 3 | 4 | undefined;
}) {
  const Heading = level === 2 ? 'h2' : level === 4 ? 'h4' : 'h3';
  return (
    <Heading id={id} className="si-list-heading">
      <span>{children}</span>
      {meta ? <span className="si-list-heading__meta">{meta}</span> : null}
    </Heading>
  );
}
