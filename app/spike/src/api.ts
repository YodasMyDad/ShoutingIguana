import { getConnection } from './connection';

export type UrlRow = {
  index: number;
  url: string;
  status: number;
  indexable: boolean;
  indexabilityReason: string | null;
  title: string;
  contentType: string;
  size: number;
  depth: number;
  responseMs: number;
  inlinks: number;
  outlinks: number;
  words: number;
};

export type UrlPage = { total: number; offset: number; rows: UrlRow[] };

export class SidecarUnavailableError extends Error {}

import { params } from './params';

const fetchMode = params.get('fetch') === 'post' ? 'post' : 'get';

export async function sidecarFetch(path: string, init: RequestInit = {}) {
  const connection = getConnection();
  if (!connection) {
    throw new SidecarUnavailableError('The sidecar is not ready');
  }
  const headers = new Headers(init.headers);
  headers.set('Authorization', `Bearer ${connection.token}`);
  // No cache: 'no-store' here: Chromium then skips its CORS preflight cache and preflights every request. The sidecar
  // sends Cache-Control: no-store instead.
  return fetch(connection.baseUrl + path, { ...init, headers });
}

/** Client-observed page latency (request start to parsed JSON), for the bench. */
export const fetchTimings: number[] = [];

export async function fetchUrlPage(pageIndex: number, pageSize: number, sort: string, signal: AbortSignal): Promise<UrlPage> {
  const offset = pageIndex * pageSize;
  const started = performance.now();
  const response =
    fetchMode === 'post'
      ? await sidecarFetch('/api/urls/query', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ offset, limit: pageSize, sort }),
          signal,
        })
      : await sidecarFetch(`/api/urls?offset=${offset}&limit=${pageSize}&sort=${encodeURIComponent(sort)}`, { signal });
  if (!response.ok) {
    throw new Error(`Page ${pageIndex} failed: HTTP ${response.status}`);
  }
  const page = (await response.json()) as UrlPage;
  fetchTimings.push(performance.now() - started);
  return page;
}
