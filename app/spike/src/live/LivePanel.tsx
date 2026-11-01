import { lazy, Suspense, useEffect, useRef, useState } from 'react';
import { formatInteger } from '../format';
import { useLive, type StatusBreakdown } from './liveStore';

export type ChartsVariant = 'echarts' | 'visx' | 'none';

// Each library is its own chunk, so the build output shows what each one costs.
const EChartsCharts = lazy(() => import('./EChartsCharts'));
const VisxCharts = lazy(() => import('./VisxCharts'));

export const STATUS_CLASSES = [
  { key: 's2xx', label: '2xx' },
  { key: 's3xx', label: '3xx' },
  { key: 's4xx', label: '4xx' },
  { key: 's5xx', label: '5xx' },
] as const;

function Counter({ label, value, unit }: { label: string; value: number | undefined; unit?: string }) {
  return (
    <div className="counter">
      <span className="counter-label">{label}</span>
      <span className="counter-value">
        {value === undefined ? <span className="skeleton wide" /> : formatInteger(value)}
        {value !== undefined && unit ? <span className="unit"> {unit}</span> : null}
      </span>
    </div>
  );
}

export function StatusLegend({ status }: { status: StatusBreakdown | undefined }) {
  const total = status ? status.s2xx + status.s3xx + status.s4xx + status.s5xx : 0;
  return (
    <ul className="legend">
      {STATUS_CLASSES.map(({ key, label }) => (
        <li key={key}>
          <span className={`swatch ${key}`} aria-hidden="true" />
          {label} {status ? `${formatInteger(status[key])} (${((status[key] / Math.max(1, total)) * 100).toFixed(1)}%)` : '–'}
        </li>
      ))}
    </ul>
  );
}

export function useElementWidth<T extends HTMLElement>() {
  const ref = useRef<T>(null);
  const [width, setWidth] = useState(0);
  useEffect(() => {
    const element = ref.current;
    if (!element) return;
    const observer = new ResizeObserver(([entry]) => setWidth(Math.floor(entry.contentRect.width)));
    observer.observe(element);
    return () => observer.disconnect();
  }, []);
  return [ref, width] as const;
}

function Notices() {
  const notices = useLive((state) => state.notices);
  const latest = notices[0];
  return (
    <div className="notice" role="status" aria-live="polite">
      {latest ? (
        <>
          <strong>{latest.code}</strong> {latest.message}
        </>
      ) : (
        'No notices'
      )}
    </div>
  );
}

export function LivePanel({ charts }: { charts: ChartsVariant }) {
  const progress = useLive((state) => state.progress);
  const status = useLive((state) => state.status);
  return (
    <section className="live" aria-label="Crawl progress">
      <div className="counters">
        <Counter label="Crawled" value={progress?.crawled} />
        <Counter label="Found" value={progress?.found} />
        <Counter label="Queued" value={progress?.queued} />
        <Counter label="Errors" value={progress?.errors} />
        <Counter label="Rate" value={progress ? Math.round(progress.pagesPerSecond) : undefined} unit="pages/s" />
        <span className={`stream ${status}`}>Stream: {status}</span>
      </div>
      <div className="charts">
        <Suspense fallback={<div className="chart-fallback" />}>
          {charts === 'echarts' ? <EChartsCharts /> : charts === 'visx' ? <VisxCharts /> : null}
        </Suspense>
      </div>
      <Notices />
    </section>
  );
}
