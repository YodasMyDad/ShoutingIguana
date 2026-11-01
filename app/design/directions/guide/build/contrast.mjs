// Computes WCAG 2.2 contrast ratios for the token pairs this direction uses, in both themes, from tokens.css.
// Usage: node build/contrast.mjs            prints a summary and exits 1 on any failure
//        node build/contrast.mjs --markdown prints the README tables
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const css = fs.readFileSync(path.join(here, '..', 'tokens.css'), 'utf8');

const tokens = {};
for (const m of css.matchAll(/--([a-z0-9-]+):\s*([^;]+);/g)) {
  const [, name, raw] = m;
  const pair = raw.match(/^light-dark\(\s*(#[0-9A-Fa-f]{6,8})\s*,\s*(#[0-9A-Fa-f]{6,8})\s*\)$/);
  if (pair) tokens[name] = { light: pair[1], dark: pair[2] };
  else if (/^#[0-9A-Fa-f]{6,8}$/.test(raw.trim())) tokens[name] = { light: raw.trim(), dark: raw.trim() };
}

const parse = hex => {
  const h = hex.replace('#', '');
  return { r: parseInt(h.slice(0, 2), 16), g: parseInt(h.slice(2, 4), 16), b: parseInt(h.slice(4, 6), 16),
    a: h.length === 8 ? parseInt(h.slice(6, 8), 16) / 255 : 1 };
};
const over = (fg, bg) => ({ r: fg.r * fg.a + bg.r * (1 - fg.a), g: fg.g * fg.a + bg.g * (1 - fg.a),
  b: fg.b * fg.a + bg.b * (1 - fg.a), a: 1 });
const lum = c => {
  const ch = v => { const s = v / 255; return s <= 0.04045 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4; };
  return 0.2126 * ch(c.r) + 0.7152 * ch(c.g) + 0.0722 * ch(c.b);
};
const ratio = (fgHex, bgHex) => {
  const bg = parse(bgHex);
  const fg = over(parse(fgHex), bg);
  const [l1, l2] = [lum(fg), lum(bg)].sort((a, b) => b - a);
  return (l1 + 0.05) / (l2 + 0.05);
};

// [foreground token, background token, need, note]. need: 4.5 text, 3 large text / graphics / boundaries.
const pairs = [
  ['text', 'surface', 4.5, 'Body text'],
  ['text', 'canvas', 4.5, 'Body text on the canvas'],
  ['text', 'surface-subtle', 4.5, 'Text in panes and grid headers'],
  ['text', 'surface-sunken', 4.5, 'Text in wells and code'],
  ['text', 'surface-selected', 4.5, 'Text in a selected row'],
  ['text-secondary', 'surface', 4.5, 'Secondary text'],
  ['text-secondary', 'surface-subtle', 4.5, 'Secondary text in panes'],
  ['text-secondary', 'surface-selected', 4.5, 'Secondary text in a selected row'],
  ['text-muted', 'surface', 4.5, 'Muted text, captions'],
  ['text-muted', 'canvas', 4.5, 'Muted text on the canvas'],
  ['text-muted', 'surface-subtle', 4.5, 'Muted text in panes'],
  ['text-muted', 'surface-sunken', 4.5, 'Placeholder text in fields'],
  ['text-muted', 'surface-hover', 4.5, 'Muted text in a hovered row'],
  ['text-muted', 'surface-selected', 4.5, 'Muted text in a selected row'],
  ['accent-text', 'surface', 4.5, 'Links'],
  ['accent-text', 'surface-subtle', 4.5, 'Links in panes'],
  ['accent-text', 'accent-tint', 4.5, 'Links on accent tint'],
  ['text', 'accent-tint', 4.5, 'Filter chip text'],
  ['on-accent', 'accent', 4.5, 'Primary button text'],
  ['on-accent', 'accent-hover', 4.5, 'Primary button text, hover'],
  ['on-accent', 'accent-press', 4.5, 'Primary button text, pressed'],
  ['error', 'surface', 4.5, 'Error word and icon'],
  ['error', 'error-tint', 4.5, 'Error on its tint'],
  ['warning', 'surface', 4.5, 'Warning word and icon'],
  ['warning', 'warning-tint', 4.5, 'Warning on its tint'],
  ['notice', 'surface', 4.5, 'Notice word and icon'],
  ['notice', 'notice-tint', 4.5, 'Notice on its tint'],
  ['neutral-mark', 'surface', 4.5, 'Skipped and passed marks'],
  ['error', 'surface-selected', 4.5, 'Error in a selected row'],
  ['warning', 'surface-selected', 4.5, 'Warning in a selected row'],
  ['notice', 'surface-selected', 4.5, 'Notice in a selected row'],
  ['sev-glyph', 'error', 3, 'Glyph inside the error icon'],
  ['sev-glyph', 'warning', 3, 'Glyph inside the warning icon'],
  ['sev-glyph', 'notice', 3, 'Glyph inside the notice icon'],
  ['status-2xx', 'surface', 4.5, '2xx'],
  ['status-3xx', 'surface', 4.5, '3xx'],
  ['status-4xx', 'surface', 4.5, '4xx'],
  ['status-5xx', 'surface', 4.5, '5xx'],
  ['status-none', 'surface', 4.5, 'No response'],
  ['status-2xx', 'surface-subtle', 4.5, '2xx in panes'],
  ['status-4xx', 'surface-subtle', 4.5, '4xx in panes'],
  ['border-control', 'surface', 3, 'Field and checkbox boundaries'],
  ['border-control', 'surface-subtle', 3, 'Field boundaries in panes'],
  ['focus', 'surface', 3, 'Focus ring'],
  ['focus', 'canvas', 3, 'Focus ring on the canvas'],
  ['focus', 'surface-selected', 3, 'Focus ring in a selected row'],
  ['accent', 'surface', 3, 'Accent bars and the selected-row edge'],
  ['series', 'surface', 3, 'Live data series'],
  ['ink-soft', 'surface', 1, 'Decorative: gridlines, connectors (no requirement)'],
];

// The sidebar is navy in both themes and resolves light-dark() pairs to their dark values.
const navPairs = [
  ['nav-text', 'nav-bg', 4.5, 'Navigation text'],
  ['nav-text', 'nav-bg-hover', 4.5, 'Navigation text, hover'],
  ['nav-text', 'nav-bg-active', 4.5, 'Navigation text, current'],
  ['nav-text-muted', 'nav-bg', 4.5, 'Counts and meta'],
  ['nav-text-muted', 'nav-bg-hover', 4.5, 'Counts, hover'],
  ['nav-text-muted', 'nav-bg-active', 4.5, 'Counts, current'],
  ['error', 'nav-bg', 4.5, 'Error mark in the sidebar'],
  ['warning', 'nav-bg', 4.5, 'Warning mark in the sidebar'],
  ['notice', 'nav-bg', 4.5, 'Notice mark in the sidebar'],
  ['accent', 'nav-bg', 3, 'Current-section bar'],
  ['focus', 'nav-bg', 3, 'Focus ring in the sidebar'],
  ['accent', 'nav-line', 3, 'Progress bar on its track'],
];

const rows = [];
for (const theme of ['light', 'dark']) {
  for (const [fg, bg, need, note] of pairs) {
    const r = ratio(tokens[fg][theme], tokens[bg][theme]);
    rows.push({ theme, fg, bg, fgHex: tokens[fg][theme], bgHex: tokens[bg][theme], r, need, note });
  }
}
for (const [fg, bg, need, note] of navPairs) {
  const r = ratio(tokens[fg].dark, tokens[bg].dark);
  rows.push({ theme: 'sidebar', fg, bg, fgHex: tokens[fg].dark, bgHex: tokens[bg].dark, r, need, note });
}

const failures = rows.filter(x => x.r < x.need);
if (process.argv.includes('--tokens')) {
  // Every colour token, light and dark, in tokens.css order.
  console.log('| Token | Light | Dark |');
  console.log('| --- | --- | --- |');
  for (const [name, v] of Object.entries(tokens)) {
    console.log(`| \`--${name}\` | ${v.light} | ${v.dark === v.light ? 'same' : v.dark} |`);
  }
} else if (process.argv.includes('--markdown')) {
  for (const theme of ['light', 'dark', 'sidebar']) {
    console.log(`\n**${theme === 'sidebar' ? 'Sidebar (both themes)' : theme[0].toUpperCase() + theme.slice(1)}**\n`);
    console.log('| Foreground | Background | Ratio | Needs | Use |');
    console.log('| --- | --- | ---: | ---: | --- |');
    for (const x of rows.filter(y => y.theme === theme)) {
      const needs = x.need === 1 ? 'none' : `${x.need}:1`;
      console.log(`| \`${x.fg}\` ${x.fgHex} | \`${x.bg}\` ${x.bgHex} | ${x.r.toFixed(2)}:1 | ${needs} | ${x.note} |`);
    }
  }
} else {
  for (const x of rows) {
    const flag = x.r < x.need ? 'FAIL' : 'ok  ';
    console.log(`${flag} ${x.theme.padEnd(7)} ${x.r.toFixed(2).padStart(5)} (${x.need})  ${x.fg} ${x.fgHex} on ${x.bg} ${x.bgHex}`);
  }
  console.log(failures.length ? `\n${failures.length} failing pairs` : '\nAll pairs pass');
}
process.exitCode = failures.length ? 1 : 0;
