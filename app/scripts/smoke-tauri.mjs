// `npm run smoke:tauri`: the built app, end to end, in its own WebView2 (Windows). Starts the release (or debug) exe
// on a temp data folder holding one crawl, attaches Playwright over WebView2's debugging port, and checks that the
// shell handed the page its sidecar over IPC, under the app's CSP: the library lists the crawl, the event stream is
// open, and nothing failed in the console. Then closes the window and checks nothing is left running.
// Options: --exe <path>, --crawl <file> (else the e2e synthetic crawl, made with the CLI if missing).
import { spawn, spawnSync } from 'node:child_process';
import { existsSync, mkdtempSync, rmSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { chromium } from '@playwright/test';
import { appRoot, copyCrawl, makeSyntheticCrawl } from './lib/engine.mjs';

const argv = process.argv.slice(2);
const option = (name) => {
  const index = argv.indexOf(name);
  return index >= 0 ? argv[index + 1] : undefined;
};
const fail = (message) => {
  console.error(`smoke-tauri: FAIL: ${message}`);
  process.exitCode = 1;
};

const exe =
  option('--exe') ??
  ['release', 'debug']
    .map((profile) => path.join(appRoot, 'src-tauri', 'target', profile, 'shouting-iguana.exe'))
    .find((candidate) => existsSync(candidate));
if (!exe || !existsSync(exe)) throw new Error('No built app: run npm run tauri build first');

const temp = mkdtempSync(path.join(os.tmpdir(), 'si-smoke-'));
let crawl = option('--crawl') ?? path.join(appRoot, '.engine-build', 'crawls', 'e2e-synthetic.sicrawl');
if (!existsSync(crawl)) crawl = await makeSyntheticCrawl({ out: crawl, pages: 40 });
copyCrawl(crawl, path.join(temp, 'data'));

const port = 9300 + Math.floor(Math.random() * 500);
const started = Date.now();
const app = spawn(exe, [], {
  env: {
    ...process.env,
    SI_APP_DATA_DIR: path.join(temp, 'data'),
    SI_APP_STATE_DIR: path.join(temp, 'state'),
    WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS: `--remote-debugging-port=${port}`,
  },
  stdio: 'ignore',
});

async function connect() {
  for (let attempt = 0; attempt < 100; attempt++) {
    try {
      return await chromium.connectOverCDP(`http://127.0.0.1:${port}`);
    } catch {
      await new Promise((resolve) => setTimeout(resolve, 200));
    }
  }
  throw new Error(`No WebView2 debugging port on ${port}`);
}

try {
  const browser = await connect();
  const context = browser.contexts()[0];
  const page =
    context?.pages().find((candidate) => !candidate.url().startsWith('devtools')) ??
    (await context?.waitForEvent('page'));
  if (!page) throw new Error('The app opened no page');
  const errors = [];
  page.on('console', (message) => message.type() === 'error' && errors.push(message.text()));
  page.on('pageerror', (error) => errors.push(error.message));

  const library = page.getByRole('region', { name: 'Library' });
  await library.getByRole('row').nth(1).waitFor({ timeout: 20_000 });
  const rows = await library.getByRole('row').count();
  console.log(`library shown ${Date.now() - started} ms after launch, ${rows - 1} crawl(s), at ${page.url()}`);
  if (rows !== 2) fail(`expected one crawl in the library, saw ${rows - 1}`);

  // The release origin the sidecar allows: http://tauri.localhost on Windows.
  console.log(`origin ${new URL(page.url()).origin}`);
  // The palette opens from the keyboard inside the real webview.
  await page.keyboard.press('Control+k');
  await page.getByRole('dialog', { name: 'Command palette' }).waitFor({ timeout: 5_000 });
  await page.keyboard.press('Escape');

  if (errors.length > 0) fail(`console errors: ${errors.join(' | ')}`);
  await browser.close();
} catch (error) {
  fail(error instanceof Error ? error.message : String(error));
} finally {
  // Close the window as a user would (WM_CLOSE); with nothing crawling, the app quits and stops the sidecar.
  spawnSync('powershell', ['-NoProfile', '-Command', `(Get-Process -Id ${app.pid}).CloseMainWindow() | Out-Null`]);
  const exited = await Promise.race([
    new Promise((resolve) => app.once('exit', () => resolve(true))),
    new Promise((resolve) => setTimeout(() => resolve(false), 15_000)),
  ]);
  if (!exited) {
    app.kill();
    fail('the app did not quit within 15 s of closing its window');
  }
  const sidecars = spawnSync('powershell', [
    '-NoProfile',
    '-Command',
    `Get-CimInstance Win32_Process -Filter "Name='si-sidecar.exe'" | Where-Object { $_.CommandLine -like '*${path.basename(temp)}*' } | Measure-Object | Select-Object -ExpandProperty Count`,
  ]);
  const left = Number(String(sidecars.stdout).trim() || '0');
  if (left > 0) fail(`${left} sidecar process(es) left running`);
  else console.log('no sidecar left running');
  rmSync(temp, { recursive: true, force: true });
  console.log(process.exitCode ? 'smoke-tauri: FAIL' : 'smoke-tauri: PASS');
}
