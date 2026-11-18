import { describe, expect, it } from 'vitest';
import type { CrawlComparison } from '../../api/fixLoop';
import type { CrawlListItem } from '../../api/types';
import { testCrawlItem } from '../../test/fakeSidecar';
import { gardenComparison } from '../../test/fixLoopSidecar';
import {
  compareCandidates,
  comparedWithWords,
  comparingOf,
  compareWithParam,
  forwardComparison,
  notComparableReason,
  periodWords,
} from './comparing';

const NOW = 'cccccccccccccccc';
const crawlOf = (id: string, startedAt: string, overrides: Partial<CrawlListItem> = {}) =>
  testCrawlItem({ id, startedAt, site: 'example.com', startUrl: 'https://example.com/', ...overrides });

const current = crawlOf(NOW, '2026-11-17T09:12:00Z', {
  previous: {
    id: 'bbbbbbbbbbbbbbbb',
    startedAt: '2026-11-03T09:12:00Z',
    score: null,
    comparable: 'full',
    needsAnalysis: false,
  },
});
const LIBRARY: CrawlListItem[] = [
  crawlOf('aaaaaaaaaaaaaaaa', '2026-10-20T09:12:00Z'),
  current,
  crawlOf('bbbbbbbbbbbbbbbb', '2026-11-03T09:12:00Z'),
  crawlOf('dddddddddddddddd', '2026-11-20T09:12:00Z'),
  crawlOf('eeeeeeeeeeeeeeee', '2026-11-10T09:12:00Z', { missing: true }),
  crawlOf('ffffffffffffffff', '2026-11-12T09:12:00Z', { status: 'interrupted' }),
  crawlOf('1111111111111111', '2026-11-13T09:12:00Z', { analyzedAt: null, issues: null }),
  testCrawlItem({ id: '2222222222222222', site: 'other.example', startUrl: 'https://other.example/' }),
];
const statusOf = (item: CrawlListItem) => item.status;

describe('Compare with…: the crawls a crawl can be compared with', () => {
  it('lists the site’s other crawls, newest first, never another site’s nor the crawl itself', () => {
    expect(compareCandidates(current, LIBRARY, statusOf).map((candidate) => candidate.item.id)).toEqual([
      'dddddddddddddddd',
      '1111111111111111',
      'ffffffffffffffff',
      'eeeeeeeeeeeeeeee',
      'bbbbbbbbbbbbbbbb',
      'aaaaaaaaaaaaaaaa',
    ]);
  });

  it('marks the default (its previous crawl) and a later crawl, which reads "until"', () => {
    const candidates = compareCandidates(current, LIBRARY, statusOf);
    const of = (id: string) => candidates.find((candidate) => candidate.item.id === id);
    expect(of('bbbbbbbbbbbbbbbb')).toMatchObject({ previous: true, later: false, reason: null });
    expect(of('dddddddddddddddd')).toMatchObject({ previous: false, later: true, reason: null });
    expect(of('aaaaaaaaaaaaaaaa')).toMatchObject({ previous: false, later: false });
  });

  it('gives each one that can’t be compared its reason', () => {
    const candidates = compareCandidates(current, LIBRARY, statusOf);
    const reasons = Object.fromEntries(candidates.map((candidate) => [candidate.item.id, candidate.reason]));
    expect(reasons).toMatchObject({
      eeeeeeeeeeeeeeee: 'File not found',
      ffffffffffffffff: 'Interrupted: continue it to compare',
      '1111111111111111': 'Not analysed',
    });
  });

  it('words every reason a crawl can’t be compared for, and none for one that can', () => {
    const item = testCrawlItem();
    expect(notComparableReason(item, 'completed')).toBeNull();
    expect(notComparableReason(item, 'stopped')).toBeNull();
    expect(notComparableReason(item, 'failed')).toBeNull();
    expect(notComparableReason(item, 'running')).toBe('Crawling now');
    expect(notComparableReason(item, 'paused')).toBe('Paused');
    expect(notComparableReason(item, 'stopping')).toBe('Stopping');
    expect(notComparableReason(item, 'analyzing')).toBe('Being analysed');
    expect(notComparableReason(item, 'rechecking')).toBe('Being rechecked');
    expect(notComparableReason(item, 'unreadable')).toBe('Can’t be read');
    expect(notComparableReason(item, 'inUse')).toBe('In use by another program');
    expect(notComparableReason(testCrawlItem({ pagesAnalyzed: 0 }), 'completed')).toBe('No page was analysed');
  });

  it('lists nothing for a crawl with no site', () => {
    expect(compareCandidates({ ...current, siteKey: null }, LIBRARY, statusOf)).toEqual([]);
  });
});

