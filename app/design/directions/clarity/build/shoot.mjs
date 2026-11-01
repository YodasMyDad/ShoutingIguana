// Screenshots every page in both themes at 1440x900, device scale factor 1, into ../screens/.
// Usage: node build/shoot.mjs [page.html ...] [--size=1280x800] [--density=compact] [--suffix=-1280]
import { createRequire } from 'node:module';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { dirname, join } from 'node:path';
import { mkdirSync } from 'node:fs';

const here = dirname(fileURLToPath(import.meta.url));
const root = join(here, '..');
const require = createRequire(join(root, '..', '..', '..', 'spike', 'package.json'));
const { chromium } = require('playwright');

const args = process.argv.slice(2);
const opt = name => args.find(a => a.startsWith(`--${name}=`))?.split('=')[1];
const [width, height] = (opt('size') ?? '1440x900').split('x').map(Number);
const density = opt('density');
const suffix = opt('suffix') ?? '';
const themes = (opt('themes') ?? 'light,dark').split(',');
const files = args.filter(a => !a.startsWith('--'));
const pages = files.length ? files : ['home.html', 'live.html', 'issues.html', 'urls.html', 'urls-loading.html'];

mkdirSync(join(root, 'screens'), { recursive: true });
const browser = await chromium.launch();
try {
  for (const theme of themes) {
    const context = await browser.newContext({ viewport: { width, height }, deviceScaleFactor: 1, colorScheme: theme });
    for (const file of pages) {
      const page = await context.newPage();
      const params = new URLSearchParams({ theme });
      if (density) params.set('density', density);
      await page.goto(`${pathToFileURL(join(root, file)).href}?${params}`, { waitUntil: 'networkidle' });
      await page.evaluate(() => document.fonts.ready);
      await page.waitForTimeout(450);
      const name = `${file.replace('.html', '')}${density ? '-' + density : ''}${suffix}-${theme}.png`;
      await page.screenshot({ path: join(root, 'screens', name) });
      const fonts = await page.evaluate(() => [...document.fonts].filter(f => f.status === 'loaded').map(f => f.family));
      console.log(`${name.padEnd(36)} fonts: ${[...new Set(fonts)].join(', ') || 'NONE LOADED'}`);
      await page.close();
    }
    await context.close();
  }
} finally {
  await browser.close();
}
