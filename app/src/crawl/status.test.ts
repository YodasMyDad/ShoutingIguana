import { describe, expect, it } from 'vitest';
import type { CrawlStatus, ProgressEvent } from '../api/types';
import { applyEvent, type LiveState } from '../live/liveStore';
import { testCrawlItem } from '../test/fakeSidecar';
import { crawlControls, effectiveStatus } from './status';

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
    let state = applyEvent(empty, { type: 'progress', data: progress('running') });
    expect(effectiveStatus(testCrawlItem({ status: 'running' }), state.crawls[ID])).toBe('running');
    // The new sidecar's hello: nothing is live; its library says the crawl was interrupted.
    state = applyEvent(state, { type: 'hello', data: { live: [] } });
    const item = testCrawlItem({ status: 'interrupted' });
    const status = effectiveStatus(item, state.crawls[ID]);
    expect(status).toBe('interrupted');
    expect(crawlControls(status, item)).toMatchObject({ canPause: false, canStop: false, canContinue: true });
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
    const state = applyEvent(empty, { type: 'progress', data: progress('paused') });
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
