// Writes the token and contrast tables into ../README.md, between the marker comments,
// straight from ../tokens.css, so the README can't drift from the tokens.
// Usage: node build/tokens-doc.mjs
import { readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
import { parseTokens, ratio } from './contrast.mjs';

const here = dirname(fileURLToPath(import.meta.url));
const root = join(here, '..');
const css = readFileSync(join(root, 'tokens.css'), 'utf8');
const rootBlock = css.slice(css.indexOf(':root {'), css.indexOf(':root[data-theme="light"]'));
const { tokens, resolve } = parseTokens(css);

const decl = name => rootBlock.match(new RegExp(`--${name}:\\s*([^;]+);`))?.[1].trim().replace(/\s+/g, ' ');
const names = prefix => [...rootBlock.matchAll(new RegExp(`--(${prefix}[a-z0-9-]*):`, 'g'))].map(m => m[1]);

// ---- Colours
const uses = {
  'color-canvas': 'The app frame: sidebar, top bar, page background',
  'color-surface': 'Cards, grids, inputs',
  'color-surface-raised': 'The inspector',
  'color-surface-overlay': 'Popovers, menus, the palette',
  'color-surface-sunken': 'Table headers in groups, the explanation band, keycaps',
  'color-surface-hover': 'Hovered rows and controls; tracks of segmented controls',
  'color-surface-active': 'Pressed controls',
  'color-surface-selected': 'Selected rows and list items',
  'color-border-subtle': 'Row rules, dividers inside cards',
  'color-border': 'Card and button edges',
  'color-border-strong': 'Hovered edges, dashed "add" chips',
  'color-border-control': 'Inputs, toggles, the selected segment (3:1)',
  'color-text': 'Primary text and figures',
  'color-text-secondary': 'Labels, supporting text',
  'color-text-muted': 'Meta text, units, placeholders',
  'color-text-on-accent': 'Text on primary buttons',
  'color-brand': 'The logo blue, as sampled',
  'color-iguana': 'The logo dark, as sampled',
  'color-accent': 'Selected-row rule, tab underline, bars, chart lines',
  'color-accent-text': 'Links, the current nav icon, the running pill',
  'color-accent-fill': 'Primary buttons',
  'color-accent-fill-hover': 'Primary buttons, hovered',
  'color-accent-subtle': 'Tinted backgrounds for live and selected things',
  'color-accent-border': 'Edges of tinted accent things; queued bars',
  'color-focus': 'Focus rings',
};
function colourTable() {
  const rows = Object.keys(tokens).map(n => {
    const l = resolve('light', n), d = resolve('dark', n);
    const use = uses[n] ?? (n.match(/-(error|warning|notice)/) ? 'Severity, only' : n.match(/-(2xx|3xx|4xx|5xx|no-response)/) ? 'Status-code class, only' : n.includes('chart') ? 'Charts' : n.includes('skeleton') ? 'Skeletons' : '');
    return `| \`--${n}\` | ${l} | ${d} | ${use} |`;
  });
  return ['| Token | Light | Dark | Use |', '| --- | --- | --- | --- |', ...rows].join('\n');
}

// ---- Type, spacing, radii, elevation, motion
function typeTable() {
  const roles = { 11: 'Chart axes, tab counts, keycaps', 12: 'Meta, captions, column headers, chips (small)', 13: 'Secondary UI, buttons, chips, lists', 14: 'Body and grid cells (comfortable)', 16: 'Running crawl name, the URL field', 18: 'Section titles, the result count', 21: 'Check titles, inspector figures', 28: 'Page titles; figures at 1280 wide', 36: 'KPI figures', 48: 'Reserved for a single hero figure' };
  const rows = names('text-').map(n => {
    const size = decl(n), lead = decl(n.replace('text', 'leading'));
    return `| \`--${n}\` | ${size} | ${lead} | ${roles[parseInt(n.split('-')[1], 10)] ?? ''} |`;
  });
  return ['| Token | Size | Line height | Used for |', '| --- | --- | --- | --- |', ...rows].join('\n') +
    `\n\nWeights: ${names('weight-').map(n => `\`--${n}\` ${decl(n)}`).join(', ')}. Tracking: ${names('tracking-').map(n => `\`--${n}\` ${decl(n)}`).join(', ')}. Inter's optical sizes are on (\`font-optical-sizing: auto\`), so titles and figures get its display cut. Every number column and figure uses tabular, lining figures.`;
}
const simple = (prefix, head) => ['| Token | Value |', '| --- | --- |', ...names(prefix).map(n => `| \`--${n}\` | ${decl(n)} |`)].join('\n');
function elevationTable() {
  const what = { 0: 'Chrome on the canvas', 1: 'Cards, grids, the current nav item', 2: 'The inspector, above the grid; the URL field', 3: 'Popovers and menus', 4: 'The command palette and dialogs' };
  return ['| Token | Light and dark (light-dark() per colour) | Level |', '| --- | --- | --- |',
    ...names('elevation-').map(n => `| \`--${n}\` | \`${decl(n)}\` | ${what[n.split('-')[1]] ?? 'Buttons and chips'} |`),
    ...names('highlight-').map(n => `| \`--${n}\` | \`${decl(n)}\` | A top highlight: on in dark, off in light (accent: both) |`)].join('\n');
}
function motionTable() {
  const what = { 'duration-instant': 'Hover and press feedback', 'duration-quick': 'Popovers, menus, tabs', 'duration-panel': 'The inspector and drawers', 'duration-skeleton-delay': 'A skeleton appears only after this', 'duration-shimmer': 'One shimmer sweep (off with reduced motion)', 'duration-pulse': 'The live dot (off with reduced motion)', 'ease-out': 'Everything that moves', 'ease-in-out': 'Reserved for things that move and come back' };
  return ['| Token | Value | Use |', '| --- | --- | --- |', ...[...names('duration-'), ...names('ease-')].map(n => `| \`--${n}\` | ${decl(n)} | ${what[n] ?? ''} |`)].join('\n');
}

