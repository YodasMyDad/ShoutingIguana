import { afterEach, describe, expect, it, vi } from 'vitest';
import { api } from '../api/client';
import { setTransport } from '../api/transport';
import type { CrawlOptions } from '../api/types';
import type * as NotifyModule from '../feedback/notify';
import { notifyError, notifyResult } from '../feedback/notify';
import { getMode } from '../mode/mode';
import { updateSettings } from '../settings/settingsStore';
import { fakeSidecar, json, problem, testCrawlItem } from '../test/fakeSidecar';
import { analyseCrawl, continueCrawl, pauseOrResume, saidWhileAnalysing, startCrawl, stopCrawl } from './actions';
import { setSecretsStore } from './secrets';

vi.mock('../feedback/notify', async (importOriginal) => ({
  ...(await importOriginal<typeof NotifyModule>()),
  notifyError: vi.fn(),
  notifyResult: vi.fn(),
}));

const ID = '0123456789abcdef';

const busy = () =>
  setTransport(
    fakeSidecar([
      ['POST', /.*/, () => problem({ type: 'crawl-busy', title: 'This crawl isn’t running here.', status: 409 })],
    ]),
  );

const unreachable = () => setTransport({ kind: 'mock', send: () => Promise.reject(new TypeError('Failed to fetch')) });

afterEach(() => {
  vi.mocked(notifyError).mockClear();
  vi.mocked(notifyResult).mockClear();
});

describe('the crawl actions’ failures', () => {
  it('offer no Retry for a pause the sidecar can’t do: the crawl isn’t running here', async () => {
    busy();
    await pauseOrResume(ID, 'running');
    const options = vi.mocked(notifyError).mock.calls[0]?.[1];
    expect(options).toEqual({ id: `crawl:${ID}:pause` });
  });

  it('offer no Retry for such a stop either', async () => {
    busy();
    await stopCrawl(ID);
    expect(vi.mocked(notifyError).mock.calls[0]?.[1]).toEqual({ id: `crawl:${ID}:stop` });
  });

  it('offer Retry when the sidecar couldn’t be reached, which can change', async () => {
    unreachable();
    await pauseOrResume(ID, 'paused');
    expect(vi.mocked(notifyError).mock.calls[0]?.[1]).toMatchObject({
      id: `crawl:${ID}:pause`,
      retry: expect.any(Function) as unknown,
    });
  });

  it('offer Retry for an analysis the file was too busy for: that ends', async () => {
    busy();
    await analyseCrawl(ID);
    expect(vi.mocked(notifyError).mock.calls[0]?.[1]).toMatchObject({
      id: `crawl:${ID}:analyse`,
      retry: expect.any(Function) as unknown,
    });
  });
});

describe('a quick start of a Shopify store whose signature is remembered (lane shopify, phase B)', () => {
  afterEach(() => setSecretsStore(null));

  it('sends the signature remembered for its host, and none to any other site', async () => {
    const signature = {
      signatureInput:
        'sig1=("@authority" "signature-agent");created=1767225600;expires=4102444800;keyid="fake-test-key"',
      signature: 'sig1=:FAKETESTONLYAAAA:',
    };
    const kept = new Map([['shopify:shop.example', JSON.stringify(signature)]]);
    setSecretsStore({ read: (key) => Promise.resolve(kept.get(key) ?? null), write: () => Promise.resolve() });
    const sidecar = fakeSidecar([
      ['GET', /^\/api\/crawl-options\/defaults$/, () => json({ options: { maxUrls: 500 }, speedPresets: [] })],
      ['POST', /^\/api\/crawls$/, () => json(testCrawlItem())],
    ]);
    setTransport(sidecar);

    await startCrawl('https://shop.example/collections/all');
    await startCrawl('https://www.shop.example/');

    const sent = sidecar.requests
      .filter((request) => request.method === 'POST')
      .map((request) => (request.body as { options: CrawlOptions }).options);
    expect(sent[0]?.signatures).toEqual({ 'shop.example': signature });
    expect(sent[0]?.maxUrls).toBe(500);
    // www and the bare domain each have a signature of their own.
    expect(sent[1]?.signatures ?? null).toBeNull();
  });
});

