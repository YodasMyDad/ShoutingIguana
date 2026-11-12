// Where preferences live: settings.json in the app's config folder in the shell, read and written through the shell's
// two settings commands (the page gets no file access of its own), and localStorage in a plain browser. Read once
// before the first render (so the theme never flashes), then written behind a short debounce on every change.
import { useSyncExternalStore } from 'react';
import { isTauri } from '../lib/platform';
import { notifyError } from '../feedback/notify';
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
/**
 * What the stored preferences hold that this version doesn't know (a later version's), written back as it was: going
 * back to an older version never deletes what a newer one keeps there.
 */
let unknown: Record<string, unknown> = {};
/** The last write failed: said once, until a write works again. */
let failing = false;

const SAVE_FAILED = 'settings.save';

/** The preferences as written: this version's, and what it found there and doesn't know. */
function stored(settings: Settings): Settings {
  return { ...unknown, ...settings };
}

/** Writes the preferences, saying once (not on every change after) when they can't be written. */
async function save(): Promise<void> {
  try {
    await backend.save(stored(current));
    failing = false;
  } catch (error) {
    if (failing) return;
    failing = true;
    notifyError(error, {
      id: SAVE_FAILED,
      title: 'Your preferences couldn’t be saved',
      description: 'What you change now lasts until the app closes.',
    });
  }
}
let saveTimer: ReturnType<typeof setTimeout> | undefined;
const listeners = new Set<() => void>();

/** Reads the stored preferences. Call once before the first render. */
export async function loadSettings(override?: SettingsBackend): Promise<Settings> {
  if (override) backend = override;
  try {
    const raw = await backend.load();
    current = readSettings(raw);
    unknown =
      typeof raw === 'object' && raw !== null && !Array.isArray(raw)
        ? Object.fromEntries(Object.entries(raw).filter(([key]) => !(key in DEFAULT_SETTINGS)))
        : {};
  } catch {
    current = DEFAULT_SETTINGS;
    unknown = {};
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
  saveTimer = setTimeout(() => void save(), SAVE_DELAY_MS);
}

/** Writes any pending change now: before the window closes. */
export async function flushSettings(): Promise<void> {
  if (saveTimer === undefined) return;
  clearTimeout(saveTimer);
  saveTimer = undefined;
  await backend.save(stored(current));
}

export function subscribeSettings(listener: () => void): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

export function useSettings<T>(select: (settings: Settings) => T): T {
  const read = () => select(current);
  return useSyncExternalStore(subscribeSettings, read, read);
}
