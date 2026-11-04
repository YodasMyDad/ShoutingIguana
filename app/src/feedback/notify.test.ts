import { toast } from 'sonner';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ProblemError, SidecarUnreachableError } from '../api/errors';
import { crawlToast, dismissToast, notifyError, notifyResult } from './notify';

vi.mock('sonner', () => {
  const toast = Object.assign(vi.fn(), { error: vi.fn(), dismiss: vi.fn(), loading: vi.fn(), success: vi.fn() });
  return { toast };
});

afterEach(() => vi.clearAllMocks());

const optionsOf = (mock: ReturnType<typeof vi.fn>, call = 0) => mock.mock.calls[call]?.[1] as Record<string, unknown>;

describe('toasts', () => {
  it('show the same failure once, however often it happens', () => {
    const error = new SidecarUnreachableError(new TypeError('x'));
    notifyError(error, { title: 'example.com: the crawl couldn’t be paused' });
    notifyError(error, { title: 'example.com: the crawl couldn’t be paused' });
    const first = optionsOf(vi.mocked(toast.error), 0);
    const second = optionsOf(vi.mocked(toast.error), 1);
    expect(first.id).toBe(second.id);
    notifyError(new ProblemError({ type: 'internal', title: 'Something else.', status: 500 }));
    expect(optionsOf(vi.mocked(toast.error), 2).id).not.toBe(first.id);
  });

  it('take the id they’re given, so a crawl’s news replaces its older news', () => {
    notifyResult('Crawl stopped', { id: crawlToast('0123456789abcdef', 'stopped') });
    expect(optionsOf(vi.mocked(toast))).toMatchObject({ id: 'crawl:0123456789abcdef:stopped' });
    dismissToast('crawl:0123456789abcdef:stopped');
    expect(toast.dismiss).toHaveBeenCalledWith('crawl:0123456789abcdef:stopped');
  });

  it('offer Retry only when there is one and it could work', () => {
    const retry = vi.fn();
    notifyError(new ProblemError({ type: 'crawl-not-found', title: 'Gone.', status: 404 }), { retry });
    expect(optionsOf(vi.mocked(toast.error), 0).action).toMatchObject({ label: 'Copy details' });
    notifyError(new SidecarUnreachableError(new TypeError('x')), { retry });
    expect(optionsOf(vi.mocked(toast.error), 1).action).toMatchObject({ label: 'Retry' });
  });
});