// ---- Contrast, as matrices
const hex = (theme, n) => resolve(theme, `color-${n}`);
function parse(v) { const h = v.replace('#', ''); return [0, 2, 4].map(i => parseInt(h.slice(i, i + 2), 16)); }
const r = (theme, fg, bg) => {
  const f = hex(theme, fg), b = hex(theme, bg);
  if (!f.startsWith('#') || !b.startsWith('#')) return '';
  return ratio(parse(f), parse(b));
};
const cell = (v, min) => (v === '' ? '' : `${v.toFixed(2)}${v < min ? ' **fail**' : ''}`);
function matrix(theme, fgs, bgs, min, label) {
  const head = `| ${label} | ${bgs.map(b => `\`${b}\``).join(' | ')} |`;
  const sep = `| --- | ${bgs.map(() => '---:').join(' | ')} |`;
  const rows = fgs.map(f => `| \`${f}\` | ${bgs.map(b => cell(r(theme, f, b), min)).join(' | ')} |`);
  return [head, sep, ...rows].join('\n');
}
function ownSubtle(theme, names, min) {
  const rows = names.map(n => {
    const t = `${n}-text`, s = `${n}-subtle`;
    return `| \`${n}\` | ${cell(r(theme, t, 'surface'), min)} | ${cell(r(theme, t, 'surface-raised'), min)} | ${cell(r(theme, t, 'surface-selected'), min)} | ${cell(r(theme, t, s), min)} | ${cell(r(theme, n === 'warning' ? 'warning-edge' : n, 'surface'), 3)} |`;
  });
  return ['| Colour | Text on `surface` | on `surface-raised` | on `surface-selected` | on its own `-subtle` | Mark or icon on `surface` (3:1) |', '| --- | ---: | ---: | ---: | ---: | ---: |', ...rows].join('\n');
}
function contrastDoc() {
  const out = [];
  for (const theme of ['light', 'dark']) {
    out.push(`#### ${theme === 'light' ? 'Light' : 'Dark'}\n`);
    out.push('Text needs 4.5:1.\n');
    out.push(matrix(theme, ['text', 'text-secondary', 'text-muted', 'accent-text'], ['canvas', 'surface', 'surface-raised', 'surface-overlay', 'surface-sunken', 'surface-hover', 'surface-selected'], 4.5, 'Text'));
    out.push(`\nOn the primary button: \`text-on-accent\` on \`accent-fill\` ${r(theme, 'text-on-accent', 'accent-fill').toFixed(2)}, on \`accent-fill-hover\` ${r(theme, 'text-on-accent', 'accent-fill-hover').toFixed(2)}.\n`);
    out.push('Severity and status colours: their text shade needs 4.5:1, their mark or icon 3:1.\n');
    out.push(ownSubtle(theme, ['error', 'warning', 'notice', '2xx', '3xx', '4xx', '5xx', 'no-response'], 4.5));
    const fill = r(theme, 'warning', 'surface');
    out.push(`\nThe warning triangle's amber fill is ${fill.toFixed(2)}:1 on \`surface\`${fill < 3 ? '; its darker edge (`warning-edge`, above) carries the 3:1' : ''}, and its glyph is ${r(theme, 'warning-glyph', 'warning').toFixed(2)}:1 on the fill. The error and notice glyphs are ${r(theme, 'surface', 'error').toFixed(2)}:1 and ${r(theme, 'surface', 'notice').toFixed(2)}:1 on their shapes.\n`);
    out.push('Boundaries and indicators need 3:1.\n');
    out.push(matrix(theme, ['border-control', 'focus', 'accent', 'chart-secondary'], ['canvas', 'surface', 'surface-raised', 'surface-sunken', 'surface-hover', 'surface-selected'], 3, 'Non-text'));
    out.push('');
  }
  return out.join('\n');
}

const between = (text, tag, body) => text.replace(new RegExp(`(<!-- ${tag}:start -->)[\\s\\S]*?(<!-- ${tag}:end -->)`), `$1\n${body}\n$2`);
let readme = readFileSync(join(root, 'README.md'), 'utf8');
readme = between(readme, 'colours', colourTable());
readme = between(readme, 'type', typeTable());
readme = between(readme, 'spacing', simple('space-'));
readme = between(readme, 'radii', simple('radius-'));
readme = between(readme, 'elevation', elevationTable());
readme = between(readme, 'motion', motionTable());
readme = between(readme, 'contrast', contrastDoc());
writeFileSync(join(root, 'README.md'), readme);
console.log('README tables written');
