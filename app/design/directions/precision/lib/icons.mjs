// The icon set, drawn for this direction: a 16 px grid, 1.5 px strokes, round caps and joins. No third-party icons.
// Severity icons are filled shapes with their glyph cut out (even-odd), so they read on any row colour, and each has
// its own silhouette (diamond, triangle, circle) so severity never depends on colour.

const r2 = (n) => Math.round(n * 100) / 100;

function polygonPoints(n, radius, rotationDeg, cx = 8, cy = 8) {
  return Array.from({ length: n }, (_, i) => {
    const a = ((rotationDeg + (360 / n) * i) * Math.PI) / 180;
    return [cx + radius * Math.cos(a), cy + radius * Math.sin(a)];
  });
}

// A polygon with its corners rounded by quadratic curves.
function roundedPolygon(points, r) {
  const n = points.length;
  let d = '';
  for (let i = 0; i < n; i++) {
    const p = points[i];
    const prev = points[(i - 1 + n) % n];
    const next = points[(i + 1) % n];
    const toPrev = [prev[0] - p[0], prev[1] - p[1]];
    const toNext = [next[0] - p[0], next[1] - p[1]];
    const lp = Math.hypot(...toPrev);
    const ln = Math.hypot(...toNext);
    const a = [p[0] + (toPrev[0] / lp) * r, p[1] + (toPrev[1] / lp) * r];
    const b = [p[0] + (toNext[0] / ln) * r, p[1] + (toNext[1] / ln) * r];
    d += `${i === 0 ? 'M' : 'L'}${r2(a[0])} ${r2(a[1])}Q${r2(p[0])} ${r2(p[1])} ${r2(b[0])} ${r2(b[1])}`;
  }
  return d + 'Z';
}

const circlePath = (cx, cy, r) => `M${r2(cx - r)} ${cy}a${r} ${r} 0 1 0 ${r2(2 * r)} 0a${r} ${r} 0 1 0 ${r2(-2 * r)} 0Z`;
const rectPath = (x, y, w, h, rr = 0.6) =>
  `M${r2(x + rr)} ${y}h${r2(w - 2 * rr)}q${rr} 0 ${rr} ${rr}v${r2(h - 2 * rr)}q0 ${rr} -${rr} ${rr}h-${r2(w - 2 * rr)}q-${rr} 0 -${rr} -${rr}v-${r2(h - 2 * rr)}q0 -${rr} ${rr} -${rr}Z`;

function gearPath() {
  const teeth = 8;
  const outer = 6.4;
  const inner = 4.9;
  const pts = [];
  for (let i = 0; i < teeth; i++) {
    const base = (360 / teeth) * i - 90;
    for (const [off, rad] of [[-15, inner], [-9, outer], [9, outer], [15, inner]]) {
      const a = ((base + off) * Math.PI) / 180;
      pts.push([8 + rad * Math.cos(a), 8 + rad * Math.sin(a)]);
    }
  }
  return roundedPolygon(pts, 0.45);
}

const diamond = roundedPolygon([[8, 0.6], [15.4, 8], [8, 15.4], [0.6, 8]], 1.4);
const triangle = roundedPolygon([[8, 1.4], [15, 14.1], [1, 14.1]], 1.6);

