// The one source of the precision tokens. build.mjs writes tokens.css from this file, and the README's token and
// contrast tables come from it too, so the numbers in the README are the numbers in the CSS.
//
// Neutrals are generated in OKLCH on the iguana's own hue (#162832 is OKLCH 0.266 0.030 234), so every grey in both
// themes leans the same way as the logo's navy. Semantic colours are picked in OKLCH too, then checked by contrast.mjs.

import { oklchToHex as o, over } from './color.mjs';

export const IGUANA_HUE = 234;
const H = IGUANA_HUE;

export const brand = {
  blue: '#2B93FF', // the logo's hexagon
  navy: '#162832', // the iguana
};

// Colour roles. Each theme defines the same names; components only ever use these.
function darkTheme() {
  const t = {
    // Surfaces: the chassis frames the app (sidebar, top bar); the canvas is where the data sits.
    'bg-chassis': o(0.222, 0.028, H),
    'bg-canvas': o(0.248, 0.031, H),
    'bg-raised': o(0.28, 0.033, H),
    'bg-hover': o(0.294, 0.034, H),
    'bg-active': o(0.322, 0.035, H),
    'bg-tooltip': o(0.375, 0.033, H),
    // Lines: hairline dividers, a stronger rule, and control boundaries that meet 3:1.
    line: o(0.342, 0.03, H),
    'line-subtle': o(0.288, 0.031, H),
    'line-strong': o(0.412, 0.03, H),
    'line-control': o(0.60, 0.03, H),
    // Ink.
    'text-strong': o(0.965, 0.006, H),
    text: o(0.9, 0.01, H),
    'text-muted': o(0.765, 0.018, H),
    'text-faint': o(0.705, 0.02, H),
    'text-on-accent': o(0.17, 0.02, H),
    'text-tooltip': o(0.965, 0.006, H),
    // The accent: focus, selection and the primary action only.
    accent: brand.blue,
    'accent-hover': o(0.70, 0.17, 252),
    'accent-text': o(0.76, 0.13, 252),
    focus: o(0.72, 0.16, 252),
    // Severity.
    'sev-error': o(0.7, 0.17, 25),
    'sev-warning': o(0.8, 0.14, 78),
    'sev-notice': o(0.76, 0.075, 212),
    // Status-code classes.
    // Inside the dark band (OKLCH L 0.48 to 0.67) and checked for colour-blind separation, since they sit side by side.
    'http-2xx': o(0.665, 0.14, 155),
    'http-3xx': o(0.66, 0.14, 300),
    'http-4xx': o(0.665, 0.15, 62),
    'http-5xx': o(0.62, 0.2, 8),
    'http-none': o(0.66, 0.02, H),
    // Measured quantities in charts and gauges: an ordinal ramp of the navy, never the accent.
    'data-1': o(0.87, 0.018, H), // done: crawled, the value needle
    'data-2': o(0.6, 0.03, H), // waiting: queued
    'data-3': o(0.455, 0.03, H), // context: the acceptable band
    'data-grid': o(0.322, 0.029, H),
    // Skeletons.
    skeleton: o(0.322, 0.034, H),
    'skeleton-shine': o(0.37, 0.036, H),
  };
  t['line-inverse'] = over(t['text-tooltip'], 0.35, t['bg-tooltip']);
  t['line-on-accent'] = over(t['text-on-accent'], 0.3, t['accent']);
  t['selection'] = over(brand.blue, 0.16, t['bg-canvas']);
  t['selection-hover'] = over(brand.blue, 0.2, t['bg-canvas']);
  t['tint-error'] = over(t['sev-error'], 0.16, t['bg-canvas']);
  t['tint-warning'] = over(t['sev-warning'], 0.14, t['bg-canvas']);
  t['tint-notice'] = over(t['sev-notice'], 0.14, t['bg-canvas']);
  t['tint-4xx'] = over(t['http-4xx'], 0.16, t['bg-canvas']);
  t['tint-5xx'] = over(t['http-5xx'], 0.16, t['bg-canvas']);
  return t;
}

