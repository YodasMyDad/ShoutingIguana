// Usage: node drive.mjs <flow.mjs> [mode=advanced] [width=2560] [height=1380]
// A flow module default-exports async ({ page, shot, base, log }) => {}.
import { pathToFileURL } from 'node:url';
import path from 'node:path';
import { chromium } from 'file:///C:/Projects/ShoutingIguana/.claude/worktrees/ui-step-3/app/node_modules/playwright/index.mjs';

const [, , flowFile, mode = 'advanced', width = '2560', height = '1380'] = process.argv;
const base = process.env.SI_BASE ?? 'http://localhost:49874';
const shotsDir = path.resolve(path.dirname(new URL(import.meta.url).pathname.slice(1)), '..', 'shots');

const browser = await chromium.launch();
const context = await browser.newContext({ viewport: { width: +width, height: +height }, deviceScaleFactor: 1 });
await context.addInitScript(
  ([m]) => {
    if (!sessionStorage.getItem('audit.seeded')) {
      localStorage.setItem('si.settings', JSON.stringify({ mode: m, theme: 'light' }));
      sessionStorage.setItem('audit.seeded', '1');
    }
  },
  [mode],
);
const page = await context.newPage();
page.on('pageerror', (e) => console.log('PAGEERROR', e.message));
const shot = async (name, opts = {}) => {
  const file = path.join(shotsDir, `${name}.png`);
  await page.screenshot({ path: file, ...opts });
  console.log('shot', file);
};
const log = (...a) => console.log(...a);
try {
  const flow = (await import(pathToFileURL(path.resolve(flowFile)).href)).default;
  await flow({ page, shot, base, log, context });
} catch (e) {
  console.log('FLOW ERROR', e.message.split('\n').slice(0, 6).join('\n'));
  await shot('error').catch(() => {});
} finally {
  await browser.close();
}
