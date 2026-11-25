// `node scripts/prove-update.mjs`: the app updating itself, end to end, on this Windows machine. Not a CI check: it
// installs a real (test-named) app, needs a desktop session and the updater's signing key, and takes two release
// builds, so about half an hour the first time.
//
// It builds the app twice, as version A (the one in tauri.conf.json) and a higher version B, both signed with the
// updater's key as the release workflow signs them. A is installed, B's installer and its latest.json are served from
// this machine, and the script watches A, in its own window (over WebView2's DevTools port):
//   - find B by itself, a little after it starts, and say so in a toast that leads to Settings › Updates;
//   - refuse an installer that was changed after signing, a signature made for another file, a signature made with
//     another key, and a latest.json that announces a version the installer wasn't signed as; nothing is installed
//     and the app carries on as A;
//   - with a crawl running, download B and keep it, without stopping the crawl or the engine, and install it only
//     once the crawl has finished;
//   - download B, check its signature, stop its engine, run the installer and open again as B, with no engine left
//     behind from A.
//
// The installed app this builds is not Shouting Iguana's own: another product name, identifier and program name
// ("Shouting Iguana UpdateTest"), no .sicrawl association, its updates from 127.0.0.1. It installs beside a real
// install and never touches it or its library: a build under another identifier keeps its engine's folders to itself
// (src-tauri/src/sidecar/launch.rs, `own_folders`). The script checks that too: the real install's program and the
// real data folders are the same before and after. It uninstalls the test app when it has finished.
//
//   --work <folder>   where the builds' installers and the proof's files go (default: a folder in %TEMP%)
//   --key <folder>    where updater.key and updater.key.password are (default: %USERPROFILE%\.shouting-iguana)
//   --skip-build      use the installers already in --work, from a run before
//   --no-crawl        leave out the crawl (it builds the CLI and serves the engine's test site)
//   --keep            leave the test app installed at the end
//
// Neither the key nor its password is printed, logged or copied: they are read into the environment of the two
// `tauri build` runs, and of `tauri signer` nowhere (the other key it signs with is a throwaway made here).
import { spawn, spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import {
  copyFileSync,
  existsSync,
  mkdirSync,
  readFileSync,
  readdirSync,
  rmSync,
  statSync,
  writeFileSync,
} from 'node:fs';
import { createServer } from 'node:http';
import os from 'node:os';
import path from 'node:path';
import { createInterface } from 'node:readline';
import { parseArgs } from 'node:util';
import { chromium } from '@playwright/test';
import { appRoot, buildCli } from './lib/engine.mjs';
import { BROWSER_ARGUMENTS, appPage, withDebuggingPort } from './lib/shell.mjs';
import { signatureProblem } from './lib/updater.mjs';

if (process.platform !== 'win32') throw new Error('prove-update installs the Windows app: run it on Windows');

const PRODUCT = 'Shouting Iguana UpdateTest';
const BINARY = 'shouting-iguana-updatetest';
const IDENTIFIER = 'uk.co.aptitude.shoutingiguana.updatetest';
const SERVE_PORT = 51899;
const DEBUG_PORT = 9800 + Math.floor(Math.random() * 100);

const { values: options } = parseArgs({
  options: {
    work: { type: 'string' },
    key: { type: 'string' },
    'skip-build': { type: 'boolean', default: false },
    'no-crawl': { type: 'boolean', default: false },
    keep: { type: 'boolean', default: false },
  },
});
const work = path.resolve(options.work ?? path.join(os.tmpdir(), 'si-prove-update'));
const keyFolder = options.key ?? path.join(os.homedir(), '.shouting-iguana');
const local = process.env.LOCALAPPDATA;
const installDir = path.join(local, PRODUCT);
const installedExe = path.join(installDir, `${BINARY}.exe`);
const ownData = path.join(local, IDENTIFIER);
const ownConfig = path.join(process.env.APPDATA, IDENTIFIER);
const conf = JSON.parse(readFileSync(path.join(appRoot, 'src-tauri', 'tauri.conf.json'), 'utf8'));
const versionA = conf.version;
const versionB = versionA.replace(/^(\d+\.\d+\.)(\d+).*$/, (_, head, patch) => `${head}${Number(patch) + 1}`);
const NOTES = 'The update proof’s notes.\nA second line of them.';

const proved = [];
const say = (line) => console.log(`prove-update: ${line}`);
function pass(what) {
  proved.push(what);
  say(`PROVED ${what}`);
}
function expectThat(condition, what) {
  if (!condition) throw new Error(what);
}
const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
async function until(what, check, timeoutMs = 60_000, everyMs = 250) {
  const deadline = Date.now() + timeoutMs;
  for (;;) {
    const value = await check();
    if (value) return value;
    if (Date.now() > deadline) throw new Error(`timed out waiting for ${what}`);
    await sleep(everyMs);
  }
}
function powershell(script) {
  const run = spawnSync('powershell', ['-NoProfile', '-NonInteractive', '-Command', script], { encoding: 'utf8' });
  return String(run.stdout ?? '').trim();
}
/** The processes started from a folder: [{ pid, name, commandLine }]. */
function processesUnder(folder) {
  const out = powershell(
    `Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -like '${folder.replaceAll("'", "''")}\\*' } | ` +
      `ForEach-Object { "$($_.ProcessId)|$($_.Name)|$($_.CommandLine)" }`,
  );
  return out
    .split(/\r?\n/)
    .filter(Boolean)
    .map((line) => {
      const [pid, name, ...rest] = line.split('|');
      return { pid: Number(pid), name, commandLine: rest.join('|') };
    });
}
const fileVersion = (file) => powershell(`(Get-Item -LiteralPath '${file}').VersionInfo.ProductVersion`);
const sha256 = (file) => createHash('sha256').update(readFileSync(file)).digest('hex');

// ------------------------------------------------------------------------------------------ what must not change
const realExe = path.join(local, 'Shouting Iguana', 'shouting-iguana.exe');
/** The data folder the real app's user chose, if they chose one (the shell's own settings, data_folder.rs). */
function chosenDataFolder() {
  try {
    const store = path.join(process.env.APPDATA, 'uk.co.aptitude.shoutingiguana', 'shell.json');
    const chosen = JSON.parse(readFileSync(store, 'utf8')).dataFolder;
    return typeof chosen === 'string' && chosen !== '' ? [chosen] : [];
  } catch {
    return [];
  }
}
// The engine's defaults: the crawls in Documents (wherever Windows keeps it: OneDrive may), the library in local data.
const realFolders = [
  path.join(
    powershell("[Environment]::GetFolderPath('MyDocuments')") || path.join(os.homedir(), 'Documents'),
    'Shouting Iguana',
  ),
  ...chosenDataFolder(),
  path.join(local, 'ShoutingIguana'),
];
/**
 * Every file of a folder with its size and time: what a run that touched it would change. Less the `si` CLI's own
 * browser profile (`cli-browser-profile`), which a crawl from the command line writes at any time and the app never
 * opens.
 */
function listing(folder) {
  if (!existsSync(folder)) return '(not there)';
  return readdirSync(folder, { recursive: true, withFileTypes: true })
    .filter((entry) => entry.isFile())
    .map((entry) => path.join(entry.parentPath, entry.name))
    .filter((file) => !path.relative(folder, file).startsWith('cli-browser-profile'))
    .map((file) => {
      const stat = statSync(file, { throwIfNoEntry: false });
      return `${file}|${stat?.size}|${stat?.mtimeMs}`;
    })
    .sort()
    .join('\n');
}
/** The files that differ between two listings, for the line that says a folder changed. */
function changedFiles(then, now) {
  const [was, is] = [new Set(then.split('\n')), new Set(now.split('\n'))];
  const names = [...was, ...is].filter((line) => !(was.has(line) && is.has(line))).map((line) => line.split('|')[0]);
  return [...new Set(names)];
}
const realRunning = () => processesUnder(path.join(local, 'Shouting Iguana')).length > 0;
const before = {
  exe: existsSync(realExe) ? sha256(realExe) : null,
  version: existsSync(realExe) ? fileVersion(realExe) : null,
  running: realRunning(),
  folders: realFolders.map(listing),
};
say(
  before.exe
    ? `the real install is ${before.version} (${realExe})${before.running ? ', and running' : ''}: it is left alone`
    : 'no real install on this machine',
);

if (existsSync(installDir))
  throw new Error(`${installDir} is already there: uninstall the test app from a run before (its uninstall.exe /S)`);

// --------------------------------------------------------------------------------------------------- the builds
mkdirSync(work, { recursive: true });
const tauri = path.join(appRoot, 'node_modules', '@tauri-apps', 'cli', 'tauri.js');
const bundleDir = path.join(appRoot, 'src-tauri', 'target', 'release', 'bundle', 'nsis');
const installerName = (version) => `${PRODUCT}_${version}_x64-setup.exe`;
const installer = (version) => path.join(work, installerName(version));

function build(version) {
  const override = path.join(work, `tauri.${version}.conf.json`);
  writeFileSync(
    override,
    JSON.stringify(
      {
        productName: PRODUCT,
        mainBinaryName: BINARY,
        identifier: IDENTIFIER,
        version,
        bundle: { createUpdaterArtifacts: true, fileAssociations: [] },
        plugins: {
          updater: {
            endpoints: [`http://127.0.0.1:${SERVE_PORT}/latest.json`],
            dangerousInsecureTransportProtocol: true,
          },
        },
      },
      null,
      2,
    ),
  );
  say(`building ${PRODUCT} ${version}, signed with the updater’s key (a release build: some minutes)`);
  const log = path.join(work, `build-${version}.log`);
  const run = spawnSync(process.execPath, [tauri, 'build', '--bundles', 'nsis', '--config', override], {
    cwd: appRoot,
    encoding: 'utf8',
    maxBuffer: 256 * 1024 * 1024,
    env: {
      ...process.env,
      TAURI_SIGNING_PRIVATE_KEY: readFileSync(path.join(keyFolder, 'updater.key'), 'utf8').trim(),
      TAURI_SIGNING_PRIVATE_KEY_PASSWORD: readFileSync(path.join(keyFolder, 'updater.key.password'), 'utf8').trim(),
    },
  });
  writeFileSync(log, `${run.stdout ?? ''}\n${run.stderr ?? ''}`);
  if (run.status !== 0) throw new Error(`the build of ${version} failed: see ${log}`);
  for (const suffix of ['', '.sig'])
    copyFileSync(path.join(bundleDir, installerName(version) + suffix), installer(version) + suffix);
}

if (!options['skip-build']) {
  build(versionA);
  build(versionB);
}
const bytesB = readFileSync(installer(versionB));
const signatureB = readFileSync(`${installer(versionB)}.sig`, 'utf8').trim();
const signatureA = readFileSync(`${installer(versionA)}.sig`, 'utf8').trim();
for (const [version, bytes, signature] of [
  [versionA, readFileSync(installer(versionA)), signatureA],
  [versionB, bytesB, signatureB],
]) {
  const wrong = signatureProblem(bytes, signature, conf.plugins.updater.pubkey, version);
  expectThat(wrong === null, `${installerName(version)}: ${wrong}`);
}
pass(
  `both installers are signed by the key in tauri.conf.json, each as its own version (${versionA}: ${statSync(installer(versionA)).size} bytes, ${versionB}: ${bytesB.length} bytes)`,
);

// A signature over B's installer made with a key the app doesn't trust.
const strangerDir = path.join(work, 'stranger');
rmSync(strangerDir, { recursive: true, force: true });
mkdirSync(strangerDir, { recursive: true });
copyFileSync(installer(versionB), path.join(strangerDir, 'setup.exe'));
const strangerKey = path.join(strangerDir, 'stranger.key');
const signer = (args) => spawnSync(process.execPath, [tauri, 'signer', ...args], { cwd: appRoot, encoding: 'utf8' });
expectThat(signer(['generate', '--ci', '-w', strangerKey, '-p', 'stranger']).status === 0, 'no throwaway key was made');
expectThat(
  signer(['sign', '-f', strangerKey, '-p', 'stranger', '--app-version', versionB, path.join(strangerDir, 'setup.exe')])
    .status === 0,
  'the throwaway key signed nothing',
);
const signatureStranger = readFileSync(path.join(strangerDir, 'setup.exe.sig'), 'utf8').trim();

// --------------------------------------------------------------------------------------------- the update server
const tampered = Buffer.from(bytesB);
tampered[tampered.length - 1] ^= 0xff;
/** What the server answers with: the case being proved. */
const CASES = {
  genuine: { version: versionB, signature: signatureB, bytes: bytesB },
  tampered: { version: versionB, signature: signatureB, bytes: tampered },
  anotherFile: { version: versionB, signature: signatureA, bytes: bytesB },
  anotherKey: { version: versionB, signature: signatureStranger, bytes: bytesB },
  anotherVersion: { version: '9.9.9', signature: signatureB, bytes: bytesB },
  nothing: null,
};
let serving = 'genuine';
const requests = [];
const server = createServer((request, response) => {
  const now = CASES[serving];
  requests.push(`${serving} ${request.url}`);
  if (!now) {
    response.writeHead(404).end();
  } else if (request.url === '/latest.json') {
    const body = JSON.stringify({
      version: now.version,
      notes: NOTES,
      pub_date: '2026-11-24T12:00:00Z',
      platforms: {
        'windows-x86_64': { signature: now.signature, url: `http://127.0.0.1:${SERVE_PORT}/setup.exe` },
      },
    });
    response.writeHead(200, { 'Content-Type': 'application/json' }).end(body);
  } else if (request.url === '/setup.exe') {
    response.writeHead(200, { 'Content-Type': 'application/octet-stream', 'Content-Length': now.bytes.length });
    response.end(now.bytes);
  } else {
    response.writeHead(404).end();
  }
});
await new Promise((resolve) => server.listen(SERVE_PORT, '127.0.0.1', resolve));

// ----------------------------------------------------------------------------------------------- install A, run
let app = null;
let testSite = null;
let failed = null;
try {
  say(`installing ${versionA} silently into ${installDir}`);
  const installed = spawnSync(installer(versionA), ['/S'], { stdio: 'ignore' });
  expectThat(installed.status === 0, `the installer of ${versionA} exited ${installed.status}`);
  expectThat(existsSync(installedExe), `no ${installedExe} after installing`);
  expectThat(
    fileVersion(installedExe) === versionA,
    `the installed app is ${fileVersion(installedExe)}, not ${versionA}`,
  );

  // No test profile: the app is as a user has it, so it looks for a newer version by itself.
  const environment = {
    ...process.env,
    [BROWSER_ARGUMENTS]: withDebuggingPort(process.env[BROWSER_ARGUMENTS], DEBUG_PORT),
  };
  for (const name of ['SI_APP_PROFILE_DIR', 'SI_APP_DATA_DIR', 'SI_APP_STATE_DIR', 'SI_UPDATER_ENDPOINT'])
    delete environment[name];
  const launchedAt = Date.now();
  app = spawn(installedExe, [], { env: environment, stdio: 'ignore', detached: true });
  const browser = await until('the app’s DevTools port', () =>
    chromium.connectOverCDP(`http://127.0.0.1:${DEBUG_PORT}`).catch(() => null),
  );
  const page = await until(
    'the app’s page',
    () => appPage(browser.contexts().flatMap((context) => context.pages())) ?? null,
  );
  const main = page.getByRole('main');
  const shell = await page.evaluate(() => globalThis.__TAURI_INTERNALS__.invoke('shell_info'));
  expectThat(shell.appVersion === versionA, `the app says it is ${shell.appVersion}, not ${versionA}`);
  expectThat(shell.updates === 'inApp' && shell.updateChecks === true, `the shell says ${JSON.stringify(shell)}`);

  const sidecarOfA = await until(
    'A’s engine',
    () => processesUnder(installDir).find((one) => one.name === 'si-sidecar.exe') ?? null,
  );
  expectThat(
    sidecarOfA.commandLine.includes(path.join(ownData, 'data')) &&
      sidecarOfA.commandLine.includes(path.join(ownData, 'state')),
    `A’s engine isn’t on the test app’s own folders: ${sidecarOfA.commandLine}`,
  );
  pass(`${versionA} is installed and running on folders of its own (${ownData}), not the real library`);

  // 1. The app finds the newer version by itself.
  const toast = page.getByText(`Version ${versionB} is available`).first();
  await toast.waitFor({ timeout: 60_000 });
  const noticedAfter = Math.round((Date.now() - launchedAt) / 1000);
  expectThat(requests.includes('genuine /latest.json'), 'the app never asked for latest.json');
  await page.getByRole('button', { name: 'See what’s new' }).click();
  await main.getByText(`Version ${versionB} is available`).waitFor({ timeout: 15_000 });
  const whatsNew = await main.getByRole('region', { name: 'What’s new' }).innerText();
  expectThat(
    whatsNew.includes('The update proof’s notes.') && whatsNew.includes('A second line'),
    `what’s new said: ${whatsNew}`,
  );
  await main.getByText('Published 24 November 2026.').waitFor();
  pass(
    `${versionA} found ${versionB} by itself ${noticedAfter} s after it started, said so in a toast, and Settings › Updates shows the version, its date and what’s new`,
  );

  const check = main.getByRole('button', { name: 'Check for updates' });
  const install = main.getByRole('button', { name: 'Install and restart' });
  const stillA = async (what) => {
    expectThat(app.exitCode === null, `the app exited after ${what}`);
    expectThat(fileVersion(installedExe) === versionA, `the installed app changed after ${what}`);
    const engine = processesUnder(installDir).find((one) => one.name === 'si-sidecar.exe');
    expectThat(engine?.pid === sidecarOfA.pid, `the engine was stopped or started again after ${what}`);
  };

  // 2. What must be refused.
  for (const [name, what, version] of [
    ['tampered', 'an installer changed after it was signed (one byte)', versionB],
    ['anotherFile', 'a signature made for another file (the app’s key’s own, over the older installer)', versionB],
    ['anotherKey', 'a signature made with another key', versionB],
    ['anotherVersion', 'a latest.json announcing a version the installer wasn’t signed as (9.9.9)', '9.9.9'],
  ]) {
    serving = name;
    const downloads = requests.filter((line) => line.endsWith('/setup.exe')).length;
    await check.click();
    await main.getByText(`Version ${version} is available`).waitFor({ timeout: 30_000 });
    await install.click();
    await main.getByText('The update wasn’t installed').waitFor({ timeout: 120_000 });
    const words = await main
      .getByText(/signature didn’t match/)
      .first()
      .innerText();
    expectThat(/thrown away\. Nothing was installed\./.test(words), `the refusal said: ${words}`);
    expectThat(
      requests.filter((line) => line.endsWith('/setup.exe')).length === downloads + 1,
      'nothing was downloaded',
    );
    await sleep(2_000);
    await stillA(what);
    pass(`refused ${what}: “${words}”; the app and its engine carried on as ${versionA}`);
  }

  // 3. Nothing to read (a 404, as GitHub answers while the repository is private): a calm sentence.
  serving = 'nothing';
  await check.click();
  await main.getByText('No update information yet').waitFor({ timeout: 30_000 });
  const calm = await main.innerText();
  expectThat(
    /GitHub answered, but no new version has been published there yet: you have /.test(calm),
    `it said: ${calm}`,
  );
  expectThat(!/error|404|json/i.test(calm), `a raw error: ${calm}`);
  pass('a 404 for latest.json says “No update information yet”, as information, with the version running');
  serving = 'genuine';

  // 4. A crawl running: the update is downloaded and kept; the crawl and the engine go on.
  const engineApi = async (route, init = {}) => {
    const now = await page.evaluate(() => globalThis.__TAURI_INTERNALS__.invoke('sidecar_state'));
    return fetch(`http://127.0.0.1:${now.port}${route}`, {
      ...init,
      headers: { Authorization: `Bearer ${now.token}`, ...(init.body ? { 'Content-Type': 'application/json' } : {}) },
    });
  };
  if (!options['no-crawl']) {
    testSite = spawn(buildCli(), ['testsite', '--pages', '400', '--port', '0'], {
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
    const started = await engineApi('/api/crawls', {
      method: 'POST',
      body: JSON.stringify({
        options: { startUrl: url, concurrency: 1, maxRequestsPerSecond: 1, browserFallback: false },
      }),
    });
    expectThat(started.ok, `the crawl didn’t start (${started.status})`);
    const crawlId = (await started.json()).id;
    const status = async () => (await (await engineApi(`/api/crawls/${crawlId}`)).json()).status;
    await until('the crawl running', async () => (await status()) === 'running');
    await check.click();
    const download = main.getByRole('button', { name: 'Download the update' });
    await download.waitFor({ timeout: 30_000 });
    expectThat((await install.count()) === 0, 'Install and restart is offered while a crawl runs');
    await download.click();
    await main.getByText(`Version ${versionB} is downloaded`).waitFor({ timeout: 120_000 });
    const restart = main.getByRole('button', { name: 'Restart and install' });
    expectThat(await restart.isDisabled(), 'Restart and install can be pressed while the crawl runs');
    await sleep(3_000);
    expectThat((await status()) === 'running', `the crawl is ${await status()} after the download`);
    await stillA('the download beside a crawl');
    const fetchedThen = (await (await engineApi(`/api/crawls/${crawlId}`)).json()).fetchedUrlCount;
    await sleep(3_000);
    const fetchedNow = (await (await engineApi(`/api/crawls/${crawlId}`)).json()).fetchedUrlCount;
    expectThat(fetchedNow > fetchedThen, `the crawl stood still (${fetchedThen} then ${fetchedNow} pages)`);
    pass(
      `with a crawl running, ${versionB} was downloaded and kept, Restart and install was disabled, and the crawl went on (${fetchedThen} then ${fetchedNow} pages) in the same engine`,
    );

    expectThat(
      (await engineApi(`/api/crawls/${crawlId}/stop`, { method: 'POST' })).status === 202,
      'the crawl didn’t stop',
    );
    await until('Restart and install enabled once nothing runs', async () => !(await restart.isDisabled()), 180_000);
    const downloadsBefore = requests.filter((line) => line.endsWith('/setup.exe')).length;
    await restart.click();
    await until('the app to close for the installer', () => app.exitCode !== null, 120_000);
    expectThat(
      requests.filter((line) => line.endsWith('/setup.exe')).length === downloadsBefore,
      'the kept download was downloaded again',
    );
    pass('once the crawl had finished, Restart and install installed the kept download, without downloading it again');
  } else {
    await check.click();
    await install.waitFor({ timeout: 30_000 });
    await install.click();
    await main
      .getByRole('progressbar', { name: `Downloading version ${versionB}` })
      .waitFor({ timeout: 30_000 })
      .catch(() => undefined);
    await until('the app to close for the installer', () => app.exitCode !== null, 180_000);
  }
  await browser.close().catch(() => undefined);

  // 5. The installer runs, and the app opens again as B.
  const closedAt = Date.now();
  const running = await until(
    `${versionB} to open`,
    () => {
      const now = processesUnder(installDir);
      const shellNow = now.find((one) => one.name === `${BINARY}.exe`);
      const engineNow = now.find((one) => one.name === 'si-sidecar.exe');
      return shellNow && engineNow && fileVersion(installedExe) === versionB ? { shellNow, engineNow, all: now } : null;
    },
    180_000,
    1_000,
  );
  expectThat(running.shellNow.pid !== app.pid, 'the same process is still running');
  expectThat(running.engineNow.pid !== sidecarOfA.pid, 'A’s engine is still running');
  expectThat(
    processesUnder(installDir).filter((one) => one.name === 'si-sidecar.exe').length === 1,
    `more than one engine is running: ${JSON.stringify(processesUnder(installDir))}`,
  );
  expectThat(
    running.engineNow.commandLine.includes(path.join(ownData, 'data')) &&
      running.engineNow.commandLine.includes(path.join(ownData, 'state')),
    `B’s engine isn’t on the test app’s own folders: ${running.engineNow.commandLine}`,
  );
  const uninstallKey = `HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\${PRODUCT}`;
  const registered = powershell(`(Get-ItemProperty -LiteralPath '${uninstallKey}').DisplayVersion`);
  expectThat(registered === versionB, `Windows has the app as ${registered}`);
  const logs = path.join(ownData, 'logs');
  const logSaid = await until(
    `B’s log to say it started`,
    () => {
      const text = existsSync(logs)
        ? readdirSync(logs)
            .map((file) => readFileSync(path.join(logs, file), 'utf8'))
            .join('\n')
        : '';
      return text.includes(`Shouting Iguana ${versionB} started`) ? text : null;
    },
    30_000,
  );
  expectThat(
    logSaid.includes('The update’s signature is good') || logSaid.includes("The update's signature is good"),
    'A’s log doesn’t say the signature was good',
  );
  expectThat(
    /Installing version .*: the engine has stopped/.test(logSaid),
    'A’s log doesn’t say the engine stopped before the installer ran',
  );
  pass(
    `${versionA} downloaded ${versionB}, checked its signature, stopped its engine, ran the installer and opened again as ${versionB} ` +
      `${Math.round((Date.now() - closedAt) / 1000)} s after closing: the program is ${versionB}, Windows lists ${versionB}, ` +
      `a new shell (pid ${running.shellNow.pid}) and one new engine (pid ${running.engineNow.pid}) run from ${installDir}, and A’s engine (pid ${sidecarOfA.pid}) is gone`,
  );

  // 6. B, asked the same, has the newest version.
  await until(
    'B’s own check',
    () => {
      const text = readdirSync(logs)
        .map((file) => readFileSync(path.join(logs, file), 'utf8'))
        .join('\n');
      return text.includes(`No newer version than ${versionB}`);
    },
    60_000,
    1_000,
  );
  pass(`${versionB} then checked by itself and found nothing newer`);
} catch (error) {
  failed = error;
} finally {
  testSite?.kill();
  server.close();
  // Whatever of the test app still runs (never anything else: by the folder it runs from).
  for (const one of processesUnder(installDir).filter((process_) => process_.name === `${BINARY}.exe`))
    powershell(`(Get-Process -Id ${one.pid}).CloseMainWindow() | Out-Null`);
  await sleep(5_000);
  for (const one of processesUnder(installDir))
    spawnSync('taskkill', ['/PID', String(one.pid), '/T', '/F'], { stdio: 'ignore' });
  if (!options.keep) {
    const uninstall = path.join(installDir, 'uninstall.exe');
    if (existsSync(uninstall)) {
      spawnSync(uninstall, ['/S'], { stdio: 'ignore' });
      await until('the test app to be uninstalled', () => !existsSync(installedExe), 60_000).catch(() => undefined);
    }
    for (const folder of [ownData, ownConfig])
      rmSync(folder, { recursive: true, force: true, maxRetries: 5, retryDelay: 500 });
    await sleep(2_000);
    say(
      existsSync(installDir)
        ? `LEFT BEHIND: ${installDir}`
        : 'the test app is uninstalled, and its own folders removed',
    );
  }
}

// ------------------------------------------------------------------------------------- the real install, unchanged
const after = {
  exe: existsSync(realExe) ? sha256(realExe) : null,
  version: existsSync(realExe) ? fileVersion(realExe) : null,
  folders: realFolders.map(listing),
};
const untouched = [];
if (after.exe !== before.exe || after.version !== before.version)
  untouched.push(`the real install changed (${before.version} to ${after.version})`);
realFolders.forEach((folder, index) => {
  if (after.folders[index] === before.folders[index] || before.running || realRunning()) return;
  const changed = changedFiles(before.folders[index], after.folders[index]);
  untouched.push(`${changed.length} files changed in ${folder}: ${changed.slice(0, 5).join(', ')}`);
});
if (untouched.length > 0) failed ??= new Error(untouched.join('; '));
else if (before.exe)
  pass(
    `the real install is as it was (${after.version}, the same program byte for byte), and so are its folders ` +
      `(${realFolders.map((folder, index) => `${folder}: ${before.folders[index] === '(not there)' ? 'not there' : `${before.folders[index].split('\n').length} files`}`).join('; ')})`,
  );

console.log('');
for (const line of proved) console.log(`  ok  ${line}`);
if (failed) {
  console.error(`prove-update: FAIL ${failed.message}`);
  process.exit(1);
}
console.log(`prove-update: passed, ${proved.length} things proved`);