function lightTheme() {
  const t = {
    'bg-chassis': o(0.966, 0.006, H),
    'bg-canvas': o(0.997, 0.0015, H),
    'bg-raised': o(0.975, 0.004, H),
    'bg-hover': o(0.955, 0.006, H),
    'bg-active': o(0.93, 0.008, H),
    'bg-tooltip': brand.navy,
    line: o(0.91, 0.009, H),
    'line-subtle': o(0.945, 0.006, H),
    'line-strong': o(0.85, 0.012, H),
    'line-control': o(0.64, 0.02, H),
    'text-strong': brand.navy,
    text: o(0.33, 0.026, H),
    'text-muted': o(0.485, 0.022, H),
    'text-faint': o(0.53, 0.02, H),
    'text-on-accent': o(0.17, 0.02, H),
    'text-tooltip': o(0.975, 0.004, H),
    accent: brand.blue,
    'accent-hover': o(0.62, 0.18, 254),
    'accent-text': o(0.52, 0.16, 254),
    focus: o(0.56, 0.17, 254),
    'sev-error': o(0.55, 0.19, 25),
    'sev-warning': o(0.62, 0.13, 70),
    'sev-notice': o(0.55, 0.08, 212),
    'http-2xx': o(0.57, 0.13, 155),
    'http-3xx': o(0.53, 0.17, 300),
    'http-4xx': o(0.62, 0.15, 62),
    'http-5xx': o(0.52, 0.2, 8),
    'http-none': o(0.58, 0.02, H),
    'data-1': o(0.36, 0.026, H),
    'data-2': o(0.625, 0.024, H),
    'data-3': o(0.86, 0.012, H),
    'data-grid': o(0.93, 0.008, H),
    skeleton: o(0.928, 0.008, H),
    'skeleton-shine': o(0.965, 0.005, H),
  };
  t['line-inverse'] = over(t['text-tooltip'], 0.35, t['bg-tooltip']);
  t['line-on-accent'] = over(t['text-on-accent'], 0.3, t['accent']);
  t['selection'] = over(brand.blue, 0.1, t['bg-canvas']);
  t['selection-hover'] = over(brand.blue, 0.14, t['bg-canvas']);
  t['tint-error'] = over(t['sev-error'], 0.1, t['bg-canvas']);
  t['tint-warning'] = over(t['sev-warning'], 0.12, t['bg-canvas']);
  t['tint-notice'] = over(t['sev-notice'], 0.1, t['bg-canvas']);
  t['tint-4xx'] = over(t['http-4xx'], 0.12, t['bg-canvas']);
  t['tint-5xx'] = over(t['http-5xx'], 0.1, t['bg-canvas']);
  return t;
}

export const themes = { dark: darkTheme(), light: lightTheme() };

export const shadows = {
  dark: {
    popover: '0 0 0 1px rgb(0 0 0 / 0.2), 0 8px 24px rgb(2 9 13 / 0.55)',
    palette: '0 0 0 1px rgb(0 0 0 / 0.25), 0 24px 64px rgb(2 9 13 / 0.6)',
    scrim: 'rgb(4 12 17 / 0.55)',
  },
  light: {
    popover: '0 1px 2px rgb(22 40 50 / 0.08), 0 8px 24px rgb(22 40 50 / 0.14)',
    palette: '0 2px 6px rgb(22 40 50 / 0.08), 0 24px 64px rgb(22 40 50 / 0.22)',
    scrim: 'rgb(22 40 50 / 0.4)',
  },
};

