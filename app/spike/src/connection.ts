// Where the sidecar is and how to authenticate. In Tauri the Rust shell owns this (it spawned the sidecar and read
// its ready line); in the browser bench the harness passes it in the URL.
import { useSyncExternalStore } from 'react';
import { invoke } from '@tauri-apps/api/core';
import { listen } from '@tauri-apps/api/event';

export type Connection = { port: number; token: string; generation: number; baseUrl: string };
type RawConnection = { port: number; token: string; generation: number };

let current: Connection | null = null;
const listeners = new Set<() => void>();

export const isTauri = '__TAURI_INTERNALS__' in window;

function toConnection(raw: RawConnection): Connection {
  return { ...raw, baseUrl: `http://127.0.0.1:${raw.port}` };
}

export function setConnection(next: Connection | null) {
  current = next;
  listeners.forEach((listener) => listener());
}

export function getConnection() {
  return current;
}

export function subscribeConnection(listener: () => void) {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}

export function useConnection() {
  return useSyncExternalStore(subscribeConnection, getConnection);
}

export async function initConnection() {
  if (isTauri) {
    // Listen first, then ask, so a ready event between the two is not lost.
    await listen<RawConnection>('sidecar://ready', (event) => setConnection(toConnection(event.payload)));
    await listen('sidecar://lost', () => setConnection(null));
    const existing = await invoke<RawConnection | null>('sidecar_connection');
    if (existing && (!current || existing.generation >= current.generation)) {
      setConnection(toConnection(existing));
    }
    return;
  }
  const params = new URLSearchParams(location.search);
  const port = Number(params.get('port'));
  const token = params.get('token');
  if (port && token) {
    setConnection(toConnection({ port, token, generation: 1 }));
  }
}

/** Bench hook: the harness restarted the sidecar and tells the page, as the Tauri ready event would. */
export function setConnectionForBench(port: number, token: string, generation: number) {
  setConnection(port ? toConnection({ port, token, generation }) : null);
}

/** Sends a timing or fact to the Rust shell's log (Tauri only). */
export function report(name: string, value: unknown) {
  if (isTauri) {
    void invoke('report', { name, value }).catch(() => undefined);
  }
}
