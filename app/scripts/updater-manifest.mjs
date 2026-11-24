// The release workflow's side of the app's updates (.github/workflows/app-release.yml). The app reads `latest.json`
// from the newest GitHub release, downloads the installer it names for its platform and runs it only if the
// signature in `latest.json` is the app's key's over that very file (src-tauri/src/updates.rs).
//
//   node scripts/updater-manifest.mjs verify <file> [--version <v>]
//       <file>.sig is the app's key's signature over <file>, signed as that version.
//   node scripts/updater-manifest.mjs expect --dir <files> --repo <owner/name> --tag <tag> --out <assets.json>
//       The assets the release will have once <files> are uploaded to it, as `gh release view --json assets` will
//       list them: so latest.json can be written and checked before anything is published.
//   node scripts/updater-manifest.mjs write --dir <files> --assets <assets.json> --version <v> [--notes <file>] --out <latest.json>
//       Writes latest.json for the release whose files are under <files> and whose assets are in <assets.json>
//       (from `expect`, or `gh release view <tag> --json assets`). A --notes file that isn't there is no notes.
//   node scripts/updater-manifest.mjs check --manifest <latest.json> --dir <files> --assets <assets.json> --version <v> [--published]
//       Every platform is there, every URL is a file of this release, every signature is good for the file it names.
//       --published: the assets are the release's own, and latest.json is among them.
//
// The public key is the one in src-tauri/tauri.conf.json, which is the one the app is built with. No private key is
// read here: `tauri build` and `tauri signer sign` sign, with TAURI_SIGNING_PRIVATE_KEY.
import { existsSync, readFileSync, readdirSync, statSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { parseArgs } from 'node:util';
import { assetName, buildManifest, manifestProblems, signatureProblem } from './lib/updater.mjs';

const appRoot = path.join(import.meta.dirname, '..');

function fail(lines) {
  for (const line of [lines].flat()) console.error(`updater-manifest: ${line}`);
  process.exit(1);
}

const publicKey = () =>
  JSON.parse(readFileSync(path.join(appRoot, 'src-tauri', 'tauri.conf.json'), 'utf8')).plugins.updater.pubkey;

/** Every file under `dir`, artifacts' folders included. */
function filesUnder(dir) {
  return readdirSync(dir, { recursive: true, withFileTypes: true })
    .filter((entry) => entry.isFile())
    .map((entry) => path.join(entry.parentPath, entry.name));
}

/** The release's published assets, as `gh release view --json assets` lists them. */
function readAssets(file) {
  const assets = JSON.parse(readFileSync(file, 'utf8')).assets;
  if (!Array.isArray(assets)) fail(`${file} has no assets`);
  return assets;
}

/** The published asset a local file was uploaded as: the same name as GitHub writes it, and the same size. */
function assetOf(assets, file) {
  const name = assetName(path.basename(file));
  return assets.find((asset) => asset.name === name && asset.size === statSync(file).size) ?? null;
}

const [command, ...rest] = process.argv.slice(2);
const { values, positionals } = parseArgs({
  args: rest,
  allowPositionals: true,
  options: {
    dir: { type: 'string' },
    assets: { type: 'string' },
    version: { type: 'string' },
    notes: { type: 'string' },
    out: { type: 'string' },
    manifest: { type: 'string' },
    repo: { type: 'string' },
    tag: { type: 'string' },
    published: { type: 'boolean', default: false },
  },
});
const need = (name) => values[name] ?? fail(`--${name} is needed`);

if (command === 'verify') {
  const [file] = positionals;
  if (!file || !existsSync(file)) fail(`no file ${file ?? ''}`);
  if (!existsSync(`${file}.sig`)) fail(`${file} has no signature beside it (${path.basename(file)}.sig)`);
  const wrong = signatureProblem(
    readFileSync(file),
    readFileSync(`${file}.sig`, 'utf8'),
    publicKey(),
    values.version ?? null,
  );
  if (wrong) fail(`${path.basename(file)}: ${wrong}`);
  console.log(
    `updater-manifest: ${path.basename(file)} is signed by the app’s key${values.version ? `, as ${values.version}` : ''}`,
  );
} else if (command === 'expect') {
  const base = `https://github.com/${need('repo')}/releases/download/${need('tag')}`;
  const assets = filesUnder(need('dir')).map((file) => {
    const name = assetName(path.basename(file));
    return { name, size: statSync(file).size, url: `${base}/${name}` };
  });
  const names = assets.map((asset) => asset.name);
  const twice = names.filter((name, index) => names.indexOf(name) !== index);
  if (twice.length > 0) fail(`two files would be one asset on the release: ${[...new Set(twice)].join(', ')}`);
  writeFileSync(need('out'), `${JSON.stringify({ assets }, null, 2)}\n`);
  console.log(`updater-manifest: ${assets.length} files will be on ${base}`);
} else if (command === 'write') {
  const assets = readAssets(need('assets'));
  const local = filesUnder(need('dir')).filter((file) => !file.endsWith('.sig'));
  const { manifest, problems } = buildManifest({
    version: need('version'),
    notes: values.notes && existsSync(values.notes) ? readFileSync(values.notes, 'utf8') : '',
    date: new Date().toISOString(),
    files: local.map((file) => ({
      name: path.basename(file),
      signature: existsSync(`${file}.sig`) ? readFileSync(`${file}.sig`, 'utf8') : null,
    })),
    urlOf: (name) => {
      const file = local.find((candidate) => path.basename(candidate) === name);
      return (file && assetOf(assets, file)?.url) ?? null;
    },
  });
  if (!manifest) fail(problems);
  writeFileSync(need('out'), `${JSON.stringify(manifest, null, 2)}\n`);
  console.log(`updater-manifest: wrote ${values.out} for ${Object.keys(manifest.platforms).join(', ')}`);
} else if (command === 'check') {
  const assets = readAssets(need('assets'));
  const local = filesUnder(need('dir'));
  let manifest;
  try {
    manifest = JSON.parse(readFileSync(need('manifest'), 'utf8'));
  } catch (error) {
    fail(`${values.manifest} can’t be read: ${error.message}`);
  }
  const problems = manifestProblems(manifest, {
    version: need('version'),
    urls: assets.map((asset) => asset.url),
    pubkey: publicKey(),
    bytesOf: (url) => {
      const file = local.find((candidate) => assetOf(assets, candidate)?.url === url);
      // A URL of the release with no file here to check it against is a problem of its own.
      if (!file) fail(`${url} is in the release, but the file it was uploaded from isn’t under ${values.dir}`);
      return readFileSync(file);
    },
  });
  if (values.published) {
    const published = assets.find((asset) => asset.name === 'latest.json');
    if (!published) problems.push('it isn’t among the release’s files, so the app can’t read it');
    else if (published.size !== statSync(values.manifest).size)
      problems.push('the latest.json on the release isn’t this one (its size differs)');
  }
  if (problems.length > 0) fail(problems.map((problem) => `${values.manifest}: ${problem}`));
  for (const [platform, entry] of Object.entries(manifest.platforms)) console.log(`  ${platform}  ${entry.url}`);
  console.log(`updater-manifest: ${values.manifest} is good: version ${manifest.version}, every signature checked`);
} else {
  fail('say verify, expect, write or check');
}
