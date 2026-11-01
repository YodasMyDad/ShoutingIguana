// Shared settings for the fixture scripts: where the crawls come from, where the copies go, where fixtures are written.
// Every path can be given on the command line (--name value or --name=value) or through an environment variable.
import { existsSync, mkdirSync, realpathSync } from 'node:fs';
import { homedir, tmpdir } from 'node:os';
import { dirname, join, relative, resolve, isAbsolute } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));

/** app/design/data */
export const DATA_DIR = resolve(here, '..', '..');
/** The repository (or worktree) root. */
export const REPO_ROOT = resolve(DATA_DIR, '..', '..', '..');

export const USAGE = `
Options (all optional; environment variable in brackets):
  --source <dir>     Folder holding the original crawl files. Only ever read.   [SI_DESIGN_SOURCE]
                     Default: %LOCALAPPDATA%\\ShoutingIguana\\dev-crawls
  --work <dir>       Scratch folder: crawls/ holds the copies the scripts open,  [SI_DESIGN_WORK]
                     state/ the sidecar's state. Default: <temp>\\si-design-data
  --out <dir>        Where fixtures are written. Default: app/design/data/fixtures [SI_DESIGN_OUT]
  --garden <file>    Garden crawl file name. Default: www.gardenfurnituresets.co.uk-20261031.sicrawl
  --synthetic <file> 100k crawl file name. Default: synthetic-100k.sicrawl
  --sidecar <path>   si-sidecar.exe (or si-sidecar.dll, run with dotnet).        [SI_SIDECAR_EXE]
                     Default: engine/src/ShoutingIguana.Sidecar/bin/{Release,Debug}/net10.0/
  --logo <file>      Default: Assets/logo.png
  --assets <dir>     Where the logo copy goes. Default: app/design/assets
`;

/** --name value, --name=value and bare --flag (true). */
export function parseArgs(argv = process.argv.slice(2)) {
    const args = {};
    for (let index = 0; index < argv.length; index++) {
        const arg = argv[index];
        if (!arg.startsWith('--')) {
            throw new Error(`Unexpected argument '${arg}'.${USAGE}`);
        }

        const equals = arg.indexOf('=');
        if (equals > 0) {
            args[arg.slice(2, equals)] = arg.slice(equals + 1);
        } else if (index + 1 < argv.length && !argv[index + 1].startsWith('--')) {
            args[arg.slice(2)] = argv[++index];
        } else {
            args[arg.slice(2)] = true;
        }
    }

    return args;
}

/** Resolves 8.3 short names (C:\Users\LEEMES~1) so paths read the way the sidecar reports them. */
function longPath(path) {
    const absolute = resolve(path);
    mkdirSync(absolute, { recursive: true });
    return realpathSync.native(absolute);
}

function findSidecar() {
    for (const configuration of ['Release', 'Debug']) {
        const exe = join(REPO_ROOT, 'engine', 'src', 'ShoutingIguana.Sidecar', 'bin', configuration, 'net10.0',
            process.platform === 'win32' ? 'si-sidecar.exe' : 'si-sidecar');
        if (existsSync(exe)) {
            return exe;
        }
    }

    return null;
}

export function settings(args = parseArgs()) {
    if (args.help) {
        console.log(USAGE);
        process.exit(0);
    }

    const localAppData = process.env.LOCALAPPDATA ?? join(homedir(), 'AppData', 'Local');
    const source = resolve(args.source ?? process.env.SI_DESIGN_SOURCE ?? join(localAppData, 'ShoutingIguana', 'dev-crawls'));
    const work = longPath(args.work ?? process.env.SI_DESIGN_WORK ?? join(tmpdir(), 'si-design-data'));
    const out = resolve(args.out ?? process.env.SI_DESIGN_OUT ?? join(DATA_DIR, 'fixtures'));
    const settings = {
        source,
        work,
        crawlsDir: join(work, 'crawls'),
        stateDir: join(work, 'state'),
        out,
        files: {
            garden: args.garden ?? 'www.gardenfurnituresets.co.uk-20261031.sicrawl',
            synthetic: args.synthetic ?? 'synthetic-100k.sicrawl'
        },
        sidecar: args.sidecar ?? process.env.SI_SIDECAR_EXE ?? findSidecar(),
        logo: resolve(args.logo ?? join(REPO_ROOT, 'Assets', 'logo.png')),
        assets: resolve(args.assets ?? join(REPO_ROOT, 'app', 'design', 'assets')),
        args
    };

    if (isInside(settings.work, source) || isInside(source, settings.work)) {
        throw new Error(`The work folder (${settings.work}) must be apart from the source folder (${source}): nothing may be written there.`);
    }

    return settings;
}

/** The copy of a crawl the scripts open: never the original. */
export function crawlCopy(settings, which) {
    const path = join(settings.crawlsDir, settings.files[which]);
    assertNotSource(settings, path);
    if (!existsSync(path)) {
        throw new Error(`${path} doesn't exist. Run copy-crawls.mjs first (or run-all.mjs).`);
    }

    return path;
}

export function assertNotSource(settings, path) {
    if (isInside(resolve(path), settings.source)) {
        throw new Error(`Refusing to open ${path}: it is in the source folder. The scripts only ever open copies.`);
    }
}

function isInside(path, folder) {
    const rel = relative(resolve(folder).toLowerCase(), resolve(path).toLowerCase());
    return rel === '' || (!rel.startsWith('..') && !isAbsolute(rel));
}