// Everything that doesn't change with the theme.
export const scale = {
  font: {
    'font-sans': '"Inter", "Segoe UI Variable Text", "Segoe UI", system-ui, sans-serif',
    'font-mono': '"JetBrains Mono", "Cascadia Mono", Consolas, ui-monospace, monospace',
  },
  // One size carries almost everything; hierarchy comes from weight and ink. Line heights sit on the 4 px grid.
  type: [
    { name: 'micro', size: 11, leading: 16, use: 'Key chips, gauge scale labels' },
    { name: 'small', size: 12, leading: 16, use: 'Column headers, meta, pills, URLs in mono' },
    { name: 'base', size: 13, leading: 20, use: 'Everything else: rows, body, buttons, navigation' },
    { name: 'title', size: 16, leading: 24, use: 'The one title of a detail pane' },
    { name: 'field', size: 18, leading: 24, use: 'The URL field on Home' },
    { name: 'readout', size: 28, leading: 32, use: 'Readouts: counters on the live crawl, a check\'s page count' },
    { name: 'hero', size: 48, leading: 52, use: 'The one hero figure per view (crawled, while live)' },
  ],
  weight: { regular: 400, medium: 500, semibold: 600 },
  tracking: [
    { name: 'hero', value: '-0.025em', use: 'The hero figure' },
    { name: 'tight', value: '-0.015em', use: 'Readouts and titles' },
  ],
  measure: '68ch',
  opacity: [
    { name: 'wash', value: 0.22, use: 'Area fill under a line' },
    { name: 'wash-strong', value: 0.3, use: 'The leading area of a stacked chart' },
  ],
  focus: { width: 2, offset: 2 },
  space: [0, 2, 4, 8, 12, 16, 20, 24, 32, 40, 48, 64],
  radius: [
    { name: 'xs', value: 3, use: 'Key chips, bar segments, small icon buttons' },
    { name: 's', value: 4, use: 'Pills, chips, buttons, fields, rows' },
    { name: 'm', value: 6, use: 'Menus, tooltips, the running crawl panel' },
    { name: 'l', value: 10, use: 'The command palette' },
    { name: 'round', value: 999, use: 'Dots and meters' },
  ],
  motion: [
    { name: 'instant', value: '80ms', use: 'Hover and press' },
    { name: 'quick', value: '150ms', use: 'Menus, tooltips, the palette' },
    { name: 'panel', value: '200ms', use: 'The inspector, folding groups' },
    { name: 'skeleton-delay', value: '150ms', use: 'A skeleton waits this long before it shows' },
    { name: 'skeleton-min', value: '300ms', use: 'Once shown, a skeleton stays at least this long' },
    { name: 'shimmer', value: '1600ms', use: 'One sweep of the skeleton shine (off with reduced motion)' },
    { name: 'pulse', value: '2000ms', use: 'The live dot (off with reduced motion)' },
  ],
  ease: { out: 'cubic-bezier(0.16, 1, 0.3, 1)', 'in-out': 'cubic-bezier(0.65, 0, 0.35, 1)' },
  layout: [
    { name: 'topbar', value: 44, use: 'Top bar and sidebar header' },
    { name: 'sidebar', value: 232, use: 'Sidebar, expanded' },
    { name: 'sidebar-collapsed', value: 52, use: 'Sidebar, icons only' },
    { name: 'inspector', value: 400, use: 'Inspector, docked right' },
    { name: 'checks', value: 368, use: 'The checks list on Issues' },
    { name: 'row', value: 28, use: 'Grid row, compact (the default)' },
    { name: 'row-comfortable', value: 36, use: 'Grid row, comfortable' },
    { name: 'row-head', value: 32, use: 'Grid header row' },
    { name: 'control', value: 28, use: 'Buttons, fields, chips' },
    { name: 'control-s', value: 24, use: 'Small buttons and filter chips' },
    { name: 'control-l', value: 48, use: 'The URL field on Home' },
    { name: 'control-xs', value: 20, use: 'Status-code pills, key chips, chip buttons' },
    { name: 'row-list', value: 32, use: 'List rows with a figure (status codes)' },
    { name: 'bar', value: 40, use: 'Filter bar, the palette footer' },
    { name: 'row-two', value: 48, use: 'Two-line rows: the library' },
    { name: 'kbd', value: 20, use: 'Key chip height' },
    { name: 'chart', value: 196, use: 'Plot height of a full-width chart' },
    { name: 'icon', value: 16, use: 'Icons' },
    { name: 'icon-s', value: 14, use: 'Severity icons, inline icons' },
    { name: 'icon-xs', value: 12, use: 'Glyphs in pills and chips' },
    { name: 'dot', value: 6, use: 'Status-code dot' },
    { name: 'swatch', value: 8, use: 'Legend swatch, live dot' },
    { name: 'label', value: 112, use: 'Label column of a fact list' },
    { name: 'meter', value: 160, use: 'A stand-alone proportion meter' },
    { name: 'search', value: 272, use: 'The search and command trigger' },
    { name: 'menu', value: 224, use: 'Menu minimum width' },
    { name: 'palette', value: 640, use: 'The command palette' },
    { name: 'palette-top', value: 96, use: "The palette’s distance from the top" },
    { name: 'content-max', value: 1152, use: "Home’s content width" },
  ],
};

// Pairs whose contrast the README reports. [foreground, background, minimum, what it is]
export const contrastPairs = [
  ...['text-strong', 'text', 'text-muted', 'text-faint', 'accent-text'].flatMap((fg) =>
    ['bg-chassis', 'bg-canvas', 'bg-raised', 'bg-hover', 'selection'].map((bg) => [fg, bg, 4.5, 'text'])),
  ['text-on-accent', 'accent', 4.5, 'text on the primary button'],
  ['text-tooltip', 'bg-tooltip', 4.5, 'tooltip text'],
  ['text-strong', 'tint-error', 4.5, 'text on an error tint'],
  ['text-strong', 'tint-warning', 4.5, 'text on a warning tint'],
  ['text-strong', 'tint-4xx', 4.5, 'text on a 4xx pill'],
  ['text-strong', 'tint-5xx', 4.5, 'text on a 5xx pill'],
  ['line-control', 'bg-canvas', 3, 'field and chip boundary'],
  ['line-control', 'bg-chassis', 3, 'field and chip boundary'],
  ['accent', 'bg-canvas', 3, 'primary button boundary'],
  ['focus', 'bg-canvas', 3, 'focus ring'],
  ['focus', 'bg-chassis', 3, 'focus ring'],
  ['focus', 'selection', 3, 'focus ring on a selected row'],
  ['data-1', 'bg-canvas', 3, 'gauge needle, crawled'],
  ['data-2', 'bg-canvas', 3, 'queued'],
  ...['sev-error', 'sev-warning', 'sev-notice', 'http-2xx', 'http-3xx', 'http-4xx', 'http-5xx', 'http-none'].flatMap((fg) =>
    ['bg-chassis', 'bg-canvas', 'bg-raised', 'selection'].map((bg) => [fg, bg, 3, 'icon or dot'])),
  ['sev-error', 'tint-error', 3, 'icon on its tint'],
  ['sev-warning', 'tint-warning', 3, 'icon on its tint'],
  ['http-4xx', 'tint-4xx', 3, 'dot on its tint'],
  ['http-5xx', 'tint-5xx', 3, 'dot on its tint'],
];

