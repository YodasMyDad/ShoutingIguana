// The updater's signatures and `latest.json` (`node --test scripts`), with a key made here: the real one is never read.
import assert from 'node:assert/strict';
import { createHash, generateKeyPairSync, sign } from 'node:crypto';
import { describe, it } from 'node:test';
import { PLATFORMS, assetName, buildManifest, manifestProblems, parseSignature, signatureProblem } from './updater.mjs';

const base64 = (text) => Buffer.from(text, 'utf8').toString('base64');

/** A minisign key pair as Tauri keeps one: the public key for tauri.conf.json, and what signs a file as it does. */
function testKey(id = '0102030405060708') {
  const { publicKey, privateKey } = generateKeyPairSync('ed25519');
  const keyId = Buffer.from(id, 'hex');
  const raw = publicKey.export({ format: 'der', type: 'spki' }).subarray(-32);
  const pubkey = base64(
    `untrusted comment: minisign public key: TEST\n${Buffer.concat([Buffer.from('Ed'), keyId, raw]).toString('base64')}\n`,
  );
  const signFile = (bytes, version, file = 'app') => {
    const signature = sign(null, createHash('blake2b512').update(bytes).digest(), privateKey);
    const comment = `timestamp:1795000000\tfile:${file}${version ? `\tversion:${version}` : ''}`;
    const global = sign(null, Buffer.concat([signature, Buffer.from(comment)]), privateKey);
    return base64(
      [
        'untrusted comment: signature from tauri secret key',
        Buffer.concat([Buffer.from('ED'), keyId, signature]).toString('base64'),
        `trusted comment: ${comment}`,
        global.toString('base64'),
        '',
      ].join('\n'),
    );
  };
  return { pubkey, signFile };
}

const INSTALLER = Buffer.from('an installer');

describe('the updater’s signatures', () => {
  const key = testKey();
  const signature = key.signFile(INSTALLER, '2.1.1');

  it('accepts a file its key signed as this version', () => {
    assert.equal(signatureProblem(INSTALLER, signature, key.pubkey, '2.1.1'), null);
    assert.equal(signatureProblem(INSTALLER, signature, key.pubkey), null);
    assert.equal(parseSignature(signature).version, '2.1.1');
  });

  it('refuses a file changed after signing, another key’s signature, and a file signed as another version', () => {
    const tampered = Buffer.from('an installer, tampered with');
    assert.equal(signatureProblem(tampered, signature, key.pubkey, '2.1.1'), 'the signature doesn’t match the file');
    const other = testKey();
    assert.equal(
      signatureProblem(INSTALLER, other.signFile(INSTALLER, '2.1.1'), key.pubkey, '2.1.1'),
      'the signature doesn’t match the file',
    );
    assert.equal(
      signatureProblem(INSTALLER, testKey('ffffffffffffffff').signFile(INSTALLER, '2.1.1'), key.pubkey, '2.1.1'),
      'the signature was made with another key than the app trusts',
    );
    assert.equal(
      signatureProblem(INSTALLER, signature, key.pubkey, '2.1.2'),
      'the file was signed as version 2.1.1, not 2.1.2',
    );
    assert.equal(
      signatureProblem(INSTALLER, key.signFile(INSTALLER, null), key.pubkey, '2.1.1'),
      'the file was signed as version (none), not 2.1.1',
    );
  });

  it('refuses a signature whose version was rewritten, and anything that isn’t a signature', () => {
    const lines = Buffer.from(signature, 'base64').toString('utf8').replace('version:2.1.1', 'version:9.9.9');
    assert.equal(
      signatureProblem(INSTALLER, base64(lines), key.pubkey, '9.9.9'),
      'the signature’s trusted comment was changed after signing',
    );
    assert.equal(signatureProblem(INSTALLER, '', key.pubkey), 'the signature isn’t minisign’s');
    assert.equal(signatureProblem(INSTALLER, base64('not a signature'), key.pubkey), 'the signature isn’t minisign’s');
    assert.equal(
      signatureProblem(INSTALLER, signature, base64('nothing')),
      'the public key isn’t a minisign Ed25519 key',
    );
  });
});

