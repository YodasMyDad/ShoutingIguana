// The signing hook (`node --test scripts`): what it does with and without a certificate configured.
import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { sign, signingConfig, signtoolArguments } from './sign.mjs';

describe('signing', () => {
  /** The review's case (M3): nothing could sign a release; until a certificate is bought, builds go on, saying so. */
  it('skips with a plain line when no certificate is configured', () => {
    const lines = [];
    assert.equal(
      sign(['C:\\build\\si-sidecar.exe', 'setup.exe'], {}, (line) => lines.push(line)),
      false,
    );
    assert.deepEqual(lines, [
      'sign: skipped si-sidecar.exe, setup.exe: no code-signing certificate is configured (set SI_SIGN_CERT_THUMBPRINT, or SI_SIGN_PFX_BASE64 and SI_SIGN_PFX_PASSWORD)',
    ]);
  });

  it('takes a certificate by thumbprint before a PFX, with DigiCert timestamps unless told otherwise', () => {
    assert.equal(signingConfig({}), null);
    assert.deepEqual(signingConfig({ SI_SIGN_CERT_THUMBPRINT: 'ab cd', SI_SIGN_PFX_BASE64: 'x' }), {
      kind: 'thumbprint',
      thumbprint: 'abcd',
      timestampUrl: 'http://timestamp.digicert.com',
    });
    assert.deepEqual(
      signingConfig({ SI_SIGN_PFX_BASE64: 'cGZ4', SI_SIGN_PFX_PASSWORD: 'pw', SI_SIGN_TIMESTAMP_URL: 'http://ts' }),
      { kind: 'pfx', pfx: 'cGZ4', password: 'pw', timestampUrl: 'http://ts' },
    );
  });

  it('signs with SHA-256 digests, a timestamp and the app’s name', () => {
    const config = { kind: 'thumbprint', thumbprint: 'abcd', timestampUrl: 'http://ts' };
    assert.deepEqual(signtoolArguments(config, 'app.exe'), [
      'sign',
      '/fd',
      'SHA256',
      '/td',
      'SHA256',
      '/tr',
      'http://ts',
      '/d',
      'Shouting Iguana',
      '/sha1',
      'abcd',
      'app.exe',
    ]);
    const pfx = signtoolArguments(
      { kind: 'pfx', pfx: 'x', password: 'pw', timestampUrl: 'http://ts' },
      'a.exe',
      'c.pfx',
    );
    assert.deepEqual(pfx.slice(-5), ['/f', 'c.pfx', '/p', 'pw', 'a.exe']);
  });
});
