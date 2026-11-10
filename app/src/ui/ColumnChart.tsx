import clsx from 'clsx';
import { useLayoutEffect, useRef, useState, type CSSProperties, type ReactNode } from 'react';
import { ChartLink, type ChartPartLink } from './ChartLink';
import { fittingText, valueTexts } from './columnValues';
import { useElementSize } from './useElementSize';

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

/** How much narrower than its column a number must be, in px: with the gap between columns, what keeps two apart. */
const VALUE_CLEARANCE = 2;

/**
 * Counts side by side: pages by depth, by response time. Every column is its number, its bar and its label, and a link
 * to its URLs, so the chart is its own table: nothing is told by the bar alone. A column with anything in it never
 * disappears, and one with nothing keeps its place.
 *
 * A number wider than its column (eleven columns of six-figure counts in half a window) would run into its
 * neighbour's, so it's written shorter there ("313k"): the whole number stays the column's name, which is what a
 * screen reader reads, and its tooltip.
 */
export function ColumnChart({ columns, label, axis, className }: ColumnChartProps) {
  const max = columns.reduce((most, column) => Math.max(most, column.value), 0);
  const [sizeRef, size] = useElementSize();
  const listRef = useRef<HTMLOListElement | null>(null);
  const probeRef = useRef<HTMLSpanElement | null>(null);
  /** The value each column shows, by key, where it isn't the whole number. */
  const [shortened, setShortened] = useState<Readonly<Record<string, string>>>({});

  // Measured, not reckoned: the numbers are set in the app's own type at the user's zoom. A probe in the values' own
  // style gives each way of writing a number its width. The room is the column's whole width (a number is centred on
  // its column, and may run over its padding), less a little, so two neighbours never touch.
  useLayoutEffect(() => {
    const list = listRef.current;
    const probe = probeRef.current;
    if (!list || !probe) return;
    const next: Record<string, string> = {};
    const items = list.querySelectorAll<HTMLElement>('.si-columns__item');
    columns.forEach((column, index) => {
      const available = (items[index]?.getBoundingClientRect().width ?? 0) - VALUE_CLEARANCE;
      const texts = valueTexts(column).map((text) => {
        probe.textContent = text;
        return { text, width: probe.getBoundingClientRect().width };
      });
      const fits = fittingText(texts, available);
      if (fits !== column.valueText) next[column.key] = fits;
    });
    probe.textContent = '';
    setShortened((previous) =>
      Object.keys(previous).length === Object.keys(next).length &&
      Object.entries(next).every(([key, text]) => previous[key] === text)
        ? previous
        : next,
    );
  }, [columns, size?.width]);

  return (
    <div className={clsx('si-columns', className)}>
      <ol
        ref={(element) => {
          listRef.current = element;
          sizeRef(element);
        }}
        className="si-columns__list"
        aria-label={label}
        style={{ '--columns': columns.length } as CSSProperties}
      >
        {columns.map((column) => {
          const short = shortened[column.key];
          const content = (
            <>
              <span className="si-columns__plot">
                <span className="si-columns__value">{short ?? column.valueText}</span>
                <span
                  aria-hidden="true"
                  className={clsx('si-columns__bar', column.value > 0 && 'si-columns__bar--some')}
                  style={{ '--share': max > 0 ? column.value / max : 0 } as CSSProperties}
                />
              </span>
              <span className="si-columns__label">{column.label}</span>
            </>
          );
          // The whole number, a hover away, where the one drawn is shortened.
          const title = short === undefined ? undefined : column.name;
          return (
            <li key={column.key} className="si-columns__item">
              {/* The column's name with its number is its text; the number, bar and label drawn are the picture of
                  it, so nothing is read twice and a link's name is what's written in it. */}
              {column.link ? (
                <ChartLink
                  {...column.link}
                  className={clsx('si-columns__column si-columns__column--link', column.className)}
                  title={title}
                >
                  <span className="sr-only">{column.name}</span>
                  <span aria-hidden="true" className="si-columns__parts">
                    {content}
                  </span>
                </ChartLink>
              ) : (
                <span
                  className={clsx('si-columns__column', column.value === 0 && 'si-columns__column--zero')}
                  title={title}
                >
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
      {/* Measures the ways of writing a value, in the values' own style. Never seen or read. */}
      <span ref={probeRef} aria-hidden="true" className="si-columns__value si-columns__probe" />
      {axis ? <p className="si-columns__axis">{axis}</p> : null}
    </div>
  );
}
