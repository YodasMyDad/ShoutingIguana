import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { resetConnectionForTests, setDirectConnection } from '../shell/connection';
import { api } from './client';
import {
  ProblemError,
  SidecarUnavailableError,
  SidecarUnreachableError,
  UnexpectedResponseError,
  errorDetail,
  errorTitle,
  isProblem,
} from './errors';
import { httpTransport, setTransport } from './transport';

const fetchMock = vi.fn<typeof fetch>();

function problem(body: Record<string, unknown>, status: number) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/problem+json' } });
}

beforeEach(() => {
  vi.stubGlobal('fetch', fetchMock);
  setTransport(httpTransport);
  setDirectConnection({ baseUrl: 'http://127.0.0.1:5000', token: 't'.repeat(64), generation: 1, version: 'test' });
});

afterEach(() => {
  fetchMock.mockReset();
  resetConnectionForTests();
});

describe('the API client', () => {
  it('sends the bearer token, and never cache: no-store (it disables the preflight cache)', async () => {
    fetchMock.mockResolvedValue(Response.json([]));
    await api.crawls();
    const [url, init] = fetchMock.mock.calls[0] ?? [];
    expect(url).toBe('http://127.0.0.1:5000/api/crawls');
    expect(new Headers(init?.headers).get('Authorization')).toBe(`Bearer ${'t'.repeat(64)}`);
    expect(init?.cache).toBeUndefined();
  });

  it('posts grid queries as JSON to one fixed URL per grid', async () => {
    fetchMock.mockResolvedValue(Response.json({ offset: 200, rows: [], total: 0, unfilteredTotal: 0 }));
    await api.urls('0123456789abcdef', { offset: 200, limit: 200, sort: [], filters: [] });
    const [url, init] = fetchMock.mock.calls[0] ?? [];
    expect(url).toBe('http://127.0.0.1:5000/api/crawls/0123456789abcdef/urls');
    expect(init?.method).toBe('POST');
    expect(new Headers(init?.headers).get('Content-Type')).toBe('application/json');
    expect(JSON.parse(init?.body as string)).toMatchObject({ offset: 200, limit: 200 });
  });

  it('resolves a body-less success with undefined', async () => {
    fetchMock.mockResolvedValue(new Response(null, { status: 204 }));
    await expect(api.pauseCrawl('0123456789abcdef')).resolves.toBeUndefined();
    fetchMock.mockResolvedValue(new Response(null, { status: 202 }));
    await expect(api.stopCrawl('0123456789abcdef')).resolves.toBeUndefined();
  });

  it('maps problem+json to a typed ProblemError, title first and detail behind it', async () => {
    fetchMock.mockResolvedValue(
      problem(
        { type: 'crawl-busy', title: 'This crawl is open in another process.', status: 409, detail: 'lock held' },
        409,
      ),
    );
    const error = await api.crawl('0123456789abcdef').catch((caught: unknown) => caught);
    expect(error).toBeInstanceOf(ProblemError);
    expect(isProblem(error, 'crawl-busy')).toBe(true);
    expect(errorTitle(error)).toBe('This crawl is open in another process.');
    expect(errorDetail(error)).toBe('crawl-busy (HTTP 409)\nlock held');
    expect((error as ProblemError).retryable).toBe(true);
  });

  it('keeps field errors for inline validation', async () => {
    fetchMock.mockResolvedValue(
      problem(
        {
          type: 'invalid-options',
          title: 'Some settings need changing.',
          status: 422,
          errors: { maxUrls: ['Too big.'], bad: 'nope' },
        },
        422,
      ),
    );
    const error = await api.startCrawl({ options: { maxUrls: -1 } }).catch((caught: unknown) => caught);
    expect(isProblem(error, 'invalid-options')).toBe(true);
    expect((error as ProblemError).errors).toEqual({ maxUrls: ['Too big.'] });
    expect((error as ProblemError).retryable).toBe(false);
  });

  it('reads what needs-secrets asks for', async () => {
    fetchMock.mockResolvedValue(
      problem(
        {
          type: 'needs-secrets',
          title: 'Give the secrets again.',
          status: 422,
          missingHeaders: ['X-Key'],
          proxyCredentials: true,
        },
        422,
      ),
    );
    const error = (await api.continueCrawl('0123456789abcdef').catch((caught: unknown) => caught)) as ProblemError;
    expect(error.missingHeaders).toEqual(['X-Key']);
    expect(error.proxyCredentials).toBe(true);
  });

  it('turns an error without a problem body into UnexpectedResponseError', async () => {
    fetchMock.mockResolvedValue(new Response('<html>Bad gateway</html>', { status: 502 }));
    const error = await api.health().catch((caught: unknown) => caught);
    expect(error).toBeInstanceOf(UnexpectedResponseError);
    expect((error as UnexpectedResponseError).status).toBe(502);
    expect(errorDetail(error)).toContain('Bad gateway');
  });

  it('turns a success that is not JSON into UnexpectedResponseError', async () => {
    fetchMock.mockResolvedValue(new Response('not json', { status: 200 }));
    await expect(api.health()).rejects.toBeInstanceOf(UnexpectedResponseError);
  });

  it('turns a failed fetch into SidecarUnreachableError', async () => {
    fetchMock.mockRejectedValue(new TypeError('Failed to fetch'));
    const error = await api.health().catch((caught: unknown) => caught);
    expect(error).toBeInstanceOf(SidecarUnreachableError);
    expect(errorTitle(error)).toBe('Couldn’t reach the engine.');
  });

  it('passes an abort through untouched, so cancelled queries stay quiet', async () => {
    fetchMock.mockRejectedValue(new DOMException('The operation was aborted.', 'AbortError'));
    await expect(api.health()).rejects.toMatchObject({ name: 'AbortError' });
  });

  it('says the engine is starting when there is no connection yet', async () => {
    resetConnectionForTests();
    await expect(api.health()).rejects.toBeInstanceOf(SidecarUnavailableError);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('encodes ids and check ids in the path', async () => {
    fetchMock.mockResolvedValue(Response.json({ rows: [], total: null }));
    await api.issues('0123456789abcdef', 'title-too-long', { limit: 200, offset: 400 });
    expect(fetchMock.mock.calls[0]?.[0]).toBe(
      'http://127.0.0.1:5000/api/crawls/0123456789abcdef/issues/title-too-long?limit=200&offset=400',
    );
  });
});
