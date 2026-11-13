import { invoke } from '@tauri-apps/api/core';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { DEFAULT_SETTINGS } from './schema';
import { notifyError } from '../feedback/notify';
import {
  flushSettings,
  getSettings,
  loadSettings,
  shellBackend,
  updateSettings,
  type SettingsBackend,
} from './settingsStore';

vi.mock('../feedback/notify', () => ({ notifyError: vi.fn() }));

vi.mock('@tauri-apps/api/core', () => ({ invoke: vi.fn() }));
const invokeMock = vi.mocked(invoke);

afterEach(() => invokeMock.mockReset());

describe('preferences in the shell', () => {
  /** The shell review's case (H1): the page used the store plugin, whose commands take any path on disk. */
  it('go through the shell’s two settings commands, which name no file', async () => {
    invokeMock.mockResolvedValueOnce({ ...DEFAULT_SETTINGS, theme: 'dark' });
    await loadSettings(shellBackend);
    expect(invokeMock).toHaveBeenCalledWith('settings_read');
    expect(getSettings().theme).toBe('dark');

    invokeMock.mockResolvedValue(undefined);
    updateSettings({ theme: 'light' });
    await flushSettings();
    const [command, args] = invokeMock.mock.calls.at(-1) ?? [];
    expect(command).toBe('settings_write');
    expect(Object.keys(args ?? {})).toEqual(['settings']);
    const written: unknown = JSON.parse((args as { settings: string }).settings);
    expect(written).toMatchObject({ theme: 'light' });
  });

  it('start from the defaults when there are none stored yet', async () => {
    invokeMock.mockResolvedValueOnce(null);
    await expect(loadSettings(shellBackend)).resolves.toEqual(DEFAULT_SETTINGS);
  });
});

describe('writing the preferences', () => {
  afterEach(() => {
    vi.useRealTimers();
    vi.mocked(notifyError).mockReset();
  });

  function backend(load: unknown, save: SettingsBackend['save']): SettingsBackend {
    return { load: () => Promise.resolve(load), save };
  }

  it('says once that they couldn’t be saved, never silently, and says it again only after a write has worked', async () => {
    vi.useFakeTimers();
    const refused = new Error('the settings are 70000 bytes, over the 65536-byte limit');
    const save = vi.fn<SettingsBackend['save']>().mockRejectedValue(refused);
    await loadSettings(backend({}, save));
    updateSettings({ zoom: 1.25 });
    await vi.advanceTimersByTimeAsync(1_000);
    updateSettings({ zoom: 1.5 });
    await vi.advanceTimersByTimeAsync(1_000);
    expect(save).toHaveBeenCalledTimes(2);
    expect(notifyError).toHaveBeenCalledOnce();
    expect(notifyError).toHaveBeenCalledWith(
      refused,
      expect.objectContaining({ title: 'Your preferences couldn’t be saved' }),
    );
    save.mockResolvedValue(undefined);
    updateSettings({ zoom: 1.75 });
    await vi.advanceTimersByTimeAsync(1_000);
    save.mockRejectedValue(refused);
    updateSettings({ zoom: 2 });
    await vi.advanceTimersByTimeAsync(1_000);
    expect(notifyError).toHaveBeenCalledTimes(2);
  });

  it('keep what a later version stored and this one doesn’t know', async () => {
    const save = vi.fn<SettingsBackend['save']>().mockResolvedValue(undefined);
    await loadSettings(backend({ theme: 'dark', laterVersionsField: { kept: true } }, save));
    updateSettings({ density: 'compact' });
    await flushSettings();
    expect(save).toHaveBeenLastCalledWith(
      expect.objectContaining({ theme: 'dark', density: 'compact', laterVersionsField: { kept: true } }),
    );
  });
});
