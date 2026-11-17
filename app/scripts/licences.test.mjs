import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { test } from 'node:test';
import { assemble, cargoPackages, engineFontPackages, npmPackages, nugetPackages, nuspecLicence } from './licences.mjs';

test('npm: the production packages only, by their own names', () => {
  const lock = {
    packages: {
      '': { name: 'app' },
      'node_modules/react': { version: '19.3.1', license: 'MIT' },
      'node_modules/@radix-ui/react-tabs': { version: '1.2.1', license: 'MIT' },
      'node_modules/a/node_modules/b': { version: '2.0.0', license: 'ISC' },
      'node_modules/vitest': { version: '4.0.0', license: 'MIT', dev: true },
      'node_modules/fsevents': { version: '2.3.3', license: 'MIT', devOptional: true },
    },
  };
  assert.deepEqual(
    npmPackages(lock, '/app').map((pkg) => `${pkg.name}@${pkg.version} ${pkg.licence}`),
    ['react@19.3.1 MIT', '@radix-ui/react-tabs@1.2.1 MIT', 'b@2.0.0 ISC'],
  );
});

test('cargo: what the shell links, through normal dependencies only', () => {
  const pkg = (name, license) => ({
    id: name,
    name,
    version: '1.0.0',
    license,
    manifest_path: `/r/${name}/Cargo.toml`,
    repository: null,
  });
  const normal = [{ kind: null, target: null }];
  const metadata = {
    packages: [
      pkg('shell', null),
      pkg('tauri', 'MIT OR Apache-2.0'),
      pkg('serde', 'MIT'),
      pkg('cc', 'MIT'),
      pkg('tempfile', 'MIT'),
    ],
    resolve: {
      root: 'shell',
      nodes: [
        {
          id: 'shell',
          deps: [
            { pkg: 'tauri', dep_kinds: normal },
            { pkg: 'tempfile', dep_kinds: [{ kind: 'dev', target: null }] },
          ],
        },
        {
          id: 'tauri',
          deps: [
            { pkg: 'serde', dep_kinds: normal },
            { pkg: 'cc', dep_kinds: [{ kind: 'build', target: null }] },
          ],
        },
        { id: 'serde', deps: [] },
      ],
    },
  };
  assert.deepEqual(
    cargoPackages(metadata).map((found) => `${found.name} ${found.licence}`),
    ['tauri MIT OR Apache-2.0', 'serde MIT'],
  );
});

test('NuGet: the packages the engine is published with, less build-only ones, and its runtimes', () => {
  const cache = mkdtempSync(path.join(os.tmpdir(), 'si-licences-'));
  try {
    const nuspec = (name, version, body) => {
      const folder = path.join(cache, name.toLowerCase(), version);
      mkdirSync(folder, { recursive: true });
      writeFileSync(
        path.join(folder, `${name.toLowerCase()}.nuspec`),
        `<package><metadata>${body}</metadata></package>`,
      );
      return folder;
    };
    nuspec('Serilog', '4.3.0', '<license type="expression">Apache-2.0</license>');
    const sqlite = nuspec('SQLitePCLRaw.core', '2.1.13', '<license type="file">LICENSE.txt</license>');
    writeFileSync(path.join(sqlite, 'LICENSE.txt'), 'Apache text');
    const assets = {
      project: {
        frameworks: {
          'net10.0': {
            dependencies: { 'Microsoft.NET.ILLink.Tasks': { suppressParent: 'All' }, Serilog: {} },
            downloadDependencies: [
              { name: 'Microsoft.NETCore.App.Runtime.win-x64', version: '[10.0.14]' },
              { name: 'Microsoft.WindowsDesktop.App.Ref', version: '[10.0.14]' },
            ],
          },
        },
      },
      targets: {
        'net10.0': {},
        'net10.0/win-x64': {
          'Serilog/4.3.0': { type: 'package' },
          'SQLitePCLRaw.core/2.1.13': { type: 'package' },
          'Microsoft.NET.ILLink.Tasks/10.0.15': { type: 'package' },
          'ShoutingIguana.Engine/1.0.0': { type: 'project' },
        },
      },
    };
    const found = nugetPackages(assets, cache, 'win-x64');
    assert.deepEqual(
      found.map((pkg) => `${pkg.name}@${pkg.version} ${pkg.licence}`),
      [
        'Serilog@4.3.0 Apache-2.0',
        'SQLitePCLRaw.core@2.1.13 See its licence',
        'Microsoft.NETCore.App.Runtime.win-x64@10.0.14 Unknown',
      ],
    );
    const list = assemble(found);
    assert.equal(list.texts.length, 1);
    assert.equal(list.packages.find((pkg) => pkg.name === 'SQLitePCLRaw.core')?.text, 0);
  } finally {
    rmSync(cache, { recursive: true, force: true });
  }
});

test("a nuspec's licence: its expression, its file, or its URL", () => {
  assert.deepEqual(nuspecLicence('<license type="expression">MIT</license>'), { licence: 'MIT', file: null });
  assert.deepEqual(nuspecLicence('<license type="file">LICENSE.md</license>'), {
    licence: 'See its licence',
    file: 'LICENSE.md',
  });
  assert.deepEqual(nuspecLicence('<licenseUrl>https://x/licence</licenseUrl>'), {
    licence: 'https://x/licence',
    file: null,
  });
});

test("the engine's font metrics: Liberation Sans's advance widths, with the OFL text beside them", () => {
  const dir = mkdtempSync(path.join(os.tmpdir(), 'licences-'));
  try {
    assert.throws(() => engineFontPackages(dir), /LiberationSans-OFL.txt/);
    writeFileSync(path.join(dir, 'LiberationSans-OFL.txt'), 'SIL OPEN FONT LICENSE Version 1.1');
    assert.deepEqual(
      engineFontPackages(dir).map(
        (pkg) => `${pkg.name}@${pkg.version} ${pkg.source} ${pkg.licence} ${path.basename(pkg.textFile)}`,
      ),
      ['Liberation Sans (advance widths)@2.1.5 font OFL-1.1 LiberationSans-OFL.txt'],
    );
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('texts are kept once, and the list is in order', () => {
  const folder = mkdtempSync(path.join(os.tmpdir(), 'si-licences-'));
  try {
    for (const name of ['b', 'a', 'c']) {
      mkdirSync(path.join(folder, name));
      writeFileSync(path.join(folder, name, 'LICENSE'), name === 'c' ? 'Other' : 'MIT text');
    }
    const list = assemble(
      ['b', 'a', 'c'].map((name) => ({
        name,
        version: '1',
        source: 'npm',
        licence: 'MIT',
        url: null,
        folder: path.join(folder, name),
      })),
    );
    assert.deepEqual(
      list.packages.map((pkg) => `${pkg.name}:${pkg.text}`),
      ['a:0', 'b:0', 'c:1'],
    );
    assert.equal(list.texts.length, 2);
  } finally {
    rmSync(folder, { recursive: true, force: true });
  }
});
