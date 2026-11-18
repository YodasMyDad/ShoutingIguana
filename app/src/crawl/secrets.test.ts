import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { api } from '../api/client';
import { httpTransport, setTransport } from '../api/transport';
import { resetConnectionForTests, setDirectConnection } from '../shell/connection';
import {
  continueWithSecrets,
  isRemembered,
  keepSecrets,
  keptSecrets,
  rememberShopifySignature,
  resetSessionSecretsForTests,
  savedShopifySignature,
  secretsOf,
  setSecretsStore,
  withSecrets,
  type SecretsStore,
} from './secrets';
import { resetPendingRechecks, startRecheck } from './recheck';

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

  it('keeps a Shopify signature with both values, and gives it back to stored options, Continue and nothing else', () => {
    const signature = { signatureInput: 'sig1=("@authority");expires=4102444800', signature: 'sig1=:FAKE:' };
    expect(
      secretsOf({ signatures: { 'shop.example': signature, 'half.example': { signatureInput: 'x', signature: '' } } }),
    ).toEqual({
      signatures: { 'shop.example': signature },
    });
    const kept = { signatures: { 'shop.example': signature } };
    // A crawl file keeps none: the kept one comes back whole, and a request's own wins.
    expect(withSecrets({ startUrl: 'https://shop.example/' }, kept).signatures).toEqual(kept.signatures);
    expect(continueWithSecrets({}, kept)).toEqual({ signatures: kept.signatures });
    expect(continueWithSecrets({ signatures: {} }, kept).signatures).toEqual({});
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

  it('keeps them for the session only in a browser, which has no credential store, and forgets what is empty', async () => {
    await keepSecrets('defaults', { proxyUrl: 'http://a:b@p:1' }, { remember: true });
    expect(await keptSecrets('defaults')).toEqual({ proxyUrl: 'http://a:b@p:1' });
    expect(await isRemembered('defaults')).toBe(false);
    // A restart: the page's memory is gone, and with it the secrets.
    resetSessionSecretsForTests();
    expect(await keptSecrets('defaults')).toBeNull();
    const store = memoryStore();
    setSecretsStore(store);
    await keepSecrets('defaults', { proxyUrl: 'http://a:b@p:1' }, { remember: true });
    expect(await keptSecrets('defaults')).toEqual({ proxyUrl: 'http://a:b@p:1' });
    await keepSecrets('defaults', {}, { remember: true });
    expect(store.kept.has('defaults')).toBe(false);
    expect(await keptSecrets('defaults')).toBeNull();
  });

  it('puts nothing in the credential store without consent: the secrets last for this session only', async () => {
    const store = memoryStore();
    setSecretsStore(store);
    const secrets = { proxyUrl: 'http://a:b@p:1', headers: { Authorization: 'Basic eDp5' } };
    await keepSecrets('defaults', secrets, { remember: false });
    expect(store.kept.size).toBe(0);
    expect(await keptSecrets('defaults')).toEqual(secrets);
    expect(await isRemembered('defaults')).toBe(false);
    resetSessionSecretsForTests();
    expect(await keptSecrets('defaults')).toBeNull();

    // With consent, the store keeps them, and they outlast a restart.
    await keepSecrets('defaults', secrets, { remember: true });
    expect(JSON.parse(store.kept.get('defaults') ?? '{}')).toEqual(secrets);
    expect(await isRemembered('defaults')).toBe(true);
    resetSessionSecretsForTests();
    expect(await keptSecrets('defaults')).toEqual(secrets);

    // Consent taken back: what was remembered leaves the store, and lasts for this session only.
    await keepSecrets('defaults', secrets, { remember: false });
    expect(store.kept.has('defaults')).toBe(false);
    expect(await isRemembered('defaults')).toBe(false);
    expect(await keptSecrets('defaults')).toEqual(secrets);
  });

  it('counts what an earlier version remembered without asking as remembered, until it is taken back', async () => {
    const store = memoryStore();
    setSecretsStore(store);
    store.kept.set('defaults', JSON.stringify({ proxyUrl: 'http://a:b@p:1' }));
    expect(await isRemembered('defaults')).toBe(true);
    expect(await keptSecrets('defaults')).toEqual({ proxyUrl: 'http://a:b@p:1' });
    // A store that can't be read remembers nothing it can show.
    setSecretsStore({ read: () => Promise.reject(new Error('locked')), write: () => Promise.resolve() });
    expect(await isRemembered('defaults')).toBe(false);
  });
});

