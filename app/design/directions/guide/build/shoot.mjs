// Screenshots every page in both themes at 1440x900, device scale factor 1, into screens/<page>-<theme>.png.
// Uses the spike's Playwright and the shared Chromium. --size 1280x800 writes to screens/1280/ instead.
// --only home,issues limits the pages. --reduced also emulates reduced motion.
import fs from 'node:fs';
import path from 'node:path';
import { createRequire } from 'node:module';
import { fileURLToPath, pathToFileURL } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const dir = path.resolve(here, '..');
const require = createRequire(path.resolve(here, '../../../../spike/package.json'));
const { chromium } = require('playwright');

const arg = (name, fallback) => { const i = process.argv.indexOf(name); return i > 0 ? process.argv[i + 1] : fallback; };
const [width, height] = arg('--size', '1440x900').split('x').map(Number);
const only = arg('--only', '');
const pages = ['home', 'live', 'overview', 'issues', 'urls', 'urls-loading'].filter(p => !only || only.split(',').includes(p));
const out = width === 1440 ? path.join(dir, 'screens') : path.join(dir, 'screens', String(width));
fs.mkdirSync(out, { recursive: true });

const browser = await chromium.launch();
for (const theme of ['light', 'dark']) {
  const context = await browser.newContext({
    viewport: { width, height }, deviceScaleFactor: 1, colorScheme: theme,
    reducedMotion: process.argv.includes('--reduced') ? 'reduce' : 'no-preference',
  });
  for (const name of pages) {
    const page = await context.newPage();
    const errors = [];
    page.on('pageerror', e => errors.push(e.message));
    page.on('console', m => { if (m.type() === 'error') errors.push(m.text()); });
    await page.goto(pathToFileURL(path.join(dir, `${name}.html`)).href, { waitUntil: 'networkidle' });
    await page.evaluate(() => document.fonts.ready);
    await page.waitForTimeout(450);
    const file = path.join(out, `${name}-${theme}.png`);
    await page.screenshot({ path: file });
    console.log(`${path.relative(dir, file)}${errors.length ? `  errors: ${errors.join(' | ')}` : ''}`);
    await page.close();
  }
  await context.close();
}
await browser.close();
