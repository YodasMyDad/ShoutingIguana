// Screenshots rows deep into a 1M-row grid to see whether row positions stay exact at large scroll offsets.
// Usage: node bench/precision.mjs [query string extras, e.g. pos=top]
import path from 'node:path';
import { chromium } from 'playwright';
import { startSidecar } from './sidecar.mjs';
import { serveDist } from './static.mjs';
import { sleep } from './scenarios.mjs';

const root = path.resolve(import.meta.dirname, '..');
const extra = process.argv[2] ?? '';
const web = await serveDist(path.join(root, 'dist'));
const sidecar = await startSidecar({ allowedOrigins: [web.origin] });
const browser = await chromium.launch({ channel: 'chromium', headless: true });
const report = [];
for (const dpr of [1, 1.25, 1.5]) {
  const context = await browser.newContext({ viewport: { width: 1536, height: 864 }, deviceScaleFactor: dpr });
  const page = await context.newPage();
  await page.goto(`${web.origin}/?port=${sidecar.port}&token=${sidecar.token}&live=0&charts=none&${extra}`);
  await page.waitForFunction(() => window.__spike.marks.firstRowsPainted !== undefined);
  for (const index of [300_000, 500_000, 700_000, 999_999]) {
    await page.evaluate((i) => window.__spike.jumpTo(i), index);
    await sleep(400);
    // Row tops on screen, relative to the header bottom: they should be exact multiples of 28 px.
    const geometry = await page.evaluate(() => {
      const header = document.querySelector('.head-row').getBoundingClientRect();
      const grid = document.querySelector('.grid').getBoundingClientRect();
      const rows = [...document.querySelectorAll('.row')]
        .map((row) => ({ index: Number(row.dataset.index), top: row.getBoundingClientRect().top }))
        .filter((r) => r.top >= header.bottom - 1 && r.top < grid.bottom - 28)
        .sort((a, b) => a.index - b.index);
      const gaps = rows.slice(1).map((r, i) => Number((r.top - rows[i].top).toFixed(2)));
      return { headerOffset: Number((header.top - grid.top).toFixed(2)), gaps: [...new Set(gaps)] };
    });
    const file = path.join(root, 'results', `precision-dpr${dpr}-${index}${extra ? '-' + extra.replace(/[^a-z0-9]/gi, '') : ''}.png`);
    await page.screenshot({ path: file, clip: { x: 230, y: 50, width: 520, height: 260 } });
    report.push({ dpr, index, ...geometry });
  }
  await context.close();
}
console.log(JSON.stringify(report));
await browser.close();
await sidecar.stop();
await web.close();