describe('a Shopify signature remembered for its store (lane shopify, phase B)', () => {
  /** 1 January 2100, as the fake signature says it ends. */
  const ENDS = 4_102_444_800;
  const NOW = new Date('2026-11-18T12:00:00Z');
  const signature = {
    signatureInput: `sig1=("@authority" "signature-agent");created=1767225600;expires=${ENDS};keyid="fake-test-key";alg="ed25519";tag="web-bot-auth"`,
    signature: 'sig1=:FAKETESTONLYAAAA:',
  };

  afterEach(() => {
    setSecretsStore(null);
    resetSessionSecretsForTests();
  });

  it('is kept by its host in the credential store, given back with when it ends, and forgotten when asked', async () => {
    const store = memoryStore();
    setSecretsStore(store);

    await rememberShopifySignature('shop.example', signature);

    expect([...store.kept.keys()]).toEqual(['shopify:shop.example']);
    expect(await savedShopifySignature('shop.example', NOW)).toEqual({ signature, expires: new Date(ENDS * 1000) });
    expect(await savedShopifySignature('other.example', NOW)).toBeNull();
    await rememberShopifySignature('shop.example', null);
    expect(store.kept.size).toBe(0);
    expect(await savedShopifySignature('shop.example', NOW)).toBeNull();
  });

  it('is forgotten once it has ended, and gives back nothing it can’t read as a signature', async () => {
    const store = memoryStore();
    setSecretsStore(store);
    await rememberShopifySignature('shop.example', signature);

    expect(await savedShopifySignature('shop.example', new Date((ENDS + 1) * 1000))).toBeNull();
    expect(store.kept.size).toBe(0);
    store.kept.set('shopify:shop.example', JSON.stringify({ signatureInput: 'sig1=(', signature: 7 }));
    expect(await savedShopifySignature('shop.example', NOW)).toBeNull();
  });

  it('is kept nowhere in a browser, which has no credential store: not even for this session', async () => {
    await rememberShopifySignature('shop.example', signature);
    expect(await savedShopifySignature('shop.example', NOW)).toBeNull();
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

  it('keeps what a crawl starts with for this session, and Continue sends it without asking again', async () => {
    fetchMock.mockImplementation(() => Promise.resolve(Response.json({ id: CRAWL })));
    await api.startCrawl({
      options: {
        startUrl: 'https://example.com/',
        proxyUrl: 'http://me:pw@proxy:3128',
        extraHeaders: { Authorization: 'Basic eDp5' },
      },
    });
    await api.continueCrawl(CRAWL);
    expect(bodyOf(1)).toEqual({ proxyUrl: 'http://me:pw@proxy:3128', extraHeaders: { Authorization: 'Basic eDp5' } });
    // Nothing went to the credential store: after a restart, Continue asks for them again.
    expect(store.kept.size).toBe(0);
    resetSessionSecretsForTests();
    await api.continueCrawl(CRAWL);
    expect(bodyOf(2)).toEqual({});
  });

  it('remembers what a crawl starts with in the credential store when asked to, and Continue sends it after a restart', async () => {
    fetchMock.mockImplementation(() => Promise.resolve(Response.json({ id: CRAWL })));
    await api.startCrawl(
      {
        options: {
          startUrl: 'https://example.com/',
          proxyUrl: 'http://me:pw@proxy:3128',
          extraHeaders: { Authorization: 'Basic eDp5' },
        },
      },
      { remember: true },
    );
    await vi.waitFor(() => expect(store.kept.get(`crawl:${CRAWL}`)).toBeDefined());
    resetSessionSecretsForTests();
    expect(JSON.parse(store.kept.get(`crawl:${CRAWL}`) ?? '{}')).toEqual({
      proxyUrl: 'http://me:pw@proxy:3128',
      headers: { Authorization: 'Basic eDp5' },
    });
    await api.continueCrawl(CRAWL);
    expect(bodyOf(1)).toEqual({ proxyUrl: 'http://me:pw@proxy:3128', extraHeaders: { Authorization: 'Basic eDp5' } });
  });

  it('remembers a Shopify signature a crawl starts with only when asked, and a recheck sends it', async () => {
    fetchMock.mockImplementation(() => Promise.resolve(Response.json({ id: CRAWL, recheckId: 1 })));
    const signatures = { 'shop.example': { signatureInput: 'sig1=("@authority")', signature: 'sig1=:FAKE:' } };
    await api.startCrawl({ options: { startUrl: 'https://shop.example/', signatures } }, { remember: true });
    await vi.waitFor(() => expect(store.kept.get(`crawl:${CRAWL}`)).toBeDefined());
    resetSessionSecretsForTests();
    expect(JSON.parse(store.kept.get(`crawl:${CRAWL}`) ?? '{}')).toEqual({ signatures });

    await startRecheck(CRAWL, { urlIds: [7] });
    expect(bodyOf(1)).toEqual({ urlIds: [7], signatures });
    resetPendingRechecks();
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
      { remember: true },
    );
    expect(bodyOf(0)).toEqual({ proxyUrl: 'http://proxy:3128' });
    expect(saved.options.proxyUrl).toBe('http://me:pw@proxy:3128');
    resetSessionSecretsForTests();
    const read = await api.crawlOptionDefaults();
    expect(read.options.proxyUrl).toBe('http://me:pw@proxy:3128');
    expect(read.options.hasRedactedProxyCredentials).toBe(false);
  });

  it('saves the defaults’ secrets for this session only, unless asked to remember them', async () => {
    fetchMock.mockImplementation(() =>
      Promise.resolve(
        Response.json({
          options: { proxyUrl: 'http://***@proxy:3128/', hasRedactedProxyCredentials: true },
          speedPresets: [],
        }),
      ),
    );
    store.kept.set('defaults', JSON.stringify({ proxyUrl: 'http://old:pw@proxy:3128' }));
    await api.saveCrawlOptionDefaults({ proxyUrl: 'http://proxy:3128' }, { proxyUrl: 'http://me:pw@proxy:3128' });
    // What an earlier version remembered is taken out; the new password lasts as long as the app is open.
    expect(store.kept.has('defaults')).toBe(false);
    expect((await api.crawlOptionDefaults()).options.proxyUrl).toBe('http://me:pw@proxy:3128');
    resetSessionSecretsForTests();
    const after = await api.crawlOptionDefaults();
    expect(after.options.proxyUrl).toBe('http://***@proxy:3128/');
    expect(after.options.hasRedactedProxyCredentials).toBe(true);
  });

  it('keeps what a Continue gives where the crawl’s secrets already were', async () => {
    fetchMock.mockImplementation(() => Promise.resolve(Response.json({ id: CRAWL })));
    await api.continueCrawl(CRAWL, { proxyUrl: 'http://me:new@proxy:3128' });
    expect(store.kept.size).toBe(0);
    expect(await keptSecrets(`crawl:${CRAWL}`)).toEqual({ proxyUrl: 'http://me:new@proxy:3128' });
    store.kept.set(`crawl:${CRAWL}`, JSON.stringify({ proxyUrl: 'http://me:pw@proxy:3128' }));
    await api.continueCrawl(CRAWL, { proxyUrl: 'http://me:newer@proxy:3128' });
    await vi.waitFor(() =>
      expect(JSON.parse(store.kept.get(`crawl:${CRAWL}`) ?? '{}')).toEqual({ proxyUrl: 'http://me:newer@proxy:3128' }),
    );
  });

  // Round 5 review R2-2: a crawl's secrets go with it.
  const SECRETS = JSON.stringify({ proxyUrl: 'http://me:pw@proxy:3128', headers: { Authorization: 'Basic eDp5' } });

  it('forgets a crawl’s secrets once its file is deleted, and keeps them when the delete fails', async () => {
    store.kept.set(`crawl:${CRAWL}`, SECRETS);
    fetchMock.mockImplementationOnce(() =>
      Promise.resolve(Response.json({ type: 'crawl-busy', title: 'Busy.', status: 409 }, { status: 409 })),
    );
    await expect(api.deleteCrawlFile(CRAWL)).rejects.toBeDefined();
    expect(store.kept.get(`crawl:${CRAWL}`)).toBe(SECRETS);
    fetchMock.mockImplementationOnce(() => Promise.resolve(new Response(null, { status: 204 })));
    await api.deleteCrawlFile(CRAWL);
    expect(store.kept.has(`crawl:${CRAWL}`)).toBe(false);
  });

  it('takes a removed crawl’s secrets out of the store, and Undo (restore) puts them back', async () => {
    store.kept.set(`crawl:${CRAWL}`, SECRETS);
    fetchMock.mockImplementationOnce(() => Promise.resolve(new Response(null, { status: 204 })));
    await api.removeCrawl(CRAWL);
    // Out of the store at once: an app closed before the Undo runs out leaves nothing behind.
    expect(store.kept.has(`crawl:${CRAWL}`)).toBe(false);
    fetchMock.mockImplementationOnce(() => Promise.resolve(Response.json({ id: CRAWL })));
    await api.restoreCrawl(CRAWL);
    expect(JSON.parse(store.kept.get(`crawl:${CRAWL}`) ?? '{}')).toEqual(JSON.parse(SECRETS));
  });

  it('puts a removed crawl’s session secrets back in the session, never in the store', async () => {
    fetchMock.mockImplementation(() => Promise.resolve(Response.json({ id: CRAWL })));
    await api.startCrawl({ options: { startUrl: 'https://example.com/', proxyUrl: 'http://me:pw@proxy:3128' } });
    fetchMock.mockImplementationOnce(() => Promise.resolve(new Response(null, { status: 204 })));
    await api.removeCrawl(CRAWL);
    expect(await keptSecrets(`crawl:${CRAWL}`)).toBeNull();
    await api.restoreCrawl(CRAWL);
    expect(await keptSecrets(`crawl:${CRAWL}`)).toEqual({ proxyUrl: 'http://me:pw@proxy:3128' });
    expect(store.kept.size).toBe(0);
  });
});
