import { afterEach, describe, expect, it, vi } from 'vitest';
import { setTransport } from '../api/transport';
import type * as NotifyModule from '../feedback/notify';
import { notifyError } from '../feedback/notify';
import { fakeSidecar, problem } from '../test/fakeSidecar';
import { analyseCrawl, pauseOrResume, stopCrawl } from './actions';

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

afterEach(() => vi.mocked(notifyError).mockClear());

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
