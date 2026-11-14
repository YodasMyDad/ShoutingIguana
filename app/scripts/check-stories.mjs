// axe on every story, in both themes: the built Storybook (`npm run build-storybook`) served from storybook-static,
// each story opened in Chromium and checked at WCAG 2.2 AA, as the end-to-end tests check the app. Zero violations
// allowed; a story that throws fails too. With --shots=<folder> it also screenshots each story in each theme.
//
//   npm run test:stories
//   npm run test:stories -- --filter=design-system --shots=test-results/design-system
//   npm run test:stories -- --forced            # under forced colours (Windows contrast themes)
//   npm run test:stories -- --reduced-motion    # with the OS asking for reduced motion
//
// Options: --port (6006 by default; SI_STORYBOOK_PORT too), --filter (a story id contains it), --themes=light,dark,
// --density=standard, --wait (ms to let a story settle, 700 by default).
import { chromium } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import { createReadStream, existsSync, mkdirSync, readFileSync, statSync } from 'node:fs';
import { createServer } from 'node:http';
import path from 'node:path';
import { whenAxeIsFree } from './lib/axe.mjs';

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
const still = args['reduced-motion'] === 'true';
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

// Run in the page: the story's root has content, or its dialog or menu has opened in a portal outside it.
const RENDERED = `(document.querySelector('#storybook-root')?.childElementCount ?? 0) > 0 ||
  document.querySelector('[role="dialog"], [role="alertdialog"], [role="menu"]') !== null`;

const browser = await chromium.launch();
const problems = [];
try {
  for (const theme of themes) {
    const context = await browser.newContext({
      viewport: { width: 1440, height: 900 },
      colorScheme: theme === 'dark' ? 'dark' : 'light',
      forcedColors: forced ? 'active' : 'none',
      reducedMotion: still ? 'reduce' : 'no-preference',
    });
    const page = await context.newPage();
    const errors = [];
    page.on('pageerror', (error) => errors.push(error.message));
    for (const story of stories) {
      errors.length = 0;
      await page.goto(
        `http://127.0.0.1:${port}/iframe.html?id=${story.id}&viewMode=story&globals=theme:${theme};density:${density}`,
      );
      // A story has rendered once its root has content, or its dialog or menu has opened in a portal.
      await page.waitForFunction(RENDERED, undefined, { timeout: 15_000 }).catch(() => undefined);
      await page.waitForTimeout(wait);
      // The a11y addon checks the story too, as it renders, with the page's one axe: ours waits for it to be free.
      const results = await whenAxeIsFree(() =>
        new AxeBuilder({ page })
          .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'])
          .exclude('#storybook-docs')
          // A sandboxed frame with scripts off (the Report's document) can't run axe: checking it never ends.
          .exclude('iframe[sandbox]')
          .analyze(),
      );
      for (const violation of results.violations) {
        const where = violation.nodes.map((node) => node.target.join(' ')).join('; ');
        problems.push(`${theme} ${story.id}: ${violation.id} (${violation.help}): ${where}`);
      }
      for (const error of errors) problems.push(`${theme} ${story.id}: the story threw: ${error}`);
      // A story that failed to render shows Storybook's error display, which axe passes (it was missed so: a story
      // whose component needed a QueryClient it wasn't given).
      const failed = await page.evaluate(() =>
        document.body.classList.contains('sb-show-errordisplay')
          ? (document.getElementById('error-message')?.textContent ?? 'it failed to render')
          : null,
      );
      if (failed) problems.push(`${theme} ${story.id}: the story didn't render: ${failed.trim()}`);
      if (shots) {
        const suffix = `${theme}${forced ? '-forced' : ''}${still ? '-still' : ''}${density === 'standard' ? '' : `-${density}`}`;
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
  `${stories.length} stories, ${themes.length} theme${themes.length === 1 ? '' : 's'}${forced ? ', forced colours' : ''}${still ? ', reduced motion' : ''}: ${problems.length === 0 ? 'no violations' : `${problems.length} problems`}`,
);
for (const problem of problems) console.log(`  ${problem}`);
process.exitCode = problems.length === 0 ? 0 : 1;
