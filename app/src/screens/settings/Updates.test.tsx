import { QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { setTransport } from '../../api/transport';
import { createQueryClient } from '../../app/queryClient';
import { getSettings, updateSettings } from '../../settings/settingsStore';
import type { ShellInfo } from '../../shell/ipc';
import { fakeSidecar, standardRoutes } from '../../test/fakeSidecar';
import { SETTINGS_SECTIONS } from './registry';
import { howUpdatesArrive } from '../../updates/updateWords';
import { RELEASES_URL, Section } from './sections/Updates';

const platform = vi.hoisted(() => ({ isTauri: false }));
vi.mock('../../lib/platform', async (original) => ({
  ...(await original<object>()),
  get isTauri() {
    return platform.isTauri;
  },
}));
const shell = vi.hoisted(() => ({ info: null as ShellInfo | null }));
vi.mock('../../shell/useShellInfo', () => ({ useShellInfo: () => shell.info }));
const os = vi.hoisted(() => ({ openInBrowser: vi.fn(() => Promise.resolve(true)) }));
vi.mock('../../shell/os', () => os);

const INSTALLED: ShellInfo = {
  appVersion: '2.1.1',
  platform: 'windows',
  debug: false,
  logDir: '',
  updates: 'inApp',
  updateFailed: null,
  updateChecks: true,
};

function show(info: ShellInfo | null) {
  platform.isTauri = info !== null;
  shell.info = info;
  setTransport(fakeSidecar(standardRoutes()));
  return render(
    <QueryClientProvider client={createQueryClient()}>
      <Section />
    </QueryClientProvider>,
  );
}

afterEach(() => {
  os.openInBrowser.mockClear();
  updateSettings({ checkForUpdates: true });
  platform.isTauri = false;
  shell.info = null;
});

// This file is beside `sections/`, not in it: the registry takes every file there into the app (a test among them
// brought the test runner into the built app, and Settings didn't open).
describe('Settings’ sections folder', () => {
  it('holds sections only', () => {
    const files = Object.keys(import.meta.glob('./sections/*.tsx'));
    expect(files.length).toBeGreaterThan(0);
    expect(files).toHaveLength(SETTINGS_SECTIONS.length);
  });
});

describe('Settings › Updates', () => {
  it('says the app updates itself, shows the version and offers a check', () => {
    show(INSTALLED);
    expect(screen.getByText('2.1.1')).toBeInTheDocument();
    expect(screen.getByText(/checks it was signed by us, runs its installer and opens again/)).toBeInTheDocument();
    expect(screen.getByText(/It never stops a crawl to do it\./)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Check for updates' })).toBeEnabled();
    expect(screen.queryByText(/doesn’t update itself/)).toBeNull();
  });

  // Each sentence is true of the copy it is shown in.
  it('says how a new version arrives in each kind of copy', () => {
    expect(howUpdatesArrive({ platform: 'windows', updates: 'inApp' })).toMatch(/runs its installer and opens again/);
    expect(howUpdatesArrive({ platform: 'macos', updates: 'inApp' })).toMatch(/replaces the app where it is/);
    expect(howUpdatesArrive({ platform: 'linux', updates: 'inApp' })).toMatch(/replaces the AppImage file where it is/);
    const deb = howUpdatesArrive({ platform: 'linux', updates: 'download' });
    expect(deb).toMatch(/installed from the \.deb package, so it can’t replace itself/);
    expect(deb).not.toMatch(/opens again|installs it/);
    for (const updates of ['inApp', 'download'] as const)
      expect(howUpdatesArrive({ platform: 'linux', updates })).toMatch(/Your crawls and settings stay as they are\./);
  });

  it('tells a .deb install it can’t replace itself', () => {
    show({ ...INSTALLED, platform: 'linux', updates: 'download' });
    expect(screen.getByText(/installed from the \.deb package, so it can’t replace itself/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Check for updates' })).toBeEnabled();
    expect(RELEASES_URL).toBe('https://github.com/YodasMyDad/ShoutingIguana/releases/latest');
  });

  it('turns the app’s own check off and on, and says what it sends', async () => {
    const user = userEvent.setup();
    show(INSTALLED);
    const automatic = screen.getByRole('switch', { name: 'Check for new versions automatically' });
    expect(automatic).toBeChecked();
    expect(automatic).toHaveAccessibleDescription(/Nothing is installed until you say so\./);
    await user.click(automatic);
    expect(getSettings().checkForUpdates).toBe(false);
    expect(automatic).not.toBeChecked();
    // Asking by hand still works.
    expect(screen.getByRole('button', { name: 'Check for updates' })).toBeEnabled();
    await user.click(automatic);
    expect(getSettings().checkForUpdates).toBe(true);
  });

  it('has nothing to check or install in a browser', () => {
    show(null);
    expect(screen.getByText('Running in a browser')).toBeInTheDocument();
    expect(screen.getByText(/The installed app checks for new versions here/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Check for updates' })).toBeNull();
    expect(screen.queryByRole('switch')).toBeNull();
  });
});
