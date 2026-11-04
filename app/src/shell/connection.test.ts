import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  applyShellState,
  getConnection,
  getSidecarStatus,
  resetConnectionForTests,
  waitForConnection,
} from './connection';
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
    applyShellState({
      state: 'restarting',
      seq: 5,
      generation: 1,
      attempt: 1,
      attempts: 5,
      reason: 'crashed',
      exitCode: null,
    });
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
    expect(
      parseShellSidecarState({
        state: 'restarting',
        seq: 4,
        generation: 1,
        attempt: 2,
        attempts: 2,
        reason: 'start-timed-out',
        exitCode: null,
      }),
    ).toEqual({
      state: 'restarting',
      seq: 4,
      generation: 1,
      attempt: 2,
      attempts: 2,
      reason: 'start-timed-out',
      exitCode: null,
    });
    // An older shell's restarting, without the attempts or the reason: a crash, one try.
    expect(parseShellSidecarState({ state: 'restarting', seq: 5, generation: 1, attempt: 3 })).toMatchObject({
      attempt: 3,
      attempts: 3,
      reason: 'crashed',
    });
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

describe('waiting for the engine', () => {
  const ready = (seq: number, generation = 1) =>
    applyShellState({ state: 'ready', seq, generation, port: 53123, token: 'secret', version: '2.0.0' });

  it('answers at once when the engine is ready, and with nothing when no one is starting one', async () => {
    await expect(waitForConnection()).resolves.toBeNull();
    ready(1);
    await expect(waitForConnection()).resolves.toMatchObject({ baseUrl: 'http://127.0.0.1:53123' });
  });

  /** The front-end review's case (M1): a call during a cold start failed for good with "The engine is starting." */
  it('waits through starting and restarting, then hands over the ready connection', async () => {
    applyShellState({ state: 'starting', seq: 1, generation: 1 });
    let settled: unknown = 'pending';
    void waitForConnection().then((connection) => (settled = connection));
    await Promise.resolve();
    applyShellState({
      state: 'restarting',
      seq: 2,
      generation: 1,
      attempt: 1,
      attempts: 2,
      reason: 'start-failed',
      exitCode: 1,
    });
    await Promise.resolve();
    expect(settled).toBe('pending');
    ready(3, 2);
    await vi.waitFor(() => expect(settled).toMatchObject({ generation: 2, token: 'secret' }));
  });

  it('gives up as soon as the engine has failed or the app is quitting', async () => {
    applyShellState({ state: 'starting', seq: 1, generation: 1 });
    const failing = waitForConnection();
    applyShellState({
      state: 'failed',
      seq: 2,
      generation: 1,
      reason: 'start-failed',
      exitCode: 1,
      message: 'No.',
    });
    await expect(failing).resolves.toBeNull();
    await expect(waitForConnection()).resolves.toBeNull();
    applyShellState({ state: 'stopping', seq: 3, generation: 1 });
    await expect(waitForConnection()).resolves.toBeNull();
  });

  it('stops waiting when the caller aborts, or after its bound', async () => {
    applyShellState({ state: 'starting', seq: 1, generation: 1 });
    const controller = new AbortController();
    const aborted = waitForConnection(controller.signal);
    controller.abort();
    await expect(aborted).rejects.toMatchObject({ name: 'AbortError' });
    await expect(waitForConnection(AbortSignal.abort())).rejects.toMatchObject({ name: 'AbortError' });
    await expect(waitForConnection(undefined, 20)).resolves.toBeNull();
  });
});
