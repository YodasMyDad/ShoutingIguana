import { describe, expect, it } from 'vitest';
import { DEFAULT_SETTINGS, readSettings } from './schema';
import { getSettings, loadSettings, updateSettings, type SettingsBackend } from './settingsStore';

describe('stored settings', () => {
  it('gives the defaults for nothing stored', () => {
    expect(readSettings(null)).toEqual(DEFAULT_SETTINGS);
    expect(readSettings('garbage')).toEqual(DEFAULT_SETTINGS);
  });

  it('keeps valid values and replaces invalid ones with defaults', () => {
    expect(
      readSettings({
        theme: 'dark',
        density: 'huge',
        zoom: 9,
        inspectorWidth: 50,
        sidebarCollapsed: 'yes',
        lastViewByCrawl: { '0123456789abcdef': 'urls', notAnId: 'urls', fedcba9876543210: '<b>' },
        lastLocation: '//evil.example/',
      }),
    ).toEqual({
      ...DEFAULT_SETTINGS,
      theme: 'dark',
      zoom: 2,
      inspectorWidth: 320,
      lastViewByCrawl: { '0123456789abcdef': 'urls' },
    });
  });

  it('leaves the inspector at the layout’s own size until it’s resized', () => {
    expect(DEFAULT_SETTINGS.inspectorWidth).toBeNull();
    expect(DEFAULT_SETTINGS.inspectorHeight).toBeNull();
    expect(readSettings({ inspectorWidth: null, inspectorHeight: 'tall' })).toMatchObject({
      inspectorWidth: null,
      inspectorHeight: null,
    });
    expect(readSettings({ inspectorWidth: 500, inspectorHeight: 300 })).toMatchObject({
      inspectorWidth: 500,
      inspectorHeight: 300,
    });
  });

  it('loads through a backend and saves changes', async () => {
    const saved: unknown[] = [];
    const backend: SettingsBackend = {
      load: () => Promise.resolve({ theme: 'light' }),
      save: (settings) => {
        saved.push(settings);
        return Promise.resolve();
      },
    };
    await loadSettings(backend);
    expect(getSettings().theme).toBe('light');
    updateSettings({ density: 'compact' });
    expect(getSettings().density).toBe('compact');
    await new Promise((resolve) => setTimeout(resolve, 400));
    expect(saved).toHaveLength(1);
  });
});
