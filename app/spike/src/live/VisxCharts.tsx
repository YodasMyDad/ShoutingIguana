import { AreaClosed, Bar, LinePath } from '@visx/shape';
import { scaleLinear } from '@visx/scale';
import { formatInteger } from '../format';
import { HISTORY_POINTS, useLive } from './liveStore';
import { STATUS_CLASSES, StatusLegend, useElementWidth } from './LivePanel';

const SPARK_HEIGHT = 48;
const BAR_HEIGHT = 14;

// SVG in the DOM: colours come from CSS tokens (so themes and forced-colors mode just work) and the text alternative
// is ordinary markup.
function Sparkline() {
  const history = useLive((state) => state.history);
  const [ref, width] = useElementWidth<HTMLDivElement>();
  const offset = HISTORY_POINTS - history.length;
  let max = 1;
  for (const value of history) if (value > max) max = value;
  const x = scaleLinear<number>({ domain: [0, HISTORY_POINTS - 1], range: [0, width] });
  const y = scaleLinear<number>({ domain: [0, max * 1.1], range: [SPARK_HEIGHT, 2] });
  const points = history.map((value, i) => [i + offset, value] as const);
  const latest = history.length ? history[history.length - 1] : undefined;
  return (
    <div ref={ref} className="chart sparkline">
      {width > 0 ? (
        <svg
          width={width}
          height={SPARK_HEIGHT}
          role="img"
          aria-label={`Pages per second over the last 5 minutes${latest === undefined ? '' : `, now ${formatInteger(latest)}`}`}
        >
          <AreaClosed data={points} x={(d) => x(d[0])} y={(d) => y(d[1])} yScale={y} className="spark-area" />
          <LinePath data={points} x={(d) => x(d[0])} y={(d) => y(d[1])} className="spark-line" />
        </svg>
      ) : null}
    </div>
  );
}

function StatusBar() {
  const status = useLive((state) => state.progress?.status);
  const [ref, width] = useElementWidth<HTMLDivElement>();
  const total = status ? status.s2xx + status.s3xx + status.s4xx + status.s5xx : 0;
  const x = scaleLinear<number>({ domain: [0, Math.max(1, total)], range: [0, width] });
  let cursor = 0;
  return (
    <div className="status-block">
      <div ref={ref} className="chart statusbar">
        {width > 0 && status ? (
          <svg width={width} height={BAR_HEIGHT} role="img" aria-label="Status codes by class; values in the legend">
            {STATUS_CLASSES.map(({ key }) => {
              const start = cursor;
              cursor += status[key];
              return <Bar key={key} x={x(start)} y={0} width={Math.max(0, x(cursor) - x(start))} height={BAR_HEIGHT} className={`bar ${key}`} />;
            })}
          </svg>
        ) : null}
      </div>
      <StatusLegend status={status} />
    </div>
  );
}

export default function VisxCharts() {
  return (
    <>
      <Sparkline />
      <StatusBar />
    </>
  );
}
