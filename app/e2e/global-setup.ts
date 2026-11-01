// Starts what the end-to-end tests run against, and returns the teardown:
// 1. a temp data folder holding one crawl: SI_E2E_CRAWL's file, or a small synthetic crawl made with the CLI;
// 2. the real sidecar (the one the shell bundles) on that folder, with a fresh state folder, allowing only the
//    preview server's origin;
// 3. the app built for it (`--mode e2e`, the sidecar's port and token baked in, as `npm run dev` passes them), served
//    by `vite preview`.
import { spawn, spawnSync } from 'node:child_process';
import { existsSync, mkdtempSync, rmSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { appRoot, copyCrawl, makeSyntheticCrawl, startSidecar } from '../scripts/lib/engine.mjs';
import { E2E_SIDECAR_PORT, PREVIEW_PORT } from '../scripts/ports.mjs';

/** Tests only: a sidecar on 127.0.0.1 over a temp folder. Never a release token. */
const E2E_TOKEN = 'shouting-iguana-end-to-end-token-not-a-secret';

async function waitForServer(url: string, timeoutMs: number) {
  const started = Date.now();
  for (;;) {
    try {
      const response = await fetch(url);
      if (response.ok) return;
    } catch {
      // Not up yet.
    }
    if (Date.now() - started > timeoutMs) throw new Error(`${url} didn't answer within ${timeoutMs} ms`);
    await new Promise((resolve) => setTimeout(resolve, 200));
  }
}

export default async function globalSetup() {
  const temp = mkdtempSync(path.join(os.tmpdir(), 'si-e2e-'));
  const dataDir = path.join(temp, 'data');
  const stateDir = path.join(temp, 'state');

  let crawl = process.env.SI_E2E_CRAWL;
  if (!crawl) {
    crawl = path.join(appRoot, '.engine-build', 'crawls', 'e2e-synthetic.sicrawl');
    if (!existsSync(crawl)) await makeSyntheticCrawl({ out: crawl, pages: 40 });
  }
  copyCrawl(path.resolve(crawl), dataDir);

  const sidecar = await startSidecar({
    port: E2E_SIDECAR_PORT,
    token: E2E_TOKEN,
    origins: [`http://localhost:${PREVIEW_PORT}`],
    dataDir,
    stateDir,
  });

  // What the library says the crawl is, for the tests to look for.
  const library = (await (
    await fetch(`http://127.0.0.1:${sidecar.port}/api/crawls`, { headers: { Authorization: `Bearer ${E2E_TOKEN}` } })
  ).json()) as { site: string | null; fileName: string }[];
  const first = library[0];
  if (!first) throw new Error('The sidecar lists no crawl in the e2e data folder');
  process.env.SI_E2E_SITE = first.site ?? first.fileName;

  const env = { ...process.env, VITE_SIDECAR_PORT: String(sidecar.port), VITE_SIDECAR_TOKEN: E2E_TOKEN };
  const vite = path.join(appRoot, 'node_modules', 'vite', 'bin', 'vite.js');
  const build = spawnSync(process.execPath, [vite, 'build', '--mode', 'e2e', '--outDir', 'dist-e2e', '--emptyOutDir'], {
    cwd: appRoot,
    env,
    stdio: 'inherit',
  });
  if (build.status !== 0) throw new Error('Building the app for the end-to-end tests failed');

  const preview = spawn(process.execPath, [vite, 'preview', '--mode', 'e2e', '--outDir', 'dist-e2e'], {
    cwd: appRoot,
    env,
    stdio: 'ignore',
  });
  await waitForServer(`http://localhost:${PREVIEW_PORT}/`, 30_000);

  return async () => {
    preview.kill();
    await sidecar.stop();
    rmSync(temp, { recursive: true, force: true });
  };
}