// Stroke icons: inner SVG markup on a 16 x 16 grid.
const stroke = {
  home: '<path d="M2.75 7.4 8 2.9l5.25 4.5v5.85H9.9V9.9H6.1v3.35H2.75z"/>',
  overview: '<path d="M2.5 11.75a5.5 5.5 0 0 1 11 0"/><path d="M8 11.75 10.6 8.4"/><path d="M2.5 14h11"/>',
  issues: '<path d="M3.5 14V2.5"/><path d="M3.5 3h8.25l-1.9 2.9 1.9 2.85H3.5"/>',
  urls: '<rect x="2" y="3" width="12" height="10" rx="1.5"/><path d="M2 6.5h12M2 9.75h12M6.25 6.5V13"/>',
  links: '<path d="M6.6 9.4 9.4 6.6"/><path d="m7.6 4.4.95-.95a2.6 2.6 0 0 1 3.68 3.68l-.95.95"/><path d="m8.4 11.6-.95.95a2.6 2.6 0 0 1-3.68-3.68l.95-.95"/>',
  sitemaps: '<rect x="5.75" y="1.75" width="4.5" height="3.25" rx=".75"/><rect x="1.75" y="11" width="4.5" height="3.25" rx=".75"/><rect x="9.75" y="11" width="4.5" height="3.25" rx=".75"/><path d="M8 5v3M4 11V8h8v3"/>',
  robots: '<path d="M4 1.75h5.25L12 4.5v9.75H4z"/><path d="M9 1.75V4.75h3"/><path d="M6 8.25h4M6 11h4"/>',
  sliders: '<path d="M2.5 5h6M11.5 5h2M2.5 11h2M7.5 11h6"/><circle cx="10" cy="5" r="1.5"/><circle cx="6" cy="11" r="1.5"/>',
  gear: `<path d="${gearPath()}"/><circle cx="8" cy="8" r="2"/>`,
  back: '<path d="M10 3.5 5.5 8l4.5 4.5"/>',
  forward: '<path d="m6 3.5 4.5 4.5L6 12.5"/>',
  'chev-down': '<path d="m4.5 6.25 3.5 3.5 3.5-3.5"/>',
  'chev-right': '<path d="m6.25 4.5 3.5 3.5-3.5 3.5"/>',
  'chev-up': '<path d="m4.5 9.75 3.5-3.5 3.5 3.5"/>',
  search: '<circle cx="7" cy="7" r="4.25"/><path d="m10.25 10.25 3.25 3.25"/>',
  pause: '<path d="M5.75 3.5v9M10.25 3.5v9"/>',
  stop: '<rect x="3.75" y="3.75" width="8.5" height="8.5" rx="1.5"/>',
  play: '<path d="M5.25 3.4v9.2L12.6 8z"/>',
  refresh: '<path d="M13.25 8a5.25 5.25 0 1 1-1.6-3.77"/><path d="M13.4 2.6v2.9h-2.9"/>',
  download: '<path d="M8 2.5v7.5M4.75 6.75 8 10l3.25-3.25M3 13.25h10"/>',
  copy: '<rect x="5.5" y="5.5" width="8" height="8" rx="1.25"/><path d="M10.5 5.5V3.75c0-.69-.56-1.25-1.25-1.25h-5.5c-.69 0-1.25.56-1.25 1.25v5.5c0 .69.56 1.25 1.25 1.25H5.5"/>',
  external: '<path d="M9.25 2.5h4.25v4.25M13.5 2.5 7.5 8.5"/><path d="M11.5 9.25v3.25c0 .55-.45 1-1 1h-7c-.55 0-1-.45-1-1v-7c0-.55.45-1 1-1h3.25"/>',
  close: '<path d="m4 4 8 8M12 4l-8 8"/>',
  filter: '<path d="M2.5 4.5h11M4.75 8h6.5M7 11.5h2"/>',
  columns: '<rect x="2" y="3" width="12" height="10" rx="1.5"/><path d="M6 3v10M10 3v10"/>',
  check: '<path d="m3.25 8.5 3.1 3 6.4-7"/>',
  plus: '<path d="M8 3v10M3 8h10"/>',
  'arrow-down': '<path d="M8 3v10M4.5 9.5 8 13l3.5-3.5"/>',
  'arrow-up': '<path d="M8 13V3M4.5 6.5 8 3l3.5 3.5"/>',
  folder: '<path d="M2 4.25C2 3.56 2.56 3 3.25 3h3l1.5 1.75h5c.69 0 1.25.56 1.25 1.25v6.25c0 .69-.56 1.25-1.25 1.25h-9.5C2.56 13.5 2 12.94 2 12.25z"/>',
  sidebar: '<rect x="2" y="2.75" width="12" height="10.5" rx="1.5"/><path d="M6 2.75v10.5"/>',
  'dock-bottom': '<rect x="2" y="2.75" width="12" height="10.5" rx="1.5"/><path d="M2 9.5h12"/>',
  theme: '<circle cx="8" cy="8" r="5.5"/><path d="M8 2.5a5.5 5.5 0 0 1 0 11z" fill="currentColor" stroke="none"/>',
  'circle-check': '<circle cx="8" cy="8" r="5.75"/><path d="m5.6 8.2 1.7 1.7 3.2-3.5"/>',
  clock: '<circle cx="8" cy="8" r="5.75"/><path d="M8 4.9v3.35l2.1 1.3"/>',
  grid: '<path d="M2.5 4h11M2.5 8h11M2.5 12h11"/>',
  group: '<path d="M2.5 3.5h11M4.5 7h9M4.5 10.5h9M2.5 3.5v7"/>',
  'eye-off': '<path d="M2.5 8s2-3.75 5.5-3.75S13.5 8 13.5 8s-2 3.75-5.5 3.75S2.5 8 2.5 8z"/><path d="m3 13 10-10"/>',
  power: '<path d="M8 2.5v5"/><path d="M5 4.6a4.6 4.6 0 1 0 6 0"/>',
  enter: '<path d="M12.75 3.5v4.25c0 .69-.56 1.25-1.25 1.25H4"/><path d="M6.5 6.25 3.75 9 6.5 11.75"/>',
  command: '<path d="M2.5 4.5h11M2.5 8h7M2.5 11.5h4"/>',
  'live-chart': '<path d="M2.5 13.25h11"/><path d="m2.5 10.5 3-3 2.5 2 5.5-6"/>',
  lightning: '<path d="M9 1.75 3.5 9h4l-.75 5.25L12.5 7h-4z"/>',
};

