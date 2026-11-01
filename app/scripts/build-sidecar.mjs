// Builds the engine's sidecar for the Tauri shell (`npm run build:sidecar`): build-sidecar.ps1 on Windows,
// build-sidecar.sh elsewhere. Options, in either script's style:
//   --engine-root <folder> (-EngineRoot)     the engine folder; defaults to the repo's engine/
//   --configuration <name> (-Configuration)  Release by default
//   --target <triple> (-Target)              a Rust target triple; defaults to the host's
//   --no-ready-to-run (-NoReadyToRun)        leave out ahead-of-time compilation, which is on by default
// e.g. `npm run build:sidecar -- --engine-root ../../elsewhere/engine`.
import { spawnSync } from 'node:child_process';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const valued = { 'engine-root': 'EngineRoot', configuration: 'Configuration', target: 'Target' };
const switches = { 'no-ready-to-run': 'NoReadyToRun' };

function fail(message) {
  console.error(`build-sidecar: ${message}`);
  process.exit(1);
}

/** Reads `--engine-root x`, `--engine-root=x` and `-EngineRoot x` alike. */
function parse(args) {
  const options = new Map();
  for (let index = 0; index < args.length; index++) {
    const match = /^--?([A-Za-z][A-Za-z-]*)(?:=(.*))?$/s.exec(args[index]);
    if (!match) fail(`unexpected argument '${args[index]}'.`);
    const name = match[1].replace(/([a-z])([A-Z])/g, '$1-$2').toLowerCase();
    if (name in switches) {
      options.set(name, true);
      continue;
    }
    if (!(name in valued)) {
      fail(`unknown option '${args[index]}'. Use --engine-root, --configuration, --target or --no-ready-to-run.`);
    }
    const value = match[2] ?? args[++index];
    if (value === undefined) fail(`${args[index - 1]} needs a value.`);
    options.set(name, name === 'engine-root' ? path.resolve(value) : value);
  }
  return options;
}

const options = parse(process.argv.slice(2));
const windows = process.platform === 'win32';
const forwarded = [...options].flatMap(([name, value]) => {
  if (windows) return value === true ? [`-${switches[name]}`] : [`-${valued[name]}`, value];
  return value === true ? [`--${name}`] : [`--${name}`, value];
});
const [command, args] = windows
  ? [
      'powershell',
      ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', path.join(here, 'build-sidecar.ps1'), ...forwarded],
    ]
  : ['bash', [path.join(here, 'build-sidecar.sh'), ...forwarded]];
const result = spawnSync(command, args, { stdio: 'inherit' });
if (result.error) fail(`couldn't run ${command}: ${result.error.message}`);
process.exit(result.status ?? 1);
