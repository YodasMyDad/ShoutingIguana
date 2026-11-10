import { describe, expect, it } from 'vitest';
import type { Analysis, Check, Checks } from '../../api/types';
import {
  analysisTotals,
  answerSentence,
  buildCheckList,
  checkEntries,
  countedEntries,
  firstCheckToFix,
  hiddenTotals,
  hiddenWords,
  isInCategory,
  rankChecksToFix,
  type CheckListFilters,
} from './checkList';

const check = (id: string, category: string, severity: Check['severity'], title = id): Check => ({
  id,
  category,
  severity,
  title,
  description: `${title} is wrong.`,
  why: 'It matters.',
  fix: 'Fix it.',
  learnMore: null,
  grouping: 'detail',
});

const CHECKS: Checks = {
  categories: [
    { id: 'responseCodes', title: 'Response codes' },
    { id: 'links', title: 'Links' },
    { id: 'titles', title: 'Titles' },
  ],
  checks: [
    check('internal-client-error', 'responseCodes', 'error', 'Internal URL returns 4xx'),
    check('internal-server-error', 'responseCodes', 'error', 'Internal URL returns 5xx'),
    check('broken-internal-links', 'links', 'error', 'Links to broken internal URLs'),
    check('empty-anchor-text', 'links', 'notice', 'Internal links without anchor text'),
    check('orphan-page', 'links', 'notice', 'Orphan page'),
    check('title-too-long', 'titles', 'notice', 'Title too long'),
    check('title-duplicate', 'titles', 'warning', 'Duplicate title'),
    check('future-check', 'aNewCategory', 'warning', 'A check from a newer engine'),
  ],
};

const ANALYSIS: Analysis = {
  analyzedAt: '2026-11-01T10:21:00Z',
  engineVersion: 'test',
  pagesAnalyzed: 265,
  durationMs: 7_012,
  isPartial: true,
  outOfDate: false,
  outOfDateReasons: [],
  notes: ['No sitemap could be read, so orphan pages weren’t checked.'],
  counts: [
    { checkId: 'internal-client-error', count: 9 },
    { checkId: 'internal-server-error', count: 0 },
    { checkId: 'broken-internal-links', count: 5 },
    { checkId: 'empty-anchor-text', count: 265 },
    { checkId: 'title-too-long', count: 86 },
    { checkId: 'title-duplicate', count: 0 },
  ],
  skipped: [{ checkId: 'orphan-page', reason: { code: 'no-sitemap', text: 'No sitemap could be read.' } }],
};

const FILTERS: CheckListFilters = { severity: [], q: '', showPassed: false, showHidden: false, hidden: new Set() };

const ids = (entries: readonly { check: Check }[]) => entries.map((entry) => entry.check.id);

describe('what each check came to', () => {
  it('reads counts as found or passed, and skipped as skipped with its reason, never 0', () => {
    const entries = checkEntries(CHECKS, ANALYSIS);
    const byId = new Map(entries.map((entry) => [entry.check.id, entry]));
    expect(byId.get('internal-client-error')).toMatchObject({ outcome: 'error', count: 9 });
    expect(byId.get('internal-server-error')).toMatchObject({ outcome: 'passed', count: 0 });
    expect(byId.get('orphan-page')).toMatchObject({
      outcome: 'skipped',
      count: null,
      skipReason: 'No sitemap could be read.',
    });
    // The analysis doesn't mention it (an engine without the check): skipped, not clean, with no reason to show.
    expect(byId.get('future-check')).toMatchObject({ outcome: 'skipped', count: null, skipReason: null });
  });

  it('counts for the answer: checks that found issues, the issues, and those that passed or were skipped', () => {
    expect(analysisTotals(checkEntries(CHECKS, ANALYSIS))).toEqual({ found: 4, issues: 365, passed: 2, skipped: 2 });
  });
});

