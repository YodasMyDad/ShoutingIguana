import { useEffect, useRef } from 'react';
import * as echarts from 'echarts/core';
import { BarChart, LineChart } from 'echarts/charts';
import { AriaComponent, GridComponent } from 'echarts/components';
import { CanvasRenderer } from 'echarts/renderers';
import { HISTORY_POINTS, useLive } from './liveStore';
import { STATUS_CLASSES, StatusLegend } from './LivePanel';

echarts.use([LineChart, BarChart, GridComponent, AriaComponent, CanvasRenderer]);

// Canvas cannot read CSS variables, so token colours are resolved once at init (a theme switch must re-init).
const cssVar = (name: string) => getComputedStyle(document.documentElement).getPropertyValue(name).trim();

function useChart(initial: () => echarts.EChartsCoreOption) {
  const ref = useRef<HTMLDivElement>(null);
  const chart = useRef<echarts.ECharts | null>(null);
  useEffect(() => {
    const instance = echarts.init(ref.current!, undefined, { renderer: 'canvas' });
    instance.setOption(initial());
    chart.current = instance;
    const observer = new ResizeObserver(() => instance.resize());
    observer.observe(ref.current!);
    return () => {
      observer.disconnect();
      instance.dispose();
      chart.current = null;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);
  return { ref, chart };
}

function Sparkline() {
  const history = useLive((state) => state.history);
  const { ref, chart } = useChart(() => ({
    animation: false,
    aria: { enabled: true, label: { description: 'Pages per second over the last 5 minutes' } },
    grid: { left: 0, right: 0, top: 2, bottom: 0 },
    xAxis: { type: 'value', show: false, min: 0, max: HISTORY_POINTS - 1 },
    yAxis: { type: 'value', show: false, min: 0 },
    series: [
      {
        type: 'line',
        showSymbol: false,
        silent: true,
        data: [],
        lineStyle: { width: 1.5, color: cssVar('--accent') },
        areaStyle: { color: cssVar('--accent-soft') },
      },
    ],
  }));
  useEffect(() => {
    const offset = HISTORY_POINTS - history.length;
    chart.current?.setOption({ series: [{ data: history.map((value, i) => [i + offset, value]) }] });
  }, [history, chart]);
  return <div ref={ref} className="chart sparkline" />;
}

function StatusBar() {
  const status = useLive((state) => state.progress?.status);
  const { ref, chart } = useChart(() => ({
    animation: false,
    aria: { enabled: true, label: { description: 'Status codes by class' } },
    grid: { left: 0, right: 0, top: 0, bottom: 0 },
    xAxis: { type: 'value', show: false, max: 1 },
    yAxis: { type: 'category', show: false, data: ['status'] },
    series: STATUS_CLASSES.map(({ key, label }) => ({
      name: label,
      type: 'bar',
      stack: 'all',
      silent: true,
      barWidth: '100%',
      data: [0],
      itemStyle: { color: cssVar(`--${key}`) },
    })),
  }));
  useEffect(() => {
    if (!status) return;
    const total = status.s2xx + status.s3xx + status.s4xx + status.s5xx;
    chart.current?.setOption({ xAxis: { max: Math.max(1, total) }, series: STATUS_CLASSES.map(({ key }) => ({ data: [status[key]] })) });
  }, [status, chart]);
  return (
    <div className="status-block">
      <div ref={ref} className="chart statusbar" />
      <StatusLegend status={status} />
    </div>
  );
}

export default function EChartsCharts() {
  return (
    <>
      <Sparkline />
      <StatusBar />
    </>
  );
}
