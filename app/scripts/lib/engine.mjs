// The engine, from the app's scripts: the sidecar the shell ships (src-tauri/binaries, built by build-sidecar), the
// CLI for synthetic crawls, and crawl files for a data folder. Used by `npm run dev`, `npm run make:crawl` and the
// end-to-end tests. Never touches the developer's dev-crawls folder or real library: it only ever copies out of it.
import { spawn, spawnSync } from 'node:child_process';
import {
  copyFileSync,
  existsSync,
  mkdirSync,
  readFileSync,
  readdirSync,
  realpathSync,
  rmSync,
  statSync,
  writeFileSync,
} from 'node:fs';
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
  const ready = readyLine(child, { onLog });

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

/**
 * Resolves with a started sidecar's ready line (`{"event":"ready",…}` on stdout). Rejects if it exits first, or if
 * there's none within `timeoutMs`: it is killed then, since one that isn't serving has nothing to shut down
 * gracefully, and it mustn't outlive the run that gave up on it.
 */
export function readyLine(child, { onLog = () => undefined, timeoutMs = 30_000 } = {}) {
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => {
      child.kill();
      reject(new Error(`The sidecar wrote no ready line within ${timeoutMs / 1000} s`));
    }, timeoutMs);
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
    child.once('exit', (code) => {
      clearTimeout(timer);
      reject(new Error(`The sidecar exited with code ${code} before it was ready`));
    });
  });
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
 * The CLI's arguments for a synthetic crawl. `maxUrls` is four URLs a page unless given: enough for the pages, and it
 * leaves most of the planted cases unfetched. `storeHtml` keeps each page's HTML, for the inspector's source. The
 * browser fallback, on by default, is off: the site plants pages that block, and a browser opened for them would read
 * the rest of the site, slowly, and show a window on the computer running the tests.
 */
export function syntheticCrawlArgs({ url, out, pages, maxUrls = pages * 4, storeHtml = false }) {
  return [
    'crawl',
    url,
    '--db',
    out,
    '--max-urls',
    String(maxUrls),
    '--quiet',
    '--no-browser-fallback',
    ...(storeHtml ? ['--store-html'] : []),
  ];
}

/**
 * Crawls a synthetic site (the CLI's `testsite`, planted SEO issues) into `out`, analysed. Small by default, so it
 * takes seconds: enough for tests and for a quick look, with the 100k crawl kept for performance work. `maxUrls`
 * lifts the URL limit so the planted cases (redirect chains, hreflang, broken JSON-LD) are fetched too, and
 * `storeHtml` keeps each page's HTML in the crawl file, as the inspector's source needs.
 */
export async function makeSyntheticCrawl({
  out,
  pages = 60,
  maxUrls,
  storeHtml = false,
  root = engineRoot(),
  log = console.log,
}) {
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
    const args = syntheticCrawlArgs({ url, out, pages, storeHtml, ...(maxUrls === undefined ? {} : { maxUrls }) });
    const crawl = spawnSync(cli, args, { stdio: 'inherit' });
    if (crawl.status !== 0) throw new Error(`The crawl failed with code ${crawl.status}`);
  } finally {
    site.kill();
  }
  return out;
}

/** A SQLite database's companion files: its write-ahead log and the log's index. */
const COMPANIONS = ['-wal', '-shm'];

/** Where a path leads once links are followed; as it's written, for one that isn't there (yet). */
function realPath(file) {
  try {
    return realpathSync.native(file);
  } catch {
    return path.resolve(file);
  }
}

/**
 * Refuses the original's own folder as the place for its copy, before anything is touched: there the "copy" is the
 * original itself, so its write-ahead log and index would be removed as a stale copy's (losing what the log holds)
 * and the file copied onto itself; or, taken for a copy that's current, opened and changed in place. The folder is
 * compared as it resolves (`.`, `..`, a link or a junction to it, and on Windows any letter case).
 * @param {string} source
 * @param {string} folder
 */
function refuseOwnFolder(source, folder) {
  const [from, to] = [realPath(source), realPath(path.join(folder, path.basename(source)))];
  const same = process.platform === 'win32' ? from.toLowerCase() === to.toLowerCase() : from === to;
  if (same) {
    throw new Error(
      `A crawl is never copied into its own folder (${path.dirname(from)}): the copy would be the original. Give another folder.`,
    );
  }
}

/**
 * Copies a crawl file and its SQLite companions (-wal, -shm) into a folder. Never opens the original, and refuses its
 * own folder. Companions the target has from an earlier copy go first: SQLite would replay a stale write-ahead log
 * onto the new copy.
 */
