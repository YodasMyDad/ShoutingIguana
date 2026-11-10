import clsx from 'clsx';
import type { CSSProperties, ReactNode } from 'react';
import { ChartLink, type ChartPartLink } from './ChartLink';

export interface ChartColumn {
  key: string;
  /** Under the column: "3", "10+", "250–499 ms". */
  label: ReactNode;
  value: number;
  /** The value as it reads above the column: "1,740". */
  valueText: string;
  /** The column in words, with its number: "Depth 3: 1,740 pages". The link's name. */
  name: string;
  /** Opens its URLs. Without one (nothing there) the column is drawn only. */
  link?: ChartPartLink | null | undefined;
  className?: string | undefined;
}

export interface ColumnChartProps {
  columns: readonly ChartColumn[];
  /** What the chart shows: "Pages by depth". The list's name. */
  label: string;
  /** Under the axis, what the labels count: "Clicks from the start URL". */
  axis?: ReactNode;
  className?: string | undefined;
}

/**
 * Counts side by side: pages by depth, by response time. Every column is its number, its bar and its label, and a link
 * to its URLs, so the chart is its own table: nothing is told by the bar alone. A column with anything in it never
 * disappears, and one with nothing keeps its place.
 */
export function ColumnChart({ columns, label, axis, className }: ColumnChartProps) {
  const max = columns.reduce((most, column) => Math.max(most, column.value), 0);
  return (
    <div className={clsx('si-columns', className)}>
      <ol className="si-columns__list" aria-label={label} style={{ '--columns': columns.length } as CSSProperties}>
        {columns.map((column) => {
          const content = (
            <>
              <span className="si-columns__plot">
                <span className="si-columns__value">{column.valueText}</span>
                <span
                  aria-hidden="true"
                  className={clsx('si-columns__bar', column.value > 0 && 'si-columns__bar--some')}
                  style={{ '--share': max > 0 ? column.value / max : 0 } as CSSProperties}
                />
              </span>
              <span className="si-columns__label">{column.label}</span>
            </>
          );
          return (
            <li key={column.key} className="si-columns__item">
              {column.link ? (
                <ChartLink
                  {...column.link}
                  className={clsx('si-columns__column si-columns__column--link', column.className)}
                  aria-label={column.name}
                >
                  {content}
                </ChartLink>
              ) : (
                <span className={clsx('si-columns__column', column.value === 0 && 'si-columns__column--zero')}>
                  <span className="sr-only">{column.name}</span>
                  <span aria-hidden="true" className="si-columns__parts">
                    {content}
                  </span>
                </span>
              )}
            </li>
          );
        })}
      </ol>
      {axis ? <p className="si-columns__axis">{axis}</p> : null}
    </div>
  );
}
