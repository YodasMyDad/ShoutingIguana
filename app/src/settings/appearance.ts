// Applies the theme, density and zoom to the page (data attributes that tokens.css keys on) and to the native window.
// High contrast needs nothing here: tokens.css maps the roles under `forced-colors: active`.
import { setWindowTheme, setZoom } from '../shell/os';
import type { Settings } from './schema';
import { getSettings, subscribeSettings } from './settingsStore';

export function applyAppearance(settings: Settings, root: HTMLElement = document.documentElement): void {
  if (settings.theme === 'system') delete root.dataset.theme;
  else root.dataset.theme = settings.theme;
  root.dataset.density = settings.density;
}

/** Keeps the page and the window in step with the settings for the app's lifetime. */
export function startAppearance(): () => void {
  let applied: Settings | null = null;
  const apply = () => {
    const settings = getSettings();
    applyAppearance(settings);
    if (applied?.theme !== settings.theme) {
      void setWindowTheme(settings.theme === 'system' ? null : settings.theme).catch(() => undefined);
    }
    if (applied?.zoom !== settings.zoom) void setZoom(settings.zoom).catch(() => undefined);
    applied = settings;
  };
  apply();
  return subscribeSettings(apply);
}

/** The theme in use now: the setting, or the OS's when the setting is "system". */
export function resolvedTheme(settings: Settings): 'light' | 'dark' {
  if (settings.theme !== 'system') return settings.theme;
  return typeof matchMedia === 'function' && matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
}
