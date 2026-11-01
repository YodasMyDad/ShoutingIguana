// `npm run dev`: the UI in a plain browser against a real sidecar, with nothing of yours at risk.
// - A data folder in the temp folder, with copies of your dev crawls (%LOCALAPPDATA%\ShoutingIguana\dev-crawls, copied
//   once and refreshed when they change, never opened in place), or a small synthetic crawl when there are none.
// - A fresh state folder each run, so your real library and defaults are never touched.
// - The sidecar on a fixed port with a fixed development token, allowing only the dev server's origin.
// - Vite, given the port and token. Ctrl+C stops both; the sidecar stops crawls cleanly on its way out.
// Options: --synthetic (use a synthetic crawl even if you have dev crawls), --engine-root <folder> (for make-crawl).
import { spawn } from 'node:child_process';
import { existsSync, mkdtempSync, readdirSync, rmSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { copyCrawl, devCrawlsFolder, isCurrentCopy, makeSyntheticCrawl, startSidecar } from './lib/engine.mjs';
import { DEV_SERVER_PORT, DEV_SIDECAR_PORT } from './ports.mjs';

/** Development only: the sidecar is on 127.0.0.1, checks the origin, and holds copies. Never a release token. */
const DEV_TOKEN = 'shouting-iguana-development-token-not-a-secret';

const work = path.join(os.tmpdir(), 'si-app-dev');
const dataDir = path.join(work, 'data');
const stateDir = mkdtempSync(path.join(os.tmpdir(), 'si-app-dev-state-'));

async function prepareData() {
  const source = devCrawlsFolder();
  const crawls =
    existsSync(source) && !process.argv.includes('--synthetic')
      ? readdirSync(source).filter((name) => name.endsWith('.sicrawl'))
      : [];
  if (crawls.length > 0) {
    for (const name of crawls) {
      const from = path.join(source, name);
      if (isCurrentCopy(from, path.join(dataDir, name))) continue;
      console.log(`Copying ${name}…`);
      copyCrawl(from, dataDir);
    }
    return;
  }
  const synthetic = path.join(dataDir, 'synthetic-small.sicrawl');
  if (!existsSync(synthetic)) await makeSyntheticCrawl({ out: synthetic });
}

await prepareData();
const sidecar = await startSidecar({
  port: DEV_SIDECAR_PORT,
  token: DEV_TOKEN,
  origins: [`http://localhost:${DEV_SERVER_PORT}`],
  dataDir,
  stateDir,
  onLog: (line) => process.env.SI_DEV_LOG && console.error(`[sidecar] ${line}`),
});
console.log(`Sidecar ${sidecar.version} on 127.0.0.1:${sidecar.port}, data in ${dataDir}`);

const vite = spawn(process.execPath, [path.join('node_modules', 'vite', 'bin', 'vite.js')], {
  stdio: 'inherit',
  env: { ...process.env, VITE_SIDECAR_PORT: String(sidecar.port), VITE_SIDECAR_TOKEN: DEV_TOKEN },
});

let stopping = false;
async function stop(code = 0) {
  if (stopping) return;
  stopping = true;
  vite.kill();
  await sidecar.stop();
  rmSync(stateDir, { recursive: true, force: true });
  process.exit(code);
}

vite.once('exit', (code) => void stop(code ?? 0));
process.once('SIGINT', () => void stop(0));
process.once('SIGTERM', () => void stop(0));
