import { invoke } from '@tauri-apps/api/core';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { DEFAULT_SETTINGS } from './schema';
import { flushSettings, getSettings, loadSettings, shellBackend, updateSettings } from './settingsStore';

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
