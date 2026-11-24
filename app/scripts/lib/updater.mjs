// The app's updates from outside, for the release workflow and the update proof (updater-manifest.mjs,
// prove-update.ps1): a signature checked as the app's updater checks it, and `latest.json`, the file the app reads to
// learn of a newer version. Nothing here reads a private key, and nothing starts anything, so all of it is tested
// (updater.test.mjs).
//
// The updater's signatures are minisign's: Ed25519 over the file's BLAKE2b-512 hash, with a second signature over
// the first and a "trusted comment" that says which version the file was signed as. Tauri keeps both the public key
// and each signature as base64 of minisign's text.
import { createHash, createPublicKey, verify as verifySignature } from 'node:crypto';

/** What the updater's `latest.json` must name for the app to update itself on every system it can. */
export const PLATFORMS = [
  { key: 'windows-x86_64', what: 'the Windows installer', file: /_x64-setup\.exe$/ },
  { key: 'darwin-aarch64', what: 'the macOS app archive for Apple silicon', file: /_aarch64\.app\.tar\.gz$/ },
  { key: 'darwin-x86_64', what: 'the macOS app archive for Intel', file: /_x64\.app\.tar\.gz$/ },
  { key: 'linux-x86_64', what: 'the Linux AppImage', file: /_amd64\.AppImage$/ },
];

/** The name GitHub gives a release asset uploaded under `name`: it has no spaces. */
export const assetName = (name) => name.replaceAll(' ', '.');

const ED25519_SPKI = Buffer.from('302a300506032b6570032100', 'hex');
const base64Text = (value) => Buffer.from(value.trim(), 'base64').toString('utf8');

/**
 * The public key in tauri.conf.json (`plugins.updater.pubkey`): the key's id and the key itself.
 * @param {string} pubkey base64 of minisign's public key file
 */
export function parsePublicKey(pubkey) {
  const line = base64Text(pubkey).split(/\r?\n/).filter(Boolean).at(-1) ?? '';
  const bytes = Buffer.from(line, 'base64');
  if (bytes.length !== 42 || bytes.subarray(0, 2).toString('latin1') !== 'Ed')
    throw new Error('the public key isn’t a minisign Ed25519 key');
  return { id: bytes.subarray(2, 10), key: bytes.subarray(10) };
}

/**
 * A `.sig` file's content: the signature, the comment it covers and the signature over both.
 * @param {string} signature base64 of minisign's signature file
 */
export function parseSignature(signature) {
  const lines = base64Text(signature).split(/\r?\n/);
  const trusted = lines[2] ?? '';
  if (lines.length < 4 || !trusted.startsWith('trusted comment: ')) throw new Error('the signature isn’t minisign’s');
  const bytes = Buffer.from(lines[1], 'base64');
  const algorithm = bytes.subarray(0, 2).toString('latin1');
  if (bytes.length !== 74 || (algorithm !== 'ED' && algorithm !== 'Ed'))
    throw new Error('the signature isn’t an Ed25519 signature');
  const comment = trusted.slice('trusted comment: '.length);
  return {
    prehashed: algorithm === 'ED',
    id: bytes.subarray(2, 10),
    signature: bytes.subarray(10),
    comment,
    global: Buffer.from(lines[3], 'base64'),
    /** The version the file was signed as (`tauri build` writes it; the app refuses a file signed as another). */
    version:
      comment
        .split('\t')
        .find((field) => field.startsWith('version:'))
        ?.slice('version:'.length) ?? null,
  };
}

/**
 * What's wrong with a file's signature, or null when the app would accept it: made by the key the app trusts, over
 * these very bytes, for this version.
 * @param {Buffer} bytes the file
 * @param {string} signature the `.sig` file's content
 * @param {string} pubkey tauri.conf.json's `plugins.updater.pubkey`
 * @param {string | null} version the version the file must be signed as; null doesn't ask
 */
