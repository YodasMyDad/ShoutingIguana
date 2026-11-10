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

/** As a crawl that's done says it: only a URL the crawl hadn't reached reads differently while it runs. */
const layoutOf = (subject: Parameters<typeof redirectChainLayout>[0]) => redirectChainLayout(subject, false);

describe('redirectChainLayout', () => {
  it('is the URL alone when it doesn’t redirect', () => {
    const layout = layoutOf(testUrlDetail());
    expect(layout.hops).toBe(0);
    expect(layout.title).toBe('Doesn’t redirect');
    expect(layout.steps).toHaveLength(1);
    expect(layout.steps[0]).toMatchObject({ id: 2, role: 'end', current: true, via: null, statusCode: 200 });
    expect(layout.resolves).toBe(true);
  });

  it('puts the URL itself first with its own status, then each hop, the last where it ends', () => {
    const layout = layoutOf(redirecting([hop(6, 302), hop(7, 200)]));
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
    const layout = layoutOf(redirecting([hop(6, 200)]));
    expect(layout.title).toBe('Redirects once');
    expect(layout.summary).toBe('It reaches a working page in one hop.');
  });

  it('keeps how each hop was reached: a meta refresh counts as one', () => {
    const layout = layoutOf(redirecting([hop(6, 200, { via: 'metaRefresh' })]));
    expect(layout.steps[1]?.via).toBe('metaRefresh');
  });

  it('calls out a loop, marking where it closes', () => {
    const layout = layoutOf(
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
    const layout = layoutOf(redirecting(hops, { tooLong: true }));
    expect(layout.tooLong).toBe(true);
    expect(layout.title).toBe('Redirects 20 times');
    expect(layout.summary).toBe('The chain goes on past 20 hops, further than the crawl follows.');
    expect(layout.steps).toHaveLength(21);
    expect(layout.resolves).toBe(false);
  });

  it('says when the chain ends somewhere broken', () => {
    expect(layoutOf(redirecting([hop(6, 404)])).summary).toBe('The chain ends at a URL that answers 404.');
    expect(layoutOf(redirecting([hop(6, null)])).summary).toBe('The chain ends at a URL that didn’t answer.');
    expect(layoutOf(redirecting([hop(6, 302)])).summary).toBe(
      'The chain stops at a redirect the crawl couldn’t follow.',
    );
    expect(layoutOf(redirecting([hop(6, 404)])).resolves).toBe(false);
  });

  it('has no status for a start that was never fetched', () => {
    const layout = layoutOf(testUrlDetail({ response: null }));
    expect(layout.steps[0]).toMatchObject({ statusCode: null, outcome: null, noCode: 'Not fetched' });
    expect(layout.resolves).toBe(false);
  });

  describe('a URL that never answered', () => {
    const discovery = testUrlDetail().discovery;
    const unanswered = (overrides: Parameters<typeof testUrlDetail>[0]) =>
      testUrlDetail({ content: null, headers: [], ...overrides });

    it('isn’t said to answer for itself when the crawl never requested it: it says why', () => {
      const layout = layoutOf(
        unanswered({ response: null, discovery: { ...discovery, state: 'skipped', notFetchedBecause: 'noFollow' } }),
      );
      expect(layout.title).toBe('Not fetched');
      expect(layout.summary).toBe(
        'Every link to it is nofollow, and the crawl doesn’t follow nofollow links. So the crawl doesn’t know whether it redirects.',
      );
      expect(layout.hops).toBe(0);
    });

    it('says robots.txt blocked it, or that it couldn’t be reached', () => {
      const blocked = layoutOf(
        unanswered({ response: testResponse({ outcome: 'robotsDisallowed', statusCode: null }) }),
      );
      expect(blocked.title).toBe('Blocked by robots.txt');
      expect(blocked.summary).toBe(
        'robots.txt disallows it, so the crawl didn’t request it. So the crawl doesn’t know whether it redirects.',
      );
      expect(blocked.steps[0]?.noCode).toBe('Blocked by robots.txt');
      const failed = layoutOf(unanswered({ response: testResponse({ outcome: 'networkError', statusCode: null }) }));
      expect(failed.title).toBe('Couldn’t be reached');
      expect(failed.summary).toMatch(/^The connection failed before it answered\. So the crawl doesn’t know/);
    });

    it('says a queued URL is on its way while the crawl runs, and was never reached once it stopped', () => {
      const queued = unanswered({ response: null, discovery: { ...discovery, state: 'queued' } });
      expect(redirectChainLayout(queued, true).title).toBe('Not fetched yet');
      expect(redirectChainLayout(queued, false).summary).toBe(
        'The crawl stopped before it reached this URL. So the crawl doesn’t know whether it redirects.',
      );
    });

    it('still says a URL that answered with an error doesn’t redirect', () => {
      const layout = layoutOf(unanswered({ response: testResponse({ outcome: 'clientError', statusCode: 404 }) }));
      expect(layout.title).toBe('Doesn’t redirect');
      expect(layout.summary).toBe('This URL answers for itself: nothing sends it on.');
    });
  });

  it('says of a chain’s end without a status only what its outcome says', () => {
    const end = (outcome: RedirectHop['outcome']) => layoutOf(redirecting([hop(6, null, { outcome })]));
    expect(end('robotsDisallowed').summary).toBe(
      'The chain ends at a URL robots.txt disallows, so the crawl didn’t request it.',
    );
    expect(end('robotsDisallowed').steps[1]?.noCode).toBe('Blocked by robots.txt');
    expect(end('networkError').summary).toBe('The chain ends at a URL that didn’t answer.');
    // No outcome either: whether it was never fetched isn't said with a hop.
    expect(end(null).summary).toBe('The chain ends at a URL the crawl has no answer from.');
    expect(end(null).steps[1]?.noCode).toBe('No status');
  });
});
