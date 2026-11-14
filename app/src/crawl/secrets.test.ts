import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { api } from '../api/client';
import { httpTransport, setTransport } from '../api/transport';
import { resetConnectionForTests, setDirectConnection } from '../shell/connection';
import {
  continueWithSecrets,
  keepSecrets,
  keptSecrets,
  secretsOf,
  setSecretsStore,
  withSecrets,
  type SecretsStore,
} from './secrets';

const CRAWL = '0123456789abcdef';

/** The OS credential store, as a map. */
function memoryStore(): SecretsStore & { kept: Map<string, string> } {
  const kept = new Map<string, string>();
  return {
    kept,
    read: (key) => Promise.resolve(kept.get(key) ?? null),
    write: (key, secrets) => {
      if (secrets === null) kept.delete(key);
      else kept.set(key, secrets);
      return Promise.resolve();
    },
  };
}

describe('the secrets a crawl is given (gap 18: the credential store)', () => {
  afterEach(() => setSecretsStore(null));

  it('takes the proxy with a user name and the headers with values, nothing else', () => {
    expect(
      secretsOf({
        proxyUrl: 'http://me:pw@proxy.example.com:3128',
        extraHeaders: { Authorization: 'Basic eDp5', 'X-Empty': '' },
      }),
    ).toEqual({ proxyUrl: 'http://me:pw@proxy.example.com:3128', headers: { Authorization: 'Basic eDp5' } });
    expect(secretsOf({ proxyUrl: 'http://proxy.example.com:3128', extraHeaders: null })).toEqual({});
    expect(secretsOf({ proxyUrl: 'http://***@proxy.example.com:3128' })).toEqual({});
  });

  it('puts them back into stored options: the same proxy only, and headers stored by name', () => {
    const stored = {
      proxyUrl: 'http://***@proxy.example.com:3128/',
      hasRedactedProxyCredentials: true,
      extraHeaders: { Authorization: '', 'X-Kept': 'shown' },
    };
    const secrets = {
      proxyUrl: 'http://me:pw@proxy.example.com:3128',
      headers: { Authorization: 'Basic eDp5', 'X-Kept': 'old' },
    };
    expect(withSecrets(stored, secrets)).toEqual({
      proxyUrl: 'http://me:pw@proxy.example.com:3128',
      hasRedactedProxyCredentials: false,
      extraHeaders: { Authorization: 'Basic eDp5', 'X-Kept': 'shown' },
    });
    // Another proxy now: its password isn't this one's.
    const other = withSecrets({ ...stored, proxyUrl: 'http://***@elsewhere.example.com:8080/' }, secrets);
    expect(other.proxyUrl).toBe('http://***@elsewhere.example.com:8080/');
    expect(other.hasRedactedProxyCredentials).toBe(true);
    expect(withSecrets(stored, null)).toBe(stored);
  });

  it('continues with what was kept, unless the request gives its own', () => {
    const kept = { proxyUrl: 'http://me:pw@proxy:3128', headers: { Authorization: 'Basic eDp5' } };
    expect(continueWithSecrets({}, kept)).toEqual({
      proxyUrl: 'http://me:pw@proxy:3128',
      extraHeaders: { Authorization: 'Basic eDp5' },
    });
    expect(continueWithSecrets({ proxyUrl: 'http://new:pw@proxy:3128' }, kept).proxyUrl).toBe(
      'http://new:pw@proxy:3128',
    );
    expect(continueWithSecrets({ skipMissingHeaders: true }, kept).extraHeaders).toBeUndefined();
    expect(continueWithSecrets({ concurrency: 2 }, null)).toEqual({ concurrency: 2 });
  });

  it('keeps nothing in a browser, and forgets what is empty', async () => {
    await keepSecrets('defaults', { proxyUrl: 'http://a:b@p:1' });
    expect(await keptSecrets('defaults')).toBeNull();
    const store = memoryStore();
    setSecretsStore(store);
    await keepSecrets('defaults', { proxyUrl: 'http://a:b@p:1' });
    expect(await keptSecrets('defaults')).toEqual({ proxyUrl: 'http://a:b@p:1' });
    await keepSecrets('defaults', {});
    expect(store.kept.has('defaults')).toBe(false);
  });
});

describe('the API client with the credential store', () => {
  const fetchMock = vi.fn<typeof fetch>();
  let store: ReturnType<typeof memoryStore>;

  beforeEach(() => {
    vi.stubGlobal('fetch', fetchMock);
    setTransport(httpTransport);
    setDirectConnection({ baseUrl: 'http://127.0.0.1:5000', token: 't'.repeat(64), generation: 1, version: 'test' });
    store = memoryStore();
    setSecretsStore(store);
  });

  afterEach(() => {
    fetchMock.mockReset();
    resetConnectionForTests();
    setSecretsStore(null);
  });

  const bodyOf = (call: number) => {
    const body = fetchMock.mock.calls[call]?.[1]?.body;
    return JSON.parse(typeof body === 'string' ? body : '{}') as Record<string, unknown>;
  };

  it('keeps what a crawl starts with, and Continue sends it without asking again', async () => {
    fetchMock.mockImplementation(() => Promise.resolve(Response.json({ id: CRAWL })));
    await api.startCrawl({
      options: {
        startUrl: 'https://example.com/',
        proxyUrl: 'http://me:pw@proxy:3128',
        extraHeaders: { Authorization: 'Basic eDp5' },
      },
    });
    await vi.waitFor(() => expect(store.kept.get(`crawl:${CRAWL}`)).toBeDefined());
    expect(JSON.parse(store.kept.get(`crawl:${CRAWL}`) ?? '{}')).toEqual({
      proxyUrl: 'http://me:pw@proxy:3128',
      headers: { Authorization: 'Basic eDp5' },
    });
    await api.continueCrawl(CRAWL);
    expect(bodyOf(1)).toEqual({ proxyUrl: 'http://me:pw@proxy:3128', extraHeaders: { Authorization: 'Basic eDp5' } });
  });

  it('saves the defaults without secrets to the engine, and keeps them in the store', async () => {
    fetchMock.mockImplementation(() =>
      Promise.resolve(
        Response.json({
          options: { proxyUrl: 'http://***@proxy:3128/', hasRedactedProxyCredentials: true },
          speedPresets: [],
        }),
      ),
    );
    const saved = await api.saveCrawlOptionDefaults(
      { proxyUrl: 'http://proxy:3128' },
      { proxyUrl: 'http://me:pw@proxy:3128' },
    );
    expect(bodyOf(0)).toEqual({ proxyUrl: 'http://proxy:3128' });
    expect(saved.options.proxyUrl).toBe('http://me:pw@proxy:3128');
    const read = await api.crawlOptionDefaults();
    expect(read.options.proxyUrl).toBe('http://me:pw@proxy:3128');
    expect(read.options.hasRedactedProxyCredentials).toBe(false);
  });
});