describe('hidden checks', () => {
  it('are left out of what an answer counts, and what they came to is said apart', () => {
    // An error with 9 issues and a notice with 265, hidden; a passed check hidden too.
    const hidden = new Set(['internal-client-error', 'empty-anchor-text', 'title-duplicate']);
    const entries = checkEntries(CHECKS, ANALYSIS, hidden);
    expect(ids(countedEntries(entries))).toEqual([
      'internal-server-error',
      'broken-internal-links',
      'orphan-page',
      'title-too-long',
      'future-check',
    ]);
    expect(analysisTotals(countedEntries(entries))).toEqual({ found: 2, issues: 91, passed: 1, skipped: 2 });
    expect(hiddenTotals(entries)).toEqual({ checks: 3, found: 2, issues: 274 });
    expect(hiddenTotals(checkEntries(CHECKS, ANALYSIS))).toBeNull();
  });

  it('are said after the answer, with what they found', () => {
    const words = (totals: { checks: number; found: number; issues: number }) => {
      const { count, rest } = hiddenWords(totals);
      return `${count}${rest}`;
    };
    expect(words({ checks: 3, found: 2, issues: 274 })).toBe(
      '3 hidden checks aren’t counted: 2 of them found 274 issues.',
    );
    expect(words({ checks: 1, found: 1, issues: 30 })).toBe('1 hidden check isn’t counted: it found 30 issues.');
    expect(words({ checks: 1, found: 1, issues: 1 })).toBe('1 hidden check isn’t counted: it found 1 issue.');
    expect(words({ checks: 2, found: 0, issues: 0 })).toBe('2 hidden checks aren’t counted.');
    expect(words({ checks: 1, found: 0, issues: 0 })).toBe('1 hidden check isn’t counted.');
  });
});

describe('the answer', () => {
  it('says how many checks found how many issues, then how many passed and were skipped', () => {
    expect(answerSentence({ found: 19, issues: 933, passed: 63, skipped: 3 })).toBe(
      '19 checks found 933 issues on this crawl. 63 passed and 3 were skipped.',
    );
    expect(answerSentence({ found: 1, issues: 1, passed: 1, skipped: 1 })).toBe(
      '1 check found 1 issue on this crawl. 1 passed and 1 was skipped.',
    );
    expect(answerSentence({ found: 2, issues: 12_345, passed: 5, skipped: 0 })).toBe(
      '2 checks found 12,345 issues on this crawl. 5 passed.',
    );
  });

  it('says plainly when nothing was found', () => {
    expect(answerSentence({ found: 0, issues: 0, passed: 82, skipped: 0 })).toBe(
      'No check found an issue on this crawl. All 82 checks passed.',
    );
    expect(answerSentence({ found: 0, issues: 0, passed: 80, skipped: 2 })).toBe(
      'No check found an issue on this crawl. 80 passed and 2 were skipped.',
    );
    expect(answerSentence({ found: 0, issues: 0, passed: 0, skipped: 3 })).toBe(
      'No check found an issue on this crawl. 3 were skipped.',
    );
  });
});

describe('the first check to fix', () => {
  it('is the most severe, then the one with the most pages or URLs affected', () => {
    expect(firstCheckToFix(checkEntries(CHECKS, ANALYSIS))?.check.id).toBe('internal-client-error');
    const noErrors = checkEntries(CHECKS, {
      ...ANALYSIS,
      counts: [
        { checkId: 'empty-anchor-text', count: 265 },
        { checkId: 'title-too-long', count: 86 },
        { checkId: 'title-duplicate', count: 2 },
      ],
    });
    expect(firstCheckToFix(noErrors)?.check.id).toBe('title-duplicate');
  });

  it('passes over hidden checks, unless every check with issues is hidden', () => {
    const hidden = new Set(['internal-client-error']);
    expect(firstCheckToFix(checkEntries(CHECKS, ANALYSIS, hidden))?.check.id).toBe('broken-internal-links');
    const all = new Set(['internal-client-error', 'broken-internal-links', 'empty-anchor-text', 'title-too-long']);
    expect(firstCheckToFix(checkEntries(CHECKS, ANALYSIS, all))?.check.id).toBe('internal-client-error');
  });

  it('is nothing when no check found an issue', () => {
    expect(firstCheckToFix(checkEntries(CHECKS, { ...ANALYSIS, counts: [] }))).toBeNull();
  });
});

