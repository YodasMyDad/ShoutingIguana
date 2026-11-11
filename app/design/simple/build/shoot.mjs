// Screenshots the Simple mockups into ../screens/ (gitignored), with Playwright.
//   node build/shoot.mjs                 every page, light and dark, at 1440x900
//   node build/shoot.mjs --shell         the shell at 1280x720, narrow (900x720) and 200% zoom
//   node build/shoot.mjs --report        the report as it prints: one PNG a page, two-page spreads, and report.pdf
//   node build/shoot.mjs --full          full-page captures of the long pages, into ../screens/full/ (for review)
//   node build/shoot.mjs summary.html    only these pages
// Playwright isn't a dependency of the mockups: set SI_PLAYWRIGHT to a folder that has node_modules/playwright
// (app/spike, once `npm ci` has run there).
import { createRequire } from 'node:module';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { dirname, join } from 'node:path';
import { mkdirSync, writeFileSync, existsSync, readFileSync } from 'node:fs';
import { pages as allPages } from './build.mjs';

const here = dirname(fileURLToPath(import.meta.url));
const root = join(here, '..');
const screens = join(root, 'screens');
const candidates = [process.env.SI_PLAYWRIGHT, join(root, '..', '..', 'spike'), join(root, '..', '..')].filter(Boolean);
const base = candidates.find(c => existsSync(join(c, 'node_modules', 'playwright')));
if (!base) { console.error('Playwright not found. Set SI_PLAYWRIGHT to a folder with node_modules/playwright.'); process.exit(1); }
const { chromium } = createRequire(join(base, 'package.json'))('playwright');

const args = process.argv.slice(2);
const flag = name => args.includes(`--${name}`);
const files = args.filter(a => !a.startsWith('--'));
const everything = !flag('shell') && !flag('report') && !flag('full');
mkdirSync(screens, { recursive: true });

const browser = await chromium.launch();
async function shoot(file, { theme, width, height, scale = 1, name, fullPage = false, dir = screens }) {
  const context = await browser.newContext({ viewport: { width, height }, deviceScaleFactor: scale, colorScheme: theme });
  const page = await context.newPage();
  await page.goto(`${pathToFileURL(join(root, file)).href}?theme=${theme}`, { waitUntil: 'networkidle' });
  await page.evaluate(() => document.fonts.ready);
  await page.waitForTimeout(350);
  if (fullPage) {
    // The app scrolls inside <main>: let it grow so the whole screen is captured.
    await page.addStyleTag({ content: '.app { height: auto !important; min-height: 100vh; } .main { overflow: visible !important; } .sidebar { position: sticky; top: 0; height: 100vh; }' });
  }
  await page.screenshot({ path: join(dir, name), fullPage });
  const fonts = await page.evaluate(() => [...new Set([...document.fonts].filter(f => f.status === 'loaded').map(f => f.family))].join(', '));
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth);
  console.log(`${name.padEnd(44)} fonts: ${fonts || 'NONE'}${overflow ? '   SIDEWAYS SCROLL' : ''}`);
  await context.close();
}

