// How requests reach the sidecar. The client only ever sees this interface: `http` (the real sidecar) or `mock` (the
// design fixtures, for Storybook, the prototype and the grids the engine doesn't serve yet). VITE_TRANSPORT picks one.
import { waitForConnection } from '../shell/connection';
import { SidecarUnavailableError, SidecarUnreachableError, isAbort } from './errors';

export interface TransportRequest {
  method: 'GET' | 'POST' | 'PUT' | 'DELETE';
  /** An absolute API path such as `/api/crawls`, with any query string. */
  path: string;
  /** Sent as JSON. */
  body?: unknown;
  headers?: Record<string, string>;
  signal?: AbortSignal | undefined;
}

export interface Transport {
  readonly kind: 'http' | 'mock';
  /** Resolves with the sidecar's response, whatever its status; rejects only when no response came. */
  send(request: TransportRequest): Promise<Response>;
}

/**
 * The real sidecar, over fetch with the bearer token. A call made while the shell starts or restarts the sidecar
 * waits for it. Never `cache: 'no-store'`: it turns Chromium's CORS preflight cache off, and every call would pay for
 * a preflight (the sidecar sends `Cache-Control: no-store` itself).
 */
export const httpTransport: Transport = {
  kind: 'http',
  async send({ method, path, body, headers, signal }) {
    const connection = await waitForConnection(signal);
    if (!connection) throw new SidecarUnavailableError();
    const init: RequestInit = {
      method,
      headers: {
        Authorization: `Bearer ${connection.token}`,
        ...(body === undefined ? {} : { 'Content-Type': 'application/json' }),
        ...headers,
      },
      ...(body === undefined ? {} : { body: JSON.stringify(body) }),
      ...(signal ? { signal } : {}),
    };
    try {
      return await fetch(connection.baseUrl + path, init);
    } catch (error) {
      if (isAbort(error)) throw error;
      throw new SidecarUnreachableError(error);
    }
  },
};

let current: Transport = httpTransport;

/** The transport every call uses. main.tsx sets it once at start-up; tests set their own. */
export function getTransport(): Transport {
  return current;
}

export function setTransport(transport: Transport): void {
  current = transport;
}

/** VITE_TRANSPORT=mock serves the design fixtures; anything else is the real sidecar. */
export function wantsMockTransport(): boolean {
  return import.meta.env.VITE_TRANSPORT === 'mock';
}
