// The checks list and the fix loop (docs/v2-fix-loop.md, Advanced, "Issues, the checks list"): the Change sort, the
// New, Fixed, Fewer, More and Changed filters, and the checks fixed since then listed apart from those that passed.
import { describe, expect, it } from 'vitest';
import type { CheckChange } from '../../api/fixLoop';
import type { Analysis, Check, Checks } from '../../api/types';
import { buildCheckList, checkEntries, sortChecks, type CheckListFilters } from './checkList';

const check = (id: string, category: string, severity: Check['severity']): Check => ({
  id,
  category,
  severity,
  title: id,
  description: `${id} is wrong.`,
  why: 'It matters.',
  fix: 'Fix it.',
  learnMore: null,
  grouping: 'detail',
});

const CHECKS: Checks = {
  categories: [
    { id: 'responseCodes', title: 'Response codes' },
    { id: 'titles', title: 'Titles' },
  ],
  checks: [
    check('internal-client-error', 'responseCodes', 'error'),
    check('internal-server-error', 'responseCodes', 'error'),
    check('broken-internal-links', 'responseCodes', 'error'),
    check('title-too-long', 'titles', 'notice'),
    check('title-duplicate', 'titles', 'warning'),
    check('title-missing', 'titles', 'warning'),
  ],
};

const ANALYSIS: Analysis = {
  analyzedAt: '2026-11-17T10:21:00Z',
  engineVersion: 'test',
  pagesAnalyzed: 265,
  durationMs: 7_012,
  isPartial: false,
  outOfDate: false,
  outOfDateReasons: [],
  notes: [],
  counts: [
    { checkId: 'internal-client-error', count: 4 },
    { checkId: 'internal-server-error', count: 0 },
    { checkId: 'broken-internal-links', count: 12 },
    { checkId: 'title-too-long', count: 86 },
    { checkId: 'title-duplicate', count: 0 },
    { checkId: 'title-missing', count: 3 },
  ],
  skipped: [],
};

const change = (
  checkId: string,
  state: CheckChange['state'],
  then: number | null,
  now: number | null,
): CheckChange => ({
  checkId,
  severity: null,
  severityThen: null,
  state,
  then,
  now,
  fixed: null,
  gone: null,
  notChecked: null,
  still: null,
  new: null,
  newUrl: null,
  notComparedThen: null,
});

/** 4xx down from 9, 5xx fixed (3 then), broken links up from 2, titles too long the same, a missing title new. */
const CHANGES = new Map(
  [
    change('internal-client-error', 'fewer', 9, 4),
    change('internal-server-error', 'fixed', 3, 0),
    change('broken-internal-links', 'more', 2, 12),
    change('title-too-long', 'same', 86, 86),
    change('title-missing', 'new', 0, 3),
  ].map((entry) => [entry.checkId, entry]),
);

const FILTERS: CheckListFilters = { severity: [], q: '', showPassed: false, showHidden: false, hidden: new Set() };
const ids = (entries: readonly { check: Check }[]) => entries.map((entry) => entry.check.id);
const entries = checkEntries(CHECKS, ANALYSIS);

describe('the checks list compared with another crawl', () => {
  it('sorts by Change: the largest drop first, a growth last', () => {
    const sorted = sortChecks(entries, { sort: 'change', changes: CHANGES });
    expect(ids(sorted).slice(0, 4)).toEqual([
      'internal-client-error',
      'title-too-long',
      'title-missing',
      'broken-internal-links',
    ]);
  });

  it('lists the checks fixed since then apart, under their own heading, never with the ones that passed', () => {
    const list = buildCheckList(
      CHECKS,
      entries,
      { ...FILTERS, changes: CHANGES, fixedTitle: 'Fixed since 17 Oct' },
      { sort: 'fixFirst' },
    );
    expect(list.categories.map((part) => part.title)).toEqual(['Found issues', 'Fixed since 17 Oct']);
    expect(ids(list.categories[1]?.entries ?? [])).toEqual(['internal-server-error']);
    // Passed and not fixed: title-duplicate. The fixed one isn't counted twice.
    expect(list.passedCount).toBe(1);
  });

  it('lists a fixed check in its category, by category', () => {
    const list = buildCheckList(CHECKS, entries, { ...FILTERS, changes: CHANGES, fixedTitle: 'Fixed since 17 Oct' });
    expect(ids(list.categories[0]?.entries ?? [])).toEqual([
      'internal-client-error',
      'internal-server-error',
      'broken-internal-links',
    ]);
  });

  it('narrows to the checks that moved one way, or any way', () => {
    const only = (change: CheckListFilters['change']) =>
      ids(buildCheckList(CHECKS, entries, { ...FILTERS, changes: CHANGES, change }).visible);
    expect(only('new')).toEqual(['title-missing']);
    expect(only('fixed')).toEqual(['internal-server-error']);
    expect(only('fewer')).toEqual(['internal-client-error']);
    expect(only('more')).toEqual(['broken-internal-links']);
    expect(only('changed')).toEqual([
      'internal-client-error',
      'internal-server-error',
      'broken-internal-links',
      'title-missing',
    ]);
  });

  it('says a change filter that keeps nothing filtered to nothing', () => {
    const none = new Map([...CHANGES].filter(([, entry]) => entry.state !== 'new'));
    expect(buildCheckList(CHECKS, entries, { ...FILTERS, changes: none, change: 'new' }).filteredToNothing).toBe(true);
  });

  it('is as before with no comparison', () => {
    const list = buildCheckList(CHECKS, entries, FILTERS, { sort: 'fixFirst' });
    expect(list.categories.map((part) => part.title)).toEqual(['Found issues']);
    expect(list.passedCount).toBe(2);
  });
});
