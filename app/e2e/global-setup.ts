// Starts what the end-to-end tests run against, and returns the teardown:
// 1. a temp data folder holding one crawl: SI_E2E_CRAWL's file, or a small synthetic crawl made with the CLI, every
//    planted case fetched and its HTML stored;
// 2. the real sidecar (the one the shell bundles) on that folder, with a fresh state folder, allowing only the
//    preview server's origin. The crawl's analysis must stand, as the tests assume: a cached synthetic crawl an
//    older engine analysed is made again, and SI_E2E_CRAWL's copy is analysed again (startOnCurrentCrawl);
// 3. the engine's synthetic test site (the CLI's `testsite`), for the tests that crawl live: its URL is
//    SI_E2E_TESTSITE. Never a real site;
// 4. the app built for it (`--mode e2e`, the sidecar's port and token baked in, as `npm run dev` passes them), served
//    by `vite preview`.
import { spawn, spawnSync } from 'node:child_process';
import { mkdtempSync, rmSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { createInterface } from 'node:readline';
import { appRoot, buildCli, makeSyntheticCrawl, startOnCurrentCrawl, startSidecar } from '../scripts/lib/engine.mjs';
import { E2E_SIDECAR_PORT, PREVIEW_PORT } from '../scripts/ports.mjs';

/** Tests only: a sidecar on 127.0.0.1 over a temp folder. Never a release token. */
const E2E_TOKEN = 'shouting-iguana-end-to-end-token-not-a-secret';

/** Pages on the test site: enough for a live crawl to be watched, paused and stopped before it ends. */
const TESTSITE_PAGES = 6_000;

/** Serves the engine's synthetic site on a free port, and resolves with it and its URL once it says it's ready. */
async function startTestSite() {
  const site = spawn(buildCli(), ['testsite', '--pages', String(TESTSITE_PAGES), '--port', '0'], {
    stdio: ['ignore', 'pipe', 'inherit'],
    windowsHide: true,
  });
  const url = await new Promise<string>((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error('testsite wrote no READY line within 30 s')), 30_000);
    createInterface({ input: site.stdout }).on('line', (line) => {
      if (!line.startsWith('READY ')) return;
      clearTimeout(timer);
      resolve(line.slice('READY '.length).split(' ')[0] ?? '');
    });
    site.once('exit', (code) => reject(new Error(`testsite exited with code ${code}`)));
  });
  return { site, url };
}

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

  const given = process.env.SI_E2E_CRAWL;
  const { sidecar, item } = await startOnCurrentCrawl({
    // Every planted case fetched and its HTML stored, for the inspector: a cache from before that has another name,
    // so it's made again.
    crawl: given ? path.resolve(given) : path.join(appRoot, '.engine-build', 'crawls', 'e2e-synthetic-full.sicrawl'),
    cached: !given,
    dataDir,
    token: E2E_TOKEN,
    start: () =>
      startSidecar({
        port: E2E_SIDECAR_PORT,
        token: E2E_TOKEN,
        origins: [`http://localhost:${PREVIEW_PORT}`],
        dataDir,
        stateDir,
      }),
    remake: (out) => makeSyntheticCrawl({ out, pages: 40, maxUrls: 5_000, storeHtml: true }),
  });

  // What the library says the crawl is, for the tests to look for.
  process.env.SI_E2E_SITE = item.site ?? item.fileName;
  // And as Home's rows name it: its host with any port of its own, as one host on two ports is two sites.
  const { startUrl } = item as { startUrl?: string | null };
  const port = item.site && startUrl ? new URL(startUrl).port : '';
  process.env.SI_E2E_SITE_ROW = port ? `${item.site}:${port}` : (item.site ?? item.fileName);

  const testSite = await startTestSite().catch(async (error: unknown) => {
    await sidecar.stop();
    throw error;
  });
  process.env.SI_E2E_TESTSITE = testSite.url;
  // The tests that start crawls remove them through the sidecar, with the token it was started with.
  process.env.SI_E2E_SIDECAR = `http://127.0.0.1:${sidecar.port}`;
  process.env.SI_E2E_TOKEN = E2E_TOKEN;

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
    testSite.site.kill();
    await sidecar.stop();
    rmSync(temp, { recursive: true, force: true });
  };
}