export function copyCrawl(source, folder) {
  refuseOwnFolder(source, folder);
  mkdirSync(folder, { recursive: true });
  const target = path.join(folder, path.basename(source));
  for (const suffix of COMPANIONS) rmSync(target + suffix, { force: true });
  for (const suffix of ['', ...COMPANIONS]) {
    if (existsSync(source + suffix)) copyFileSync(source + suffix, target + suffix);
  }
  return target;
}

/**
 * Whether `target` is a current copy of `source`, by size and time, so big copies happen once: the crawl file and each
 * companion alike (one the source has and the target hasn't, or the other way round, isn't current).
 */
export function isCurrentCopy(source, target) {
  if (!existsSync(target)) return false;
  return ['', ...COMPANIONS].every((suffix) => isSameFile(source + suffix, target + suffix));
}

/** Both missing, or both there with the same size and the copy no older than the original. */
function isSameFile(original, copy) {
  const [hasOriginal, hasCopy] = [existsSync(original), existsSync(copy)];
  if (!hasOriginal || !hasCopy) return hasOriginal === hasCopy;
  const a = statSync(original);
  const b = statSync(copy);
  return a.size === b.size && b.mtimeMs >= a.mtimeMs;
}

/**
 * What a crawl file and each of its companions are now, by size and modified time: what `keepOrCopyCrawl` goes by to
 * tell whether the original has changed since it was copied. A file that isn't there is null.
 * @param {string} source
 * @returns {Record<string, { size: number, mtimeMs: number } | null>}
 */
export function sourceStamp(source) {
  return Object.fromEntries(
    ['', ...COMPANIONS].map((suffix) => {
      if (!existsSync(source + suffix)) return [suffix, null];
      const { size, mtimeMs } = statSync(source + suffix);
      return [suffix, { size, mtimeMs }];
    }),
  );
}

/** Whether two stamps are of the same files: each there or not alike, with the same size and modified time. */
function isSameStamp(a, b) {
  return ['', ...COMPANIONS].every((suffix) => {
    const [one, other] = [a?.[suffix] ?? null, b?.[suffix] ?? null];
    if (one === null || other === null) return one === other;
    return one.size === other.size && one.mtimeMs === other.mtimeMs;
  });
}

/** The stamp kept for a copy, or null when there's none that can be read. */
function readStamp(file) {
  try {
    const stamp = JSON.parse(readFileSync(file, 'utf8'));
    return typeof stamp === 'object' && stamp !== null ? stamp : null;
  } catch {
    return null;
  }
}

/**
 * A copy of `source` in `folder` for a development run that may change it (analyse it, continue it): the copy that's
 * there is kept while the original is as it was when the copy was made, by the size and modified time of the crawl
 * file and each companion, so a copy analysed in one run is still analysed in the next. `isCurrentCopy` can't say
 * that: an analysed copy is bigger than its original, so it reads as stale and was copied over on every start.
 *
 * What the original was is kept in `stamps` (a folder of the caller's, never the original's own: nothing is ever
 * written beside the original). A copy from before there were stamps is taken as it is when it's still the same as
 * the original, and copied again otherwise. The stamp goes before a copy starts and comes back when it's done, so a
 * copy cut short is made again.
 * @param {string} source
 * @param {string} folder
 * @param {string} stamps
 * @param {() => void} [onCopy] Called just before a copy starts: a big crawl's takes a while.
 * @returns {{ path: string, copied: boolean }}
 */
export function keepOrCopyCrawl(source, folder, stamps, onCopy = () => undefined) {
  // Before the copy that's there is looked at: in its own folder the original would read as a copy that's current,
  // and be handed to a run that analyses and continues it.
  refuseOwnFolder(source, folder);
  const target = path.join(folder, path.basename(source));
  const stampFile = path.join(stamps, `${path.basename(source)}.json`);
  const now = sourceStamp(source);
  const kept = existsSync(target) && isSameStamp(readStamp(stampFile), now);
  if (kept) return { path: target, copied: false };
  const untouched = !existsSync(stampFile) && isCurrentCopy(source, target);
  rmSync(stampFile, { force: true });
  if (!untouched) {
    onCopy();
    copyCrawl(source, folder);
  }
  mkdirSync(stamps, { recursive: true });
  writeFileSync(stampFile, JSON.stringify(now));
  return { path: target, copied: !untouched };
}

/**
 * @typedef {{ id: string, site: string | null, fileName: string, status: string, error?: string | null,
 *   analyzedAt: string | null, outOfDate: boolean }} LibraryItem
 * @typedef {{ port: number, stop: () => Promise<unknown> }} RunningSidecar
 */

