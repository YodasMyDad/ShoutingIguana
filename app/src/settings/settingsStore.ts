// Where preferences live: settings.json in the app's config folder in the shell, read and written through the shell's
// two settings commands (the page gets no file access of its own), and localStorage in a plain browser. Read once
// before the first render (so the theme never flashes), then written behind a short debounce on every change.
import { useSyncExternalStore } from 'react';
import { isTauri } from '../lib/platform';
import { ipc } from '../shell/ipc';
import { DEFAULT_SETTINGS, readSettings, type Settings } from './schema';

const LOCAL_STORAGE_KEY = 'si.settings';
const SAVE_DELAY_MS = 300;

export interface SettingsBackend {
  load(): Promise<unknown>;
  save(settings: Settings): Promise<void>;
}

export const localStorageBackend: SettingsBackend = {
  load: () => {
    try {
      const raw = localStorage.getItem(LOCAL_STORAGE_KEY);
      return Promise.resolve(raw ? (JSON.parse(raw) as unknown) : null);
    } catch {
      return Promise.resolve(null);
    }
  },
  save: (settings) => {
    try {
      localStorage.setItem(LOCAL_STORAGE_KEY, JSON.stringify(settings));
    } catch {
      // Storage full or blocked: the preferences still apply for this session.
    }
    return Promise.resolve();
  },
};

/** The shell's settings commands: one file, which the shell checks is a JSON object of at most 64 KB. */
export const shellBackend: SettingsBackend = {
  load: () => ipc.settingsRead(),
  save: (settings) => ipc.settingsWrite(JSON.stringify(settings)),
};

let backend: SettingsBackend = isTauri ? shellBackend : localStorageBackend;
let current: Settings = DEFAULT_SETTINGS;
let saveTimer: ReturnType<typeof setTimeout> | undefined;
const listeners = new Set<() => void>();

/** Reads the stored preferences. Call once before the first render. */
export async function loadSettings(override?: SettingsBackend): Promise<Settings> {
  if (override) backend = override;
  try {
    current = readSettings(await backend.load());
  } catch {
    current = DEFAULT_SETTINGS;
  }
  listeners.forEach((listener) => listener());
  return current;
}

export function getSettings(): Settings {
  return current;
}

export function updateSettings(patch: Partial<Settings> | ((settings: Settings) => Partial<Settings>)): void {
  const changes = typeof patch === 'function' ? patch(current) : patch;
  const next = readSettings({ ...current, ...changes });
  if (JSON.stringify(next) === JSON.stringify(current)) return;
  current = next;
  listeners.forEach((listener) => listener());
  clearTimeout(saveTimer);
  saveTimer = setTimeout(() => void backend.save(current).catch(() => undefined), SAVE_DELAY_MS);
}

/** Writes any pending change now: before the window closes. */
export async function flushSettings(): Promise<void> {
  if (saveTimer === undefined) return;
  clearTimeout(saveTimer);
  saveTimer = undefined;
  await backend.save(current);
}

export function subscribeSettings(listener: () => void): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

export function useSettings<T>(select: (settings: Settings) => T): T {
  const read = () => select(current);
  return useSyncExternalStore(subscribeSettings, read, read);
}
