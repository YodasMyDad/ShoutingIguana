import { QueryClient, QueryObserver } from '@tanstack/react-query';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { api } from '../api/client';
import { SidecarUnavailableError } from '../api/errors';
import { httpTransport, setTransport } from '../api/transport';
import { applyShellState, resetConnectionForTests } from './connection';
import { refetchFailedQueriesOnFirstReady } from './readiness';

const fetchMock = vi.fn<typeof fetch>();

const ready = (seq: number, generation: number) =>
  applyShellState({ state: 'ready', seq, generation, port: 53123, token: 'secret', version: '2.0.0' });

beforeEach(() => {
  vi.stubGlobal('fetch', fetchMock);
  setTransport(httpTransport);
});

afterEach(() => {
  fetchMock.mockReset();
  resetConnectionForTests();
});

describe('calls made while the shell starts the engine', () => {
  /** The front-end review's case (M1): calls failed at once, and a restored view stayed on "The engine is starting." */
  it('wait for it, then go to the new engine with its token', async () => {
    fetchMock.mockResolvedValue(Response.json({ status: 'ok' }));
    applyShellState({ state: 'starting', seq: 1, generation: 1 });
    const call = api.health();
    await Promise.resolve();
    expect(fetchMock).not.toHaveBeenCalled();
    ready(2, 1);
    await call;
    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock.mock.calls[0] ?? [];
    expect(url).toBe('http://127.0.0.1:53123/api/health');
    expect(new Headers(init?.headers).get('Authorization')).toBe('Bearer secret');
  });

  it('fail at once when the engine can’t run', async () => {
    applyShellState({ state: 'failed', seq: 1, generation: 1, reason: 'missing-binary', exitCode: null, message: '' });
    await expect(api.health()).rejects.toBeInstanceOf(SidecarUnavailableError);
    expect(fetchMock).not.toHaveBeenCalled();
  });
});

describe('queries that failed before the first engine was ready', () => {
  /** The engine failed to start, so a view's query failed; "Try again" brought one up, and the view came back. */
  it('fetch again once it is, and only then', async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: Infinity } } });
    applyShellState({ state: 'failed', seq: 1, generation: 1, reason: 'start-failed', exitCode: 1, message: '' });
    let calls = 0;
    const observer = new QueryObserver(queryClient, {
      queryKey: ['library'],
      queryFn: () => {
        calls += 1;
        return calls === 1 ? Promise.reject(new SidecarUnavailableError()) : Promise.resolve(['crawl']);
      },
    });
    const unsubscribe = observer.subscribe(() => undefined);
    await vi.waitFor(() => expect(observer.getCurrentResult().status).toBe('error'));

    const stop = refetchFailedQueriesOnFirstReady(queryClient);
    applyShellState({ state: 'starting', seq: 2, generation: 2 });
    expect(calls).toBe(1);
    ready(3, 2);
    await vi.waitFor(() => expect(observer.getCurrentResult().data).toEqual(['crawl']));
    expect(calls).toBe(2);

    // A later engine is the live bridge's to refresh, not this.
    ready(4, 3);
    await Promise.resolve();
    expect(calls).toBe(2);
    stop();
    unsubscribe();
  });
});