/**
 * Starts a sidecar (`start`, on `dataDir`) on a copy of `crawl` whose analysis stands, and resolves with it and the
 * crawl's library item. The end-to-end tests assume one: with an out-of-date analysis the screens offer to analyse
 * again (a Tab stop of its own, before the grid) and leave the analysis's columns empty. The sidecar judges, being the
 * engine under test. `cached` marks the tests' own synthetic crawl: made with `remake(path)` when it's missing, and
 * made again when an older engine analysed it, so the next run starts from a current one. Any other crawl is never
 * touched: its copy is analysed again.
 * @param {{ crawl: string, cached: boolean, dataDir: string, token: string, start: () => Promise<RunningSidecar>,
 *   remake: (out: string) => Promise<unknown>, log?: (line: string) => void, timeoutMs?: number }} options
 * @returns {Promise<{ sidecar: RunningSidecar, item: LibraryItem }>}
 */
export async function startOnCurrentCrawl({
  crawl,
  cached,
  dataDir,
  token,
  start,
  remake,
  log = console.log,
  timeoutMs = 600_000,
}) {
  if (cached && !existsSync(crawl)) await remake(crawl);
  copyCrawl(crawl, dataDir);
  let sidecar = await start();
  try {
    let item = await listedCrawl(sidecar, token);
    if (analysisStands(item)) return { sidecar, item };
    const why = await outOfDateBecause(sidecar, token, item);
    if (cached) {
      log(`${crawl}: its analysis ${why}. Making the synthetic crawl again with this engine…`);
      await sidecar.stop();
      for (const suffix of ['', ...COMPANIONS]) rmSync(crawl + suffix, { force: true });
      await remake(crawl);
      copyCrawl(crawl, dataDir);
      sidecar = await start();
    } else {
      log(`${crawl}: its analysis ${why}. Analysing the tests' copy of it again (the original is left alone)…`);
      await analyse(sidecar, token, item.id, timeoutMs);
    }
    item = await listedCrawl(sidecar, token);
    if (!analysisStands(item)) {
      throw new Error(
        `The sidecar says ${item.fileName}'s analysis ${await outOfDateBecause(sidecar, token, item)}, even after ` +
          `${cached ? 'making it again' : 'analysing it again'}${item.error ? `: ${item.error}` : ''}. Is the sidecar ` +
          'older than the engine that made it? Build it again: npm run build:sidecar',
      );
    }
    return { sidecar, item };
  } catch (error) {
    await sidecar.stop();
    throw error;
  }
}

/** Analysed, by this engine, since the crawl last changed (`outOfDate` is false before the first analysis). */
const analysisStands = (/** @type {LibraryItem} */ item) => item.analyzedAt !== null && !item.outOfDate;

/** A request to the sidecar, with its token. */
function call(/** @type {RunningSidecar} */ sidecar, /** @type {string} */ token, route, init = {}) {
  return fetch(`http://127.0.0.1:${sidecar.port}${route}`, { ...init, headers: { Authorization: `Bearer ${token}` } });
}

/** The one crawl in the tests' data folder, as the library lists it. */
async function listedCrawl(sidecar, token) {
  const response = await call(sidecar, token, '/api/crawls');
  if (!response.ok) throw new Error(`The sidecar's library answered ${response.status}`);
  const [first] = /** @type {LibraryItem[]} */ (await response.json());
  if (!first) throw new Error('The sidecar lists no crawl in the e2e data folder');
  return first;
}

/** "is out of date (engineChanged)", or "is missing" before the first analysis, for the log and errors. */
async function outOfDateBecause(sidecar, token, /** @type {LibraryItem} */ item) {
  if (item.analyzedAt === null) return 'is missing';
  const response = await call(sidecar, token, `/api/crawls/${item.id}/analysis`);
  const reasons = response.ok ? (await response.json()).outOfDateReasons : [];
  return `is out of date${reasons?.length ? ` (${reasons.join(', ')})` : ''}`;
}

/** Analyses the crawl and waits for it to finish. The sidecar lists it as analysing before it answers 202. */
async function analyse(sidecar, token, id, timeoutMs) {
  const response = await call(sidecar, token, `/api/crawls/${id}/analyze`, { method: 'POST' });
  if (response.status !== 202) {
    throw new Error(`The sidecar wouldn't analyse the crawl: ${response.status} ${await response.text()}`);
  }
  const started = Date.now();
  while ((await listedCrawl(sidecar, token)).status === 'analyzing') {
    if (Date.now() - started > timeoutMs) throw new Error(`The analysis didn't finish within ${timeoutMs / 1000} s`);
    await new Promise((resolve) => setTimeout(resolve, 250));
  }
}
