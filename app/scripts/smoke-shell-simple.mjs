// `npm run smoke:shell` walks Simple after Advanced (or `node scripts/smoke-shell.mjs --mode simple`): the built app in
// its own WebView2, as a first-time user meets it. A new profile (so it opens in Simple) on an empty data folder:
//   first start (Home, its window named "Home · Shouting Iguana"); a check of the engine's test site from Home;
//   the window closed with the check going on: hidden to the tray, with Simple's notice ("Still checking") and tray
//   tooltip ("checking …"), the finish notice in Simple's words; back through the tray icon onto the Summary, its
//   score; the Report; quitting with a check running asks "Stop checking and quit?" (Cancel, then Stop and quit);
//   nothing left running.
// It never crawls a real site. Not run in CI: it needs a desktop session. Options as smoke-shell.mjs's (--exe, --port,
// --pages, --shots, --keep).
/* global document, window -- the functions handed to page.evaluate run in the page */
import { execFileSync, spawn, spawnSync } from 'node:child_process';
import { existsSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { createInterface } from 'node:readline';
import { chromium } from '@playwright/test';
import { appRoot, buildCli } from './lib/engine.mjs';
import { Walk, appPage, builtApp, parseOptions, shellEnvironment, verdict } from './lib/shell.mjs';

if (process.platform !== 'win32') throw new Error('smoke-shell walks the Windows shell (WebView2): run it on Windows');
const options = parseOptions(
  process.argv.slice(2).filter((argument, index, all) => argument !== '--mode' && all[index - 1] !== '--mode'),
);
const exe = options.exe ?? builtApp(appRoot, existsSync);
if (!exe || !existsSync(exe)) throw new Error('No built app: run npm run tauri build first');

const temp = mkdtempSync(path.join(os.tmpdir(), 'si-shell-simple-'));
// An empty data folder, and no preferences: the app's first start, in Simple.
mkdirSync(path.join(temp, 'data'), { recursive: true });
if (options.shots) mkdirSync(options.shots, { recursive: true });
// A small site, so the check ends while the window is in the tray.
const pages = options.pages === 20_000 ? 300 : options.pages;

const port = options.port ?? 9800 + Math.floor(Math.random() * 500);
const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
const walk = new Walk(console.log);
const expectThat = (condition, message) => {
  if (!condition) throw new Error(message);
};
const until = async (what, check, timeoutMs = 15_000) => {
  const started = Date.now();
  for (;;) {
    const value = await check();
    if (value) return value;
    if (Date.now() - started > timeoutMs) throw new Error(`${what} didn't happen within ${timeoutMs / 1000} s`);
    await sleep(150);
  }
};

const app = spawn(exe, [], { env: shellEnvironment({ folder: temp, port }), stdio: 'ignore' });
const alive = () => app.exitCode === null && app.signalCode === null;
/** The window from outside (scripts/shell-window.ps1): what DevTools can't reach. */
function shellWindow(command, argument) {
  const args = ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', path.join(appRoot, 'scripts', 'shell-window.ps1')];
  const result = spawnSync(
    'powershell',
    [...args, command, String(app.pid), ...(argument === undefined ? [] : [argument])],
    {
      encoding: 'utf8',
    },
  );
  if (result.status !== 0)
    throw new Error(`shell-window ${command}: ${(result.stderr || result.stdout).trim().split('\n')[0]}`);
  return command === 'state' ? JSON.parse(result.stdout) : undefined;
}
/** The shell's own log, in the run's profile: what it said outside the window (notices, the tray). */
const shellLog = () => {
  const folder = path.join(temp, 'profile', 'logs');
  if (!existsSync(folder)) return '';
  return readdirSync(folder)
    .filter((file) => file.startsWith('shell'))
    .map((file) => readFileSync(path.join(folder, file), 'utf8'))
    .join('\n');
};
const sidecars = () =>
  Number(
    execFileSync('powershell', [
      '-NoProfile',
      '-Command',
      `(Get-CimInstance Win32_Process -Filter "Name='si-sidecar.exe'" | Where-Object { $_.CommandLine -like '*${path.basename(temp)}*' } | Measure-Object).Count`,
    ])
      .toString()
      .trim() || '0',
  );

let browser;
let page;
let testSite;
const errors = [];
try {
  await walk.step(
    'first start: the app opens in Simple, on Home, and names its window',
    async () => {
      browser = await until(
        'the DevTools port',
        () => chromium.connectOverCDP(`http://127.0.0.1:${port}`).catch(() => null),
        30_000,
      );
      page = await until('the app’s page', () => appPage(browser.contexts().flatMap((context) => context.pages())));
      await page.emulateMedia({ colorScheme: null, reducedMotion: null, forcedColors: null });
      page.setDefaultTimeout(10_000);
      page.on('pageerror', (error) => errors.push(error.message));
      await page.getByRole('main').getByRole('textbox', { name: 'Web address' }).waitFor({ timeout: 30_000 });
      const mode = await page.getByRole('banner').getByRole('radio', { name: 'Simple' }).getAttribute('aria-checked');
      expectThat(mode === 'true', 'the app didn’t open in Simple');
      const title = await until('the window’s title', () => {
        const state = shellWindow('state');
        return state.title === 'Home · Shouting Iguana' ? state.title : null;
      });
      if (options.shots) await page.screenshot({ path: path.join(options.shots, 'simple-01-first-start.png') });
      return `Simple, Home, the window "${title}"`;
    },
    { needed: true },
  );

  /** The engine as the shell handed it to the page. */
  const engine = async (route, init = {}) => {
    const now = await page.evaluate(() => window.__TAURI_INTERNALS__.invoke('sidecar_state'));
    return fetch(`http://127.0.0.1:${now.port}${route}`, {
      ...init,
      headers: { Authorization: `Bearer ${now.token}`, ...(init.body ? { 'Content-Type': 'application/json' } : {}) },
    });
  };
  let url = '';
  let crawlId = '';
  await walk.step(
    'a check of the engine’s test site, started from Home',
    async () => {
      testSite = spawn(buildCli(), ['testsite', '--pages', String(pages), '--port', '0'], {
        stdio: ['ignore', 'pipe', 'ignore'],
        windowsHide: true,
      });
      url = await new Promise((resolve, reject) => {
        const timer = setTimeout(() => reject(new Error('testsite wrote no READY line within 30 s')), 30_000);
        createInterface({ input: testSite.stdout }).on('line', (line) => {
          if (!line.startsWith('READY ')) return;
          clearTimeout(timer);
          resolve(line.slice('READY '.length).split(' ')[0]);
        });
      });
      // Saved defaults: Fast and a limit, so the check ends while the window is in the tray.
      const saved = await engine('/api/crawl-options/defaults', {
        method: 'PUT',
        body: JSON.stringify({ concurrency: 8, maxRequestsPerSecond: 0, maxUrls: 1_500 }),
      });
      expectThat(saved.ok, `the defaults weren't saved (${saved.status})`);
      const address = page.getByRole('main').getByRole('textbox', { name: 'Web address' });
      await address.fill(url);
      await address.press('Enter');
      await page.waitForURL(/\/crawls\/[0-9a-f]{16}\/overview$/);
      crawlId = /crawls\/([0-9a-f]{16})/.exec(page.url())[1];
      await page
        .getByRole('main')
        .getByRole('heading', { level: 1, name: /^Checking / })
        .waitFor({ timeout: 15_000 });
      if (options.shots) await page.screenshot({ path: path.join(options.shots, 'simple-02-checking.png') });
      return `check ${crawlId} of ${url}`;
    },
    { needed: true },
  );

  await walk.step(
    'closed with the check going on: hidden to the tray, in Simple’s words; the finish notice says it is checked',
    async () => {
      const state = await page.evaluate(() => window.__TAURI_INTERNALS__.invoke('sidecar_state'));
      const asked = async () =>
        (
          await fetch(`http://127.0.0.1:${state.port}/api/crawls/${crawlId}`, {
            headers: { Authorization: `Bearer ${state.token}` },
          })
        ).json();
      expectThat(
        ['running', 'analyzing'].includes((await asked()).status),
        `the check is ${(await asked()).status}: it ended too soon`,
      );
      shellWindow('close');
      await until('the window hiding', () => !shellWindow('state').window);
      expectThat(alive(), 'the app quit instead of hiding');
      const notice = await until('the hide-to-tray notice', () => /Notification: (Still [^:]+): (.+)/.exec(shellLog()));
      expectThat(
        notice[1] === 'Still checking' || notice[1] === 'Still working out the results',
        `the notice says "${notice[1]}"`,
      );
      expectThat(!/crawl|analys/i.test(notice[0]), `the notice speaks Advanced: "${notice[0]}"`);
      const tray = /Tray icon shown: "([^"]+)"/.exec(shellLog());
      expectThat(tray !== null && !/crawl|URL|analys/.test(tray[1]), `the tray's tooltip: "${tray?.[1]}"`);
      // The check ends in the tray: the system notice says so in Simple's words.
      const finished = await until(
        'the finish notice',
        () =>
          /Notification: (\S+ is checked): (.+)/.exec(shellLog()) ?? /Notification: (The check of .+)/.exec(shellLog()),
        180_000,
      );
      return `"${notice[1]}: ${notice[2]}"; tray "${tray[1]}"; then "${finished[1]}${finished[2] ? `: ${finished[2]}` : ''}"`;
    },
  );

  await walk.step('back through the tray icon, onto the Summary and its score', async () => {
    shellWindow('tray-click');
    await until('the window coming back', () => shellWindow('state').window);
    if (page.isClosed() || !browser.isConnected()) {
      await browser.close().catch(() => undefined);
      browser = await until('the DevTools port again', () =>
        chromium.connectOverCDP(`http://127.0.0.1:${port}`).catch(() => null),
      );
      page = await until('the app’s page again', () =>
        appPage(browser.contexts().flatMap((context) => context.pages())),
      );
      page.on('pageerror', (error) => errors.push(error.message));
    }
    await until('the page answering', () =>
      Promise.race([page.evaluate(() => document.visibilityState === 'visible'), sleep(1000).then(() => false)]),
    );
    const score = page.getByRole('main').getByRole('img', { name: /^Score \d+ out of 100/ });
    await score.waitFor({ timeout: 60_000 });
    const named = await score.getAttribute('aria-label');
    const title = await until('the window’s title', () => {
      const now = shellWindow('state');
      return now.title.startsWith('Summary · ') ? now.title : null;
    });
    if (options.shots) await page.screenshot({ path: path.join(options.shots, 'simple-03-summary.png') });
    return `${named}; the window "${title}"`;
  });

  await walk.step('the Report shows the engine’s report', async () => {
    await page
      .getByRole('navigation', { name: 'Main' })
      .getByRole('link', { name: /^Report/ })
      .click();
    const frame = page.frameLocator('iframe[title^="The report for"]');
    await frame.getByRole('heading', { level: 1 }).waitFor({ timeout: 20_000 });
    const title = await until('the window’s title', () => {
      const now = shellWindow('state');
      return now.title.startsWith('Report · ') ? now.title : null;
    });
    if (options.shots) await page.screenshot({ path: path.join(options.shots, 'simple-04-report.png') });
    return `the report, the window "${title}"`;
  });

  await walk.step(
    'quitting with a check running asks "Stop checking and quit?"; Cancel keeps both, then it quits',
    async () => {
      const started = await engine('/api/crawls', {
        method: 'POST',
        body: JSON.stringify({ options: { startUrl: url, concurrency: 1, maxRequestsPerSecond: 1 } }),
      });
      expectThat(started.ok, `the second check didn't start (${started.status})`);
      const second = (await started.json()).id;
      await until(
        'the second check running',
        async () => (await (await engine(`/api/crawls/${second}`)).json()).status === 'running',
      );
      const ask = async () => {
        await page.keyboard.press('Control+k');
        const palette = page.getByRole('dialog', { name: 'Command palette' });
        await palette.waitFor();
        await page.keyboard.type('Quit Shouting Iguana');
        await palette
          .getByRole('option', { name: /Quit Shouting Iguana/ })
          .first()
          .waitFor();
        await page.keyboard.press('Enter');
        return until('the question', () => shellWindow('state').dialog);
      };
      const question = await ask();
      expectThat(question === 'Stop checking and quit?', `the dialog is "${question}"`);
      shellWindow('press', 'Cancel');
      await until('the question going', () => shellWindow('state').dialog === null);
      expectThat(alive(), 'Cancel didn’t keep the app');
      await ask();
      const pressed = Date.now();
      shellWindow('press', 'Stop and quit');
      await until('the app quitting', () => !alive(), 40_000);
      return `"${question}", Cancel kept it; Stop and quit: gone ${Date.now() - pressed} ms later`;
    },
  );

  await walk.step('nothing is left running, and the page raised no error', async () => {
    await until('the engine going', () => sidecars() === 0, 10_000);
    expectThat(errors.length === 0, `page errors: ${errors.join(' | ')}`);
  });
} finally {
  await browser?.close().catch(() => undefined);
  testSite?.kill();
  if (alive()) spawnSync('taskkill', ['/PID', String(app.pid), '/T', '/F']);
  if (!options.keep) rmSync(temp, { recursive: true, force: true, maxRetries: 5, retryDelay: 500 });
  else console.log(`the run's folder is kept: ${temp}`);
  console.log(verdict(walk));
  process.exitCode = walk.failed === 0 ? 0 : 1;
}
