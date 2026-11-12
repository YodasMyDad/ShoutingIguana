import clsx from 'clsx';
import type { HTMLAttributes, ReactNode, Ref } from 'react';

/** The fewest rows a screen's own grid keeps room for (`GridCore`'s `minRows`), however short the window. */
export const GRID_SCREEN_MIN_ROWS = 5;

/**
 * The frame of a screen that is one grid under its toolbars (URLs, Links): a header, presets, a filter bar, banners,
 * then the grid (`GridScreenBody`). The grid takes the height the toolbars leave and never less than its own least
 * (`minRows`); the toolbars keep their full height. In a window too short for both (200% zoom of 1280x720 is 640x360,
 * where four rows of toolbars left the Links grid 22 px), the screen is taller than the window and the main region
 * scrolls, rather than the grid being squeezed to no rows and its banners overprinting.
 */
export function GridScreen({
  ref,
  className,
  children,
}: {
  ref?: Ref<HTMLDivElement> | undefined;
  className?: string | undefined;
  children: ReactNode;
}) {
  return (
    <div ref={ref} className={clsx('si-grid-screen', className)}>
      {children}
    </div>
  );
}

/** The grid's place in a `GridScreen`: what's left under the toolbars, at least the grid's own least height. */
export function GridScreenBody({
  ref,
  className,
  children,
  ...rest
}: HTMLAttributes<HTMLDivElement> & { ref?: Ref<HTMLDivElement> | undefined }) {
  return (
    <div ref={ref} className={clsx('si-grid-screen__body', className)} {...rest}>
      {children}
    </div>
  );
}
