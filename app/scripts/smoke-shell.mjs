// `npm run smoke:shell`: a walk of the five flows in the real shell (Windows: the built app in its own WebView2).
// Where `smoke:tauri` checks that the app starts, this uses it: it starts the release (or debug) exe on a temp data
// folder holding one crawl, with a temp profile so it never touches the user's copy of the app and runs beside it,
// attaches Playwright to WebView2's DevTools port, and walks:
//   the library and the engine the shell handed over; a crawl's answer; a figure to exactly its URLs; the inspector
//   (open, walk a link, back, the HTML source and its search, Esc); Issues; the palette, "Go to URL…" and the
//   shortcut sheet; the browser's own keys being off (F5, F3: pressed in the webview itself, see shell-window.ps1);
//   the engine killed and restarted; a crawl of the engine's test site from Home, paused and resumed with F7; the
//   window closed to the tray with the crawl going on, and back through the tray icon; "Stop crawling and quit?"
//   (Cancel, then Stop and quit); nothing left running.
// It never crawls a real site. Not run in CI: it needs a desktop session (a disconnected one will do).
// Options: --exe <path>, --crawl <file>, --port <n>, --pages <n> (the test site's), --shots <folder> (a picture of
// the page after each step), --skip keys,live,tray,restart, --keep (leave the run's folder behind), --mode both|advanced|simple
// (both, by default: this walk in Advanced, then scripts/smoke-shell-simple.mjs in Simple, each on an app of its own).
/* global document, window -- the functions handed to page.evaluate run in the page */
import { execFileSync, spawn, spawnSync } from 'node:child_process';
import { existsSync, mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { createInterface } from 'node:readline';
import { chromium } from '@playwright/test';
import { appRoot, buildCli, copyCrawl, makeSyntheticCrawl } from './lib/engine.mjs';
import {
  VIRTUAL_KEYS,
  Walk,
  advancedSettings,
  appPage,
  builtApp,
  parseOptions,
  shellEnvironment,
  shellMemory,
  verdict,
} from './lib/shell.mjs';

if (process.platform !== 'win32') throw new Error('smoke-shell walks the Windows shell (WebView2): run it on Windows');
const modeAt = process.argv.indexOf('--mode');
const mode = modeAt === -1 ? 'both' : process.argv[modeAt + 1];
if (!['both', 'advanced', 'simple'].includes(mode))
  throw new Error(`--mode takes both, advanced or simple, not '${mode}'`);
const rest = process.argv
  .slice(2)
  .filter((argument, index, all) => argument !== '--mode' && all[index - 1] !== '--mode');
const simpleWalk = () =>
  spawnSync(process.execPath, [path.join(import.meta.dirname, 'smoke-shell-simple.mjs'), ...rest], { stdio: 'inherit' })
    .status ?? 1;
if (mode === 'simple') process.exit(simpleWalk());
const options = parseOptions(rest);
const exe = options.exe ?? builtApp(appRoot, existsSync);
if (!exe || !existsSync(exe)) throw new Error('No built app: run npm run tauri build first');

const temp = mkdtempSync(path.join(os.tmpdir(), 'si-shell-'));
// Every planted case fetched and its HTML kept, as the end-to-end tests' crawl (and the same cached file).
let crawl = options.crawl ?? path.join(appRoot, '.engine-build', 'crawls', 'e2e-synthetic-full.sicrawl');
if (!existsSync(crawl)) crawl = await makeSyntheticCrawl({ out: crawl, pages: 40, maxUrls: 5_000, storeHtml: true });
copyCrawl(crawl, path.join(temp, 'data'));
// The walk drives Advanced's screens; a new profile would open in Simple.
const settings = advancedSettings(temp);
mkdirSync(path.dirname(settings.file), { recursive: true });
writeFileSync(settings.file, settings.text);
if (options.shots) mkdirSync(options.shots, { recursive: true });

const port = options.port ?? 9300 + Math.floor(Math.random() * 500);
const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
const walk = new Walk(console.log);
const expectThat = (condition, message) => {
  if (!condition) throw new Error(message);
};

/** The window from outside (scripts/shell-window.ps1): what DevTools can't reach. */
function shellWindow(command, argument) {
  const args = ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', path.join(appRoot, 'scripts', 'shell-window.ps1')];
  const result = spawnSync(
    'powershell',
    [...args, command, String(app.pid), ...(argument === undefined ? [] : [argument])],
    { encoding: 'utf8' },
  );
  if (result.status !== 0)
    throw new Error(`shell-window ${command}: ${(result.stderr || result.stdout).trim().split('\n')[0]}`);
  return command === 'state' ? JSON.parse(result.stdout) : undefined;
}
const until = async (what, check, timeoutMs = 15_000) => {
  const started = Date.now();
  for (;;) {
    const value = await check();
    if (value) return value;
    if (Date.now() - started > timeoutMs) throw new Error(`${what} didn't happen within ${timeoutMs / 1000} s`);
    await sleep(150);
  }
};
const alive = () => app.exitCode === null && app.signalCode === null;
/** What the shell and its WebView2 hold now, three ways (shellMemory): one read of every process, from outside. */
const memory = () => {
  const rows = JSON.parse(
    execFileSync('powershell', [
      '-NoProfile',
      '-Command',
      [
        '$private = @{}',
        'Get-CimInstance Win32_PerfRawData_PerfProc_Process | ForEach-Object { $private[[int]$_.IDProcess] = [double]$_.WorkingSetPrivate }',
        'Get-CimInstance Win32_Process | ForEach-Object { [pscustomobject]@{ pid = [int]$_.ProcessId; parent = [int]$_.ParentProcessId; name = $_.Name; workingSet = [double]$_.WorkingSetSize; privateBytes = [double]$_.PrivatePageCount; privateWorkingSet = $private[[int]$_.ProcessId] } } | ConvertTo-Json -Compress',
      ].join('; '),
    ]).toString(),
  );
  return shellMemory(rows, app.pid);
};
const memoryWords = (sample) =>
  `${sample.workingSetMb} MB working set, ${sample.privateWorkingSetMb ?? '–'} MB private working set, ${sample.privateBytesMb} MB private bytes (${sample.processes} processes)`;
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

const launched = Date.now();
const app = spawn(exe, [], { env: shellEnvironment({ folder: temp, port }), stdio: 'ignore' });
let browser;
let page;
let testSite;
/** The engine as the shell handed it to the page, called from here with the page's own token. */
let engine = async () => {
  throw new Error('no engine yet');
};

try {
  await walk.step(
    'the app starts, and its page answers on WebView2’s DevTools port',
    async () => {
      browser = await until(
        'the DevTools port',
        () => chromium.connectOverCDP(`http://127.0.0.1:${port}`).catch(() => null),
        30_000,
      );
      page = await until('the app’s page', () => appPage(browser.contexts().flatMap((context) => context.pages())));
      // Playwright emulates a light colour scheme on every context it attaches to: the page gets its own back.
      await page.emulateMedia({ colorScheme: null, reducedMotion: null, forcedColors: null });
      page.setDefaultTimeout(10_000);
      return `${Date.now() - launched} ms after launch, at ${new URL(page.url()).origin}`;
    },
    { needed: true },
  );
  const errors = [];
  let main, nav, grid, inspector, palette, library;
  /** The page the walk drives, and its parts: again after a reconnect (see the tray). */
  const attach = (next) => {
    page = next;
    page?.on('pageerror', (error) => errors.push(error.message));
    main = page?.getByRole('main');
    nav = page?.getByRole('navigation', { name: 'Main' });
    grid = page?.getByRole('grid', { name: 'URLs' });
    inspector = page?.getByRole('complementary', { name: 'Inspector' });
    palette = page?.getByRole('dialog', { name: 'Command palette' });
    library = page?.getByRole('region', { name: 'Library' });
  };
  attach(page);
  const heading = () => inspector.getByRole('heading', { level: 2 }).innerText();
  const count = (text) => Number((text ?? '').replace(/[^\d]/g, ''));
  const picture = async (name) => {
    if (options.shots) await page.screenshot({ path: path.join(options.shots, `${name}.png`) });
  };
  /** Home. The app reopens where it was left, so it's reached by its link, never by loading `/`. */
  const home = async () => {
    await nav.getByRole('link', { name: 'Home' }).click();
    await library.getByRole('row').nth(1).waitFor({ timeout: 30_000 });
  };

  await walk.step(
    'the shell handed the page its engine, and the library lists the crawl',
    async () => {
      await library.getByRole('row').nth(1).waitFor({ timeout: 30_000 });
      const state = await page.evaluate(() => window.__TAURI_INTERNALS__.invoke('sidecar_state'));
      expectThat(state.state === 'ready' && state.token.length === 64, `the engine is ${state.state}`);
      engine = async (route, init = {}) => {
        const now = await page.evaluate(() => window.__TAURI_INTERNALS__.invoke('sidecar_state'));
        return fetch(`http://127.0.0.1:${now.port}${route}`, {
          ...init,
          headers: {
            Authorization: `Bearer ${now.token}`,
            ...(init.body ? { 'Content-Type': 'application/json' } : {}),
          },
        });
      };
      const rows = (await library.getByRole('row').count()) - 1;
      expectThat(rows === 1, `the library lists ${rows} crawls`);
      await picture('01-home');
      return `library shown ${Date.now() - launched} ms after launch; engine ${state.version} on port ${state.port}`;
    },
    { needed: true },
  );

  await walk.step('a crawl opens to its answer', async () => {
    await library.getByRole('link').first().click();
    await main.getByRole('heading', { name: 'Fix first' }).waitFor({ timeout: 30_000 });
    await picture('02-overview');
    return (await main.getByText(/^\d+ checks found [\d,]+ issues on this crawl\./).innerText()).slice(0, 80);
  });

  await walk.step('a status class opens exactly its URLs', async () => {
    const part = page
      .getByRole('region', { name: 'Status codes' })
      .getByRole('link', { name: /^(3xx|4xx|5xx): [\d,]+ URLs?$/ })
      .first();
    const name = await part.getAttribute('aria-label');
    await part.click();
    await page.waitForURL(/\/urls\?filter=/);
    await grid.getByRole('row').nth(1).waitFor();
    const shown = count(await page.locator('.si-result-count__n').first().innerText());
    expectThat(shown === count(name.split(':')[1]), `"${name}" landed on ${shown} URLs`);
    await picture('03-urls');
    return `${name}`;
  });

  await walk.step(
    'the inspector opens on Enter, walks a link, comes back with Alt+Left and closes with Esc',
    async () => {
      await nav.getByRole('link', { name: /^URLs/ }).click();
      await page.waitForURL(/\/urls$/);
      await grid.getByRole('row').nth(1).getByRole('gridcell').nth(1).click();
      await page.keyboard.press('Enter');
      await inspector.getByRole('list', { name: 'Indexability tests' }).waitFor();
      const first = await heading();
      await inspector.getByRole('tab', { name: /^Outlinks/ }).click();
      const links = inspector.getByRole('grid', { name: 'Outlinks' });
      await links.getByRole('row').nth(1).waitFor();
      await links.focus();
      await page.keyboard.press('ArrowDown');
      await page.keyboard.press('Enter');
      await until('the walk', async () => (await heading()) !== first);
      const walked = await heading();
      await page.keyboard.press('Alt+ArrowLeft');
      await until('Alt+Left', async () => (await heading()) === first);
      await picture('04-inspector');
      await page.keyboard.press('Escape');
      await inspector.waitFor({ state: 'hidden' });
      expectThat(!page.url().includes('inspect='), 'Esc left the inspector in the address');
      return `${first} -> ${walked} -> back`;
    },
  );

  await walk.step('Source shows the stored HTML, and Ctrl+F searches it', async () => {
    await grid.getByRole('row').nth(1).getByRole('gridcell').nth(1).click();
    await page.keyboard.press('Enter');
    await inspector.getByRole('tab', { name: /^Source/ }).click();
    const source = inspector.getByRole('textbox', { name: 'HTML source' });
    await source.waitFor({ timeout: 15_000 });
    await source.click();
    await page.keyboard.press('Control+f');
    const search = inspector.getByRole('searchbox', { name: 'Search the HTML source' });
    expectThat(
      await search.evaluate((element) => element === document.activeElement),
      'Ctrl+F didn’t reach the search field',
    );
    await page.keyboard.type('html');
    const matches = await inspector
      .getByText(/^\d+ of [\d,+]+$/)
      .first()
      .innerText();
    await picture('05-source');
    await inspector.getByRole('tab', { name: /^Summary/ }).click();
    await page.keyboard.press('Escape');
    await inspector.waitFor({ state: 'hidden' });
    return `"html": ${matches}`;
  });

  await walk.step('Issues shows a check: what is wrong, why it matters, how to fix it', async () => {
    await page.keyboard.press('Control+2');
    await page.waitForURL(/\/issues/);
    await page.getByRole('listbox', { name: 'Checks' }).getByRole('option').first().click();
    for (const text of ['What’s wrong', 'Why it matters', 'How to fix it'])
      await main.getByText(text).first().waitFor();
    await picture('06-issues');
    return (
      await page.getByRole('listbox', { name: 'Checks' }).getByRole('option', { selected: true }).innerText()
    ).replace(/\s+/g, ' ');
  });

  await walk.step('an export’s toast: Open folder shows the file in Explorer, Open file opens it', async () => {
    // Started through the engine with a path, as the save dialog's answer would be: a native dialog is no page to drive.
    // The toast is the app's from there: it hears the export on its event stream, as every export.
    const folder = path.join(temp, 'exports');
    mkdirSync(folder, { recursive: true });
    const file = path.join(folder, 'smoke-walk URLs.csv');
    const crawlId = /\/crawls\/([0-9a-f]{16})/.exec(page.url())?.[1];
    expectThat(crawlId, `no crawl in ${page.url()}`);
    const started = await engine(`/api/crawls/${crawlId}/export`, {
      method: 'POST',
      body: JSON.stringify({ kind: 'urls', format: 'csv', path: file }),
    });
    expectThat(started.status === 201, `the export wasn't started: ${started.status} ${await started.text()}`);
    const toast = page.locator('.si-export-toast').filter({ hasText: 'smoke-walk URLs.csv' });
    await toast.getByText('Saved', { exact: true }).waitFor({ timeout: 30_000 });
    await picture('06b-export-saved');
    const opened = (what, target) =>
      execFileSync(
        'powershell',
        [
          '-NoProfile',
          '-ExecutionPolicy',
          'Bypass',
          '-File',
          path.join(appRoot, 'scripts', 'shell-opened.ps1'),
          what,
          target,
        ],
        { encoding: 'utf8' },
      ).trim();
    await toast.getByRole('button', { name: 'Open folder' }).click();
    await until('Explorer showing the export’s folder', () => opened('folder', folder) === 'yes', 20_000);
    const asked = Date.now();
    await toast.getByRole('button', { name: 'Open file' }).click();
    const program = await until(
      'the export’s file open in its program',
      () => {
        const found = opened('file', file);
        return found === 'no' ? null : found;
      },
      45_000,
    );
    const closed = execFileSync(
      'powershell',
      [
        '-NoProfile',
        '-ExecutionPolicy',
        'Bypass',
        '-File',
        path.join(appRoot, 'scripts', 'shell-opened.ps1'),
        'close-file',
        file,
        String(asked - 1_000),
      ],
      { encoding: 'utf8' },
    ).trim();
    expectThat(closed === 'closed', `the export's file couldn't be closed again: ${closed}`);
    return `Open folder showed ${folder} in Explorer (closed again); Open file opened the CSV in ${program} (closed again)`;
  });

  await walk.step('the Report shows the engine’s report styled, and prints the frame alone', async () => {
    await nav.getByRole('link', { name: /^Report/ }).click();
    await page.waitForURL(/\/report/);
    const frame = page.frameLocator('iframe[title^="The report for"]');
    await frame.locator('h1').waitFor({ timeout: 20_000 });
    // The shell's policy for the page, as the shell serves it: a nonce or a hash in style-src would unstyle the report.
    const policy = await page.evaluate(async () =>
      (await fetch(window.location.href)).headers.get('content-security-policy'),
    );
    const styleSrc = /style-src ([^;]*)/.exec(policy ?? '')?.[1] ?? '(none)';
    // The report's one style sheet applies (its font, its sizes): without it the frame shows the browser's own look.
    const styled = await frame.locator('body').evaluate((body) => ({
      sheets: body.ownerDocument.styleSheets.length,
      font: body.ownerDocument.defaultView.getComputedStyle(body).fontFamily,
      margin: body.ownerDocument.defaultView.getComputedStyle(body).margin,
    }));
    expectThat(
      /Inter|Segoe UI/.test(styled.font),
      `the report is unstyled (font "${styled.font}", style-src ${styleSrc})`,
    );
    expectThat(!/nonce-|sha256-/.test(styleSrc), `style-src holds a nonce or a hash: ${styleSrc}`);
    await picture('06b-report');
    // Print: the frame's print() blocks the page until its dialog closes, so the button is pressed without waiting on
    // it, the dialog is looked for from outside, and Esc, pressed in the webview, closes it. WebView2's print dialog
    // isn't a window of its own: it is drawn in the app's window, as one more of the webview's surfaces in it.
    const before = shellWindow('state');
    void page
      .getByRole('button', { name: /Print or save as PDF/ })
      .click({ noWaitAfter: true })
      .catch(() => undefined);
    const opened = await until(
      'the print dialog',
      () => {
        const state = shellWindow('state');
        return state.surfaces > before.surfaces || state.webviewWindows > before.webviewWindows ? state : null;
      },
      20_000,
    ).catch((error) => {
      // Whatever was missed, a dialog left up would block the page, and the walk, for good.
      shellWindow('keys', String(VIRTUAL_KEYS.Escape));
      throw error;
    });
    // No picture of it: the page can't be drawn for DevTools while the dialog holds it.
    shellWindow('keys', String(VIRTUAL_KEYS.Escape));
    await until('the print dialog going', () => {
      const state = shellWindow('state');
      return state.surfaces <= before.surfaces && state.webviewWindows <= before.webviewWindows;
    });
    // The page runs again once the dialog has gone.
    await page.evaluate(() => 1);
    await page.keyboard.press('Control+2');
    await page.waitForURL(/\/issues/);
    return `styled (${styled.font.split(',')[0]}; style-src ${styleSrc}); WebView2's print dialog opened over the window (${opened.surfaces - before.surfaces} more surface) and closed on Esc`;
  });

  await walk.step('the palette (Ctrl+K), "Go to URL…" and the shortcut sheet (?)', async () => {
    await page.keyboard.press('Control+k');
    await palette.waitFor();
    await page.keyboard.type('Go to URL');
    await palette
      .getByRole('option', { name: /Go to URL…/ })
      .first()
      .click();
    await page.getByRole('textbox', { name: 'Go to URL' }).fill('/');
    await page.keyboard.press('Enter');
    await inspector.getByRole('list', { name: 'Indexability tests' }).waitFor();
    const found = await heading();
    await page.keyboard.press('Escape');
    await inspector.waitFor({ state: 'hidden' });
    await main.click({ position: { x: 300, y: 10 } });
    await page.keyboard.press('?');
    const sheet = page.getByRole('dialog', { name: 'Keyboard shortcuts' });
    await sheet.waitFor();
    await picture('07-shortcuts');
    await page.keyboard.press('Escape');
    await sheet.waitFor({ state: 'hidden' });
    return `Go to URL "/" opened ${found}`;
  });

  if (options.skip.has('keys')) walk.skip('the browser’s own keys are off', 'left out with --skip');
  else {
    await walk.step(
      'the browser’s own keys are off: F5 doesn’t reload and F3 finds nothing, pressed in the webview itself',
      async () => {
        await page.evaluate(() => {
          window.__smoke = [];
          window.addEventListener('keydown', (event) => window.__smoke.push(event.key), true);
        });
        shellWindow('keys', [VIRTUAL_KEYS.F9, VIRTUAL_KEYS.F5, VIRTUAL_KEYS.F3].join(','));
        await sleep(1200);
        const seen = await page.evaluate(() => window.__smoke ?? null);
        expectThat(seen !== null, 'F5 reloaded the page');
        // The keys did arrive: or their doing nothing would show nothing.
        expectThat(seen.join() === 'F9,F5,F3', `the page saw ${seen.join() || 'no keys'}`);
        const state = shellWindow('state');
        expectThat(
          state.webviewWindows === 0,
          `the webview opened ${state.webviewWindows} window(s) of its own (a find bar?)`,
        );
        return 'the page saw F9, F5 and F3, stayed as it was, and the webview opened nothing';
      },
    );
  }

  if (options.skip.has('restart')) walk.skip('the engine is started again when it dies', 'left out with --skip');
  else {
    await walk.step('the engine is started again when it dies, and the app goes on', async () => {
      await home();
      const before = await page.evaluate(() => window.__TAURI_INTERNALS__.invoke('sidecar_state'));
      const killedAt = Date.now();
      execFileSync('powershell', [
        '-NoProfile',
        '-Command',
        `Get-CimInstance Win32_Process -Filter "ParentProcessId=${app.pid} AND Name='si-sidecar.exe'" | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }`,
      ]);
      const after = await until(
        'the new engine',
        async () => {
          const now = await page.evaluate(() => window.__TAURI_INTERNALS__.invoke('sidecar_state'));
          return now.state === 'ready' && now.generation > before.generation ? now : null;
        },
        30_000,
      );
      await library.getByRole('row').nth(1).waitFor({ timeout: 30_000 });
      expectThat((await engine('/api/crawls')).ok, 'the new engine doesn’t answer');
      return `ready again ${Date.now() - killedAt} ms after it was killed, on port ${after.port}`;
    });
  }

  const live = !options.skip.has('live');
  let crawlId = '';
  if (!live) walk.skip('a crawl of the test site from Home', 'left out with --skip');
  else {
    await walk.step(
      'a crawl of the engine’s test site, started from Home, runs live; F7 pauses and resumes it',
      async () => {
        testSite = spawn(buildCli(), ['testsite', '--pages', String(options.pages), '--port', '0'], {
          stdio: ['ignore', 'pipe', 'ignore'],
          windowsHide: true,
        });
        const url = await new Promise((resolve, reject) => {
          const timer = setTimeout(() => reject(new Error('testsite wrote no READY line within 30 s')), 30_000);
          createInterface({ input: testSite.stdout }).on('line', (line) => {
            if (!line.startsWith('READY ')) return;
            clearTimeout(timer);
            resolve(line.slice('READY '.length).split(' ')[0]);
          });
        });
        await home();
        await page.getByPlaceholder('Paste a URL, then press Enter').fill(url);
        await page.keyboard.press('Enter');
        await page.waitForURL(/\/crawls\/[0-9a-f]{16}\/overview$/);
        crawlId = /crawls\/([0-9a-f]{16})/.exec(page.url())[1];
        await main.getByText('Live', { exact: true }).waitFor({ timeout: 15_000 });
        await page.keyboard.press('F7');
        await main.getByText('The crawl is paused.', { exact: false }).waitFor();
        await picture('08-paused');
        await page.keyboard.press('F7');
        await main.getByText('The crawl is paused.', { exact: false }).waitFor({ state: 'hidden' });
        return `crawl ${crawlId} of ${url}`;
      },
    );
  }

  const status = async () => (await (await engine(`/api/crawls/${crawlId}`)).json()).status;
  if (!live || options.skip.has('tray'))
    walk.skip('the tray, and quitting with a crawl running', live ? 'left out with --skip' : 'needs the live crawl');
  else {
    await walk.step(
      'closing the window hides it to the tray, the crawl goes on, and the tray icon brings it back',
      async () => {
        expectThat((await status()) === 'running', `the crawl is ${await status()}: give the test site more --pages`);
        const item = async () => (await (await engine(`/api/crawls/${crawlId}`)).json()).fetchedUrlCount;
        // The token, before the page is suspended: the engine is asked from here while the window is hidden.
        const state = await page.evaluate(() => window.__TAURI_INTERNALS__.invoke('sidecar_state'));
        const fetched = async () =>
          (
            await (
              await fetch(`http://127.0.0.1:${state.port}/api/crawls/${crawlId}`, {
                headers: { Authorization: `Bearer ${state.token}` },
              })
            ).json()
          ).fetchedUrlCount;
        void item;
        shellWindow('close');
        await until('the window hiding', () => !shellWindow('state').window);
        expectThat(alive(), 'the app quit instead of hiding');
        const before = await fetched();
        // --tray-stay: the window stays hidden that long, the shell's memory read every 30 s (shellMemory).
        const stayMs = (options.trayStay ?? 1.5) * 1000;
        const hiddenAt = Date.now();
        const samples = [];
        if (options.trayStay) {
          for (let at = 30_000; at <= stayMs; at += 30_000) {
            await sleep(hiddenAt + at - Date.now());
            // Asked from here: the page is suspended while hidden.
            const now = await fetch(`http://127.0.0.1:${state.port}/api/crawls/${crawlId}`, {
              headers: { Authorization: `Bearer ${state.token}` },
            });
            const sample = { seconds: at / 1000, crawling: (await now.json()).status === 'running', ...memory() };
            samples.push(sample);
            console.log(
              `  in the tray ${sample.seconds} s${sample.crawling ? ', crawling' : ''}: ${memoryWords(sample)}`,
            );
          }
        }
        await sleep(Math.max(0, hiddenAt + stayMs - Date.now()));
        const after = await fetched();
        expectThat(after > before, `the crawl stood still at ${after} URLs`);
        const last = samples.at(-1);
        shellWindow('tray-click');
        await until('the window coming back', () => shellWindow('state').window);
        // A long stay hidden (--tray-stay) can outlast the DevTools connection, not the page: then it's attached
        // again, and the page is checked as it is, not reloaded.
        let reattached = false;
        if (page.isClosed() || !browser.isConnected()) {
          await browser.close().catch(() => undefined);
          browser = await until('the DevTools port again', () =>
            chromium.connectOverCDP(`http://127.0.0.1:${port}`).catch(() => null),
          );
          attach(
            await until('the app’s page again', () =>
              appPage(browser.contexts().flatMap((context) => context.pages())),
            ),
          );
          await page.emulateMedia({ colorScheme: null, reducedMotion: null, forcedColors: null });
          page.setDefaultTimeout(10_000);
          reattached = true;
        }
        await until('the page answering', () =>
          Promise.race([page.evaluate(() => document.visibilityState === 'visible'), sleep(1000).then(() => false)]),
        );
        await main.waitFor();
        return `hidden with the crawl at ${before} URLs, ${after} URLs ${stayMs / 1000} s later, back from the tray${
          reattached ? ' (DevTools attached again: its connection had dropped while hidden)' : ''
        }${last ? `; after ${last.seconds} s hidden: ${memoryWords(last)}` : ''}`;
      },
    );

    await walk.step('quitting with a crawl running asks "Stop crawling and quit?"; Cancel keeps both', async () => {
      const ask = async () => {
        await page.keyboard.press('Control+k');
        await palette.waitFor();
        await page.keyboard.type('Quit Shouting Iguana');
        await palette
          .getByRole('option', { name: /Quit Shouting Iguana/ })
          .first()
          .waitFor();
        await page.keyboard.press('Enter');
        return until('the question', () => shellWindow('state').dialog);
      };
      expectThat((await status()) === 'running', `the crawl is ${await status()}: give the test site more --pages`);
      const question = await ask();
      expectThat(question === 'Stop crawling and quit?', `the dialog is "${question}"`);
      shellWindow('press', 'Cancel');
      await until('the question going', () => shellWindow('state').dialog === null);
      expectThat(alive() && (await status()) === 'running', 'Cancel didn’t keep the app and its crawl');
      await ask();
      const pressed = Date.now();
      shellWindow('press', 'Stop and quit');
      await until('the app quitting', () => !alive(), 40_000);
      return `"${question}", Cancel kept both; Stop and quit: gone ${Date.now() - pressed} ms later`;
    });
  }

  if (alive()) {
    await walk.step('closing the window with nothing running quits the app', async () => {
      if (crawlId && ['running', 'paused'].includes(await status())) {
        await page.keyboard.press('F8');
        await until(
          'the crawl stopping',
          async () => !['running', 'paused', 'stopping'].includes(await status()),
          40_000,
        );
      }
      shellWindow('close');
      await until('the app quitting', () => !alive(), 20_000);
    });
  }
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
if (mode === 'both') {
  console.log('');
  console.log('The same app, as a first-time user meets it, in Simple:');
  if (simpleWalk() !== 0) process.exitCode = 1;
}
