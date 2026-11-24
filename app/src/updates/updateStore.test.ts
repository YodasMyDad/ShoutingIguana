import { describe, expect, it, vi } from 'vitest';
import type { UpdateInstallOutcome, UpdateProgress } from '../shell/ipc';
import { createUpdateStore, reduce, type UpdateState, type Updater } from './updateStore';

const NEWER = { version: '2.1.2', date: '2026-12-01T10:00:00Z', notes: 'Quicker checks.', downloaded: false };

/** What Tauri's invoke rejects with: the command's error as the shell serialised it, not an Error. */
const rejected = (reason: unknown): Promise<never> =>
  // eslint-disable-next-line @typescript-eslint/prefer-promise-reject-errors -- the shell's own value, as invoke gives it
  Promise.reject(reason);

function updater(overrides: Partial<Updater> = {}) {
  let handler: ((progress: UpdateProgress) => void) | null = null;
  const stopped = vi.fn();
  const shell: Updater = {
    check: vi.fn(() => Promise.resolve({ currentVersion: '2.1.1', available: NEWER })),
    install: vi.fn(() => new Promise<UpdateInstallOutcome>(() => undefined)),
    onProgress: vi.fn((next: (progress: UpdateProgress) => void) => {
      handler = next;
      return Promise.resolve(() => {
        handler = null;
        stopped();
      });
    }),
    ...overrides,
  };
  return { shell, stopped, emit: (progress: UpdateProgress) => handler?.(progress) };
}

describe('the update’s states', () => {
  it('go from a check to the newest version or a newer one', () => {
    expect(reduce({ step: 'idle' }, { type: 'check' })).toEqual({ step: 'checking' });
    const at = '2026-12-02T09:00:00Z';
    expect(
      reduce({ step: 'checking' }, { type: 'checked', result: { currentVersion: '2.1.1', available: null }, at }),
    ).toEqual({ step: 'upToDate', version: '2.1.1', checkedAt: at });
    expect(
      reduce({ step: 'checking' }, { type: 'checked', result: { currentVersion: '2.1.1', available: NEWER }, at }),
    ).toEqual({ step: 'available', update: NEWER });
    // Downloaded while a crawl ran, and checked again since: it only has to be installed.
    const downloaded = { ...NEWER, downloaded: true };
    expect(
      reduce({ step: 'checking' }, { type: 'checked', result: { currentVersion: '2.1.1', available: downloaded }, at }),
    ).toEqual({ step: 'ready', update: downloaded, held: false });
  });

  it('go from a newer version through its download to its install, or to waiting', () => {
    const available: UpdateState = { step: 'available', update: NEWER };
    const downloading = reduce(available, { type: 'install' });
    expect(downloading).toEqual({ step: 'downloading', update: NEWER, progress: null });
    const progress = { downloaded: 10, total: 100 };
    expect(reduce(downloading, { type: 'progress', progress })).toEqual({ ...downloading, progress });
    expect(reduce(downloading, { type: 'installing' })).toEqual({ step: 'installing', update: NEWER });
    const ready = reduce(downloading, { type: 'waiting' });
    expect(ready).toEqual({ step: 'ready', update: { ...NEWER, downloaded: true }, held: true });
    // Already downloaded: installing it shows no download.
    expect(reduce(ready, { type: 'install' })).toEqual({ step: 'installing', update: { ...NEWER, downloaded: true } });
  });

  it('keep the version a failed install was for, and ignore what has no version to act on', () => {
    expect(reduce({ step: 'downloading', update: NEWER, progress: null }, { type: 'failed', message: 'No.' })).toEqual({
      step: 'failed',
      message: 'No.',
      update: NEWER,
      failure: undefined,
    });
    const idle: UpdateState = { step: 'idle' };
    expect(reduce(idle, { type: 'install' })).toBe(idle);
    expect(reduce(idle, { type: 'waiting' })).toBe(idle);
    expect(reduce(idle, { type: 'installing' })).toBe(idle);
    expect(reduce(idle, { type: 'progress', progress: { downloaded: 1, total: null } })).toBe(idle);
  });
});