try {
  if (everything) {
    const list = files.length ? files : Object.keys(allPages).filter(f => f !== 'report.html');
    for (const theme of ['light', 'dark']) {
      for (const file of list) await shoot(file, { theme, width: 1440, height: 900, name: `${file.replace('.html', '')}-${theme}.png` });
    }
  }

  if (flag('shell')) {
    const sizes = [
      { id: '1280x720', width: 1280, height: 720, scale: 1 },
      { id: 'narrow-900', width: 900, height: 720, scale: 1 },
      { id: 'zoom-200', width: 640, height: 360, scale: 2 },   // a 1280x720 window at 200% zoom
    ];
    for (const file of ['summary.html', 'checking.html', 'advanced-live.html', 'advanced-overview.html']) {
      for (const s of sizes) {
        await shoot(file, { theme: 'light', width: s.width, height: s.height, scale: s.scale, name: `shell-${file.replace('.html', '')}-${s.id}-light.png` });
      }
    }
    await shoot('summary.html', { theme: 'dark', width: 640, height: 360, scale: 2, name: 'shell-summary-zoom-200-dark.png' });
    await shoot('advanced-live.html', { theme: 'dark', width: 900, height: 720, name: 'shell-advanced-live-narrow-900-dark.png' });
  }

  if (flag('full')) {
    mkdirSync(join(screens, 'full'), { recursive: true });
    const list = files.length ? files : ['summary.html', 'fix.html', 'problem.html', 'problem-template.html', 'home.html', 'new-simple.html', 'new-advanced.html', 'summary-stopped.html', 'summary-perfect.html', 'checking.html'];
    for (const file of list) await shoot(file, { theme: 'light', width: 1440, height: 900, name: `${file.replace('.html', '')}-full.png`, fullPage: true, dir: join(screens, 'full') });
  }

  if (flag('report')) {
    // The report as it prints. Chromium makes the real PDF; the PNGs lay the same print styles out at A4's content
    // width and cut them where a printer would: before a block that doesn't fit, and at the forced page breaks.
    const A4 = { width: 794, height: 1123, marginX: 57, marginY: 60 };   // 96 dpi; margins as @page: 15 mm and 16 mm
    const context = await browser.newContext({ viewport: { width: A4.width - 2 * A4.marginX, height: A4.height }, deviceScaleFactor: 2, colorScheme: 'light' });
    const page = await context.newPage();
    await page.goto(pathToFileURL(join(root, 'report.html')).href, { waitUntil: 'networkidle' });
    await page.pdf({ path: join(screens, 'report.pdf'), format: 'A4', printBackground: true });
    const pdfPages = (readFileSync(join(screens, 'report.pdf'), 'latin1').match(/\/Type\s*\/Page[^s]/g) ?? []).length;
    await page.emulateMedia({ media: 'print' });
    const units = await page.evaluate(() => [...document.querySelectorAll('[data-unit]')].map(el => {
      const r = el.getBoundingClientRect();
      return { top: r.top + scrollY, bottom: r.bottom + scrollY, breakBefore: el.hasAttribute('data-break'), keep: el.hasAttribute('data-keep') };
    }));
    const total = await page.evaluate(() => document.documentElement.scrollHeight);
    const room = A4.height - 2 * A4.marginY;
    const cuts = [0];
    for (let i = 0; i < units.length; i++) {
      const start = cuts.at(-1);
      if (!units[i].breakBefore && units[i].bottom - start <= room) continue;
      // A heading stays with what follows it: cut before the headings that lead up to this block.
      let at = i;
      while (!units[i].breakBefore && at > 0 && units[at - 1].keep && units[at - 1].top - 2 > start) at--;
      if (units[at].top - 2 > start) cuts.push(Math.floor(units[at].top - 2));
    }
    cuts.push(total);
    const slices = [];
    for (let i = 0; i < cuts.length - 1; i++) {
      const name = `report-print-p${i + 1}.png`;
      const height = Math.min(cuts[i + 1] - cuts[i], room);
      await page.screenshot({ path: join(screens, name), fullPage: true, clip: { x: 0, y: cuts[i], width: A4.width - 2 * A4.marginX, height } });
      slices.push({ name, height });
    }
    await context.close();
    console.log(`report: ${slices.length} pages laid out here, ${pdfPages} in report.pdf`);

    // A print preview: the pages as sheets of A4, for the gallery.
    const sheet = (s, i) => `<figure class="sheet"><img src="${s.name}" width="${A4.width - 2 * A4.marginX}" height="${s.height}" alt="Page ${i + 1}"><figcaption>${i + 1} of ${slices.length}</figcaption></figure>`;
    const preview = (from, count) => `<!doctype html><html lang="en-GB"><meta charset="utf-8"><title>The report, as it prints</title><style>
      body { margin: 0; background: #E9E9EC; font: 12px/1 system-ui, sans-serif; color: #68686F; }
      .spread { display: flex; gap: 36px; justify-content: center; padding: 34px 30px; zoom: 0.74; }
      .sheet { position: relative; flex: none; box-sizing: border-box; margin: 0; width: ${A4.width}px; height: ${A4.height}px; padding: ${A4.marginY}px ${A4.marginX}px; background: #fff; box-shadow: 0 2px 4px rgb(16 16 20 / 0.06), 0 12px 32px -8px rgb(16 16 20 / 0.18); }
      .sheet img { display: block; }
      figcaption { position: absolute; left: 0; right: 0; bottom: 22px; text-align: center; font-size: 12px; }
    </style><div class="spread">${slices.slice(from, from + count).map((s, i) => sheet(s, from + i)).join('')}</div></html>`;
    const spreads = [['report-print-light.png', 0], ['report-print-2-light.png', 2], ['report-print-3-light.png', slices.length - 2]];
    for (const [name, from] of spreads) {
      if (from >= slices.length) continue;
      const html = join(screens, name.replace('.png', '.html'));
      writeFileSync(html, preview(from, 2));
      const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 }, deviceScaleFactor: 1 });
      const p = await ctx.newPage();
      await p.goto(pathToFileURL(html).href, { waitUntil: 'networkidle' });
      await p.screenshot({ path: join(screens, name) });
      await ctx.close();
      console.log(name);
    }
  }
} finally {
  await browser.close();
}
