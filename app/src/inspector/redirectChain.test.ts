import { describe, expect, it } from 'vitest';
import type { RedirectHop } from '../api/inspector';
import { testResponse, testUrlDetail } from '../test/inspectorSidecar';
import { redirectChainLayout } from './redirectChain';

const hop = (id: number, statusCode: number | null, overrides: Partial<RedirectHop> = {}): RedirectHop => ({
  id,
  url: `https://example.com/hop-${id}/`,
  statusCode,
  outcome: statusCode === null ? 'timeout' : statusCode >= 300 && statusCode < 400 ? 'redirect' : 'success',
  via: 'http',
  ...overrides,
});

const redirecting = (hops: RedirectHop[], flags: { loop?: boolean; tooLong?: boolean } = {}) => {
  const last = hops[hops.length - 1];
  if (!last) throw new Error('a chain has hops');
  return testUrlDetail({
    id: 5,
    url: 'https://example.com/old/',
    response: testResponse({ outcome: 'redirect', statusCode: 301 }),
    content: null,
    redirects: {
      hops,
      final: { id: last.id, url: last.url, statusCode: last.statusCode },
      loop: flags.loop ?? false,
      tooLong: flags.tooLong ?? false,
    },
  });
};

describe('redirectChainLayout', () => {
  it('is the URL alone when it doesn’t redirect', () => {
    const layout = redirectChainLayout(testUrlDetail());
    expect(layout.hops).toBe(0);
    expect(layout.title).toBe('Doesn’t redirect');
    expect(layout.steps).toHaveLength(1);
    expect(layout.steps[0]).toMatchObject({ id: 2, role: 'end', current: true, via: null, statusCode: 200 });
    expect(layout.resolves).toBe(true);
  });

  it('puts the URL itself first with its own status, then each hop, the last where it ends', () => {
    const layout = redirectChainLayout(redirecting([hop(6, 302), hop(7, 200)]));
    expect(layout.steps.map((step) => [step.id, step.statusCode, step.role, step.via])).toEqual([
      [5, 301, 'start', null],
      [6, 302, 'hop', 'http'],
      [7, 200, 'end', 'http'],
    ]);
    expect(layout.steps.map((step) => step.current)).toEqual([true, false, false]);
    expect(layout.title).toBe('Redirects twice');
    expect(layout.summary).toBe('It reaches a working page after 2 hops: link straight to the last one.');
    expect(layout.resolves).toBe(true);
  });

  it('says once for one hop that works', () => {
    const layout = redirectChainLayout(redirecting([hop(6, 200)]));
    expect(layout.title).toBe('Redirects once');
    expect(layout.summary).toBe('It reaches a working page in one hop.');
  });

  it('keeps how each hop was reached: a meta refresh counts as one', () => {
    const layout = redirectChainLayout(redirecting([hop(6, 200, { via: 'metaRefresh' })]));
    expect(layout.steps[1]?.via).toBe('metaRefresh');
  });

  it('calls out a loop, marking where it closes', () => {
    const layout = redirectChainLayout(
      redirecting([hop(6, 301), hop(5, 301, { url: 'https://example.com/old/' })], { loop: true }),
    );
    expect(layout.loop).toBe(true);
    expect(layout.resolves).toBe(false);
    expect(layout.summary).toBe('The redirects come back on themselves, so they never reach a page.');
    expect(layout.steps.map((step) => step.repeat)).toEqual([false, false, true]);
    expect(layout.steps[2]?.current).toBe(true);
    // Every step keeps a key of its own, the repeat included.
    expect(new Set(layout.steps.map((step) => step.key)).size).toBe(3);
  });

  it('calls out a chain longer than the crawl follows', () => {
    const hops = Array.from({ length: 20 }, (_, index) => hop(100 + index, 301));
    const layout = redirectChainLayout(redirecting(hops, { tooLong: true }));
    expect(layout.tooLong).toBe(true);
    expect(layout.title).toBe('Redirects 20 times');
    expect(layout.summary).toBe('The chain goes on past 20 hops, further than the crawl follows.');
    expect(layout.steps).toHaveLength(21);
    expect(layout.resolves).toBe(false);
  });

  it('says when the chain ends somewhere broken', () => {
    expect(redirectChainLayout(redirecting([hop(6, 404)])).summary).toBe('The chain ends at a URL that answers 404.');
    expect(redirectChainLayout(redirecting([hop(6, null)])).summary).toBe(
      'The chain ends at a URL that didn’t answer.',
    );
    expect(redirectChainLayout(redirecting([hop(6, 302)])).summary).toBe(
      'The chain stops at a redirect the crawl couldn’t follow.',
    );
    expect(redirectChainLayout(redirecting([hop(6, 404)])).resolves).toBe(false);
  });

  it('has no status for a start that was never fetched', () => {
    const layout = redirectChainLayout(testUrlDetail({ response: null }));
    expect(layout.steps[0]).toMatchObject({ statusCode: null, outcome: null });
    expect(layout.resolves).toBe(false);
  });
});
