// `node scripts/sign.mjs <file>...`: Authenticode-signs Windows binaries (the sidecar, the shell, the installer and its
// uninstaller) when a code-signing certificate is configured, and says plainly that it skipped them when none is, so
// every build works with or without one. build-sidecar.* signs the sidecar as soon as it is published, and Tauri's
// bundler calls this for the rest (`bundle.windows.signCommand` in src-tauri/tauri.conf.json).
//
// Configure one of:
//   SI_SIGN_CERT_THUMBPRINT   a certificate in the current user's or the machine's store (a token, or one imported
//                             on the build machine), by its SHA-1 thumbprint;
//   SI_SIGN_PFX_BASE64 and    a PFX and its password, as CI secrets.
//   SI_SIGN_PFX_PASSWORD
// SI_SIGN_TIMESTAMP_URL is the RFC 3161 timestamp server (DigiCert's by default), so signatures outlive the
// certificate. SI_SIGNTOOL names signtool.exe if the Windows SDK's can't be found.
import { spawnSync } from 'node:child_process';
import { existsSync, mkdtempSync, readdirSync, rmSync, writeFileSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

const DEFAULT_TIMESTAMP_URL = 'http://timestamp.digicert.com';
const DESCRIPTION = 'Shouting Iguana';

/** How this build signs, from the environment: by thumbprint, by PFX, or not at all. */
export function signingConfig(environment = process.env) {
  const timestampUrl = environment.SI_SIGN_TIMESTAMP_URL || DEFAULT_TIMESTAMP_URL;
  const thumbprint = environment.SI_SIGN_CERT_THUMBPRINT?.replace(/\s/g, '');
  if (thumbprint) return { kind: 'thumbprint', thumbprint, timestampUrl };
  if (environment.SI_SIGN_PFX_BASE64) {
    return {
      kind: 'pfx',
      pfx: environment.SI_SIGN_PFX_BASE64,
      password: environment.SI_SIGN_PFX_PASSWORD ?? '',
      timestampUrl,
    };
  }
  return null;
}

/** signtool's arguments for one file: SHA-256 file and timestamp digests, the app's name, and the certificate. */
export function signtoolArguments(config, file, pfxPath) {
  const certificate =
    config.kind === 'thumbprint' ? ['/sha1', config.thumbprint] : ['/f', pfxPath, '/p', config.password];
  return [
    'sign',
    '/fd',
    'SHA256',
    '/td',
    'SHA256',
    '/tr',
    config.timestampUrl,
    '/d',
    DESCRIPTION,
    ...certificate,
    file,
  ];
}

/** The Windows SDK's newest x64 signtool.exe, or SI_SIGNTOOL. */
function findSigntool() {
  if (process.env.SI_SIGNTOOL) return process.env.SI_SIGNTOOL;
  const kits = path.join(process.env['ProgramFiles(x86)'] ?? 'C:\\Program Files (x86)', 'Windows Kits', '10', 'bin');
  const versions = existsSync(kits)
    ? readdirSync(kits)
        .filter((name) => /^\d+\.\d+\.\d+\.\d+$/.test(name))
        .sort((a, b) => a.localeCompare(b, 'en', { numeric: true }))
        .reverse()
    : [];
  const found = versions.map((version) => path.join(kits, version, 'x64', 'signtool.exe')).find(existsSync);
  if (!found) throw new Error(`signtool.exe wasn't found under ${kits}: install the Windows SDK, or set SI_SIGNTOOL.`);
  return found;
}

/** Who signed the file, from Windows itself: the signer's subject, or null when it carries no signature. */
function signer(file) {
  const script = `$s = Get-AuthenticodeSignature -LiteralPath $env:SI_SIGNED_FILE; if ($s.SignerCertificate) { $s.SignerCertificate.Subject }`;
  const result = spawnSync('powershell', ['-NoProfile', '-NonInteractive', '-Command', script], {
    encoding: 'utf8',
    env: { ...process.env, SI_SIGNED_FILE: file },
  });
  return result.stdout.trim() || null;
}

/** Signs each file, or says why it didn't. Throws if signing was configured and failed. */
export function sign(files, environment = process.env, log = console.log) {
  const config = signingConfig(environment);
  if (!config) {
    log(
      `sign: skipped ${files.map((file) => path.basename(file)).join(', ')}: no code-signing certificate is configured (set SI_SIGN_CERT_THUMBPRINT, or SI_SIGN_PFX_BASE64 and SI_SIGN_PFX_PASSWORD)`,
    );
    return false;
  }
  if (process.platform !== 'win32') throw new Error('Authenticode signing needs Windows (signtool).');
  const signtool = findSigntool();
  const temp = config.kind === 'pfx' ? mkdtempSync(path.join(os.tmpdir(), 'si-sign-')) : null;
  try {
    const pfxPath = temp ? path.join(temp, 'certificate.pfx') : undefined;
    if (temp && pfxPath) writeFileSync(pfxPath, Buffer.from(config.pfx, 'base64'));
    for (const file of files) {
      if (!existsSync(file)) throw new Error(`there's no ${file} to sign`);
      const result = spawnSync(signtool, signtoolArguments(config, file, pfxPath), { encoding: 'utf8' });
      if (result.status !== 0) {
        // signtool's own words, never its command line: that would carry the PFX's password.
        throw new Error(
          `signtool couldn't sign ${file} (exit code ${result.status}): ${result.stdout}${result.stderr}`,
        );
      }
      const subject = signer(file);
      if (!subject) throw new Error(`${file} carries no signature after signing`);
      log(`sign: signed ${path.basename(file)} as ${subject}`);
    }
  } finally {
    if (temp) rmSync(temp, { recursive: true, force: true });
  }
  return true;
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const files = process.argv.slice(2);
  if (files.length === 0) {
    console.error('sign: name the files to sign: node scripts/sign.mjs <file>...');
    process.exit(2);
  }
  try {
    sign(files.map((file) => path.resolve(file)));
  } catch (error) {
    console.error(`sign: ${error instanceof Error ? error.message : String(error)}`);
    process.exit(1);
  }
}
