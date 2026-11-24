import { renderHook } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { updateSettings } from '../settings/settingsStore';
import type { ShellInfo, UpdateCheck, UpdateInstallOutcome } from '../shell/ipc';
import { createUpdateStore, type UpdateState } from './updateStore';
import { CHECK_EVERY_MS, FIRST_CHECK_MS, forgetUpdateNotices, updateNotice, useUpdateChecks } from './useUpdateChecks';

const navigate = vi.hoisted(() => vi.fn());
vi.mock('@tanstack/react-router', () => ({ useNavigate: () => navigate }));
const notify = vi.hoisted(() => ({ notifyResult: vi.fn(), notifyError: vi.fn() }));
vi.mock('../feedback/notify', () => notify);

const SHELL: ShellInfo = {
  appVersion: '2.1.1',
  platform: 'windows',
  debug: false,
  logDir: '',
  updates: 'inApp',
  updateFailed: null,
  updateChecks: true,
};
const NEWER = { version: '2.1.2', date: null, notes: 'Quicker checks.', downloaded: false };

/** What Tauri's invoke rejects with: the command's error as the shell serialised it, not an Error. */
const rejected = (reason: unknown): Promise<never> =>
  // eslint-disable-next-line @typescript-eslint/prefer-promise-reject-errors -- the shell's own value, as invoke gives it
  Promise.reject(reason);

function storeAnswering(check: () => Promise<UpdateCheck>, initial?: UpdateState) {
  const asked = vi.fn(check);
  const store = createUpdateStore(
    {
      check: asked,
      install: () => new Promise<UpdateInstallOutcome>(() => undefined),
      onProgress: () => Promise.resolve(() => undefined),
    },
    initial,
  );
  return { store, asked };
}

beforeEach(() => {
  vi.useFakeTimers();
  forgetUpdateNotices();
});

afterEach(() => {
  vi.useRealTimers();
  vi.clearAllMocks();
  updateSettings({ checkForUpdates: true });
});

describe('the app’s own check for a newer version', () => {
  it('asks a little after the app starts, then a few times a day, and says a newer version once', async () => {
    const { store, asked } = storeAnswering(() => Promise.resolve({ currentVersion: '2.1.1', available: NEWER }));
    renderHook(() => useUpdateChecks(store, SHELL));
    expect(asked).not.toHaveBeenCalled();
    await vi.advanceTimersByTimeAsync(FIRST_CHECK_MS);
    expect(asked).toHaveBeenCalledOnce();
    expect(notify.notifyResult).toHaveBeenCalledOnce();
    const [title, options] = notify.notifyResult.mock.calls[0] as [
      string,
      { description: string; action: { label: string; onClick: () => void } },
    ];
    expect(title).toBe('Version 2.1.2 is available');
    expect(options.description).toBe('Settings › Updates says what’s new and installs it when you’re ready.');
    expect(options.action.label).toBe('See what’s new');
    options.action.onClick();
    expect(navigate).toHaveBeenCalledWith({ to: '/settings/{-$section}', params: { section: 'updates' } });
    // Settings › Updates shows what the check found, with nothing more to press.
    expect(store.getState()).toEqual({ step: 'available', update: NEWER });

    await vi.advanceTimersByTimeAsync(CHECK_EVERY_MS);
    expect(asked).toHaveBeenCalledTimes(2);
    expect(notify.notifyResult).toHaveBeenCalledOnce();
  });

  it('says nothing when this is the newest version, or when it couldn’t ask', async () => {
    const newest = storeAnswering(() => Promise.resolve({ currentVersion: '2.1.1', available: null }));
    const { unmount } = renderHook(() => useUpdateChecks(newest.store, SHELL));
    await vi.advanceTimersByTimeAsync(FIRST_CHECK_MS);
    expect(newest.store.getState().step).toBe('upToDate');
    unmount();

    // Offline, or GitHub answering 404 (nothing published yet, or the repository is private).
    for (const problem of ['noConnection', 'noUpdateInformation']) {
      const failing = storeAnswering(() =>
        rejected({ problem, message: 'The shell’s sentence.', currentVersion: '2.1.1' }),
      );
      const hook = renderHook(() => useUpdateChecks(failing.store, SHELL));
      await vi.advanceTimersByTimeAsync(FIRST_CHECK_MS);
      expect(failing.asked).toHaveBeenCalledOnce();
      expect(failing.store.getState()).toMatchObject({ step: 'failed', failure: { problem } });
      hook.unmount();
    }
    expect(notify.notifyResult).not.toHaveBeenCalled();
    expect(notify.notifyError).not.toHaveBeenCalled();
  });

  it('doesn’t ask when the setting is off, in a test run of the app, or in a browser', async () => {
    const { store, asked } = storeAnswering(() => Promise.resolve({ currentVersion: '2.1.1', available: NEWER }));
    updateSettings({ checkForUpdates: false });
    const off = renderHook(() => useUpdateChecks(store, SHELL));
    await vi.advanceTimersByTimeAsync(FIRST_CHECK_MS + CHECK_EVERY_MS);
    off.unmount();
    updateSettings({ checkForUpdates: true });
    const test = renderHook(() => useUpdateChecks(store, { ...SHELL, updateChecks: false }));
    await vi.advanceTimersByTimeAsync(FIRST_CHECK_MS + CHECK_EVERY_MS);
    test.unmount();
    const browser = renderHook(() => useUpdateChecks(store, null));
    await vi.advanceTimersByTimeAsync(FIRST_CHECK_MS + CHECK_EVERY_MS);
    browser.unmount();
    expect(asked).not.toHaveBeenCalled();
    expect(notify.notifyResult).not.toHaveBeenCalled();
  });

  it('leaves a download alone', async () => {
    const { store, asked } = storeAnswering(() => Promise.resolve({ currentVersion: '2.1.1', available: NEWER }), {
      step: 'downloading',
      update: NEWER,
      progress: null,
    });
    renderHook(() => useUpdateChecks(store, SHELL));
    await vi.advanceTimersByTimeAsync(FIRST_CHECK_MS + CHECK_EVERY_MS);
    expect(asked).not.toHaveBeenCalled();
    expect(store.getState().step).toBe('downloading');
  });

  it('tells a .deb install the new version is out, not that it installs', async () => {
    const { store } = storeAnswering(() => Promise.resolve({ currentVersion: '2.1.1', available: NEWER }));
    renderHook(() => useUpdateChecks(store, { ...SHELL, platform: 'linux', updates: 'download' }));
    await vi.advanceTimersByTimeAsync(FIRST_CHECK_MS);
    expect(notify.notifyResult.mock.calls[0]?.[0]).toBe('Version 2.1.2 is out');
    expect(updateNotice('2.1.2', 'download').description).toBe(
      'Settings › Updates says what’s new and where to download it.',
    );
  });

  it('says once that the run before couldn’t install a version', () => {
    const { store } = storeAnswering(() => Promise.resolve({ currentVersion: '2.1.1', available: null }));
    const failed = { ...SHELL, updateFailed: '2.1.2' };
    const first = renderHook(() => useUpdateChecks(store, failed));
    first.unmount();
    renderHook(() => useUpdateChecks(store, failed));
    expect(notify.notifyError).toHaveBeenCalledOnce();
    expect(notify.notifyError.mock.calls[0]?.[1]).toMatchObject({
      title: 'Version 2.1.2 couldn’t be installed',
      description: 'Shouting Iguana opened again as it was. Try again from Settings › Updates.',
    });
  });
});
