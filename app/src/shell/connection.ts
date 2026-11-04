// Where the sidecar is, and whether it's there. In the shell, Rust owns the sidecar and hands its port and token over
// IPC (never in a URL or localStorage). In a plain browser, `npm run dev` passes them to Vite as VITE_SIDECAR_PORT
// and VITE_SIDECAR_TOKEN (a fixed development token). The mock transport needs neither.
import { useSyncExternalStore } from 'react';
import { isTauri } from '../lib/platform';
import { ipc, type ShellSidecarState, type SidecarFailureReason, type SidecarRestartReason } from './ipc';

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
  | {
      state: 'restarting';
      generation: number;
      /** Restart `attempt` of at most `attempts` before the shell gives up. */
      attempt: number;
      attempts: number;
      reason: SidecarRestartReason;
    }
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
      setStatus({
        state: 'restarting',
        generation: state.generation,
        attempt: state.attempt,
        attempts: state.attempts,
        reason: state.reason,
      });
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

/**
 * The longest a call waits for the engine. The shell gives up on a start well before this (three tries of at most
 * 20 s each), so it only bounds a shell that stopped saying anything.
 */
export const CONNECTION_WAIT_MS = 90_000;

/**
 * The connection once a sidecar is ready: at once if it is, after the shell has started (or restarted) it if not, so
 * calls made during a cold start or a restart go through instead of failing. Resolves null as soon as there's nothing
 * to wait for: the engine failed, the app is quitting, nothing is starting one (no shell, no development sidecar), or
 * `timeoutMs` passed. Rejects only when `signal` aborts.
 */
export function waitForConnection(
  signal?: AbortSignal,
  timeoutMs = CONNECTION_WAIT_MS,
): Promise<SidecarConnection | null> {
  const settled = (): SidecarConnection | null | undefined => {
    if (status.state === 'ready') return status.connection;
    // Generation 0 is before initConnection: nothing is starting an engine for this page.
    if (status.state === 'starting' && status.generation === 0) return null;
    return status.state === 'starting' || status.state === 'restarting' ? undefined : null;
  };
  const now = settled();
  if (now !== undefined) return Promise.resolve(now);
  if (signal?.aborted) return Promise.reject(abortReason(signal));
  return new Promise((resolve, reject) => {
    const finish = (settle: () => void) => {
      clearTimeout(timer);
      unsubscribe();
      signal?.removeEventListener('abort', onAbort);
      settle();
    };
    const onAbort = () => finish(() => reject(abortReason(signal)));
    const timer = setTimeout(() => finish(() => resolve(null)), timeoutMs);
    const unsubscribe = subscribeSidecarStatus(() => {
      const next = settled();
      if (next !== undefined) finish(() => resolve(next));
    });
    signal?.addEventListener('abort', onAbort, { once: true });
  });
}

/** What an aborted wait rejects with: the signal's reason, as fetch does, so callers tell it from a failure. */
function abortReason(signal: AbortSignal | undefined): Error {
  const reason: unknown = signal?.reason;
  return reason instanceof Error ? reason : new DOMException('The operation was aborted.', 'AbortError');
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