describe('latest.json', () => {
  const key = testKey();
  const names = [
    'Shouting Iguana_2.1.1_x64-setup.exe',
    'Shouting Iguana_2.1.1_aarch64.app.tar.gz',
    'Shouting Iguana_2.1.1_x64.app.tar.gz',
    'Shouting Iguana_2.1.1_amd64.AppImage',
  ];
  const bytesOf = (name) => Buffer.from(`the bytes of ${name}`);
  const files = [
    ...names.map((name) => ({ name, signature: key.signFile(bytesOf(name), '2.1.1', name) })),
    // What else a release holds: the app never updates from these.
    { name: 'Shouting Iguana_2.1.1_amd64.deb', signature: 'c2ln' },
    { name: 'Shouting Iguana_2.1.1_aarch64.dmg', signature: null },
    { name: 'Shouting Iguana_2.1.1_x64.dmg', signature: null },
    { name: 'SHA256SUMS-windows-x64.txt', signature: null },
  ];
  const urlOf = (name) => `https://github.com/o/r/releases/download/app-v2.1.1/${assetName(name)}`;
  const release = {
    version: '2.1.1',
    notes: 'The app updates itself.\n',
    date: '2026-11-25T10:00:00.000Z',
    files,
    urlOf,
  };
  const expected = {
    version: '2.1.1',
    urls: files.map((file) => urlOf(file.name)),
    pubkey: key.pubkey,
    bytesOf: (url) => bytesOf(names.find((name) => urlOf(name) === url)),
  };

  it('names each platform’s installer by the name GitHub gives it, with its signature', () => {
    const { manifest, problems } = buildManifest(release);
    assert.deepEqual(problems, []);
    assert.deepEqual(Object.keys(manifest.platforms), [
      'windows-x86_64',
      'darwin-aarch64',
      'darwin-x86_64',
      'linux-x86_64',
    ]);
    assert.equal(manifest.version, '2.1.1');
    assert.equal(manifest.notes, 'The app updates itself.');
    assert.equal(manifest.pub_date, '2026-11-25T10:00:00.000Z');
    assert.equal(
      manifest.platforms['windows-x86_64'].url,
      'https://github.com/o/r/releases/download/app-v2.1.1/Shouting.Iguana_2.1.1_x64-setup.exe',
    );
    assert.equal(manifest.platforms['linux-x86_64'].url.endsWith('Shouting.Iguana_2.1.1_amd64.AppImage'), true);
    assert.equal(manifest.platforms['darwin-aarch64'].signature, files[1].signature);
    assert.deepEqual(manifestProblems(manifest, expected), []);
  });

  it('isn’t written with a platform missing, unsigned, unpublished or built twice', () => {
    const without = (name) => files.filter((file) => file.name !== name);
    assert.deepEqual(buildManifest({ ...release, files: without(names[1]) }), {
      manifest: null,
      problems: ['darwin-aarch64: 0 files are the macOS app archive for Apple silicon, and there must be one'],
    });
    const unsigned = files.map((file) => (file.name === names[3] ? { ...file, signature: ' ' } : file));
    assert.deepEqual(buildManifest({ ...release, files: unsigned }).problems, [
      'linux-x86_64: Shouting Iguana_2.1.1_amd64.AppImage has no signature (.sig)',
    ]);
    assert.deepEqual(
      buildManifest({ ...release, urlOf: (name) => (name === names[0] ? null : urlOf(name)) }).problems,
      ['windows-x86_64: Shouting Iguana_2.1.1_x64-setup.exe isn’t among the release’s files'],
    );
    const twice = [...files, { name: 'Other_2.1.1_x64-setup.exe', signature: 'c2ln' }];
    assert.match(buildManifest({ ...release, files: twice }).problems[0], /^windows-x86_64: 2 files/);
  });

  it('is refused when the app couldn’t update from it', () => {
    const { manifest } = buildManifest(release);
    const changed = (change) => {
      const copy = structuredClone(manifest);
      change(copy);
      return manifestProblems(copy, expected);
    };
    assert.deepEqual(
      changed((m) => delete m.platforms['darwin-x86_64']),
      ['darwin-x86_64: missing'],
    );
    assert.deepEqual(
      changed((m) => (m.platforms['linux-x86_64'].signature = '')),
      ['linux-x86_64: no signature'],
    );
    assert.deepEqual(
      changed((m) => (m.version = '2.1.0')),
      ['its version is 2.1.0, not 2.1.1'],
    );
    // Every file signed as the version before: the app would refuse each.
    assert.deepEqual(manifestProblems(manifest, { ...expected, version: '2.1.2' }), [
      'its version is 2.1.1, not 2.1.2',
      ...PLATFORMS.map((platform) => `${platform.key}: the file was signed as version 2.1.1, not 2.1.2`),
    ]);
    assert.deepEqual(
      changed((m) => (m.platforms['windows-x86_64'].url = 'https://example.com/setup.exe')),
      ['windows-x86_64: https://example.com/setup.exe isn’t one of the release’s files'],
    );
    assert.deepEqual(
      changed((m) => (m.platforms['windows-x86_64'].url = 'http://github.com/o/r/setup.exe')),
      ['windows-x86_64: its URL isn’t https'],
    );
    // The Intel Mac sent the Apple silicon app: its signature is good, but for another file.
    assert.deepEqual(
      changed((m) => (m.platforms['darwin-x86_64'].url = m.platforms['darwin-aarch64'].url)),
      ['darwin-x86_64: the signature doesn’t match the file', 'two platforms share one file'],
    );
    assert.deepEqual(
      changed((m) => (m.pub_date = 'soon')),
      ['its pub_date isn’t a date'],
    );
    assert.deepEqual(manifestProblems(null, expected), ['it isn’t a JSON object']);
  });
});
