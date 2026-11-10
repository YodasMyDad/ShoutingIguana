import { scaleLinear } from '@visx/scale';
import { AreaClosed, LinePath } from '@visx/shape';
import clsx from 'clsx';
import { memo } from 'react';
import { useElementSize } from './useElementSize';

export interface SparklineProps {
  /** Oldest first. Fewer than two draws nothing, in the same room. */
  values: readonly number[];
  /** The chart in words, with its numbers: "URLs a second over the last 5 min: 12.3 now, 48.1 at most". */
  label: string;
  /** The top of the scale, when the values have a known ceiling (a rate limit). The highest value otherwise. */
  max?: number | undefined;
  size?: 'md' | 'lg' | undefined;
  className?: string | undefined;
}

/** What it's drawn at before it's measured, and in tests. */
const FALLBACK = { width: 160, height: 28 };

/**
 * A figure's recent history as a small line: the rate beside "URLs a second". Drawn with visx in real pixels, in the
 * chart tokens; it redraws in place and never tweens. Its numbers are its label.
 */
export const Sparkline = memo(function Sparkline({ values, label, max, size = 'md', className }: SparklineProps) {
  const [ref, measured] = useElementSize();
  const { width, height } = measured && measured.width > 0 && measured.height > 0 ? measured : FALLBACK;
  const top = Math.max(max ?? 0, ...values, 1);
  // A pixel of room at the top and bottom, so the line's width isn't cut at the peak or at 0.
  const x = scaleLinear({ domain: [0, Math.max(1, values.length - 1)], range: [0, width] });
  const y = scaleLinear({ domain: [0, top], range: [height - 1, 1] });
  const points = values.map((value, index) => ({ index, value }));
  return (
    <span
      ref={ref}
      role="img"
      aria-label={label}
      className={clsx('si-spark', size === 'lg' && 'si-spark--lg', className)}
    >
      {points.length > 1 ? (
        <svg className="si-chart__svg" width={width} height={height} aria-hidden="true" focusable="false">
          <AreaClosed
            className="si-chart__area"
            data={points}
            x={(point) => x(point.index)}
            y={(point) => y(point.value)}
            yScale={y}
          />
          <LinePath
            className="si-chart__line si-chart__line--primary"
            data={points}
            x={(point) => x(point.index)}
            y={(point) => y(point.value)}
          />
        </svg>
      ) : null}
    </span>
  );
});