// Filled icons (even-odd cut-outs).
const filled = {
  'sev-error': `${diamond}${rectPath(7.25, 4.1, 1.5, 4.9, 0.75)}${circlePath(8, 10.95, 0.95)}`,
  'sev-warning': `${triangle}${rectPath(7.25, 5.6, 1.5, 4.6, 0.75)}${circlePath(8, 11.95, 0.95)}`,
  'sev-notice': `${circlePath(8, 8, 6.75)}${circlePath(8, 4.85, 1)}${rectPath(7.25, 6.9, 1.5, 5, 0.75)}`,
  dot: circlePath(8, 8, 3.25),
  more: `${circlePath(3.5, 8, 1.25)}${circlePath(8, 8, 1.25)}${circlePath(12.5, 8, 1.25)}`,
};

export const iconNames = [...Object.keys(stroke), ...Object.keys(filled)];

// The sprite, inlined once per page.
export function sprite() {
  const symbols = [
    ...Object.entries(stroke).map(([id, body]) =>
      `<symbol id="i-${id}" viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round">${body}</symbol>`),
    ...Object.entries(filled).map(([id, d]) =>
      `<symbol id="i-${id}" viewBox="0 0 16 16"><path fill="currentColor" fill-rule="evenodd" d="${d}"/></symbol>`),
    // Skipped: a dashed ring with a stroke through it. Passed: a quiet ring with a tick.
    '<symbol id="i-sev-skipped" viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"><circle cx="8" cy="8" r="5.9" stroke-dasharray="2.3 2.3"/><path d="m5.6 10.4 4.8-4.8"/></symbol>',
  ];
  return `<svg class="sprite" aria-hidden="true" xmlns="http://www.w3.org/2000/svg"><defs>${symbols.join('')}</defs></svg>`;
}

export const icon = (name, cls = 'icon', label) =>
  `<svg class="${cls}"${label ? ` role="img" aria-label="${label}"` : ' aria-hidden="true"'}><use href="#i-${name}"/></svg>`;
