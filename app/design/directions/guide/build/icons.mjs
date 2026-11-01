// The icon sprite, drawn for this direction on a 16 px grid with a 1.5 px stroke. No third-party icons.
// Interface icons are strokes in currentColor. Severity icons are filled shapes in currentColor with a glyph in
// --sev-glyph, so the shape alone tells the severity: diamond error, triangle warning, circle notice.

const stroke = {
  home: '<path d="M2.75 7.25 8 2.75l5.25 4.5v5.5a.5.5 0 0 1-.5.5H9.75V9.75h-3.5v3.5H3.25a.5.5 0 0 1-.5-.5Z"/>',
  overview: '<path d="M2.75 13.25h10.5"/><path d="M4.25 10.75V7.5"/><path d="M7 10.75V3.75"/><path d="M9.75 10.75V6"/><path d="M12.5 10.75V8.75"/>',
  issues: '<path d="M3.25 2.75v10.5"/><path d="M3.25 3.25h8.25l-1.75 2.75 1.75 2.75H3.25"/>',
  urls: '<path d="M6 4h7.25"/><path d="M6 8h7.25"/><path d="M6 12h7.25"/><path d="M2.75 4h.5"/><path d="M2.75 8h.5"/><path d="M2.75 12h.5"/>',
  links: '<path d="M6.75 9.25 9.25 6.75"/><path d="M7.25 4.75 8.5 3.5a2.47 2.47 0 0 1 3.5 3.5l-1.25 1.25"/><path d="M8.75 11.25 7.5 12.5A2.47 2.47 0 0 1 4 9l1.25-1.25"/>',
  sitemaps: '<rect x="5.75" y="2.25" width="4.5" height="3.5" rx=".75"/><rect x="1.75" y="10.25" width="4.5" height="3.5" rx=".75"/><rect x="9.75" y="10.25" width="4.5" height="3.5" rx=".75"/><path d="M8 5.75V8"/><path d="M4 10.25V8h8v2.25"/>',
  robots: '<path d="M4.25 1.75h4.75l2.75 2.75v9.25a.5.5 0 0 1-.5.5h-7a.5.5 0 0 1-.5-.5V2.25a.5.5 0 0 1 .5-.5Z"/><path d="M8.75 1.75v3h3"/><path d="M6 8.25h4"/><path d="M6 11h2.5"/>',
  sliders: '<path d="M2.75 4.75h5.5"/><path d="M11.25 4.75h2"/><path d="M2.75 11.25h1.5"/><path d="M7.25 11.25h6"/><circle cx="9.75" cy="4.75" r="1.5"/><circle cx="5.75" cy="11.25" r="1.5"/>',
  settings: '<circle cx="8" cy="8" r="2"/><path d="M8 1.75v1.5M8 12.75v1.5M14.25 8h-1.5M3.25 8h-1.5M12.42 3.58l-1.06 1.06M4.64 11.36l-1.06 1.06M12.42 12.42l-1.06-1.06M4.64 4.64 3.58 3.58"/><circle cx="8" cy="8" r="4.75"/>',
  'chevron-left': '<path d="M9.75 3.75 5.5 8l4.25 4.25"/>',
  'chevron-right': '<path d="M6.25 3.75 10.5 8l-4.25 4.25"/>',
  'chevron-down': '<path d="M3.75 6.25 8 10.5l4.25-4.25"/>',
  'chevron-up': '<path d="M3.75 9.75 8 5.5l4.25 4.25"/>',
  search: '<circle cx="7.25" cy="7.25" r="4.5"/><path d="m10.5 10.5 3 3"/>',
  pause: '<path d="M5.75 3.5v9"/><path d="M10.25 3.5v9"/>',
  stop: '<rect x="3.75" y="3.75" width="8.5" height="8.5" rx="1.25"/>',
  play: '<path d="M4.75 3.25v9.5L12.5 8Z"/>',
  external: '<path d="M9.25 2.75h4v4"/><path d="m13.25 2.75-6 6"/><path d="M11.75 9.5v3.25a.5.5 0 0 1-.5.5h-8a.5.5 0 0 1-.5-.5v-8a.5.5 0 0 1 .5-.5H6.5"/>',
  copy: '<rect x="5.25" y="5.25" width="8" height="8" rx="1.25"/><path d="M10.75 5.25V3.5a.75.75 0 0 0-.75-.75H3.5a.75.75 0 0 0-.75.75V10a.75.75 0 0 0 .75.75h1.75"/>',
  close: '<path d="m4 4 8 8"/><path d="m12 4-8 8"/>',
  more: '<circle cx="3.5" cy="8" r=".75"/><circle cx="8" cy="8" r=".75"/><circle cx="12.5" cy="8" r=".75"/>',
  filter: '<path d="M2.25 3.25h11.5L9.25 8.5v4.75l-2.5-1.25V8.5Z"/>',
  columns: '<rect x="2.25" y="2.75" width="11.5" height="10.5" rx="1"/><path d="M6.08 2.75v10.5"/><path d="M9.92 2.75v10.5"/>',
  export: '<path d="M8 2.25v7.5"/><path d="m4.75 6.5 3.25 3.25 3.25-3.25"/><path d="M2.75 10.75v2a.5.5 0 0 0 .5.5h9.5a.5.5 0 0 0 .5-.5v-2"/>',
  plus: '<path d="M8 3.25v9.5"/><path d="M3.25 8h9.5"/>',
  again: '<path d="M13.25 8a5.25 5.25 0 1 1-1.54-3.71"/><path d="M13.25 2.75v3h-3"/>',
  sun: '<circle cx="8" cy="8" r="2.75"/><path d="M8 1.75v1M8 13.25v1M14.25 8h-1M2.75 8h-1M12.42 3.58l-.7.7M4.28 11.72l-.7.7M12.42 12.42l-.7-.7M4.28 4.28l-.7-.7"/>',
  moon: '<path d="M13.25 9.6A5.5 5.5 0 0 1 6.4 2.75a5.5 5.5 0 1 0 6.85 6.85Z"/>',
  monitor: '<rect x="1.75" y="2.75" width="12.5" height="8.5" rx="1"/><path d="M5.5 13.75h5"/><path d="M8 11.25v2.5"/>',
  check: '<path d="m3.5 8.25 3 3 6-6.5"/>',
  clock: '<circle cx="8" cy="8" r="5.75"/><path d="M8 4.75V8l2.25 1.5"/>',
  folder: '<path d="M1.75 4.25a.5.5 0 0 1 .5-.5h3.5l1.5 1.5h6.5a.5.5 0 0 1 .5.5v6.5a.5.5 0 0 1-.5.5H2.25a.5.5 0 0 1-.5-.5Z"/>',
  globe: '<circle cx="8" cy="8" r="5.75"/><path d="M2.25 8h11.5"/><path d="M8 2.25c1.6 1.6 2.4 3.5 2.4 5.75S9.6 12.15 8 13.75C6.4 12.15 5.6 10.25 5.6 8S6.4 3.85 8 2.25Z"/>',
  panel: '<rect x="1.75" y="2.75" width="12.5" height="10.5" rx="1"/><path d="M5.75 2.75v10.5"/>',
  'arrow-down': '<path d="M8 3v10"/><path d="m4.5 9.5 3.5 3.5 3.5-3.5"/>',
  'arrow-up': '<path d="M8 13V3"/><path d="m4.5 6.5 3.5-3.5 3.5 3.5"/>',
  shield: '<path d="M8 1.75 13 3.5v4.25c0 3-2.1 5.25-5 6.5-2.9-1.25-5-3.5-5-6.5V3.5Z"/><path d="m5.75 8 1.6 1.6L10.5 6.5"/>',
  density: '<path d="M2.75 3.25h10.5"/><path d="M2.75 6.4h10.5"/><path d="M2.75 9.6h10.5"/><path d="M2.75 12.75h10.5"/>',
  eye: '<path d="M1.75 8S4 3.75 8 3.75 14.25 8 14.25 8 12 12.25 8 12.25 1.75 8 1.75 8Z"/><circle cx="8" cy="8" r="2"/>',
  hide: '<path d="M6.3 3.95A6.6 6.6 0 0 1 8 3.75c4 0 6.25 4.25 6.25 4.25a11 11 0 0 1-1.6 2.2"/><path d="M10.1 10.1A2 2 0 0 1 5.9 5.9"/><path d="M11.35 11.4A6.4 6.4 0 0 1 8 12.25C4 12.25 1.75 8 1.75 8a11.3 11.3 0 0 1 2.9-3.4"/><path d="m2.25 2.25 11.5 11.5"/>',
  grip: '<circle cx="6" cy="4" r=".6"/><circle cx="10" cy="4" r=".6"/><circle cx="6" cy="8" r=".6"/><circle cx="10" cy="8" r=".6"/><circle cx="6" cy="12" r=".6"/><circle cx="10" cy="12" r=".6"/>',
  bookmark: '<path d="M4.25 2.25h7.5v11.5L8 11l-3.75 2.75Z"/>',
  code: '<path d="m5.5 4.75-3.25 3.25 3.25 3.25"/><path d="m10.5 4.75 3.25 3.25-3.25 3.25"/>',
  analyse: '<path d="M2.75 13.25h10.5"/><path d="m3.5 10.5 3-3.25 2.5 2 4-4.75"/>',
};