describe('the order to fix checks in', () => {
  it('is by severity, then the pages or URLs affected, then catalog order, and starts with the first to fix', () => {
    const entries = checkEntries(CHECKS, {
      ...ANALYSIS,
      counts: [...ANALYSIS.counts.filter((entry) => entry.checkId !== 'title-duplicate')].concat([
        { checkId: 'title-duplicate', count: 86 },
        { checkId: 'future-check', count: 86 },
      ]),
    });
    const ranked = rankChecksToFix(entries);
    expect(ids(ranked)).toEqual([
      'internal-client-error',
      'broken-internal-links',
      // Level on severity and count: catalog order.
      'title-duplicate',
      'future-check',
      'empty-anchor-text',
      'title-too-long',
    ]);
    expect(firstCheckToFix(entries)).toBe(ranked[0]);
  });

  it('leaves out passed, skipped and hidden checks', () => {
    const hidden = new Set(['broken-internal-links']);
    expect(ids(rankChecksToFix(checkEntries(CHECKS, ANALYSIS, hidden)))).toEqual([
      'internal-client-error',
      'empty-anchor-text',
      'title-too-long',
    ]);
  });
});

describe('the list', () => {
  const build = (filters: Partial<CheckListFilters> = {}, hidden: ReadonlySet<string> = new Set()) =>
    buildCheckList(CHECKS, checkEntries(CHECKS, ANALYSIS, hidden), { ...FILTERS, hidden, ...filters });

  it('groups checks by category in catalog order, with skipped ones always listed, and passed ones counted', () => {
    const list = build();
    expect(list.categories.map((category) => [category.title, category.passed, ids(category.entries)])).toEqual([
      ['Response codes', 1, ['internal-client-error']],
      ['Links', 0, ['broken-internal-links', 'empty-anchor-text', 'orphan-page']],
      ['Titles', 1, ['title-too-long']],
      // A category the catalog doesn't name is listed last, never by its id.
      ['Other checks', 0, ['future-check']],
    ]);
    expect(ids(list.visible)).toEqual([
      'internal-client-error',
      'broken-internal-links',
      'empty-anchor-text',
      'orphan-page',
      'title-too-long',
      'future-check',
    ]);
    expect(list.passedCount).toBe(2);
    expect(list.severityCounts).toEqual({ all: 4, error: 2, warning: 0, notice: 2 });
  });

  it('narrows to one category by its id, which its title as text can’t do', () => {
    // "Links" as text is also in "Links to broken internal URLs" and "Internal links without anchor text", and would
    // be in any other category's check that mentions links; the id names the category alone.
    const list = build({ category: 'titles' });
    expect(list.category).toEqual({ id: 'titles', title: 'Titles' });
    expect(list.categories.map((category) => [category.title, ids(category.entries)])).toEqual([
      ['Titles', ['title-too-long']],
    ]);
    expect(ids(list.matching)).toEqual(['title-too-long', 'title-duplicate']);
    expect(list.severityCounts).toEqual({ all: 1, error: 0, warning: 0, notice: 1 });
    expect(list.filteredToNothing).toBe(false);
    // With the other filters, as any filter.
    expect(ids(build({ category: 'links', severity: ['notice'] }).visible)).toEqual([
      'empty-anchor-text',
      'orphan-page',
    ]);
    expect(ids(build({ category: 'links', q: 'broken' }).visible)).toEqual(['broken-internal-links']);
    expect(build({ category: 'titles', severity: ['error'] }).filteredToNothing).toBe(true);
    expect(build().category).toBeNull();
  });

  it('reads a category the catalog doesn’t name as the checks listed under Other checks', () => {
    const list = build({ category: 'aNewCategory' });
    expect(list.category).toEqual({ id: 'aNewCategory', title: 'Other checks' });
    expect(ids(list.visible)).toEqual(['future-check']);
    expect(isInCategory(CHECKS, CHECKS.checks[0]!, 'responseCodes')).toBe(true);
    expect(isInCategory(CHECKS, CHECKS.checks[0]!, 'links')).toBe(false);
    expect(isInCategory(CHECKS, CHECKS.checks[0]!, 'gone')).toBe(false);
    expect(isInCategory(CHECKS, CHECKS.checks.at(-1)!, 'gone')).toBe(true);
  });

  it('adds the checks that passed when asked', () => {
    const list = build({ showPassed: true });
    expect(ids(list.categories[0]?.entries ?? [])).toEqual(['internal-client-error', 'internal-server-error']);
    expect(ids(list.categories[2]?.entries ?? [])).toEqual(['title-too-long', 'title-duplicate']);
  });

  it('filters by severity, the catalog’s, skipped checks of that severity included', () => {
    expect(ids(build({ severity: ['notice'] }).visible)).toEqual([
      'empty-anchor-text',
      'orphan-page',
      'title-too-long',
    ]);
    expect(ids(build({ severity: ['error'] }).visible)).toEqual(['internal-client-error', 'broken-internal-links']);
  });

  it('filters by words in the title or the category, ignoring case, and counts severities after it', () => {
    const list = build({ q: 'LINKS anchor' });
    expect(ids(list.visible)).toEqual(['empty-anchor-text']);
    expect(list.severityCounts).toEqual({ all: 1, error: 0, warning: 0, notice: 1 });
    expect(ids(build({ q: 'titles' }).visible)).toEqual(['title-too-long']);
  });

  it('says it was filtered to nothing only when the filters did it', () => {
    expect(build({ q: 'nothing like this' }).filteredToNothing).toBe(true);
    expect(build({ severity: ['warning'], q: 'title' }).filteredToNothing).toBe(true);
    const none = buildCheckList(CHECKS, checkEntries(CHECKS, { ...ANALYSIS, counts: [], skipped: [] }), FILTERS);
    // Every check skipped (none ran) is listed, so the list isn't empty; with only passed checks it's empty unfiltered.
    expect(none.filteredToNothing).toBe(false);
    const passed = buildCheckList(
      CHECKS,
      checkEntries(CHECKS, {
        ...ANALYSIS,
        counts: CHECKS.checks.map((entry) => ({ checkId: entry.id, count: 0 })),
        skipped: [],
      }),
      FILTERS,
    );
    expect(passed.visible).toEqual([]);
    expect(passed.filteredToNothing).toBe(false);
    expect(passed.passedCount).toBe(CHECKS.checks.length);
  });

  it('keeps every check the severity and text filters keep, listed or not, for the first to fix', () => {
    const hidden = new Set(['broken-internal-links']);
    const errors = build({ severity: ['error'] }, hidden);
    // Passed and hidden ones included: the first to fix leaves them out by its own rules.
    expect(ids(errors.matching)).toEqual(['internal-client-error', 'internal-server-error', 'broken-internal-links']);
    expect(firstCheckToFix(errors.matching)?.check.id).toBe('internal-client-error');
    expect(firstCheckToFix(build({ severity: ['notice'] }).matching)?.check.id).toBe('empty-anchor-text');
    expect(firstCheckToFix(build({ q: 'title' }).matching)?.check.id).toBe('title-too-long');
  });

  it('leaves hidden checks out, counting what the filters would show, and lists them when shown', () => {
    const hidden = new Set(['empty-anchor-text', 'internal-server-error']);
    const list = build({}, hidden);
    expect(ids(list.visible)).not.toContain('empty-anchor-text');
    expect(list.hiddenCount).toBe(1);
    expect(list.passedCount).toBe(1);
    expect(list.severityCounts.notice).toBe(1);
    expect(build({ severity: ['error'] }, hidden).hiddenCount).toBe(0);
    const shown = build({ showHidden: true }, hidden);
    expect(shown.visible.find((entry) => entry.check.id === 'empty-anchor-text')).toMatchObject({ hidden: true });
  });
});
