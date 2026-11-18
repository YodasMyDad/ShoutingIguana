import { describe, expect, it } from 'vitest';
import type { CheckChange, CrawlChanges, CrawlComparison, PreviousCrawl } from '../api/fixLoop';
import {
  changeCounts,
  changeSeverity,
  comparisonCounts,
  comparisonStage,
  countedChecks,
  countedItemChanges,
  hasMoved,
  marksRows,
  severitiesOf,
} from './changes';

const severityOf = severitiesOf([
  { id: 'broken-internal-links', severity: 'error' },
  { id: 'no-https', severity: 'error' },
  { id: 'sitemap-error', severity: 'warning' },
  { id: 'image-too-large', severity: 'warning' },
  { id: 'title-too-long', severity: 'notice' },
  { id: 'new-check', severity: 'warning' },
]);

const ITEM_CHANGES: CrawlChanges = {
  score: 14,
  partial: false,
  fixed: ['internal-client-error-unknown', 'no-https', 'title-too-long'],
  new: ['sitemap-error'],
  fewer: ['broken-internal-links'],
  more: ['image-too-large'],
  newCheck: ['new-check'],
  notCheckedNow: [],
};

const check = (patch: Partial<CheckChange>): CheckChange => ({
  checkId: 'broken-internal-links',
  severity: 'error',
  severityThen: 'error',
  state: 'fewer',
  then: 9,
  now: 4,
  fixed: 5,
  gone: 0,
  notChecked: 0,
  still: 4,
  new: 0,
  newUrl: 0,
  notComparedThen: 0,
  ...patch,
});

const comparison = (checks: CheckChange[], patch: Partial<CrawlComparison> = {}): CrawlComparison => ({
  crawlId: '9f2c000000000000',
  with: {
    id: '41ab000000000000',
    startedAt: '2026-11-03T09:12:00Z',
    finishedAt: '2026-11-03T09:49:25Z',
    status: 'completed',
    startUrl: 'https://www.example.com/',
    analyzedAt: '2026-11-03T09:50:00Z',
    pagesAnalyzed: 265,
    crawledUrlCount: 4_480,
    chosen: 'previous',
  },
  comparable: 'full',
  differences: [],
  previousNeedsAnalysis: [],
  score: null,
  totals: {
    checks: { fixed: 0, new: 0, fewer: 0, more: 0, same: 0, newCheck: 0, notCheckedNow: 0 },
    issues: null,
  },
  checks,
  ...patch,
});

const PREVIOUS: PreviousCrawl = {
  id: '41ab000000000000',
  startedAt: '2026-11-03T09:12:00Z',
  score: { value: 71, band: 'good', partial: false },
  comparable: 'full',
  needsAnalysis: false,
};

describe('what each mode counts of a library item’s changes', () => {
  it('counts problems only in Simple, every check in Advanced, and never a check it doesn’t know', () => {
    const simple = countedItemChanges(ITEM_CHANGES, severityOf, new Set(), 'simple');
    expect(simple.fixed).toEqual(['no-https']);
    expect(simple.new).toEqual(['sitemap-error']);
    expect(changeCounts(simple)).toEqual({ fixed: 1, new: 1, fewer: 1, more: 1, newCheck: 1, notCheckedNow: 0 });
    const advanced = countedItemChanges(ITEM_CHANGES, severityOf, new Set(), 'advanced');
    expect(advanced.fixed).toEqual(['no-https', 'title-too-long']);
  });

  it('leaves hidden checks out of every count', () => {
    const counted = countedItemChanges(ITEM_CHANGES, severityOf, new Set(['sitemap-error', 'no-https']), 'simple');
    expect(changeCounts(counted)).toEqual({ fixed: 0, new: 0, fewer: 1, more: 1, newCheck: 1, notCheckedNow: 0 });
  });
});

