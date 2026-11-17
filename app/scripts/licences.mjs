// The third-party licences of what the app ships (gap 31: About lists them), made at build time from the packages
// actually in it: the npm packages the page is built from (package-lock.json, without the development ones), the
// Rust crates the shell is built from (`cargo metadata`, the normal dependencies of the shell for this platform, not
// build scripts' or tests'), the NuGet packages the engine is published with (the sidecar's project.assets.json, less
// the build-only ones) and the .NET runtime it carries, and the fonts. Each with its licence and its licence's text,
// the texts kept once each.
//
//   node scripts/licences.mjs [--out <file>] [--strict]
//
// Vite runs it on every build (vite.config.ts) and serves the result as licences.json. --strict (and a Tauri build,
// which sets TAURI_ENV_PLATFORM) fails when a source can't be read, so a release never ships a partial list.
import { execFileSync } from 'node:child_process';
import { existsSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';

const appDir = path.resolve(import.meta.dirname, '..');

/** @typedef {{ name: string, version: string, source: 'npm' | 'cargo' | 'nuget' | 'font', licence: string, url: string | null, text: number | null }} Licensed */

const LICENCE_FILE = /^(licen[cs]e|copying|notice)([-._].*)?(\.(md|txt|markdown))?$/i;

/** The licence texts in a package's folder, joined, or null. */
export function licenceTexts(folder) {
  if (!folder || !existsSync(folder)) return null;
  const files = readdirSync(folder, { withFileTypes: true })
    .filter((entry) => entry.isFile() && LICENCE_FILE.test(entry.name))
    .map((entry) => entry.name)
    .sort();
  if (files.length === 0) return null;
  return files.map((file) => readFileSync(path.join(folder, file), 'utf8').trim()).join('\n\n');
}

/** npm: every package the lock file installs for production, from its own folder in node_modules. */
export function npmPackages(lock, root = appDir) {
  return Object.entries(lock.packages ?? {})
    .filter(([key, entry]) => key.startsWith('node_modules/') && !entry.dev && !entry.devOptional && !entry.link)
    .map(([key, entry]) => {
      const name = entry.name ?? key.slice(key.lastIndexOf('node_modules/') + 'node_modules/'.length);
      return {
        name,
        version: entry.version ?? '',
        source: 'npm',
        licence: entry.license ?? 'Unknown',
        url: `https://www.npmjs.com/package/${name}`,
        folder: path.join(root, key),
      };
    });
}

/** cargo: the crates the shell links, walked from it through normal dependencies only. */
export function cargoPackages(metadata) {
  const byId = new Map(metadata.packages.map((pkg) => [pkg.id, pkg]));
  const nodes = new Map(metadata.resolve.nodes.map((node) => [node.id, node]));
  const root = metadata.resolve.root;
  const seen = new Set();
  const queue = [root];
  while (queue.length > 0) {
    const id = queue.shift();
    if (seen.has(id)) continue;
    seen.add(id);
    for (const dep of nodes.get(id)?.deps ?? []) {
      // `kind: null` is a normal dependency; "build" and "dev" never ship.
      if (dep.dep_kinds.some((kind) => kind.kind === null)) queue.push(dep.pkg);
    }
  }
  seen.delete(root);
  return [...seen]
    .map((id) => byId.get(id))
    .filter(Boolean)
    .map((pkg) => ({
      name: pkg.name,
      version: pkg.version,
      source: 'cargo',
      licence: pkg.license ?? (pkg.license_file ? `See ${pkg.license_file}` : 'Unknown'),
      url: pkg.repository ?? `https://crates.io/crates/${pkg.name}`,
      folder: path.dirname(pkg.manifest_path),
    }));
}

/** A nuspec's licence: its expression, or the file it names, or its URL. */
export function nuspecLicence(nuspec) {
  const expression = /<license\s+type="expression"\s*>([^<]+)<\/license>/i.exec(nuspec);
  if (expression) return { licence: expression[1].trim(), file: null };
  const file = /<license\s+type="file"\s*>([^<]+)<\/license>/i.exec(nuspec);
  if (file) return { licence: 'See its licence', file: file[1].trim() };
  const url = /<licenseUrl>([^<]+)<\/licenseUrl>/i.exec(nuspec);
  return { licence: url ? url[1].trim() : 'Unknown', file: null };
}

/** NuGet: the packages the sidecar is published with for `runtime`, less the build-only ones, and the runtimes. */
export function nugetPackages(assets, cache, runtime = 'win-x64') {
  const framework = Object.keys(assets.project?.frameworks ?? {})[0];
  const target = assets.targets?.[`${framework}/${runtime}`] ?? assets.targets?.[framework] ?? {};
  const buildOnly = new Set(
    Object.entries(assets.project?.frameworks?.[framework]?.dependencies ?? {})
      .filter(([, dependency]) => dependency.suppressParent === 'All')
      .map(([name]) => name.toLowerCase()),
  );
  const packages = Object.entries(target)
    .filter(([, entry]) => entry.type === 'package')
    .map(([key]) => key.split('/'))
    .filter(([name]) => !buildOnly.has(name.toLowerCase()))
    .map(([name, version]) => ({ name, version }));
  // The runtimes the self-contained engine carries.
  for (const dependency of assets.project?.frameworks?.[framework]?.downloadDependencies ?? []) {
    if (/^Microsoft\.(NETCore|AspNetCore)\.App\.Runtime\./i.test(dependency.name)) {
      packages.push({ name: dependency.name, version: dependency.version.replace(/^\[|,.*$|\]$/g, '') });
    }
  }
  return packages.map(({ name, version }) => {
    const folder = path.join(cache, name.toLowerCase(), version);
    const nuspecPath = path.join(folder, `${name.toLowerCase()}.nuspec`);
    const nuspec = existsSync(nuspecPath) ? readFileSync(nuspecPath, 'utf8') : '';
    const { licence, file } = nuspecLicence(nuspec);
    return {
      name,
      version,
      source: 'nuget',
      licence,
      url: `https://www.nuget.org/packages/${name}/${version}`,
      folder,
      textFile: file ? path.join(folder, file) : null,
    };
  });
}

/** The fonts the page carries, each with its OFL text beside it. */
export function fontPackages(fontsDir) {
  if (!existsSync(fontsDir)) return [];
  return readdirSync(fontsDir)
    .filter((file) => file.endsWith('-OFL.txt'))
    .map((file) => ({
      name: file.replace(/-OFL\.txt$/, ''),
      version: '',
      source: 'font',
      licence: 'OFL-1.1',
      url: null,
      textFile: path.join(fontsDir, file),
    }));
}

/**
 * The font metrics the engine carries, not a font: the advance widths of Liberation Sans 2.1.5, which the title and
 * description checks measure text by (engine/src/ShoutingIguana.Engine/Analysis/Rules/ArialAdvanceWidths.cs), with the
 * OFL text the generator keeps beside the table.
 */
export function engineFontPackages(rulesDir) {
  const textFile = path.join(rulesDir, 'LiberationSans-OFL.txt');
  if (!existsSync(textFile)) throw new Error(`${textFile} is missing`);
  return [
    {
      name: 'Liberation Sans (advance widths)',
      version: '2.1.5',
      source: 'font',
      licence: 'OFL-1.1',
      url: 'https://github.com/liberationfonts/liberation-fonts',
      textFile,
    },
  ];
}

/** The list, its texts kept once: { packages, texts }. */
export function assemble(found) {
  const texts = [];
  const index = new Map();
  const packages = found
    .map((pkg) => {
      const text =
        pkg.textFile && existsSync(pkg.textFile) ? readFileSync(pkg.textFile, 'utf8').trim() : licenceTexts(pkg.folder);
      let at = null;
      if (text) {
        at = index.get(text) ?? null;
        if (at === null) {
          at = texts.push(text) - 1;
          index.set(text, at);
        }
      }
      return { name: pkg.name, version: pkg.version, source: pkg.source, licence: pkg.licence, url: pkg.url, text: at };
    })
    .sort(
      (a, b) => a.source.localeCompare(b.source) || a.name.localeCompare(b.name) || a.version.localeCompare(b.version),
    );
  return { packages, texts };
}

function hostTriple() {
  const version = execFileSync('rustc', ['-vV'], { encoding: 'utf8' });
  return /host: (\S+)/.exec(version)?.[1] ?? null;
}

const RUNTIMES = {
  'x86_64-pc-windows-msvc': 'win-x64',
  'aarch64-pc-windows-msvc': 'win-arm64',
  'x86_64-apple-darwin': 'osx-x64',
  'aarch64-apple-darwin': 'osx-arm64',
  'x86_64-unknown-linux-gnu': 'linux-x64',
};

/** Every source, read; a source that can't be read is left out with a warning, or, strict, fails. */
export function collect({ strict = false, log = console } = {}) {
  const problems = [];
  const found = [];
  const attempt = (name, read) => {
    try {
      const packages = read();
      if (packages.length === 0) throw new Error('no packages');
      found.push(...packages);
    } catch (error) {
      problems.push(`${name}: ${error instanceof Error ? error.message : String(error)}`);
    }
  };
  attempt('npm', () => npmPackages(JSON.parse(readFileSync(path.join(appDir, 'package-lock.json'), 'utf8'))));
  let triple = null;
  attempt('cargo', () => {
    triple = hostTriple();
    const metadata = JSON.parse(
      execFileSync(
        'cargo',
        [
          'metadata',
          '--format-version',
          '1',
          '--locked',
          '--manifest-path',
          path.join(appDir, 'src-tauri', 'Cargo.toml'),
          ...(triple ? ['--filter-platform', triple] : []),
        ],
        { encoding: 'utf8', maxBuffer: 256 * 1024 * 1024, stdio: ['ignore', 'pipe', 'pipe'] },
      ),
    );
    return cargoPackages(metadata);
  });
  attempt('nuget', () => {
    const assets = path.join(appDir, '.sidecar-build', 'obj', 'ShoutingIguana.Sidecar', 'project.assets.json');
    if (!existsSync(assets)) throw new Error('the sidecar is not built (npm run build:sidecar)');
    const cache = process.env.NUGET_PACKAGES ?? path.join(os.homedir(), '.nuget', 'packages');
    return nugetPackages(JSON.parse(readFileSync(assets, 'utf8')), cache, RUNTIMES[triple ?? ''] ?? 'win-x64');
  });
  attempt('fonts', () => fontPackages(path.join(appDir, 'src', 'styles', 'fonts')));
  attempt('engine fonts', () =>
    engineFontPackages(path.join(appDir, '..', 'engine', 'src', 'ShoutingIguana.Engine', 'Analysis', 'Rules')),
  );
  if (problems.length > 0) {
    const message = `licences: left out of the list: ${problems.join('; ')}`;
    if (strict) throw new Error(message);
    log.warn(message);
  }
  return { generatedAt: new Date().toISOString(), complete: problems.length === 0, ...assemble(found) };
}

if (
  import.meta.url === `file://${process.argv[1].replace(/\\/g, '/').replace(/^\/?/, '/')}` ||
  process.argv[1]?.endsWith('licences.mjs')
) {
  const args = process.argv.slice(2);
  const out = args.includes('--out') ? args[args.indexOf('--out') + 1] : null;
  const list = collect({ strict: args.includes('--strict') });
  const text = JSON.stringify(list);
  if (out) writeFileSync(out, text);
  console.log(
    `licences: ${list.packages.length} packages (${['npm', 'cargo', 'nuget', 'font'].map((source) => `${list.packages.filter((pkg) => pkg.source === source).length} ${source}`).join(', ')}), ${list.texts.length} texts, ${Math.round(text.length / 1024)} KB${out ? ` -> ${out}` : ''}`,
  );
}
