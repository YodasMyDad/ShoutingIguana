// The Tauri shell's IPC, typed (app/src-tauri/src/commands.rs and sidecar/). Only called inside the shell: every
// caller checks `isTauri` first. The webview never spawns anything, and never names a path for the shell to read or
// write: Rust owns the sidecar, the preferences file and the crawl files' folders.
import { invoke } from '@tauri-apps/api/core';
import { listen, type UnlistenFn } from '@tauri-apps/api/event';

export type SidecarFailureReason = 'missing-binary' | 'bad-arguments' | 'state-in-use' | 'start-failed' | 'crashed';

/** Why the engine is being started again: it stopped while serving, stopped while starting, or never said it was ready. */
export type SidecarRestartReason = 'crashed' | 'start-failed' | 'start-timed-out';

/** The shell's `sidecar://state` payload. `seq` only ever grows, so the newest wins whichever arrives first. */
export type ShellSidecarState =
  | { state: 'starting'; seq: number; generation: number }
  | { state: 'ready'; seq: number; generation: number; port: number; token: string; version: string }
  | {
      state: 'restarting';
      seq: number;
      generation: number;
      /** Restart `attempt` of at most `attempts` before the shell gives up. */
      attempt: number;
      attempts: number;
      reason: SidecarRestartReason;
      exitCode: number | null;
    }
  | {
      state: 'failed';
      seq: number;
      generation: number;
      reason: SidecarFailureReason;
      exitCode: number | null;
      message: string;
    }
  | { state: 'stopping'; seq: number; generation: number };

export interface ShellInfo {
  appVersion: string;
  platform: 'windows' | 'macos' | 'linux';
  debug: boolean;
  logDir: string;
}

/** Validates a payload from the shell; anything malformed gives null rather than a half-read state. */
export function parseShellSidecarState(value: unknown): ShellSidecarState | null {
  if (typeof value !== 'object' || value === null) return null;
  const v = value as Record<string, unknown>;
  const seq = v.seq;
  const generation = v.generation;
  if (typeof seq !== 'number' || typeof generation !== 'number') return null;
  const exitCode = typeof v.exitCode === 'number' ? v.exitCode : null;
  switch (v.state) {
    case 'starting':
    case 'stopping':
      return { state: v.state, seq, generation };
    case 'ready':
      if (typeof v.port !== 'number' || typeof v.token !== 'string') return null;
      return {
        state: 'ready',
        seq,
        generation,
        port: v.port,
        token: v.token,
        version: typeof v.version === 'string' ? v.version : '',
      };
    case 'restarting': {
      const attempt = typeof v.attempt === 'number' ? v.attempt : 1;
      return {
        state: 'restarting',
        seq,
        generation,
        attempt,
        attempts: typeof v.attempts === 'number' ? v.attempts : attempt,
        reason: isRestartReason(v.reason) ? v.reason : 'crashed',
        exitCode,
      };
    }
    case 'failed':
      return {
        state: 'failed',
        seq,
        generation,
        reason: isFailureReason(v.reason) ? v.reason : 'crashed',
        exitCode,
        message: typeof v.message === 'string' ? v.message : '',
      };
    default:
      return null;
  }
}

function isRestartReason(value: unknown): value is SidecarRestartReason {
  return value === 'crashed' || value === 'start-failed' || value === 'start-timed-out';
}

function isFailureReason(value: unknown): value is SidecarFailureReason {
  return (
    value === 'missing-binary' ||
    value === 'bad-arguments' ||
    value === 'state-in-use' ||
    value === 'start-failed' ||
    value === 'crashed'
  );
}