describe('what each mode counts of a comparison', () => {
  const checks = [
    check({ checkId: 'no-https', state: 'fixed', severity: null, severityThen: 'error', then: 1, now: 0 }),
    check({ checkId: 'sitemap-error', state: 'new', severity: 'warning', severityThen: null, then: 0, now: 1 }),
    check({ checkId: 'title-too-long', state: 'fixed', severity: 'notice', severityThen: 'notice', then: 14, now: 0 }),
    check({ checkId: 'image-too-large', state: 'same', severity: 'warning', then: 12, now: 12, fixed: 0, new: 0 }),
    check({ checkId: 'new-check', state: 'newCheck', severity: 'warning', severityThen: null, then: null, now: 3 }),
  ];

  it('takes a fixed check’s severity from then, a new one’s from now', () => {
    expect(changeSeverity(checks[0]!)).toBe('error');
    expect(changeSeverity(checks[1]!)).toBe('warning');
    expect(changeSeverity(check({ severity: null, severityThen: null }), severityOf)).toBe('error');
  });

  it('counts by state, a newCheck never as new, hidden checks out', () => {
    const simple = countedChecks(comparison(checks), new Set(), 'simple');
    expect(simple.map((entry) => entry.checkId)).toEqual(['no-https', 'sitemap-error', 'image-too-large', 'new-check']);
    expect(comparisonCounts(simple)).toEqual({ fixed: 1, new: 1, fewer: 0, more: 0, newCheck: 1, notCheckedNow: 0 });
    const advanced = countedChecks(comparison(checks), new Set(['sitemap-error']), 'advanced');
    expect(comparisonCounts(advanced)).toEqual({ fixed: 2, new: 0, fewer: 0, more: 0, newCheck: 1, notCheckedNow: 0 });
  });

  it('marks a same-count check only when its rows moved', () => {
    expect(hasMoved(check({ state: 'same', fixed: 0, gone: 0, new: 0, newUrl: 0 }))).toBe(false);
    expect(hasMoved(check({ state: 'same', fixed: 2, new: 2 }))).toBe(true);
    expect(hasMoved(check({ state: 'fewer' }))).toBe(true);
  });
});

describe('the states, at a glance', () => {
  const full = comparison([]);

  it('shows nothing for a first crawl, and nothing while this crawl is live', () => {
    expect(comparisonStage({ previous: null }, 'completed', undefined)).toEqual({ kind: 'none' });
    expect(comparisonStage({}, 'completed', undefined)).toEqual({ kind: 'none' });
    expect(comparisonStage({ previous: PREVIOUS }, 'running', full)).toEqual({ kind: 'live' });
    expect(comparisonStage({ previous: PREVIOUS }, 'rechecking', full)).toEqual({ kind: 'live' });
    expect(comparisonStage({ previous: PREVIOUS }, 'analyzing', full)).toEqual({ kind: 'live' });
  });

  it('waits for the comparison, then shows it as full, partial or counts only', () => {
    expect(comparisonStage({ previous: PREVIOUS }, 'completed', undefined)).toEqual({ kind: 'loading' });
    expect(comparisonStage({ previous: PREVIOUS }, 'completed', null)).toEqual({ kind: 'none' });
    expect(comparisonStage({ previous: PREVIOUS }, 'completed', full)).toEqual({
      kind: 'shown',
      comparison: full,
      comparable: 'full',
    });
    const partial = comparison([], { comparable: 'partial', differences: [{ code: 'stoppedEarly', side: 'now' }] });
    expect(comparisonStage({ previous: PREVIOUS }, 'stopped', partial)).toMatchObject({
      kind: 'shown',
      comparable: 'partial',
    });
    expect(marksRows('partial')).toBe(true);
    expect(marksRows('countsOnly')).toBe(false);
  });

  it('says the previous crawl needs analysing again when the two analyses differ', () => {
    const stale = comparison([], { comparable: 'countsOnly', previousNeedsAnalysis: ['engineChanged'] });
    expect(comparisonStage({ previous: PREVIOUS }, 'completed', stale)).toEqual({
      kind: 'needsAnalysis',
      previousId: '41ab000000000000',
      comparison: stale,
    });
    expect(comparisonStage({ previous: { ...PREVIOUS, needsAnalysis: true } }, 'completed', full)).toMatchObject({
      kind: 'needsAnalysis',
    });
  });
});
