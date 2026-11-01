// Computes WCAG 2.2 contrast ratios for the colour tokens in ../tokens.css, in both themes.
// Usage: node build/contrast.mjs            prints a summary and exits 1 on any failure
//        node build/contrast.mjs --markdown prints the README tables
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const css = readFileSync(join(here, '..', 'tokens.css'), 'utf8');

// ---- Parse the :root colour tokens into { light, dark } pairs.
export function parseTokens(source = css) {
  const root = source.slice(source.indexOf(':root {'), source.indexOf(':root[data-theme="light"]'));
  const tokens = {};
  const re = /--(color-[a-z0-9-]+):\s*([^;]+);/g;
  let m;
  while ((m = re.exec(root))) {
    const value = m[2].trim();
    const ld = value.match(/^light-dark\((.+),\s*(rgb\([^)]*\)|#[0-9A-Fa-f]{6})\)$/);
    if (ld) tokens[m[1]] = { light: ld[1].trim(), dark: ld[2].trim() };
    else tokens[m[1]] = { light: value, dark: value };
  }
  const resolve = (theme, name, depth = 0) => {
    const v = tokens[name]?.[theme];
    if (!v) throw new Error(`Unknown token ${name}`);
    const ref = v.match(/^var\(--([a-z0-9-]+)\)$/);
    return ref && depth < 8 ? resolve(theme, ref[1], depth + 1) : v;
  };
  return { tokens, resolve };
}

// ---- Colour maths
function hexToRgb(hex) {
  const h = hex.replace('#', '');
  return [0, 2, 4].map(i => parseInt(h.slice(i, i + 2), 16));
}
function parseColour(v) {
  if (v.startsWith('#')) return { rgb: hexToRgb(v), a: 1 };
  const m = v.match(/rgb\((\d+)\s+(\d+)\s+(\d+)\s*\/\s*([\d.]+)\)/);
  if (m) return { rgb: [+m[1], +m[2], +m[3]], a: +m[4] };
  throw new Error(`Can't parse colour ${v}`);
}
function over(fg, bg) {
  return fg.rgb.map((c, i) => Math.round(c * fg.a + bg[i] * (1 - fg.a)));
}
function luminance([r, g, b]) {
  const f = c => {
    const s = c / 255;
    return s <= 0.04045 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4;
  };
  return 0.2126 * f(r) + 0.7152 * f(g) + 0.0722 * f(b);
}
export function ratio(a, b) {
  const [l1, l2] = [luminance(a), luminance(b)].sort((x, y) => y - x);
  return (l1 + 0.05) / (l2 + 0.05);
}
const toHex = rgb => '#' + rgb.map(c => c.toString(16).padStart(2, '0')).join('').toUpperCase();

// ---- The pairs to check. [foreground, background, minimum, what it is]
const TEXT = 4.5, UI = 3;
const surfaces = ['canvas', 'surface', 'surface-raised', 'surface-overlay', 'surface-sunken', 'surface-hover', 'surface-selected'];
const pairs = [];
for (const fg of ['text', 'text-secondary', 'text-muted'])
  for (const bg of surfaces) pairs.push([`color-${fg}`, `color-${bg}`, TEXT, 'Text']);
for (const bg of ['canvas', 'surface', 'surface-raised', 'surface-hover', 'surface-selected', 'accent-subtle'])
  pairs.push(['color-accent-text', `color-${bg}`, TEXT, 'Link and selected text']);
pairs.push(['color-text-on-accent', 'color-accent-fill', TEXT, 'Primary button']);
pairs.push(['color-text-on-accent', 'color-accent-fill-hover', TEXT, 'Primary button, hover']);
for (const s of ['error', 'warning', 'notice']) {
  for (const bg of ['surface', 'surface-raised', 'surface-selected', `${s}-subtle`])
    pairs.push([`color-${s}-text`, `color-${bg}`, TEXT, 'Severity text']);
}
for (const s of ['2xx', '3xx', '4xx', '5xx', 'no-response']) {
  for (const bg of ['surface', 'surface-raised', 'surface-selected', `${s}-subtle`])
    pairs.push([`color-${s}-text`, `color-${bg}`, TEXT, 'Status text']);
}
// Non-text: component boundaries, focus, marks and chart lines.
for (const bg of ['canvas', 'surface', 'surface-raised', 'surface-sunken'])
  pairs.push(['color-border-control', `color-${bg}`, UI, 'Input boundary']);
for (const bg of ['canvas', 'surface', 'surface-raised', 'surface-selected', 'surface-hover'])
  pairs.push(['color-focus', `color-${bg}`, UI, 'Focus ring']);
for (const bg of ['canvas', 'surface', 'surface-raised', 'surface-selected'])
  pairs.push(['color-accent', `color-${bg}`, UI, 'Accent mark (selected-row rule, bars)']);
for (const s of ['error', 'warning-edge', 'notice'])
  for (const bg of ['surface', 'surface-raised'])
    pairs.push([`color-${s}`, `color-${bg}`, UI, 'Severity icon']);
pairs.push(['color-warning-glyph', 'color-warning', UI, 'Warning icon glyph']);
pairs.push(['color-surface', 'color-error', UI, 'Error icon glyph']);
pairs.push(['color-surface', 'color-notice', UI, 'Notice icon glyph']);
for (const s of ['2xx', '3xx', '4xx', '5xx', 'no-response'])
  for (const bg of ['surface', 'surface-raised'])
    pairs.push([`color-${s}`, `color-${bg}`, UI, 'Status mark and bar']);
for (const bg of ['surface', 'surface-raised'])
  pairs.push(['color-chart-secondary', `color-${bg}`, UI, 'Chart secondary line']);
pairs.push(['color-chart-axis', 'color-surface', TEXT, 'Chart axis labels']);

export function evaluate() {
  const { resolve } = parseTokens();
  const rows = [];
  for (const theme of ['light', 'dark']) {
    for (const [fg, bg, min, what] of pairs) {
      const bgC = parseColour(resolve(theme, bg));
      const bgRgb = bgC.a === 1 ? bgC.rgb : over(bgC, parseColour(resolve(theme, 'color-surface')).rgb);
      const fgRgb = over(parseColour(resolve(theme, fg)), bgRgb);
      const r = ratio(fgRgb, bgRgb);
      rows.push({ theme, fg, bg, fgHex: toHex(fgRgb), bgHex: toHex(bgRgb), ratio: r, min, what, pass: r >= min });
    }
  }
  return rows;
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const rows = evaluate();
  if (process.argv.includes('--markdown')) {
    for (const theme of ['light', 'dark']) {
      console.log(`\n#### ${theme === 'light' ? 'Light' : 'Dark'}\n`);
      console.log('| What | Foreground | Background | Ratio | Needs |');
      console.log('| --- | --- | --- | ---: | ---: |');
      for (const r of rows.filter(x => x.theme === theme))
        console.log(`| ${r.what} | \`--${r.fg}\` ${r.fgHex} | \`--${r.bg}\` ${r.bgHex} | ${r.ratio.toFixed(2)}:1 | ${r.min}:1 |`);
    }
  } else {
    const fails = rows.filter(r => !r.pass);
    const tight = rows.filter(r => r.pass && r.ratio < r.min + 0.25);
    for (const r of fails) console.log(`FAIL ${r.theme.padEnd(5)} ${r.fg} on ${r.bg}: ${r.ratio.toFixed(2)} < ${r.min}  (${r.fgHex} on ${r.bgHex})`);
    for (const r of tight) console.log(`tight ${r.theme.padEnd(5)} ${r.fg} on ${r.bg}: ${r.ratio.toFixed(2)}  (${r.fgHex} on ${r.bgHex})`);
    console.log(`${rows.length} pairs, ${fails.length} failing, ${tight.length} within 0.25 of the minimum`);
    process.exitCode = fails.length ? 1 : 0;
  }
}
