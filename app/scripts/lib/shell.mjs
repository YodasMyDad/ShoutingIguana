// The built app from outside, for `npm run smoke:shell`: the environment of a test run, the script's options, the
// steps of a walk and what they come to. Nothing here starts anything, so all of it is tested (shell.test.mjs).
import path from 'node:path';

/** WebView2 reads extra browser arguments from here: how a run of the app gets its DevTools port. */
export const BROWSER_ARGUMENTS = 'WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS';

/** Virtual-key codes of the keys the walk presses in the webview itself (scripts/shell-window.ps1 `keys`). */
export const VIRTUAL_KEYS = { F3: 0x72, F5: 0x74, F9: 0x78, Escape: 0x1b };

/**
 * The browser arguments with the debugging port set to `port`: any port already asked for is replaced, anything else
 * the developer set is kept.
 * @param {string | undefined} existing
 * @param {number} port
 */
export function withDebuggingPort(existing, port) {
  const kept = (existing ?? '')
    .split(/\s+/)
    .filter((argument) => argument && !argument.startsWith('--remote-debugging-port'));
  return [...kept, `--remote-debugging-port=${port}`].join(' ');
}

/**
 * The environment of a test run of the app: its own data, state and profile folders under `folder` (so it never
 * touches the user's copy of the app, and runs beside it), and WebView2's DevTools port.
 * @param {{ folder: string, port: number, environment?: Record<string, string | undefined> }} options
 */
export function shellEnvironment({ folder, port, environment = process.env }) {
  return {
    ...environment,
    SI_APP_DATA_DIR: path.join(folder, 'data'),
    SI_APP_STATE_DIR: path.join(folder, 'state'),
    SI_APP_PROFILE_DIR: path.join(folder, 'profile'),
    [BROWSER_ARGUMENTS]: withDebuggingPort(environment[BROWSER_ARGUMENTS], port),
  };
}

/** The steps of the walk that can be left out with `--skip`. */
export const SKIPPABLE = ['keys', 'live', 'tray', 'restart'];

/**
 * The script's options: `--exe <path>`, `--crawl <file>`, `--port <n>`, `--shots <folder>`, `--pages <n>` (the test
 * site's), `--skip keys,live,tray,restart`, `--keep` (leave the run's folder behind), `--tray-stay <s>` (how long the
 * window stays in the tray, its memory measured every 30 s). Anything else is refused.
 * @param {string[]} argv
 */
export function parseOptions(argv) {
  /** @type {{ exe?: string, crawl?: string, port?: number, shots?: string, pages: number, keep: boolean, skip: Set<string>, trayStay?: number }} */
  const options = { pages: 20_000, keep: false, skip: new Set() };
  for (let index = 0; index < argv.length; index++) {
    const name = argv[index];
    if (name === '--keep') {
      options.keep = true;
      continue;
    }
    const value = argv[index + 1];
    if (!['--exe', '--crawl', '--port', '--shots', '--pages', '--skip', '--tray-stay'].includes(name))
      throw new Error(`Unknown option '${name}'`);
    if (value === undefined || value.startsWith('--')) throw new Error(`${name} needs a value`);
    index++;
    if (name === '--tray-stay') {
      const seconds = Number(value);
      if (!Number.isInteger(seconds) || seconds < 1 || seconds > 3_600)
        throw new Error(`--tray-stay must be a whole number of seconds from 1 to 3600, not '${value}'`);
      options.trayStay = seconds;
    } else if (name === '--port' || name === '--pages') {
      const number = Number(value);
      const limit = name === '--port' ? 65_535 : 1_000_000;
      if (!Number.isInteger(number) || number < 1 || number > limit)
        throw new Error(`${name} must be a whole number from 1 to ${limit}, not '${value}'`);
      options[name === '--port' ? 'port' : 'pages'] = number;
    } else if (name === '--skip') {
      for (const part of value.split(',')) {
        if (!SKIPPABLE.includes(part)) throw new Error(`--skip takes ${SKIPPABLE.join(', ')}, not '${part}'`);
        options.skip.add(part);
      }
    } else {
      options[/** @type {'exe' | 'crawl' | 'shots'} */ (name.slice(2))] = path.resolve(value);
    }
  }
  return options;
}

/**
 * What the shell holds in memory: the shell and every process it started, its WebView2 ones among them, but not its
 * engine (`si-sidecar.exe`) or anything the engine started, whose memory is the crawl's. Three measures, in MB, as
 * Windows keeps them per process: the working set (every page in RAM, the pages shared with other processes, such as
 * the system's DLLs, counted in each), the private working set (only the pages in RAM no other process shares: Task
 * Manager's "Memory" column, and its process group's sum), and the private bytes (memory committed to the process
 * alone, in RAM or paged out).
 * @param {{ pid: number, parent: number, name: string, workingSet: number, privateBytes: number, privateWorkingSet: number | null }[]} processes
 * @param {number} shellPid
 */
