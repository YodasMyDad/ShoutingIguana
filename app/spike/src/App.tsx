import { memo, useEffect, useState } from 'react';
import { useConnection } from './connection';
import { UrlGrid } from './grid/UrlGrid';
import { LivePanel, type ChartsVariant } from './live/LivePanel';
import { pushMessage, pushStatus } from './live/liveStore';
import { startProgressStream, type StreamStats } from './live/sse';
import { markConnection } from './perf/probe';

import { params } from './params';
const initialCharts = (params.get('charts') ?? 'visx') as ChartsVariant;
const liveEnabled = params.get('live') !== '0';
const sseIntervalMs = Number(params.get('sseInterval') ?? 500);

export const appHooks: { setCharts?: (variant: ChartsVariant) => void; sse?: StreamStats } = {};

const Grid = memo(UrlGrid);

const NAV = ['Overview', 'URLs', 'Issues', 'Links', 'Sitemaps', 'Settings'];

export function App() {
  const connection = useConnection();
  const [charts, setCharts] = useState<ChartsVariant>(initialCharts);

  useEffect(() => {
    appHooks.setCharts = setCharts;
    if (!liveEnabled) return;
    const stream = startProgressStream(pushMessage, pushStatus, sseIntervalMs);
    appHooks.sse = stream.stats;
    return () => stream.stop();
  }, []);

  useEffect(() => {
    if (connection) markConnection();
  }, [connection]);

  return (
    <div className="shell">
      <header className="topbar">
        <span className="brand">Shouting Iguana</span>
        <span className="crumb">www.example-homeware.co.uk · crawl of 1 Nov 2026</span>
        <span className="spacer" />
        <span className={connection ? 'engine ok' : 'engine starting'} role="status">
          {connection ? `Engine connected · port ${connection.port}` : 'Starting the engine…'}
        </span>
        <label className="picker">
          Charts{' '}
          <select value={charts} onChange={(event) => setCharts(event.target.value as ChartsVariant)}>
            <option value="visx">visx (SVG)</option>
            <option value="echarts">ECharts (canvas)</option>
            <option value="none">none</option>
          </select>
        </label>
      </header>
      <nav className="sidebar" aria-label="Main">
        <ul>
          {NAV.map((item) => (
            <li key={item} className={item === 'URLs' ? 'active' : undefined}>
              {item}
            </li>
          ))}
        </ul>
      </nav>
      <main className="main">
        {liveEnabled ? <LivePanel charts={charts} /> : null}
        <Grid />
      </main>
    </div>
  );
}
