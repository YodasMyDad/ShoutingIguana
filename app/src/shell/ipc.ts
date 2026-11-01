// The Tauri shell's IPC, typed (app/src-tauri/src/commands.rs and sidecar/). Only called inside the shell: every
// caller checks `isTauri` first. The webview never spawns anything; Rust owns the sidecar.
import { invoke } from '@tauri-apps/api/core';
import { listen, type UnlistenFn } from '@tauri-apps/api/event';

export type SidecarFailureReason = 'missing-binary' | 'bad-arguments' | 'state-in-use' | 'start-failed' | 'crashed';

/** The shell's `sidecar://state` payload. `seq` only ever grows, so the newest wins whichever arrives first. */
export type ShellSidecarState =
  | { state: 'starting'; seq: number; generation: number }
  | { state: 'ready'; seq: number; generation: number; port: number; token: string; version: string }
  | { state: 'restarting'; seq: number; generation: number; attempt: number; exitCode: number | null }
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
    case 'restarting':
      return { state: 'restarting', seq, generation, attempt: typeof v.attempt === 'number' ? v.attempt : 1, exitCode };
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
  takePendingOpenFiles: async (): Promise<string[]> => {
    const paths = await invoke<unknown>('take_pending_open_files');
    return Array.isArray(paths) ? paths.filter((path): path is string => typeof path === 'string') : [];
  },
  onOpenCrawlFiles: (handler: (paths: string[]) => void): Promise<UnlistenFn> =>
    listen<{ paths?: unknown }>('shell://open-crawl-files', (event) => {
      const paths = event.payload.paths;
      if (Array.isArray(paths)) handler(paths.filter((path): path is string => typeof path === 'string'));
    }),
};
