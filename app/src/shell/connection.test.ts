import { afterEach, describe, expect, it } from 'vitest';
import { applyShellState, getConnection, getSidecarStatus, resetConnectionForTests } from './connection';
import { parseShellSidecarState } from './ipc';

afterEach(() => resetConnectionForTests());

describe('the sidecar connection from the shell', () => {
  it('connects on ready, at 127.0.0.1 with the port and token the shell gave', () => {
    applyShellState({ state: 'ready', seq: 1, generation: 1, port: 53123, token: 'secret', version: '2.0.0' });
    expect(getConnection()).toEqual({
      baseUrl: 'http://127.0.0.1:53123',
      token: 'secret',
      generation: 1,
      version: '2.0.0',
    });
  });

  it('keeps the newest state, whichever arrives first', () => {
    applyShellState({ state: 'restarting', seq: 5, generation: 1, attempt: 1, exitCode: null });
    applyShellState({ state: 'ready', seq: 3, generation: 1, port: 1, token: 'old', version: '' });
    expect(getSidecarStatus().state).toBe('restarting');
    expect(getConnection()).toBeNull();
  });

  it('has no connection while restarting, failed or stopping', () => {
    applyShellState({ state: 'ready', seq: 1, generation: 1, port: 1, token: 't', version: '' });
    applyShellState({ state: 'stopping', seq: 2, generation: 1 });
    expect(getConnection()).toBeNull();
    expect(getSidecarStatus().state).toBe('stopping');
  });
});

describe('the shell payload', () => {
  it('reads every state the shell sends', () => {
    expect(
      parseShellSidecarState({
        state: 'failed',
        seq: 9,
        generation: 2,
        reason: 'state-in-use',
        exitCode: 3,
        message: 'Busy.',
      }),
    ).toEqual({ state: 'failed', seq: 9, generation: 2, reason: 'state-in-use', exitCode: 3, message: 'Busy.' });
    expect(parseShellSidecarState({ state: 'starting', seq: 0, generation: 1 })).toEqual({
      state: 'starting',
      seq: 0,
      generation: 1,
    });
  });

  it('refuses a malformed payload rather than half-reading it', () => {
    expect(parseShellSidecarState({ state: 'ready', seq: 1, generation: 1 })).toBeNull();
    expect(parseShellSidecarState({ state: 'ready', seq: 'x', generation: 1, port: 1, token: 't' })).toBeNull();
    expect(parseShellSidecarState({ state: 'exploded', seq: 1, generation: 1 })).toBeNull();
    expect(parseShellSidecarState(null)).toBeNull();
  });
});
