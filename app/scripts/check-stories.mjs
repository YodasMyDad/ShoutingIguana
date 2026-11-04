// axe on every story, in both themes: the built Storybook (`npm run build-storybook`) served from storybook-static,
// each story opened in Chromium and checked at WCAG 2.2 AA, as the end-to-end tests check the app. Zero violations
// allowed; a story that throws fails too. With --shots=<folder> it also screenshots each story in each theme.
//
//   npm run test:stories
//   npm run test:stories -- --filter=design-system --shots=test-results/design-system
//   npm run test:stories -- --forced            # under forced colours (Windows contrast themes)
//
// Options: --port (6006 by default; SI_STORYBOOK_PORT too), --filter (a story id contains it), --themes=light,dark,
// --density=standard, --wait (ms to let a story settle, 700 by default).
import { chromium } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import { createReadStream, existsSync, mkdirSync, readFileSync, statSync } from 'node:fs';
import { createServer } from 'node:http';
import path from 'node:path';

const appDir = path.resolve(import.meta.dirname, '..');
const root = path.join(appDir, 'storybook-static');
const args = Object.fromEntries(
  process.argv.slice(2).map((arg) => {
    const [key, value = 'true'] = arg.replace(/^--/, '').split('=');
    return [key, value];
  }),
);
const port = Number(args.port ?? process.env.SI_STORYBOOK_PORT ?? 6006);
const themes = (args.themes ?? 'light,dark').split(',');
const density = args.density ?? 'standard';
const forced = args.forced === 'true';
const wait = Number(args.wait ?? 700);
const shots = args.shots ? path.resolve(appDir, args.shots) : null;

if (!existsSync(path.join(root, 'index.json'))) {
  console.error('No storybook-static/index.json: run `npm run build-storybook` first.');
  process.exit(2);
}

const TYPES = {
  '.html': 'text/html',
  '.js': 'text/javascript',
  '.mjs': 'text/javascript',
  '.css': 'text/css',
  '.json': 'application/json',
  '.woff2': 'font/woff2',
  '.png': 'image/png',
  '.svg': 'image/svg+xml',
};
const server = createServer((request, response) => {
  let file = path.join(root, decodeURIComponent(new URL(request.url ?? '/', 'http://localhost').pathname));
  if (!file.startsWith(root)) {
    response.writeHead(403).end();
    return;
  }
  if (existsSync(file) && statSync(file).isDirectory()) file = path.join(file, 'index.html');
  if (!existsSync(file)) {
    response.writeHead(404).end();
    return;
  }
  response.writeHead(200, { 'content-type': TYPES[path.extname(file)] ?? 'application/octet-stream' });
  createReadStream(file).pipe(response);
});
await new Promise((resolve) => server.listen(port, '127.0.0.1', () => resolve(undefined)));

const index = JSON.parse(readFileSync(path.join(root, 'index.json'), 'utf8'));
const stories = Object.values(index.entries).filter(
  (entry) => entry.type === 'story' && (!args.filter || entry.id.includes(args.filter)),
);
if (shots) mkdirSync(shots, { recursive: true });

const browser = await chromium.launch();
const problems = [];
try {
  for (const theme of themes) {
    const context = await browser.newContext({
      viewport: { width: 1440, height: 900 },
      colorScheme: theme === 'dark' ? 'dark' : 'light',
      forcedColors: forced ? 'active' : 'none',
    });
    const page = await context.newPage();
    const errors = [];
    page.on('pageerror', (error) => errors.push(error.message));
    for (const story of stories) {
      errors.length = 0;
      await page.goto(
        `http://127.0.0.1:${port}/iframe.html?id=${story.id}&viewMode=story&globals=theme:${theme};density:${density}`,
      );
      await page.waitForSelector('#storybook-root > *', { timeout: 15_000 }).catch(() => undefined);
      await page.waitForTimeout(wait);
      const results = await new AxeBuilder({ page })
        .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'])
        .exclude('#storybook-docs')
        .analyze();
      for (const violation of results.violations) {
        const where = violation.nodes.map((node) => node.target.join(' ')).join('; ');
        problems.push(`${theme} ${story.id}: ${violation.id} (${violation.help}): ${where}`);
      }
      for (const error of errors) problems.push(`${theme} ${story.id}: the story threw: ${error}`);
      if (shots) {
        const suffix = `${theme}${forced ? '-forced' : ''}${density === 'standard' ? '' : `-${density}`}`;
        await page.screenshot({ path: path.join(shots, `${story.id}-${suffix}.png`), fullPage: true });
      }
    }
    await context.close();
  }
} finally {
  await browser.close();
  server.close();
}

console.log(
  `${stories.length} stories, ${themes.length} theme${themes.length === 1 ? '' : 's'}${forced ? ', forced colours' : ''}: ${problems.length === 0 ? 'no violations' : `${problems.length} problems`}`,
);
for (const problem of problems) console.log(`  ${problem}`);
process.exitCode = problems.length === 0 ? 0 : 1;
