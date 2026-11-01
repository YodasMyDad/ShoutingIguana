// Chart markup: SVG plots that stretch to their box, with HTML labels that keep their size.
import { num } from './format.mjs';

const VB_W = 1000, VB_H = 100;

function path(values, max, { close = false } = {}) {
  const n = values.length - 1 || 1;
  const pts = values.map((v, i) => [(i / n) * VB_W, VB_H - (Math.max(0, v) / max) * VB_H]);
  const d = pts.map(([x, y], i) => `${i ? 'L' : 'M'}${x.toFixed(1)} ${y.toFixed(2)}`).join(' ');
  return close ? `${d} L${VB_W} ${VB_H} L0 ${VB_H} Z` : d;
}

// A sparkline. limit draws a dashed reference line (for example the Polite rate).
export function sparkline(values, { max, limit, label, height = 'var(--spark-height)' } = {}) {
  const top = max ?? Math.max(...values, limit ?? 0) * 1.15;
  const limitY = limit != null ? VB_H - (limit / top) * VB_H : null;
  return `<div class="chart stat__spark" style="height:${height}" role="img" aria-label="${label ?? ''}">
    <svg class="chart__plot" viewBox="0 0 ${VB_W} ${VB_H}" preserveAspectRatio="none" aria-hidden="true">
      ${limitY != null ? `<line class="chart__limit" x1="0" x2="${VB_W}" y1="${limitY.toFixed(2)}" y2="${limitY.toFixed(2)}"/>` : ''}
      <path class="chart__area" d="${path(values, top, { close: true })}"/>
      <path class="chart__line chart__line--primary" d="${path(values, top)}"/>
    </svg>
  </div>`;
}

// A line chart with a y axis, an x axis in minutes and labels at the end of each line.
export function lineChart({ series, max, yTicks, xTicks, xMax, height, label }) {
  const grid = yTicks.map(t => {
    const y = VB_H - (t / max) * VB_H;
    return `<line class="chart__grid" x1="0" x2="${VB_W}" y1="${y.toFixed(2)}" y2="${y.toFixed(2)}"/>`;
  }).join('');
  const lines = series.map(s => `
      ${s.area ? `<path class="chart__area" d="${path(s.values, max, { close: true })}"/>` : ''}
      <path class="chart__line chart__line--${s.kind}" d="${path(s.values, max)}"/>`).join('');
  const yLabels = yTicks.map(t => `<span class="chart__y" style="top:${(100 - (t / max) * 100).toFixed(2)}%">${num(t)}</span>`).join('');
  const xLabels = xTicks.map(t => `<span class="chart__x" style="left:${((t.at / xMax) * 100).toFixed(2)}%">${t.label}</span>`).join('');
  const ends = series.map(s => {
    const v = s.values.at(-1);
    return `<span class="chart__dot" style="left:100%;top:${(100 - (v / max) * 100).toFixed(2)}%;background:var(${s.kind === 'primary' ? '--color-chart-primary' : '--color-chart-secondary'})"></span>
      <span class="chart__end" style="top:${(100 - (v / max) * 100).toFixed(2)}%;transform:translate(100%,-50%)"><b>${num(v)}</b><span>${s.name}</span></span>`;
  }).join('');
  return `<div class="chart-frame" style="--chart-h:${height}" role="img" aria-label="${label}">
    <div class="chart-frame__y" aria-hidden="true">${yLabels}</div>
    <div class="chart chart-frame__plot" aria-hidden="true">
      <svg class="chart__plot" viewBox="0 0 ${VB_W} ${VB_H}" preserveAspectRatio="none">${grid}${lines}</svg>
      ${ends}
    </div>
    <div class="chart-frame__x" aria-hidden="true">${xLabels}</div>
  </div>`;
}
