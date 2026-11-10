import clsx from 'clsx';
import type { CSSProperties, ReactNode } from 'react';

export interface StackPart {
  key: string;
  label: string;
  value: number;
  /** The value as it reads in the key: "2,713". */
  valueText: string;
  /** `primary`: done. `soft`: on its way. `hatched`: left out. */
  tone: 'primary' | 'soft' | 'hatched';
}

export interface StackBarProps {
  parts: readonly StackPart[];
  /** The bar in words, with its numbers: "Of 5,722 URLs found: 2,713 crawled, 1,417 queued, 1,592 not followed". */
  label: string;
  /** At the end of the key: "Of the 5,722 found". */
  total?: ReactNode;
  className?: string | undefined;
}

/**
 * A whole split into a few parts, with its key: what a crawl found, as crawled, queued and not followed. The key has
 * every part's name and number, so the bar is only the picture of it, and each tone differs by more than its colour
 * (solid, tinted, hatched).
 */
export function StackBar({ parts, label, total, className }: StackBarProps) {
  return (
    <div className={clsx('si-stack', className)}>
      <div className="si-stack__bar" role="img" aria-label={label}>
        {parts
          .filter((part) => part.value > 0)
          .map((part) => (
            <span
              key={part.key}
              className={clsx('si-stack__part', `si-stack__part--${part.tone}`)}
              style={{ '--share': part.value } as CSSProperties}
            />
          ))}
      </div>
      <Legend>
        {parts.map((part) => (
          <LegendItem
            key={part.key}
            swatch={<span className={clsx('si-stack__part', `si-stack__part--${part.tone}`)} />}
          >
            {part.label} <span className="si-legend__value">{part.valueText}</span>
          </LegendItem>
        ))}
        {total ? <span className="si-legend__item si-legend__item--end">{total}</span> : null}
      </Legend>
    </div>
  );
}

/** A chart's key: each series or part with its swatch, its name and its number. */
export function Legend({ className, children }: { className?: string | undefined; children: ReactNode }) {
  return <div className={clsx('si-legend', className)}>{children}</div>;
}

export function LegendItem({ swatch, children }: { swatch: ReactNode; children: ReactNode }) {
  return (
    <span className="si-legend__item">
      <span className="si-legend__swatch" aria-hidden="true">
        {swatch}
      </span>
      <span>{children}</span>
    </span>
  );
}
