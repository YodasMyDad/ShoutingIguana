// The development ports (`node --test scripts`): fixed defaults, each movable from the environment.
import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { portFrom } from './ports.mjs';

describe('a development port', () => {
  /** The review's case (L10): two dev or e2e runs on one machine collided on fixed ports, with no way to move them. */
  it('is the default unless the environment moves it', () => {
    assert.equal(portFrom('SI_PREVIEW_PORT', 51876, {}), 51876);
    assert.equal(portFrom('SI_PREVIEW_PORT', 51876, { SI_PREVIEW_PORT: '' }), 51876);
    assert.equal(portFrom('SI_PREVIEW_PORT', 51876, { SI_PREVIEW_PORT: '52000' }), 52000);
  });

  it('refuses anything that isn’t a port', () => {
    for (const value of ['0', '65536', '51876.5', 'abc', '-1']) {
      assert.throws(
        () => portFrom('SI_PREVIEW_PORT', 51876, { SI_PREVIEW_PORT: value }),
        /SI_PREVIEW_PORT must be a port/,
      );
    }
  });
});
