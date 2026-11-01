// Screenshots every page in both themes at 1440 x 900, device scale factor 1, into screens/<page>-<theme>.png.
//   node app/design/directions/precision/shoot.mjs [--size 1280x800] [--out <dir>] [page ...]
// Uses the spike's Playwright and the shared Chromium cache. Animations are stopped, so the shots are repeatable.

import { mkdirSync } from 'node:fs';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { dirname, join } from 'node:path';
import { chromium } from '../../../spike/node_modules/playwright/index.mjs';

const here = dirname(fileURLToPath(import.meta.url));
const args = process.argv.slice(2);
const opt = (name, fallback) => {
  const i = args.indexOf(name);
  if (i < 0) return fallback;
  const value = args[i + 1];
  args.splice(i, 2);
  return value;
};
const [width, height] = opt('--size', '1440x900').split('x').map(Number);
const outDir = opt('--out', join(here, 'screens'));
const pages = args.length ? args : ['home', 'live', 'issues', 'urls', 'urls-loading', 'palette'];
mkdirSync(outDir, { recursive: true });

const browser = await chromium.launch();
for (const theme of ['dark', 'light']) {
  const context = await browser.newContext({ viewport: { width, height }, deviceScaleFactor: 1, colorScheme: theme });
  for (const name of pages) {
    const page = await context.newPage();
    const errors = [];
    page.on('pageerror', (e) => errors.push(e.message));
    page.on('console', (m) => m.type() === 'error' && errors.push(m.text()));
    await page.goto(pathToFileURL(join(here, `${name}.html`)).href, { waitUntil: 'networkidle' });
    await page.evaluate(() => document.fonts.ready);
    await page.waitForTimeout(600);
    const file = join(outDir, `${name}-${theme}.png`);
    await page.screenshot({ path: file, animations: 'disabled' });
    console.log(file, errors.length ? `errors: ${errors.join(' | ')}` : '');
    await page.close();
  }
  await context.close();
}
await browser.close();