describe('what a crawl is compared with', () => {
  const candidates = compareCandidates(current, LIBRARY, statusOf);

  it('is its previous crawl by default, and nothing for a site’s first crawl', () => {
    expect(comparingOf(current, 'previous', candidates)).toEqual({
      kind: 'previous',
      id: 'bbbbbbbbbbbbbbbb',
      startedAt: '2026-11-03T09:12:00Z',
      later: false,
    });
    expect(comparingOf({ ...current, previous: null }, 'previous', candidates)).toEqual({ kind: 'none', why: 'first' });
  });

  it('is the crawl picked, when it can be compared, else the previous crawl', () => {
    expect(comparingOf(current, 'dddddddddddddddd', candidates)).toEqual({
      kind: 'picked',
      id: 'dddddddddddddddd',
      startedAt: '2026-11-20T09:12:00Z',
      later: true,
    });
    expect(comparingOf(current, 'eeeeeeeeeeeeeeee', candidates)).toMatchObject({ kind: 'previous' });
    expect(comparingOf(current, '9999999999999999', candidates)).toMatchObject({ kind: 'previous' });
  });

  it('is nothing after Don’t compare', () => {
    expect(comparingOf(current, 'none', candidates)).toEqual({ kind: 'none', why: 'off' });
  });

  it('asks the sidecar for the previous crawl, or the one picked, by id', () => {
    expect(compareWithParam(comparingOf(current, 'previous', candidates))).toBe('previous');
    expect(compareWithParam(comparingOf(current, 'dddddddddddddddd', candidates))).toBe('dddddddddddddddd');
    expect(compareWithParam({ kind: 'none', why: 'off' })).toBeNull();
  });

  it('says what it is compared with: the control’s words', () => {
    expect(comparedWithWords(comparingOf(current, 'previous', candidates))).toMatch(/^Compared with 3 Nov, \d\d:\d\d$/);
    expect(comparedWithWords({ kind: 'none', why: 'off' })).toBe('Not compared');
    expect(comparedWithWords({ kind: 'none', why: 'first' })).toBe('Compare with…');
  });

  it('says the period: since an earlier crawl, until a later one', () => {
    expect(periodWords('2026-11-03T09:12:00Z', false)).toMatch(/^Since 3 Nov, \d\d:\d\d$/);
    expect(periodWords('2026-11-20T09:12:00Z', true)).toMatch(/^Until 20 Nov, \d\d:\d\d$/);
  });
});

describe('a comparison with a later crawl, read forward from this crawl to that one', () => {
  const since: CrawlComparison = gardenComparison();
  const until = forwardComparison(since);

  it('turns the score round: from this crawl’s to the later one’s', () => {
    expect(since.score).not.toBeNull();
    expect(until.score?.then).toEqual(since.score?.now);
    expect(until.score?.now).toEqual(since.score?.then);
    expect(until.score?.change).toBe(-(since.score?.change ?? 0));
    expect(until.score?.lines[0]).toEqual({
      checkId: since.score?.lines[0]?.checkId,
      then: since.score?.lines[0]?.now,
      now: since.score?.lines[0]?.then,
    });
  });

  it('turns each check round: fixed is new, fewer is more, a new check is one not checked now', () => {
    const states = {
      new: 'fixed',
      fixed: 'new',
      fewer: 'more',
      more: 'fewer',
      same: 'same',
      newCheck: 'notCheckedNow',
      notCheckedNow: 'newCheck',
    };
    since.checks.forEach((check, index) => {
      const turned = until.checks[index];
      expect(turned?.checkId).toBe(check.checkId);
      expect(turned?.state).toBe(states[check.state]);
      expect([turned?.then, turned?.now]).toEqual([check.now, check.then]);
      expect([turned?.severity, turned?.severityThen]).toEqual([check.severityThen, check.severity]);
      expect([turned?.fixed, turned?.gone, turned?.notChecked, turned?.still]).toEqual([
        check.new,
        check.newUrl,
        check.notComparedThen,
        check.still,
      ]);
      expect([turned?.new, turned?.newUrl, turned?.notComparedThen]).toEqual([
        check.fixed,
        check.gone,
        check.notChecked,
      ]);
    });
  });

  it('turns the totals and the differences round', () => {
    expect(until.totals.checks.fixed).toBe(since.totals.checks.new);
    expect(until.totals.checks.new).toBe(since.totals.checks.fixed);
    expect(until.totals.checks.fewer).toBe(since.totals.checks.more);
    expect(until.totals.issues?.fixed).toBe(since.totals.issues?.new);
    const stopped = forwardComparison({
      ...since,
      differences: [
        { code: 'stoppedEarly', side: 'now' },
        { code: 'limits', field: 'maxUrls', then: null, now: 5_000 },
      ],
    });
    expect(stopped.differences).toEqual([
      { code: 'stoppedEarly', side: 'then' },
      { code: 'limits', field: 'maxUrls', then: 5_000, now: null },
    ]);
  });
});
