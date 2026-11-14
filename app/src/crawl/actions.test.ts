import { afterEach, describe, expect, it, vi } from 'vitest';
import { api } from '../api/client';
import { setTransport } from '../api/transport';
import type * as NotifyModule from '../feedback/notify';
import { notifyError, notifyResult } from '../feedback/notify';
import { getMode } from '../mode/mode';
import { updateSettings } from '../settings/settingsStore';
import { fakeSidecar, json, problem, testCrawlItem } from '../test/fakeSidecar';
import { analyseCrawl, continueCrawl, pauseOrResume, saidWhileAnalysing, stopCrawl } from './actions';

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