export const ipc = {
  sidecarState: async (): Promise<ShellSidecarState | null> => parseShellSidecarState(await invoke('sidecar_state')),
  onSidecarState: (handler: (state: ShellSidecarState) => void): Promise<UnlistenFn> =>
    listen('sidecar://state', (event) => {
      const state = parseShellSidecarState(event.payload);
      if (state) handler(state);
    }),
  restartSidecar: (): Promise<void> => invoke('restart_sidecar'),
  requestQuit: (): Promise<void> => invoke('request_quit'),
  shellInfo: (): Promise<ShellInfo> => invoke<ShellInfo>('shell_info'),
  /** The `.sicrawl` files waiting to be opened, each handed over once. */
  takePendingOpenFiles: async (): Promise<string[]> => {
    const paths = await invoke<unknown>('take_pending_open_files');
    return Array.isArray(paths) ? paths.filter((path): path is string => typeof path === 'string') : [];
  },
  /** More crawl files are waiting: take them when the engine can open them. */
  onOpenCrawlFiles: (handler: () => void): Promise<UnlistenFn> => listen('shell://open-crawl-files', () => handler()),
  /** The stored preferences, or null when there are none yet. */
  settingsRead: (): Promise<unknown> => invoke<unknown>('settings_read'),
  /** Stores the preferences: a JSON object, at most 64 KB. */
  settingsWrite: (settings: string): Promise<void> => invoke('settings_write', { settings }),
  /** Shows a library crawl's file in Explorer or Finder. The shell finds the file from the crawl's id. */
  revealCrawlFile: (crawlId: string): Promise<void> => invoke('reveal_crawl_file', { crawlId }),
  /** Shows a finished export's file in Explorer or Finder. The shell asks the engine where the export wrote it. */
  revealExportFile: (exportId: string): Promise<void> => invoke('reveal_export_file', { exportId }),
  /** Opens a finished export's file in the program the system gives its kind. Only a file an export wrote. */
  openExportFile: (exportId: string): Promise<void> => invoke('open_export_file', { exportId }),
  /** Opens a log folder in Explorer or Finder: the app's own (`app`) or the engine's (`engine`). */
  openLogsFolder: (which: 'app' | 'engine'): Promise<void> => invoke('open_logs_folder', { which }),
  /** Asks the release feed whether there's a newer version, and what's new in it. */
  updateCheck: async (): Promise<UpdateCheck> => parseUpdateCheck(await invoke<unknown>('update_check')),
  /** Installs the version the last check found: the app closes and the installer runs. */
  updateInstall: (version: string): Promise<void> => invoke('update_install', { version }),
  /** The update's download, as it goes. */
  onUpdateProgress: (handler: (progress: UpdateProgress) => void): Promise<UnlistenFn> =>
    listen<unknown>('shell://update-progress', (event) => {
      const progress = parseUpdateProgress(event.payload);
      if (progress) handler(progress);
    }),
  /** The secrets kept in the credential store for the crawl defaults or a crawl, as the JSON text they were kept as. */
  secretsRead: async (key: string): Promise<string | null> => {
    const secrets = await invoke<unknown>('secrets_read', { key });
    return typeof secrets === 'string' ? secrets : null;
  },
  /** Keeps secrets (a JSON object, as text) in the credential store; null forgets them. */
  secretsWrite: (key: string, secrets: string | null): Promise<void> => invoke('secrets_write', { key, secrets }),
  /** The data folder the user chose, and the one before it. */
  dataFolderChoice: async (): Promise<DataFolderChoice> =>
    parseDataFolderChoice(await invoke<unknown>('data_folder_choice')),
  /** The system's folder dialog, then the switch: null when the dialog was cancelled. The engine starts again. */
  dataFolderChoose: async (): Promise<DataFolderChoice | null> => {
    const choice = await invoke<unknown>('data_folder_choose');
    return choice === null ? null : parseDataFolderChoice(choice);
  },
  /** Back to the folder before the last change, or to the default. The engine starts again. */
  dataFolderReset: async (to: 'previous' | 'default'): Promise<DataFolderChoice> =>
    parseDataFolderChoice(await invoke<unknown>('data_folder_reset', { to })),
  /**
   * Files dragged onto the window (src-tauri/src/drop.rs): `enter` while they're held over it, `drop` when they're
   * let go, `leave` when they're taken away. `crawlFiles` are the paths that are `.sicrawl` files.
   */
  onDragDrop: (handler: (event: ShellDragDrop) => void): Promise<UnlistenFn> =>
    listen<unknown>('shell://drag-drop', (event) => {
      const drop = parseShellDragDrop(event.payload);
      if (drop) handler(drop);
    }),
};

export interface UpdateCheck {
  currentVersion: string;
  /** The newer version, or null when this one is the newest. */
  available: { version: string; date: string | null; notes: string | null } | null;
}

export interface UpdateProgress {
  downloaded: number;
  total: number | null;
}

export interface DataFolderChoice {
  /** The folder the user chose; null is the default. */
  chosen: string | null;
  /** The folder before the last change (null: the default), when `hasPrevious`. */
  previous: string | null;
  hasPrevious: boolean;
  /** Set for this run by its environment (a test or development run): it can't be changed. */
  fixed: boolean;
}

export type ShellDragDrop =
  { type: 'enter' | 'drop'; paths: string[]; crawlFiles: string[] } | { type: 'leave'; paths: []; crawlFiles: [] };

const textOrNull = (value: unknown): string | null => (typeof value === 'string' ? value : null);
const strings = (value: unknown): string[] =>
  Array.isArray(value) ? value.filter((item): item is string => typeof item === 'string') : [];

export function parseUpdateCheck(value: unknown): UpdateCheck {
  const v = (typeof value === 'object' && value !== null ? value : {}) as Record<string, unknown>;
  const available =
    typeof v.available === 'object' && v.available !== null ? (v.available as Record<string, unknown>) : null;
  return {
    currentVersion: textOrNull(v.currentVersion) ?? '',
    available:
      available && typeof available.version === 'string'
        ? { version: available.version, date: textOrNull(available.date), notes: textOrNull(available.notes) }
        : null,
  };
}

export function parseUpdateProgress(value: unknown): UpdateProgress | null {
  if (typeof value !== 'object' || value === null) return null;
  const { downloaded, total } = value as Record<string, unknown>;
  if (typeof downloaded !== 'number') return null;
  return { downloaded, total: typeof total === 'number' ? total : null };
}

export function parseDataFolderChoice(value: unknown): DataFolderChoice {
  const v = (typeof value === 'object' && value !== null ? value : {}) as Record<string, unknown>;
  return {
    chosen: textOrNull(v.chosen),
    previous: textOrNull(v.previous),
    hasPrevious: v.hasPrevious === true,
    fixed: v.fixed === true,
  };
}

export function parseShellDragDrop(value: unknown): ShellDragDrop | null {
  if (typeof value !== 'object' || value === null) return null;
  const v = value as Record<string, unknown>;
  if (v.type === 'leave') return { type: 'leave', paths: [], crawlFiles: [] };
  if (v.type !== 'enter' && v.type !== 'drop') return null;
  return { type: v.type, paths: strings(v.paths), crawlFiles: strings(v.crawlFiles) };
}