describe('a quick start asks a person when a site needs one, as the full form does', () => {
  const sentFor = async (options: Partial<CrawlOptions>) => {
    const sidecar = fakeSidecar([
      ['GET', /^\/api\/crawl-options\/defaults$/, () => json({ options, speedPresets: [] })],
      ['POST', /^\/api\/crawls$/, () => json(testCrawlItem())],
    ]);
    setTransport(sidecar);
    await startCrawl('https://example.com/');
    const post = sidecar.requests.find((request) => request.method === 'POST');
    return (post?.body as { options: CrawlOptions }).options;
  };

  it('sends solveChallenges with the browser fallback on, though the engine keeps it off for the command line', async () => {
    expect((await sentFor({ browserFallback: true, solveChallenges: false })).solveChallenges).toBe(true);
  });

  it('asks nobody when the browser fallback is off', async () => {
    expect((await sentFor({ browserFallback: false, solveChallenges: false })).solveChallenges).toBe(false);
  });

  it('follows the app’s default, on, when the saved defaults leave the fallback out', async () => {
    expect((await sentFor({})).solveChallenges).toBe(true);
  });
});

describe('round 5: a crawl analysed since it was stopped refuses Continue, Remove and Delete as busy', () => {
  const refused = () =>
    problem({
      type: 'crawl-busy',
      title: 'This crawl is being analysed. Wait for that to finish, or cancel it.',
      status: 409,
    });
  const busyAnalysing = (status: 'analyzing' | 'stopped') => {
    const sidecar = fakeSidecar([
      [
        'GET',
        new RegExp(`^/api/crawls/${ID}$`),
        () => json(testCrawlItem({ id: ID, site: '127.0.0.1', startUrl: 'http://127.0.0.1:8080/', status })),
      ],
      ['POST', /.*/, refused],
      ['DELETE', /.*/, refused],
    ]);
    setTransport(sidecar);
    return sidecar;
  };

  it('says so in Advanced’s words with Cancel the analysis, never the bare refusal', async () => {
    const sidecar = busyAnalysing('analyzing');
    expect(await continueCrawl(ID)).toBe(false);
    expect(notifyError).not.toHaveBeenCalled();
    const [title, options] = vi.mocked(notifyResult).mock.calls[0] ?? [];
    expect(title).toBe('127.0.0.1:8080 is being analysed.');
    expect(options).toMatchObject({
      id: `crawl:${ID}:continue`,
      description: 'It can be continued once the analysis ends. Wait for it, or cancel the analysis.',
      action: { label: 'Cancel the analysis' },
    });
    options?.action?.onClick();
    await vi.waitFor(() =>
      expect(sidecar.requests.map((request) => request.path)).toContain(`/api/crawls/${ID}/analyze/cancel`),
    );
  });

  it('says so in Simple’s words, for a removal and a deletion too', async () => {
    const before = getMode();
    updateSettings({ mode: 'simple' });
    try {
      busyAnalysing('analyzing');
      const error = await api.removeCrawl(ID).catch((failure: unknown) => failure);
      expect(await saidWhileAnalysing(ID, 'remove', error)).toBe(true);
      expect(vi.mocked(notifyResult).mock.calls[0]).toEqual([
        'The results for 127.0.0.1:8080 are being worked out.',
        expect.objectContaining({
          description: 'You can take it off Your sites once they’re ready. Wait for them, or stop working them out.',
          action: expect.objectContaining({ label: 'Stop working them out' }) as unknown,
        }),
      ]);
      expect(await saidWhileAnalysing(ID, 'delete', error)).toBe(true);
    } finally {
      updateSettings({ mode: before });
    }
  });

  it('leaves any other busy, a crawl another program has, to the usual failure with Retry', async () => {
    busyAnalysing('stopped');
    expect(await continueCrawl(ID)).toBe(false);
    expect(notifyResult).not.toHaveBeenCalled();
    expect(vi.mocked(notifyError).mock.calls[0]?.[1]).toMatchObject({ id: `crawl:${ID}:continue` });
  });
});

describe('continuing a crawl whose file doesn’t keep what it needs', () => {
  const needs = (extra: { missingHeaders: string[]; proxyCredentials: boolean }) =>
    setTransport(
      fakeSidecar([
        [
          'POST',
          /\/continue$/,
          () => problem({ type: 'needs-secrets', title: 'Some settings must be given again.', status: 422, ...extra }),
        ],
      ]),
    );
  const said = () => (vi.mocked(notifyResult).mock.calls[0]?.[1] as { description?: string } | undefined)?.description;

  // The audit of 2026-11-24: a crawl that signed in was told its proxy password was missing, with no proxy at all.
  it('says it is the sign-in when neither a header nor the proxy is what’s missing', async () => {
    needs({ missingHeaders: [], proxyCredentials: false });
    expect(await continueCrawl(ID)).toBe(false);
    expect(said()).toMatch(/^Its passwords and cookie values aren’t kept in the crawl file/);
    expect(said()).not.toMatch(/proxy/);
  });

  it('still says it is the proxy’s password when it is', async () => {
    needs({ missingHeaders: [], proxyCredentials: true });
    await continueCrawl(ID);
    expect(said()).toMatch(/^Its proxy password isn’t kept/);
  });
});
