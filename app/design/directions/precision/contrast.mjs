// Computes every contrast ratio the README reports, and writes the token and contrast tables into README.md between
// its <!-- generated:... --> markers. Run after changing lib/tokens.mjs:
//   node app/design/directions/precision/contrast.mjs
// Exits 1 if any pair falls below its minimum.

import { readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
import { themes, shadows, scale, contrastPairs, brand } from './lib/tokens.mjs';
import { contrast } from './lib/color.mjs';

const here = dirname(fileURLToPath(import.meta.url));
const readmePath = join(here, 'README.md');

const roles = {
  'bg-chassis': 'Sidebar and top bar',
  'bg-canvas': 'Where the data sits: main area, inspector',
  'bg-raised': 'Fields, buttons, key chips, the palette',
  'bg-hover': 'Hover',
  'bg-active': 'Pressed; favicon tile',
  'bg-tooltip': 'Tooltips',
  line: 'Hairline dividers',
  'line-subtle': 'Row separators',
  'line-strong': 'Button edges, ticks',
  'line-control': 'Field and chip boundaries (3:1)',
  'line-inverse': 'Key chips on a tooltip',
  'line-on-accent': 'Key chips on the primary button',
  'text-strong': 'Figures, titles, values',
  text: 'Body text',
  'text-muted': 'Labels, secondary text',
  'text-faint': 'Row numbers, axis labels, zero counts',
  'text-on-accent': 'Text on the primary button',
  'text-tooltip': 'Tooltip text',
  accent: 'Primary action fill, selected tab',
  'accent-hover': 'Primary action, hover',
  'accent-text': 'Links, the current section\'s icon',
  focus: 'Focus ring',
  selection: 'Selected row, item or section',
  'selection-hover': 'Selected and hovered; text selection',
  'sev-error': 'Error',
  'sev-warning': 'Warning',
  'sev-notice': 'Notice',
  'tint-error': 'Error background',
  'tint-warning': 'Warning background',
  'tint-notice': 'Notice background',
  'http-2xx': '2xx',
  'http-3xx': '3xx',
  'http-4xx': '4xx',
  'http-5xx': '5xx',
  'http-none': 'No response',
  'tint-4xx': '4xx pill',
  'tint-5xx': '5xx pill',
  'data-1': 'Measured: crawled, gauge needles',
  'data-2': 'Measured: queued',
  'data-3': 'A gauge\'s acceptable band',
  'data-grid': 'Chart gridlines, meter tracks',
  skeleton: 'Skeleton blocks',
  'skeleton-shine': 'Skeleton shimmer',
};

const swatchOrder = Object.keys(themes.dark);
const colourTable = [
  '| Token | Dark | Light | Used for |',
  '| --- | --- | --- | --- |',
  ...swatchOrder.map((k) => `| \`--color-${k}\` | \`${themes.dark[k]}\` | \`${themes.light[k]}\` | ${roles[k] ?? ''} |`),
].join('\n');

const typeTable = [
  '| Token | Size / line height | Used for |',
  '| --- | --- | --- |',
  ...scale.type.map((t) => `| \`--text-${t.name}\` | ${t.size} / ${t.leading} px | ${t.use} |`),
].join('\n');

const spaceList = scale.space.map((v) => (v === 0 ? '`--space-0` 0' : v === 2 ? '`--space-0-5` 2' : `\`--space-${v / 4}\` ${v}`)).join(', ');
const radiusTable = ['| Token | px | Used for |', '| --- | ---: | --- |', ...scale.radius.map((r) => `| \`--radius-${r.name}\` | ${r.value} | ${r.use} |`)].join('\n');
const motionTable = ['| Token | Value | Used for |', '| --- | --- | --- |', ...scale.motion.map((m) => `| \`--duration-${m.name}\` | ${m.value} | ${m.use} |`), ...Object.entries(scale.ease).map(([k, v]) => `| \`--ease-${k}\` | \`${v}\` | ${k === 'out' ? 'Everything that moves' : 'The skeleton sweep'} |`)].join('\n');
const elevationTable = ['| Token | Dark | Light |', '| --- | --- | --- |', ...['popover', 'palette'].map((k) => `| \`--shadow-${k}\` | \`${shadows.dark[k]}\` | \`${shadows.light[k]}\` |`), `| \`--color-scrim\` | \`${shadows.dark.scrim}\` | \`${shadows.light.scrim}\` |`].join('\n');
const layoutTable = ['| Token | px | Used for |', '| --- | ---: | --- |', ...scale.layout.map((l) => `| \`--size-${l.name}\` | ${l.value} | ${l.use} |`)].join('\n');

const tokens = `### Colour

Brand: \`--brand-blue\` \`${brand.blue}\` (the hexagon) and \`--brand-navy\` \`${brand.navy}\` (the iguana). Every neutral is generated
in OKLCH on the iguana's hue, 234, so both themes lean the same way as the logo.

${colourTable}

### Type

Inter for text, JetBrains Mono for URLs and code, with ligatures off so a URL never shows a glyph that isn't in it.
Weights: \`--weight-regular\` ${scale.weight.regular}, \`--weight-medium\` ${scale.weight.medium}, \`--weight-semibold\` ${scale.weight.semibold}.
Tracking: \`--tracking-hero\` ${scale.tracking[0].value}, \`--tracking-tight\` ${scale.tracking[1].value}. Measure: \`--measure\` ${scale.measure}.

${typeTable}

### Spacing

A 4 px grid: ${spaceList}.

### Radii

${radiusTable}

### Elevation

Surfaces are told apart by tone and hairlines. Only things that float get a shadow.

${elevationTable}

### Motion

${motionTable}

### Sizes

Focus ring: \`--focus-width\` ${scale.focus.width} px, \`--focus-offset\` ${scale.focus.offset} px. Washes: ${scale.opacity.map((o) => `\`--opacity-${o.name}\` ${o.value}`).join(', ')}.

${layoutTable}`;

let failures = 0;
const rows = contrastPairs.map(([fg, bg, min, what]) => {
  const d = contrast(themes.dark[fg], themes.dark[bg]);
  const l = contrast(themes.light[fg], themes.light[bg]);
  const ok = d >= min && l >= min;
  if (!ok) failures++;
  return `| \`${fg}\` on \`${bg}\` | ${what} | ${d.toFixed(2)} | ${l.toFixed(2)} | ${min}:1 | ${ok ? 'Pass' : '**Fail**'} |`;
});
const contrastTable = ['| Pair | What | Dark | Light | Needs | Result |', '| --- | --- | ---: | ---: | ---: | --- |', ...rows].join('\n');

let readme = readFileSync(readmePath, 'utf8');
const put = (name, body) => {
  const re = new RegExp(`(<!-- generated:${name} -->)[\\s\\S]*?(<!-- /generated:${name} -->)`);
  if (!re.test(readme)) throw new Error(`README.md has no ${name} markers`);
  readme = readme.replace(re, `$1\n\n${body}\n\n$2`);
};
put('tokens', tokens);
put('contrast', contrastTable);
writeFileSync(readmePath, readme);
console.log(`${contrastPairs.length} pairs, ${failures} below their minimum`);
process.exit(failures ? 1 : 0);
