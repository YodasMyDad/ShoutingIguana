import { describe, expect, it } from 'vitest';
import { DEFAULT_SETTINGS, readSettings } from '../settings/schema';
import { parseShellInfo, parseUpdateCheck, parseUpdateCheckFailure, parseUpdateProgress } from './ipc';

describe('what the shell says about updates', () => {
  it('reads a check: the newest version, a newer one, and one already downloaded', () => {
    expect(parseUpdateCheck({ currentVersion: '2.1.1', available: null })).toEqual({
      currentVersion: '2.1.1',
      available: null,
    });
    const available = { version: '2.1.2', date: '2026-12-01T10:00:00Z', notes: 'Quicker checks.', downloaded: true };
    expect(parseUpdateCheck({ currentVersion: '2.1.1', available }).available).toEqual(available);
    // A release that says nothing more than its version.
    expect(parseUpdateCheck({ currentVersion: '2.1.1', available: { version: '2.1.2' } }).available).toEqual({
      version: '2.1.2',
      date: null,
      notes: null,
      downloaded: false,
    });
    expect(parseUpdateCheck('nonsense')).toEqual({ currentVersion: '', available: null });
  });

  it('reads why a check showed nothing, and a download’s progress', () => {
    expect(
      parseUpdateCheckFailure({ problem: 'noUpdateInformation', message: 'Nothing yet.', currentVersion: '2.1.1' }),
    ).toEqual({ problem: 'noUpdateInformation', message: 'Nothing yet.', currentVersion: '2.1.1' });
    expect(parseUpdateCheckFailure({ problem: 'somethingNew', message: 'x' })).toBeNull();
    expect(parseUpdateCheckFailure('A sentence.')).toBeNull();
    expect(parseUpdateProgress({ downloaded: 5, total: 10 })).toEqual({ downloaded: 5, total: 10 });
    expect(parseUpdateProgress({ downloaded: 5, total: null })).toEqual({ downloaded: 5, total: null });
    expect(parseUpdateProgress({})).toBeNull();
  });

  it('reads how a new version arrives in this copy, and whether the app may look for one by itself', () => {
    const shell = { appVersion: '2.1.1', platform: 'linux', debug: false, logDir: '/logs' };
    expect(parseShellInfo({ ...shell, updates: 'download', updateFailed: null, updateChecks: true })).toEqual({
      ...shell,
      updates: 'download',
      updateFailed: null,
      updateChecks: true,
    });
    expect(parseShellInfo({ ...shell, updates: 'inApp', updateFailed: '2.1.2', updateChecks: false })).toMatchObject({
      updates: 'inApp',
      updateFailed: '2.1.2',
      updateChecks: false,
    });
    // An answer with no word on updates: the app installs them, and doesn't look by itself.
    expect(parseShellInfo(shell)).toMatchObject({ updates: 'inApp', updateFailed: null, updateChecks: false });
  });
});

describe('the automatic check setting', () => {
  it('is on until it is turned off', () => {
    expect(DEFAULT_SETTINGS.checkForUpdates).toBe(true);
    expect(readSettings({}).checkForUpdates).toBe(true);
    expect(readSettings({ checkForUpdates: false }).checkForUpdates).toBe(false);
    expect(readSettings({ checkForUpdates: 'no' }).checkForUpdates).toBe(true);
  });
});