export function shellMemory(processes, shellPid) {
  const children = new Map();
  for (const process of processes) {
    const siblings = children.get(process.parent) ?? [];
    siblings.push(process);
    children.set(process.parent, siblings);
  }
  const counted = [];
  const visit = (process) => {
    if (process.name.toLowerCase() === 'si-sidecar.exe') return;
    counted.push(process);
    for (const child of children.get(process.pid) ?? []) if (child.pid !== process.pid) visit(child);
  };
  const shell = processes.find((process) => process.pid === shellPid);
  if (shell) visit(shell);
  const mb = (bytes) => Math.round((bytes / 1024 / 1024) * 10) / 10;
  const sum = (pick) => counted.reduce((total, process) => total + (pick(process) ?? 0), 0);
  return {
    processes: counted.length,
    workingSetMb: mb(sum((process) => process.workingSet)),
    privateWorkingSetMb: counted.every((process) => process.privateWorkingSet !== null)
      ? mb(sum((process) => process.privateWorkingSet))
      : null,
    privateBytesMb: mb(sum((process) => process.privateBytes)),
  };
}

/**
 * The built app: the release build if there is one, else the debug build.
 * @param {string} appRoot
 * @param {(file: string) => boolean} exists
 */
export function builtApp(appRoot, exists) {
  return ['release', 'debug']
    .map((profile) => path.join(appRoot, 'src-tauri', 'target', profile, 'shouting-iguana.exe'))
    .find((candidate) => exists(candidate));
}

/**
 * The app's page among the targets on the DevTools port: not DevTools' own pages, nor WebView2's (edge://…).
 * @template {{ url(): string }} P
 * @param {P[]} pages
 * @returns {P | undefined}
 */
export function appPage(pages) {
  return pages.find((page) => /^https?:\/\/tauri\.localhost\/|^tauri:\/\/localhost\//.test(page.url()));
}

/**
 * What a list of frame times (ms between animation frames) comes to. The share of frames over 33.4 ms is what
 * "60 frames a second" turns on: a frame that long is a dropped one, whatever the display's own rate.
 * @param {number[]} frames
 */
export function frameStats(frames) {
  if (frames.length === 0) return { frames: 0, seconds: 0, fps: 0, p50: 0, p99: 0, max: 0, over33ms: 0 };
  const sorted = [...frames].sort((a, b) => a - b);
  const at = (/** @type {number} */ share) =>
    round(sorted[Math.min(sorted.length - 1, Math.floor(sorted.length * share))]);
  const total = sorted.reduce((sum, frame) => sum + frame, 0);
  return {
    frames: sorted.length,
    seconds: round(total / 1000),
    fps: round((sorted.length / total) * 1000),
    p50: at(0.5),
    p99: at(0.99),
    max: round(sorted[sorted.length - 1]),
    over33ms: sorted.filter((frame) => frame > 33.4).length,
  };
}

const round = (/** @type {number} */ value) => Math.round(value * 10) / 10;

/**
 * A walk: named steps run in order, each recorded as it ends. A step that fails doesn't stop the walk, unless what
 * follows needs it (`needed`): then the rest is skipped, and says so.
 */
export class Walk {
  /** @type {{ name: string, outcome: 'ok' | 'failed' | 'skipped', detail: string, ms: number }[]} */
  steps = [];
  #stopped = '';

  /** @param {(line: string) => void} log */
  constructor(log = () => undefined) {
    this.log = log;
  }

  /**
   * @param {string} name
   * @param {() => Promise<string | void>} run What it returns is the step's evidence, in a few words.
   * @param {{ needed?: boolean }} [options]
   */
  async step(name, run, { needed = false } = {}) {
    if (this.#stopped) return this.#record(name, 'skipped', `after "${this.#stopped}" failed`, 0);
    const started = Date.now();
    try {
      const detail = (await run()) ?? '';
      return this.#record(name, 'ok', detail, Date.now() - started);
    } catch (error) {
      if (needed) this.#stopped = name;
      return this.#record(name, 'failed', firstLine(error), Date.now() - started);
    }
  }

  /** Records a step left out on purpose. */
  skip(name, why) {
    return this.#record(name, 'skipped', why, 0);
  }

  #record(name, outcome, detail, ms) {
    const step = { name, outcome, detail, ms };
    this.steps.push(step);
    this.log(formatStep(step));
    return outcome === 'ok';
  }

  get failed() {
    return this.steps.filter((step) => step.outcome === 'failed').length;
  }
}

const firstLine = (/** @type {unknown} */ error) =>
  String(error instanceof Error ? error.message : error)
    .split('\n')[0]
    .slice(0, 300);

/** @param {{ name: string, outcome: string, detail: string, ms: number }} step */
export function formatStep(step) {
  const mark = step.outcome === 'ok' ? 'ok  ' : step.outcome === 'failed' ? 'FAIL' : 'skip';
  return `${mark} ${step.name}${step.detail ? `: ${step.detail}` : ''}${step.outcome === 'skipped' ? '' : ` (${step.ms} ms)`}`;
}

/** The walk's last line. @param {Walk} walk */
export function verdict(walk) {
  const count = (/** @type {string} */ outcome) => walk.steps.filter((step) => step.outcome === outcome).length;
  const skipped = count('skipped') > 0 ? `, ${count('skipped')} skipped` : '';
  return walk.failed === 0
    ? `smoke-shell: PASS (${count('ok')} steps${skipped})`
    : `smoke-shell: FAIL (${walk.failed} of ${walk.steps.length} steps failed${skipped})`;
}