const px = (v) => `${v}px`;

// Writes tokens.css: the scale on :root, light on :root, dark under prefers-color-scheme unless data-theme="light",
// and again under data-theme="dark".
export function tokensCss() {
  const lines = [];
  const push = (s = '') => lines.push(s);
  const colourBlock = (theme, indent) =>
    Object.entries(themes[theme]).map(([k, v]) => `${indent}--color-${k}: ${v};`)
      .concat(Object.entries(shadows[theme]).map(([k, v]) => `${indent}--${k === 'scrim' ? 'color-scrim' : 'shadow-' + k}: ${v};`));

  push('/* Shouting Iguana, direction A: precision. Generated by build.mjs from lib/tokens.mjs: edit that, not this. */');
  push('/* Neutrals sit on the iguana\'s hue (OKLCH 234). The accent, #2B93FF, marks focus, selection and the primary action only. */');
  push();
  push(':root {');
  push('  color-scheme: light dark;');
  push();
  push('  /* Brand */');
  push(`  --brand-blue: ${brand.blue};`);
  push(`  --brand-navy: ${brand.navy};`);
  push();
  push('  /* Type */');
  for (const [k, v] of Object.entries(scale.font)) push(`  --${k}: ${v};`);
  for (const t of scale.type) push(`  --text-${t.name}: ${px(t.size)};  --leading-${t.name}: ${px(t.leading)};`);
  for (const [k, v] of Object.entries(scale.weight)) push(`  --weight-${k}: ${v};`);
  for (const t of scale.tracking) push(`  --tracking-${t.name}: ${t.value};`);
  push(`  --measure: ${scale.measure};`);
  for (const o of scale.opacity) push(`  --opacity-${o.name}: ${o.value};`);
  push();
  push('  /* Space: a 4 px grid (space-1 is 4 px; space-0-5 is the 2 px hairline offset) */');
  push('  --space-0: 0;');
  push('  --space-0-5: 2px;');
  for (const [i, v] of scale.space.entries()) if (v >= 4) push(`  --space-${v / 4}: ${px(v)};`);
  push();
  push('  /* Focus: the one loud use of the accent */');
  push(`  --focus-width: ${px(scale.focus.width)};`);
  push(`  --focus-offset: ${px(scale.focus.offset)};`);
  push();
  push('  /* Radii: small things get small corners */');
  for (const r of scale.radius) push(`  --radius-${r.name}: ${px(r.value)};`);
  push();
  push('  /* Motion: quick, eased out, for orientation only */');
  for (const m of scale.motion) push(`  --duration-${m.name}: ${m.value};`);
  for (const [k, v] of Object.entries(scale.ease)) push(`  --ease-${k}: ${v};`);
  push();
  push('  /* Layout */');
  for (const l of scale.layout) push(`  --size-${l.name}: ${px(l.value)};`);
  push();
  push('  /* Colour: light */');
  lines.push(...colourBlock('light', '  '));
  push('}');
  push();
  push('@media (prefers-color-scheme: dark) {');
  push('  :root:not([data-theme="light"]) {');
  push('    color-scheme: dark;');
  lines.push(...colourBlock('dark', '    '));
  push('  }');
  push('}');
  push();
  push(':root[data-theme="light"] {');
  push('  color-scheme: light;');
  push('}');
  push();
  push(':root[data-theme="dark"] {');
  push('  color-scheme: dark;');
  lines.push(...colourBlock('dark', '  '));
  push('}');
  push();
  push('/* Windows high contrast: let the system colours through, and keep focus and selection visible. */');
  push('@media (forced-colors: active) {');
  push('  :root {');
  push('    --color-focus: Highlight;');
  push('    --color-accent: Highlight;');
  push('    --color-selection: Highlight;');
  push('    --color-line: CanvasText;');
  push('    --color-line-control: CanvasText;');
  push('  }');
  push('}');
  push();
  return lines.join('\n');
}
