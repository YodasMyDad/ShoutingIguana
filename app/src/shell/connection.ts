// Where the sidecar is, and whether it's there. In the shell, Rust owns the sidecar and hands its port and token over
// IPC (never in a URL or localStorage). In a plain browser, `npm run dev` passes them to Vite as VITE_SIDECAR_PORT
// and VITE_SIDECAR_TOKEN (a fixed development token). The mock transport needs neither.
import { useSyncExternalStore } from 'react';
import { isTauri } from '../lib/platform';
import { ipc, type ShellSidecarState, type SidecarFailureReason } from './ipc';

export interface SidecarConnection {
  baseUrl: string;
  token: string;
  /** Which sidecar process this is. A new generation is a new port and token, and its own event stream. */
  generation: number;
  version: string;
}

export type SidecarStatus =
  | { state: 'starting'; generation: number }
  | { state: 'ready'; generation: number; connection: SidecarConnection }
  | { state: 'restarting'; generation: number; attempt: number }
  | {
      state: 'failed';
      generation: number;
      reason: SidecarFailureReason | 'not-configured';
      message: string;
      exitCode: number | null;
    }
  | { state: 'stopping'; generation: number };

let status: SidecarStatus = { state: 'starting', generation: 0 };
let lastSeq = -1;
const listeners = new Set<() => void>();

export function getSidecarStatus(): SidecarStatus {
  return status;
}

/** The connection to call, or null while there's no ready sidecar. */
export function getConnection(): SidecarConnection | null {
  return status.state === 'ready' ? status.connection : null;
}

export function subscribeSidecarStatus(listener: () => void): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

export function useSidecarStatus(): SidecarStatus {
  return useSyncExternalStore(subscribeSidecarStatus, getSidecarStatus, getSidecarStatus);
}

function setStatus(next: SidecarStatus): void {
  status = next;
  listeners.forEach((listener) => listener());
}

/** Applies a state from the shell, keeping only the newest (by `seq`) whichever way it arrived. */
export function applyShellState(state: ShellSidecarState): void {
  if (state.seq <= lastSeq) return;
  lastSeq = state.seq;
  switch (state.state) {
    case 'ready':
      setStatus({
        state: 'ready',
        generation: state.generation,
        connection: {
          baseUrl: `http://127.0.0.1:${state.port}`,
          token: state.token,
          generation: state.generation,
          version: state.version,
        },
      });
      break;
    case 'restarting':
      setStatus({ state: 'restarting', generation: state.generation, attempt: state.attempt });
      break;
    case 'failed':
      setStatus({
        state: 'failed',
        generation: state.generation,
        reason: state.reason,
        message: state.message,
        exitCode: state.exitCode,
      });
      break;
    default:
      setStatus({ state: state.state, generation: state.generation });
  }
}

/** Sets the connection directly: the browser's development sidecar, the mock transport, and tests. */
export function setDirectConnection(connection: SidecarConnection | null): void {
  setStatus(
    connection
      ? { state: 'ready', generation: connection.generation, connection }
      : {
          state: 'failed',
          generation: 0,
          reason: 'not-configured',
          message: 'No engine to connect to. Start the app with npm run dev, or npm run dev:mock for sample data.',
          exitCode: null,
        },
  );
}

/** Asks the shell to start the sidecar again after it failed. */
export async function restartSidecar(): Promise<void> {
  if (isTauri) await ipc.restartSidecar();
}

/** Connects to wherever the sidecar is. Called once, before the first render. */
export async function initConnection(options: { mock: boolean }): Promise<void> {
  if (options.mock) {
    setDirectConnection({ baseUrl: 'mock://sidecar', token: 'mock', generation: 1, version: 'mock' });
    return;
  }
  if (isTauri) {
    // Listen first, then ask: a state sent between the two is then never lost, and seq keeps the newest.
    await ipc.onSidecarState(applyShellState);
    const current = await ipc.sidecarState();
    if (current) applyShellState(current);
    return;
  }
  const port = Number(import.meta.env.VITE_SIDECAR_PORT);
  const token = import.meta.env.VITE_SIDECAR_TOKEN;
  setDirectConnection(
    Number.isInteger(port) && port > 0 && typeof token === 'string' && token.length > 0
      ? { baseUrl: `http://127.0.0.1:${port}`, token, generation: 1, version: 'dev' }
      : null,
  );
}

/** Tests only: back to the state before initConnection. */
export function resetConnectionForTests(): void {
  status = { state: 'starting', generation: 0 };
  lastSeq = -1;
  listeners.clear();
}