describe('the update store', () => {
  it('tells its listeners of each state, and stops telling one that has gone', async () => {
    const { shell } = updater();
    const store = createUpdateStore(shell);
    const steps: string[] = [];
    const stop = store.subscribe(() => steps.push(store.getState().step));
    await store.check();
    expect(steps).toEqual(['checking', 'available']);
    stop();
    await store.check();
    expect(steps).toHaveLength(2);
  });

  it('follows a download’s progress only while it downloads', async () => {
    const { shell, emit, stopped } = updater({ install: () => Promise.resolve<UpdateInstallOutcome>('waiting') });
    const store = createUpdateStore(shell, { step: 'available', update: NEWER });
    const seen: (UpdateProgress | null)[] = [];
    store.subscribe(() => {
      const state = store.getState();
      if (state.step === 'downloading') seen.push(state.progress);
    });
    const installing = store.install();
    await vi.waitFor(() => expect(shell.onProgress).toHaveBeenCalled());
    emit({ downloaded: 5, total: 10 });
    expect(await installing).toBe(false);
    expect(seen).toEqual([null, { downloaded: 5, total: 10 }]);
    expect(stopped).toHaveBeenCalledOnce();
    expect(store.getState()).toEqual({ step: 'ready', update: { ...NEWER, downloaded: true }, held: true });
  });

  it('says a refused download in the shell’s words, and stops listening', async () => {
    const words = 'The download’s signature didn’t match this app’s, so it was thrown away. Nothing was installed.';
    const { shell, stopped } = updater({ install: () => rejected(words) });
    const store = createUpdateStore(shell, { step: 'available', update: NEWER });
    expect(await store.install()).toBe(true);
    expect(store.getState()).toEqual({ step: 'failed', message: words, update: NEWER, failure: undefined });
    expect(stopped).toHaveBeenCalledOnce();
  });

  it('says a check that couldn’t ask as its case: offline, nothing published yet (a 404, a private repository)', async () => {
    for (const problem of ['noConnection', 'noUpdateInformation'] as const) {
      const { shell } = updater({
        check: () => rejected({ problem, message: 'The shell’s sentence.', currentVersion: '2.1.1' }),
      });
      const store = createUpdateStore(shell);
      await store.check({ quiet: true });
      const state = store.getState();
      expect(state.step).toBe('failed');
      expect(state).toMatchObject({ update: null, failure: { problem, currentVersion: '2.1.1' } });
    }
  });

  it('installs nothing unless a version is shown, and never two things at once', async () => {
    const { shell } = updater();
    const store = createUpdateStore(shell);
    expect(await store.install()).toBe(false);
    expect(shell.install).not.toHaveBeenCalled();
    await store.check();
    void store.install();
    await vi.waitFor(() => expect(shell.install).toHaveBeenCalledOnce());
    void store.install();
    await store.check();
    expect(shell.install).toHaveBeenCalledOnce();
    expect(shell.check).toHaveBeenCalledOnce();
    expect(store.getState().step).toBe('downloading');
  });

  it('leaves a downloaded update alone when the app checks by itself, and checks again when asked', async () => {
    const downloaded = { ...NEWER, downloaded: true };
    const { shell } = updater({
      check: vi.fn(() => Promise.resolve({ currentVersion: '2.1.1', available: downloaded })),
    });
    const store = createUpdateStore(shell, { step: 'ready', update: downloaded, held: true });
    await store.check({ quiet: true });
    expect(shell.check).not.toHaveBeenCalled();
    await store.check();
    expect(shell.check).toHaveBeenCalledOnce();
    expect(store.getState()).toEqual({ step: 'ready', update: downloaded, held: false });
  });
});