const glyph = 'style="stroke:var(--sev-glyph);stroke-width:1.75;stroke-linecap:round;fill:none"';
const dot = 'style="fill:var(--sev-glyph)"';

const filled = {
  'sev-error': `<path d="M8 .75 15.25 8 8 15.25.75 8Z"/><path d="M8 4.75v3.9" ${glyph}/><circle cx="8" cy="11.15" r="1" ${dot}/>`,
  'sev-warning': `<path d="M7.13 1.9a1 1 0 0 1 1.74 0l6.3 11.1a1 1 0 0 1-.87 1.5H1.7a1 1 0 0 1-.87-1.5Z"/><path d="M8 6v3.6" ${glyph}/><circle cx="8" cy="11.9" r="1" ${dot}/>`,
  'sev-notice': `<circle cx="8" cy="8" r="7"/><path d="M8 7.4v4.1" ${glyph}/><circle cx="8" cy="4.9" r="1" ${dot}/>`,
  'sev-skipped': '<circle cx="8" cy="8" r="6.25" style="fill:none;stroke:currentColor;stroke-width:1.5;stroke-dasharray:2.45 1.85"/><path d="M5.25 8h5.5" style="fill:none;stroke:currentColor;stroke-width:1.5;stroke-linecap:round"/>',
  'sev-passed': '<circle cx="8" cy="8" r="6.25" style="fill:none;stroke:currentColor;stroke-width:1.5"/><path d="m5.1 8.2 2 2 3.8-4.1" style="fill:none;stroke:currentColor;stroke-width:1.5;stroke-linecap:round;stroke-linejoin:round"/>',
};

export function sprite() {
  const s = Object.entries(stroke).map(([id, body]) => `<symbol id="i-${id}" viewBox="0 0 16 16">${body}</symbol>`);
  const f = Object.entries(filled).map(([id, body]) => `<symbol id="${id}" viewBox="0 0 16 16">${body}</symbol>`);
  return `<svg class="sprite" aria-hidden="true" focusable="false">\n${[...s, ...f].join('\n')}\n</svg>`;
}

export const iconIds = Object.keys(stroke);
