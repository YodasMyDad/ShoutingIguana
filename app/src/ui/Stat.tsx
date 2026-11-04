import { createLink } from '@tanstack/react-router';
import clsx from 'clsx';
import type { AnchorHTMLAttributes, CSSProperties, ReactNode, Ref } from 'react';

export interface StatContentProps {
  /** What's counted: "Crawled". */
  label: ReactNode;
  /** The figure: a `Num` or a `Duration`, so its unit is set smaller. Words ("Skipped") when there's no number. */
  value: ReactNode;
  /** A line that explains the number: "URLs, of a 10,000 limit", "Down from 3,410 at 3 min 47 s". */
  context?: ReactNode;
  /** 36 px figures (28 below 1360 px) by default; `md` 28 px; `sm` 21 px, as a running crawl's figures. */
  size?: 'lg' | 'md' | 'sm' | undefined;
  /** The value is words, not a number: "Not analysed yet", "Skipped: no sitemap". Never shown as 0. */
  words?: boolean | undefined;
  /** Without the cell's padding and rule: a figure inside a card's own layout. */
  compact?: boolean | undefined;
}

function StatContent({ label, value, context, size = 'lg', words = false }: StatContentProps) {
  return (
    <>
      <span className="si-stat__label">{label}</span>
      <span
        className={clsx('si-stat__value', size !== 'lg' && `si-stat__value--${size}`, words && 'si-stat__value--words')}
      >
        {value}
      </span>
      {context ? <span className="si-stat__context">{context}</span> : null}
    </>
  );
}

const statClass = (compact: boolean | undefined, className: string | undefined, link = false) =>
  clsx('si-stat', compact && 'si-stat--compact', link && 'si-stat--link', className);

/** A figure: a large number with its label and the line that explains it. */
export function Stat({ className, compact, ...content }: StatContentProps & { className?: string | undefined }) {
  return (
    <div className={statClass(compact, className)}>
      <StatContent {...content} />
    </div>
  );
}

type StatAnchorProps = StatContentProps &
  Omit<AnchorHTMLAttributes<HTMLAnchorElement>, 'children'> & { ref?: Ref<HTMLAnchorElement> | undefined };

function StatAnchor({ label, value, context, size, words, compact, className, ref, ...anchor }: StatAnchorProps) {
  return (
    <a ref={ref} {...anchor} className={statClass(compact, className, true)}>
      <StatContent label={label} value={value} context={context} size={size} words={words} />
    </a>
  );
}

/**
 * A figure that opens its URLs ("every number opens its URLs"): a router link with the figure's look, lit on hover.
 * Takes the router's `to`, `params` and `search`, carrying the filter with it.
 */
export const StatLink = createLink(StatAnchor);

/** Figures side by side, divided by hairlines: put it in a `Card`. */
export function Stats({
  columns,
  className,
  children,
}: {
  columns: number;
  className?: string | undefined;
  children: ReactNode;
}) {
  return (
    <div className={clsx('si-stats', className)} style={{ '--stat-columns': columns } as CSSProperties}>
      {children}
    </div>
  );
}

/** A small figure in a sunken strip of three: the inspector's response, size and depth. */
export function MiniStat({ label, value, context }: { label: ReactNode; value: ReactNode; context?: ReactNode }) {
  return (
    <div className="si-mini-stat">
      <div className="si-mini-stat__label">{label}</div>
      <div className="si-mini-stat__value">{value}</div>
      {context ? <div className="si-mini-stat__context">{context}</div> : null}
    </div>
  );
}

export function MiniStats({ columns = 3, children }: { columns?: number | undefined; children: ReactNode }) {
  return (
    <div className="si-mini-stats" style={{ '--stat-columns': columns } as CSSProperties}>
      {children}
    </div>
  );
}
