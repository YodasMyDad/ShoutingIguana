import { Slot, Slottable } from '@radix-ui/react-slot';
import clsx from 'clsx';
import { ChevronRight, Layers } from 'lucide-react';
import type { HTMLAttributes, ReactNode, Ref } from 'react';
import { Icon } from './Icon';
import { SeverityWord } from './SeverityWord';
import type { CheckOutcome } from './severity';

export interface PlainCheckRowProps extends Omit<HTMLAttributes<HTMLElement>, 'title'> {
  ref?: Ref<HTMLElement> | undefined;
  /** What the check came to: its severity, or skipped or passed. The mark is named in Simple's word. */
  outcome: CheckOutcome;
  /** The check's plain title. */
  title: ReactNode;
  /**
   * The line under the title: the first sentence of what's wrong, a skipped check's plain reason, or a passed check's
   * good news. Kept to one line, the whole of it in the row's name.
   */
  summary?: ReactNode;
  /** On the right: the count and its share ("9 of 4,521 addresses"), or "Not checked". */
  count?: ReactNode;
  /** Its place in "Fix these first": 1 to 5. */
  number?: number | undefined;
  /** One cause covers most of what it flagged: the "One fix" tag beside the title. */
  oneFix?: boolean | undefined;
  /** A thing worth a look, a skipped or a passed check: a lighter title. */
  quiet?: boolean | undefined;
  /**
   * Renders its child (a router `Link`) with the row's look, a chevron at its end: the row opens the problem. Without
   * it the row is a plain `div`.
   */
  asChild?: boolean | undefined;
  children?: ReactNode;
}

/**
 * A check in Simple (docs/v2-ui-simple.md, "What to fix" and "The Summary"): its mark, its plain title with at most
 * one more label ("One fix"), the first sentence of what's wrong, and its count and share. The Summary's "Fix these
 * first", What to fix and its folded lists are made of these. The spaces between the parts aren't drawn (the row is a
 * grid) but keep the words apart in its accessible name.
 */
export function PlainCheckRow({
  ref,
  outcome,
  title,
  summary,
  count,
  number,
  oneFix = false,
  quiet = false,
  asChild = false,
  className,
  children,
  ...rest
}: PlainCheckRowProps) {
  const content = (
    <>
      {number !== undefined ? <span className="si-plain-row__number">{number}</span> : null}{' '}
      <span className="si-plain-row__mark">
        <SeverityWord severity={outcome} variant="mark" />
      </span>{' '}
      <span className="si-plain-row__text">
        <span className="si-plain-row__title">
          <span className="si-plain-row__title-words">{title}</span>
          {oneFix ? (
            <>
              {' '}
              <OneFixTag />
            </>
          ) : null}
        </span>{' '}
        {summary ? <span className="si-plain-row__summary">{summary}</span> : null}
      </span>{' '}
      {count !== undefined && count !== null ? <span className="si-plain-row__count">{count}</span> : <span />}
      {asChild ? <Icon icon={ChevronRight} size="md" className="si-plain-row__chevron" /> : <span />}
    </>
  );
  const classes = clsx(
    'si-plain-row',
    number !== undefined && 'si-plain-row--numbered',
    quiet && 'si-plain-row--quiet',
    className,
  );
  if (asChild) {
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

/**
 * "One fix": one cause, usually in the part of the site every page shares (the engine's `likelyTemplate`), covers most
 * of what a check flagged, so one change clears it.
 */
export function OneFixTag({ className }: { className?: string | undefined }) {
  return (
    <span className={clsx('si-onefix-tag', className)}>
      <Icon icon={Layers} size="xs" />
      One fix
    </span>
  );
}

/** A list of `PlainCheckRow`s on one card, divided by hairlines: a `ul` whose items are the rows. */
export function PlainCheckList({
  label,
  className,
  children,
}: {
  /** The list's name, when its heading doesn't name it (`aria-labelledby` on the heading is better). */
  label?: string | undefined;
  className?: string | undefined;
  children: ReactNode;
}) {
  return (
    <ul className={clsx('si-plain-list', className)} aria-label={label}>
      {children}
    </ul>
  );
}
