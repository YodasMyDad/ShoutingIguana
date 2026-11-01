// The engine, from the app's scripts: the sidecar the shell ships (src-tauri/binaries, built by build-sidecar), the
// CLI for synthetic crawls, and crawl files for a data folder. Used by `npm run dev`, `npm run make:crawl` and the
// end-to-end tests. Never touches the developer's dev-crawls folder or real library: it only ever copies out of it.
import { spawn, spawnSync } from 'node:child_process';
import { copyFileSync, existsSync, mkdirSync, readdirSync, statSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { createInterface } from 'node:readline';
import { fileURLToPath } from 'node:url';

export const appRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
export const repoRoot = path.resolve(appRoot, '..');
const exe = process.platform === 'win32' ? '.exe' : '';

/** The engine folder to build from: --engine-root, SI_ENGINE_ROOT, or the repo's own engine/. */
export function engineRoot(argv = process.argv) {
  const index = argv.indexOf('--engine-root');
  const given = index >= 0 ? argv[index + 1] : process.env.SI_ENGINE_ROOT;
  return path.resolve(given ?? path.join(repoRoot, 'engine'));
}

/** The developer's own crawls (app/design/data/README.md): read-only, copied out, never opened in place. */
export function devCrawlsFolder() {
  const local = process.env.LOCALAPPDATA ?? path.join(os.homedir(), '.local', 'share');
  return path.join(local, 'ShoutingIguana', 'dev-crawls');
}

/** The sidecar binary the shell bundles. Built by `npm run build:sidecar`. */
export function sidecarBinary() {
  const folder = path.join(appRoot, 'src-tauri', 'binaries');
  const name = existsSync(folder)
    ? readdirSync(folder).find((file) => file.startsWith('si-sidecar-') && file.endsWith(exe))
    : undefined;
  if (!name) throw new Error(`No sidecar in ${folder}. Build it first: npm run build:sidecar`);
  return path.join(folder, name);
}

/**
 * Starts a sidecar as the contract says (docs/v2-sidecar-api.md, Process): the token in its environment, the
 * allowed origins, its own data and state folders, stdin as the lifeline. Resolves once it has written its ready line.
 */
export function startSidecar({ port = 0, token, origins, dataDir, stateDir, onLog = () => undefined }) {
  mkdirSync(dataDir, { recursive: true });
  mkdirSync(stateDir, { recursive: true });
  const child = spawn(
    sidecarBinary(),
    ['--data-dir', dataDir, '--state-dir', stateDir, '--port', String(port), '--stdin-lifeline'],
    {
      env: {
        ...process.env,
        SI_SIDECAR_TOKEN: token,
        SI_SIDECAR_ORIGINS: origins.join(','),
        SI_PARENT_PID: String(process.pid),
      },
      stdio: ['pipe', 'pipe', 'pipe'],
      windowsHide: true,
    },
  );
  createInterface({ input: child.stderr }).on('line', onLog);
  const exited = new Promise((resolve) => child.once('exit', (code) => resolve(code)));

  const ready = new Promise((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error('The sidecar wrote no ready line within 30 s')), 30_000);
    createInterface({ input: child.stdout }).on('line', (line) => {
      try {
        const message = JSON.parse(line);
        if (message.event === 'ready') {
          clearTimeout(timer);
          resolve(message);
        }
      } catch {
        onLog(`stdout: ${line}`);
      }
    });
    void exited.then((code) => {
      clearTimeout(timer);
      reject(new Error(`The sidecar exited with code ${code} before it was ready`));
    });
  });

  /** Asks it to stop (stdin `shutdown`), and kills it only if it hasn't within the contract's 30 s and a margin. */
  async function stop() {
    if (child.exitCode !== null) return child.exitCode;
    child.stdin.end('shutdown\n');
    const timeout = new Promise((resolve) => setTimeout(() => resolve('timeout'), 35_000));
    const result = await Promise.race([exited, timeout]);
    if (result === 'timeout') child.kill();
    return result;
  }

  return ready.then((message) => ({ port: message.port, version: message.version, child, stop }));
}

/** Builds the engine's CLI (`si`) into app/.engine-build, apart from every other build of the engine. */
export function buildCli(root = engineRoot()) {
  const project = path.join(root, 'src', 'ShoutingIguana.Cli', 'ShoutingIguana.Cli.csproj');
  if (!existsSync(project)) throw new Error(`No CLI project at ${project}`);
  const artifacts = path.join(appRoot, '.engine-build');
  const result = spawnSync(
    'dotnet',
    ['build', project, '-c', 'Release', '--artifacts-path', artifacts, '-v', 'quiet', '-nologo'],
    {
      stdio: 'inherit',
    },
  );
  if (result.status !== 0) throw new Error('Building the CLI failed');
  const cli = path.join(artifacts, 'bin', 'ShoutingIguana.Cli', 'release', `si${exe}`);
  if (!existsSync(cli)) throw new Error(`The CLI build left no ${cli}`);
  return cli;
}

/**
 * Crawls a synthetic site (the CLI's `testsite`, planted SEO issues) into `out`, analysed. Small by default, so it
 * takes seconds: enough for tests and for a quick look, with the 100k crawl kept for performance work.
 */
export async function makeSyntheticCrawl({ out, pages = 60, root = engineRoot(), log = console.log }) {
  const cli = buildCli(root);
  mkdirSync(path.dirname(out), { recursive: true });
  const site = spawn(cli, ['testsite', '--pages', String(pages), '--port', '0'], {
    stdio: ['ignore', 'pipe', 'inherit'],
    windowsHide: true,
  });
  try {
    const url = await new Promise((resolve, reject) => {
      const timer = setTimeout(() => reject(new Error('testsite wrote no READY line within 30 s')), 30_000);
      createInterface({ input: site.stdout }).on('line', (line) => {
        if (!line.startsWith('READY ')) return;
        clearTimeout(timer);
        resolve(line.slice('READY '.length).split(' ')[0]);
      });
      site.once('exit', (code) => reject(new Error(`testsite exited with code ${code}`)));
    });
    log(`Crawling the synthetic site at ${url}…`);
    const crawl = spawnSync(cli, ['crawl', url, '--db', out, '--max-urls', String(pages * 4), '--quiet'], {
      stdio: 'inherit',
    });
    if (crawl.status !== 0) throw new Error(`The crawl failed with code ${crawl.status}`);
  } finally {
    site.kill();
  }
  return out;
}

/** Copies a crawl file and its SQLite companions (-wal, -shm) into a folder. Never opens the original. */
export function copyCrawl(source, folder) {
  mkdirSync(folder, { recursive: true });
  const target = path.join(folder, path.basename(source));
  for (const suffix of ['', '-wal', '-shm']) {
    if (existsSync(source + suffix)) copyFileSync(source + suffix, target + suffix);
  }
  return target;
}

/** Whether `target` is a current copy of `source`, by size and time, so big copies happen once. */
export function isCurrentCopy(source, target) {
  if (!existsSync(target)) return false;
  const a = statSync(source);
  const b = statSync(target);
  return a.size === b.size && b.mtimeMs >= a.mtimeMs;
}
