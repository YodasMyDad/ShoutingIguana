import { Group } from '@visx/group';
import { scaleLinear } from '@visx/scale';
import { AreaClosed, LinePath } from '@visx/shape';
import clsx from 'clsx';
import { memo, type CSSProperties } from 'react';
import { useElementSize } from './useElementSize';

export interface LineChartPoint {
  x: number;
  y: number;
}

export interface LineChartSeries {
  key: string;
  /** `primary`: the accent, with its area filled. `secondary`: a quieter line, for what the primary is measured against. */
  tone: 'primary' | 'secondary';
  /** Oldest first. */
  points: readonly LineChartPoint[];
}

export interface LineChartTick {
  value: number;
  label: string;
}

/** A moment marked on the time axis (a crawl continued): a quiet upright rule. What it means is said in the label. */
export interface LineChartMarker {
  key: string;
  x: number;
}

export interface LineChartProps {
  series: readonly LineChartSeries[];
  /** The chart in words, with its numbers: "Found and crawled URLs over 22 min 26 s: 5,722 found, 2,713 crawled". */
  label: string;
  /** The ends of the axes (they start at 0). */
  xMax: number;
  yMax: number;
  xTicks: readonly LineChartTick[];
  yTicks: readonly LineChartTick[];
  /** Moments marked across the plot, drawn behind the lines. Say each in `label`, which the picture can't. */
  markers?: readonly LineChartMarker[] | undefined;
  className?: string | undefined;
}

const NO_MARKERS: readonly LineChartMarker[] = [];

/** What the plot is drawn at before it's measured, and in tests. */
const FALLBACK = { width: 480, height: 188 };
/** Room inside the plot for an end dot, so it isn't cut at the edge. */
const INSET = 4;

/**
 * Lines over time, with round ticks on both axes: a crawl's found and crawled. Drawn with visx in real pixels, in the
 * chart tokens; it grows to fill its card, redraws in place and never tweens. The axis labels are text (they take the
 * app's type and zoom), the plot is the picture, and the label says what it shows with its numbers.
 */
export const LineChart = memo(function LineChart({
  series,
  label,
  xMax,
  yMax,
  xTicks,
  yTicks,
  markers = NO_MARKERS,
  className,
}: LineChartProps) {
  const [ref, measured] = useElementSize();
  const { width, height } = measured && measured.width > 0 && measured.height > 0 ? measured : FALLBACK;
  const x = scaleLinear({ domain: [0, Math.max(1, xMax)], range: [0, width - INSET] });
  const y = scaleLinear({ domain: [0, Math.max(1, yMax)], range: [height, INSET] });
  return (
    <div role="img" aria-label={label} className={clsx('si-line-chart', className)}>
      <div className="si-line-chart__y" aria-hidden="true">
        {yTicks.map((tick) => (
          <span
            key={tick.value}
            className="si-line-chart__tick si-line-chart__tick--y"
            style={{ '--at': `${y(tick.value)}px` } as CSSProperties}
          >
            {tick.label}
          </span>
        ))}
      </div>
      <div ref={ref} className="si-line-chart__plot">
        <svg className="si-chart__svg" width={width} height={height} aria-hidden="true" focusable="false">
          <Group>
            {yTicks.map((tick) => (
              <line
                key={tick.value}
                className="si-chart__grid"
                x1={0}
                x2={width}
                y1={y(tick.value)}
                y2={y(tick.value)}
              />
            ))}
          </Group>
          <Group>
            {markers.map((marker) => (
              <line
                key={marker.key}
                className="si-chart__marker"
                x1={x(marker.x)}
                x2={x(marker.x)}
                y1={INSET}
                y2={height}
              />
            ))}
          </Group>
          {series.map((line) =>
            line.points.length > 1 ? (
              <Group key={line.key}>
                {line.tone === 'primary' ? (
                  <AreaClosed
                    className="si-chart__area"
                    data={line.points as LineChartPoint[]}
                    x={(point) => x(point.x)}
                    y={(point) => y(point.y)}
                    yScale={y}
                  />
                ) : null}
                <LinePath
                  className={clsx('si-chart__line', `si-chart__line--${line.tone}`)}
                  data={line.points as LineChartPoint[]}
                  x={(point) => x(point.x)}
                  y={(point) => y(point.y)}
                />
              </Group>
            ) : null,
          )}
          {series.map((line) => {
            const end = line.points.at(-1);
            return end ? (
              <circle
                key={line.key}
                className={clsx('si-chart__dot', `si-chart__dot--${line.tone}`)}
                cx={x(end.x)}
                cy={y(end.y)}
                r={INSET}
              />
            ) : null;
          })}
        </svg>
      </div>
      <div className="si-line-chart__x" aria-hidden="true">
        {xTicks.map((tick) => (
          <span
            key={tick.value}
            className="si-line-chart__tick si-line-chart__tick--x"
            style={{ '--at': `${x(tick.value)}px` } as CSSProperties}
          >
            {tick.label}
          </span>
        ))}
      </div>
    </div>
  );
});
