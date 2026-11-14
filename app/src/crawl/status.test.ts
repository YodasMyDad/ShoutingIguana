import { describe, expect, it } from 'vitest';
import type { CrawlStatus, ProgressEvent } from '../api/types';
import { applyEvent, liveProgressOf, type LiveState } from '../live/liveStore';
import { testCrawlItem } from '../test/fakeSidecar';
import { crawlControls, crawledUrls, effectiveStatus } from './status';

const ID = '0123456789abcdef';

const progress = (status: CrawlStatus): ProgressEvent => ({
  crawlId: ID,
  status,
  elapsedMs: 1,
  discovered: 1,
  queued: 1,
  inFlight: 1,
  fetched: 1,
  htmlPages: 1,
  redirects: 0,
  errors: 0,
  blocked: 0,
  robotsDisallowed: 0,
  skipped: 0,
  bytesDownloaded: 0,
  urlsPerSecond: 1,
});

const empty: LiveState = { stream: 'open', live: [], crawls: {} };

describe('the status a crawl shows', () => {
  it('after the sidecar restarts mid-crawl, is the new sidecar’s: interrupted, with Continue', () => {
    let state = applyEvent(empty, { type: 'progress', data: liveProgressOf(progress('running')) });
    expect(effectiveStatus(testCrawlItem({ status: 'running' }), state.crawls[ID])).toBe('running');
    // The new sidecar's hello: nothing is live; its library says the crawl was interrupted.
    state = applyEvent(state, { type: 'hello', data: { live: [] } });
    const item = testCrawlItem({ status: 'interrupted' });
    const status = effectiveStatus(item, state.crawls[ID]);
    expect(status).toBe('interrupted');
    expect(crawlControls(status, item)).toMatchObject({ canPause: false, canStop: false, canContinue: true });
  });

  it('offers no Continue for a stopped or interrupted crawl whose file isn’t there any more (round 5 review R1-11)', () => {
    for (const status of ['stopped', 'interrupted'] as const) {
      expect(crawlControls(status, testCrawlItem({ status, missing: true })).canContinue).toBe(false);
      expect(crawlControls(status, testCrawlItem({ status })).canContinue).toBe(true);
    }
  });

  it('after an analysis is cut off by a restart, offers Analyse again', () => {
    let state = applyEvent(empty, { type: 'crawl', data: { crawlId: ID, status: 'analyzing' } });
    state = applyEvent(state, { type: 'hello', data: { live: [] } });
    const item = testCrawlItem({ status: 'completed' });
    const status = effectiveStatus(item, state.crawls[ID]);
    expect(status).toBe('completed');
    expect(crawlControls(status, item).canAnalyse).toBe(true);
  });

  it('is this sidecar’s while it works on the crawl, even before the library catches up', () => {
    const state = applyEvent(empty, { type: 'progress', data: liveProgressOf(progress('paused')) });
    expect(effectiveStatus(testCrawlItem({ status: 'completed' }), state.crawls[ID])).toBe('paused');
  });

  it('is the library’s once this sidecar isn’t working on it, whatever the last event said', () => {
    // The crawl finished here, then another program continued it: the library knows, the stream can't.
    const state = applyEvent(empty, { type: 'crawl', data: { crawlId: ID, status: 'completed' } });
    expect(effectiveStatus(testCrawlItem({ status: 'inUse' }), state.crawls[ID])).toBe('inUse');
    expect(effectiveStatus(undefined, state.crawls[ID])).toBe('completed');
    expect(effectiveStatus(undefined, undefined)).toBeNull();
  });
});

describe('a crawl’s "N URLs"', () => {
  // A crawl whose twin host stopped answering: 6,914 outcomes stored, 5 of them never asked.
  const progress = (extra: Partial<ProgressEvent> = {}) =>
    liveProgressOf({
      crawlId: ID,
      status: 'running',
      elapsedMs: 1_000,
      discovered: 7_054,
      queued: 0,
      inFlight: 0,
      fetched: 6_914,
      htmlPages: 6_172,
      redirects: 0,
      errors: 0,
      blocked: 0,
      robotsDisallowed: 0,
      skipped: 0,
      bytesDownloaded: 0,
      urlsPerSecond: 0,
      noResponse: { robotsDisallowed: 0, timeout: 0, networkError: 0, notChecked: 5, processingError: 0 },
      ...extra,
    });

  it('is the URLs the grid lists as crawled, at rest and live, wherever it’s shown', () => {
    expect(crawledUrls(testCrawlItem({ fetchedUrlCount: 6_914, crawledUrlCount: 6_909 }))).toBe(6_909);
    expect(crawledUrls(undefined, progress({ crawled: 6_909 }))).toBe(6_909);
    // The live count wins over the item's, which is of the file a moment ago.
    expect(crawledUrls(testCrawlItem({ crawledUrlCount: 10 }), progress({ crawled: 6_909 }))).toBe(6_909);
  });

  it('is worked out the same way from an engine that doesn’t send it', () => {
    expect(crawledUrls(testCrawlItem({ fetchedUrlCount: 6_914 }))).toBe(6_914);
    expect(crawledUrls(undefined, progress())).toBe(6_909);
    expect(crawledUrls(undefined, undefined)).toBeNull();
  });
});
