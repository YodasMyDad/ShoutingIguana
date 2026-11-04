import clsx from 'clsx';
import { formatBytes, formatCount, formatMilliseconds, formatPercent, formatRate } from '../lib/format';
import { splitFigure } from './figures';

export interface NumProps {
  value: number | null | undefined;
  /**
   * How it reads (lib/format.ts): `count` 4,521; `bytes` 18.3 MB; `ms` 312 ms or 1 min 12 s; `rate` 12.3/s;
   * `percent` of `total`, 99.6%.
   */
  format?: 'count' | 'bytes' | 'ms' | 'rate' | 'percent' | undefined;
  /** For `percent`: the whole. */
  total?: number | null | undefined;
  /** A counted noun after a count: "URL" reads "1 URL", "4,521 URLs". */
  noun?: string | undefined;
  /** The noun's plural, when it isn't the noun and an s. */
  plural?: string | undefined;
  className?: string | undefined;
}

/**
 * A number as the brief shows it: thousands separators, tabular figures, and its unit always, smaller and muted. A
 * missing number is an en dash, never 0. Inside a `Stat` the unit is set at half the figure's size.
 */
export function Num({ value, format = 'count', total, noun, plural, className }: NumProps) {
  const text =
    format === 'bytes'
      ? formatBytes(value)
      : format === 'ms'
        ? formatMilliseconds(value)
        : format === 'rate'
          ? formatRate(value)
          : format === 'percent'
            ? formatPercent(value, total)
            : formatCount(value);
  const missing = text === '–';
  const word = noun && !missing ? (value === 1 ? noun : (plural ?? `${noun}s`)) : null;
  return (
    <span className={clsx('figures', className)}>
      <FigureParts text={text} />
      {word ? (
        <>
          {' '}
          <span className="si-unit">{word}</span>
        </>
      ) : null}
    </span>
  );
}

/** The parts of a formatted figure, its units muted: shared by `Num` and `Duration`. */
export function FigureParts({ text }: { text: string }) {
  return (
    <>
      {splitFigure(text).map((part, index) =>
        part.unit ? (
          <span key={index} className="si-unit">
            {/* The unit's own space, so screen readers and copying read "312 ms". */}
            {index > 0 && !/^[%/]/.test(part.text) ? ' ' : ''}
            {part.text}
          </span>
        ) : (
          <span key={index}>
            {index > 0 ? ' ' : ''}
            {part.text}
          </span>
        ),
      )}
    </>
  );
}