export function signatureProblem(bytes, signature, pubkey, version = null) {
  let key;
  let parsed;
  try {
    key = parsePublicKey(pubkey);
    parsed = parseSignature(signature);
  } catch (error) {
    return error.message;
  }
  if (!parsed.id.equals(key.id)) return 'the signature was made with another key than the app trusts';
  const publicKey = createPublicKey({ key: Buffer.concat([ED25519_SPKI, key.key]), format: 'der', type: 'spki' });
  const message = parsed.prehashed ? createHash('blake2b512').update(bytes).digest() : bytes;
  if (!verifySignature(null, message, publicKey, parsed.signature)) return 'the signature doesn’t match the file';
  const signed = Buffer.concat([parsed.signature, Buffer.from(parsed.comment, 'utf8')]);
  if (!verifySignature(null, signed, publicKey, parsed.global))
    return 'the signature’s trusted comment was changed after signing';
  if (version !== null && parsed.version !== version)
    return `the file was signed as version ${parsed.version ?? '(none)'}, not ${version}`;
  return null;
}

/**
 * `latest.json` for one release: each platform's installer and its signature, by the names the files have here.
 * @param {object} release
 * @param {string} release.version
 * @param {string} release.notes what's new, as the app shows it; may be empty
 * @param {string} release.date RFC 3339
 * @param {{ name: string, signature: string | null }[]} release.files the release's files, each with its `.sig` content
 * @param {(name: string) => string | null} release.urlOf where a file downloads from; null when it isn't published
 * @returns {{ manifest: object | null, problems: string[] }}
 */
export function buildManifest({ version, notes, date, files, urlOf }) {
  const problems = [];
  const platforms = {};
  for (const platform of PLATFORMS) {
    const matches = files.filter((file) => platform.file.test(file.name));
    if (matches.length !== 1) {
      problems.push(`${platform.key}: ${matches.length} files are ${platform.what}, and there must be one`);
      continue;
    }
    const [file] = matches;
    const url = urlOf(file.name);
    if (!file.signature?.trim()) problems.push(`${platform.key}: ${file.name} has no signature (.sig)`);
    else if (!url) problems.push(`${platform.key}: ${file.name} isn’t among the release’s files`);
    else platforms[platform.key] = { signature: file.signature.trim(), url };
  }
  if (problems.length > 0) return { manifest: null, problems };
  return { manifest: { version, notes: notes.trim(), pub_date: date, platforms }, problems };
}

/**
 * What's wrong with a `latest.json`, as a list: empty when the app can update from it on every platform.
 * @param {unknown} manifest
 * @param {object} expected
 * @param {string} expected.version
 * @param {string[]} expected.urls where the release's files download from
 * @param {string} expected.pubkey tauri.conf.json's `plugins.updater.pubkey`
 * @param {(url: string) => Buffer | null} expected.bytesOf the file a URL names, to check its signature; null skips
 */
export function manifestProblems(manifest, { version, urls, pubkey, bytesOf }) {
  if (typeof manifest !== 'object' || manifest === null) return ['it isn’t a JSON object'];
  const problems = [];
  if (manifest.version !== version) problems.push(`its version is ${manifest.version}, not ${version}`);
  if (typeof manifest.notes !== 'string') problems.push('its notes aren’t text');
  if (typeof manifest.pub_date !== 'string' || Number.isNaN(Date.parse(manifest.pub_date)))
    problems.push('its pub_date isn’t a date');
  const platforms = typeof manifest.platforms === 'object' && manifest.platforms !== null ? manifest.platforms : {};
  for (const { key } of PLATFORMS) {
    const entry = platforms[key];
    if (typeof entry !== 'object' || entry === null) {
      problems.push(`${key}: missing`);
      continue;
    }
    const signed = typeof entry.signature === 'string' && entry.signature.trim() !== '';
    if (!signed) problems.push(`${key}: no signature`);
    if (typeof entry.url !== 'string' || !entry.url.startsWith('https://')) {
      problems.push(`${key}: its URL isn’t https`);
      continue;
    }
    if (!urls.includes(entry.url)) {
      problems.push(`${key}: ${entry.url} isn’t one of the release’s files`);
      continue;
    }
    const bytes = signed ? bytesOf(entry.url) : null;
    const wrong = bytes ? signatureProblem(bytes, entry.signature, pubkey, version) : null;
    if (wrong) problems.push(`${key}: ${wrong}`);
  }
  const used = PLATFORMS.map(({ key }) => platforms[key]?.url).filter(Boolean);
  if (new Set(used).size !== used.length) problems.push('two platforms share one file');
  return problems;
}
