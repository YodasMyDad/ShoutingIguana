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
  sortChecks,
  causeWords,
  answerParts,
  relatedChecks,
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
    check('orphan-page', 'links', 'notice', 'Orphan page in a sitemap'),
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
    // An analysis that doesn't count the things to change (an earlier sidecar's) has none to add up: null, never short.
    expect(analysisTotals(checkEntries(CHECKS, ANALYSIS))).toEqual({
      found: 4,
      issues: 365,
      passed: 2,
      skipped: 2,
      causes: null,
      templateCauses: 0,
      fixCauses: null,
      fixTemplateCauses: 0,
      noticeCauses: null,
    });
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
    expect(analysisTotals(countedEntries(entries))).toEqual({
      found: 2,
      issues: 91,
      passed: 1,
      skipped: 2,
      causes: null,
      templateCauses: 0,
      fixCauses: null,
      fixTemplateCauses: 0,
      noticeCauses: null,
    });
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

  it('words one check that passed in the singular: every other one hidden', () => {
    expect(answerSentence({ found: 0, issues: 0, passed: 1, skipped: 0 })).toBe(
      'No check found an issue on this crawl. The 1 check counted passed.',
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

  it('is the engine’s (the analysis’s fixFirst) when it sends one: a site-wide fault leads its severity, however few', () => {
    // The engine ranks the warning first among warnings (a fault of the whole site), and the errors by its own rules.
    const entries = checkEntries(CHECKS, {
      ...ANALYSIS,
      counts: [...ANALYSIS.counts.filter((entry) => entry.checkId !== 'title-duplicate')].concat([
        { checkId: 'title-duplicate', count: 1 },
      ]),
    });
    const order = [
      'broken-internal-links',
      'internal-client-error',
      'title-duplicate',
      'empty-anchor-text',
      'title-too-long',
    ];
    expect(ids(rankChecksToFix(entries, order))).toEqual(order);
    expect(firstCheckToFix(entries, order)?.check.id).toBe('broken-internal-links');
    // Never sorted again by count: by count the warning of 1 would still lead its severity here, but the errors
    // would swap.
    expect(ids(rankChecksToFix(entries)).slice(0, 2)).toEqual(['internal-client-error', 'broken-internal-links']);
  });

  it('leaves hidden checks out of the engine’s order too, and puts a check it doesn’t rank after the rest', () => {
    const hidden = new Set(['broken-internal-links']);
    const entries = checkEntries(CHECKS, ANALYSIS, hidden);
    expect(ids(rankChecksToFix(entries, ['title-too-long', 'broken-internal-links', 'internal-client-error']))).toEqual(
      [
        'title-too-long',
        'internal-client-error',
        // Not in the order the engine sent (a newer catalog's check, say): after it, by the app's own rule.
        'empty-anchor-text',
      ],
    );
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

  it('finds a check by a word of what it says is wrong, or of its id, as well as its title (A-11: “404”)', () => {
    const checks: Checks = {
      ...CHECKS,
      checks: CHECKS.checks.map((entry) =>
        entry.id === 'internal-client-error'
          ? { ...entry, description: 'This internal URL answered with a 4xx client error such as 404 Not Found.' }
          : entry,
      ),
    };
    const found = (q: string) => ids(buildCheckList(checks, checkEntries(checks, ANALYSIS), { ...FILTERS, q }).visible);
    // "404" is in no title: the check's own description says it.
    expect(found('404')).toEqual(['internal-client-error']);
    expect(found('not found')).toEqual(['internal-client-error']);
    // A check's id, as the address and an export name it, with its hyphens or as words.
    expect(found('orphan-page')).toEqual(['orphan-page']);
    expect(found('client error')).toEqual(['internal-client-error']);
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

/** The analysis as the step 3b sidecar gives it: each check's things to change, and what a fix of it clears. */
const COUNTED: Analysis = {
  ...ANALYSIS,
  counts: [
    {
      checkId: 'internal-client-error',
      count: 9,
      causes: 9,
      templateCauses: 0,
      clears: [{ checkId: 'broken-internal-links', pages: 5, all: true }],
    },
    { checkId: 'internal-server-error', count: 0, causes: null, templateCauses: null, clears: [] },
    { checkId: 'broken-internal-links', count: 5, causes: 3, templateCauses: 0, clears: [] },
    { checkId: 'empty-anchor-text', count: 265, causes: 2, templateCauses: 1, clears: [] },
    { checkId: 'title-too-long', count: 86, causes: 86, templateCauses: 0, clears: [] },
    { checkId: 'title-duplicate', count: 0, causes: null, templateCauses: null, clears: [] },
  ],
};

describe('things to change', () => {
  it('come with each check that found issues, with what a fix of it clears', () => {
    const byId = new Map(checkEntries(CHECKS, COUNTED).map((entry) => [entry.check.id, entry]));
    expect(byId.get('internal-client-error')).toMatchObject({
      causes: 9,
      templateCauses: 0,
      clears: [{ checkId: 'broken-internal-links', pages: 5, all: true }],
    });
    // A check that passed or was skipped has none, whatever the analysis says.
    expect(byId.get('internal-server-error')).toMatchObject({ causes: null, templateCauses: null, clears: [] });
    expect(byId.get('orphan-page')).toMatchObject({ causes: null, templateCauses: null, clears: [] });
    // An earlier sidecar's analysis says nothing of them.
    expect(checkEntries(CHECKS, ANALYSIS)[0]).toMatchObject({ causes: null, templateCauses: null, clears: [] });
  });

  it('add up for the answer, with those in the template, a check another’s fix clears entirely being that job', () => {
    // 9 + 2 + 86: the links to the 404s are cleared by fixing the 404s, so their 3 aren't counted again.
    expect(analysisTotals(checkEntries(CHECKS, COUNTED))).toEqual({
      found: 4,
      issues: 365,
      passed: 2,
      skipped: 2,
      causes: 97,
      templateCauses: 1,
      // What moves the score: the errors' and warnings' alone, the links again the 404s' job; then the notices.
      fixCauses: 9,
      fixTemplateCauses: 0,
      noticeCauses: 88,
    });
    // With the 404s hidden, the links are a job of their own again.
    const hidden = checkEntries(CHECKS, COUNTED, new Set(['internal-client-error']));
    expect(analysisTotals(countedEntries(hidden)).causes).toBe(91);
  });

  it('lead the answer with what moves the score, then the notices’ things in the same unit, then the counts', () => {
    const totals = { found: 21, issues: 1_124, passed: 112, skipped: 4, causes: 852, templateCauses: 3 };
    // Things, then more things: the units agree; the checks and issues are named as such.
    expect(answerSentence({ ...totals, fixCauses: 77, fixTemplateCauses: 3, noticeCauses: 775 })).toBe(
      'About 77 things to fix, 3 of them in the site’s template · about 775 more worth a look · 21 checks found 1,124 issues on this crawl. 112 passed and 4 were skipped.',
    );
    expect(answerParts({ ...totals, fixCauses: 1, fixTemplateCauses: 0, noticeCauses: 1 })).toEqual({
      fix: '1 thing to fix',
      notices: '1 more worth a look',
      counts: '21 checks found 1,124 issues on this crawl. 112 passed and 4 were skipped.',
    });
    // Only notices: nothing to fix, and their things follow, named.
    expect(
      answerSentence({ ...totals, found: 2, issues: 14, fixCauses: 0, fixTemplateCauses: 0, noticeCauses: 9 }),
    ).toBe(
      'Nothing to fix · about 9 things worth a look · 2 checks found 14 issues on this crawl. 112 passed and 4 were skipped.',
    );
    // No notices: none said.
    expect(answerParts({ ...totals, fixCauses: 5, fixTemplateCauses: 0, noticeCauses: 0 }).notices).toBeNull();
    // Nothing found, or an earlier sidecar's analysis: the counts alone, as before.
    expect(answerSentence({ found: 0, issues: 0, passed: 82, skipped: 0, fixCauses: 0, noticeCauses: 0 })).toBe(
      answerSentence({ found: 0, issues: 0, passed: 82, skipped: 0 }),
    );
    expect(answerSentence({ ...totals, fixCauses: null })).toBe(
      '21 checks found 1,124 issues on this crawl. 112 passed and 4 were skipped.',
    );
    // Only notices found something, on an analysis that counts no things: not "nothing to fix", the counts alone.
    expect(answerSentence({ ...totals, causes: null, fixCauses: 0, noticeCauses: null })).toBe(
      '21 checks found 1,124 issues on this crawl. 112 passed and 4 were skipped.',
    );
  });

  it('are worded with "about" for more than one, and "all" when every one is in the template', () => {
    expect(causeWords(1, 0)).toBe('1 thing to change');
    expect(causeWords(12, 0)).toBe('about 12 things to change');
    expect(causeWords(1_204, 2)).toBe('about 1,204 things to change, 2 of them in the site’s template');
    expect(causeWords(2, 2)).toBe('about 2 things to change, all of them in the site’s template');
    expect(causeWords(1, 1)).toBe('1 thing to change, in the site’s template');
    expect(causeWords(38, 4, 'fix')).toBe('about 38 things to fix, 4 of them in the site’s template');
  });
});

describe('the list sorted', () => {
  // The engine's order puts broken-internal-links first, though it flagged fewer URLs than the 4xx: a site-wide
  // reason, say; the app takes the engine's order as it is.
  const FIX_FIRST = ['broken-internal-links', 'internal-client-error', 'empty-anchor-text', 'title-too-long'];
  const POINTS = new Map([
    ['internal-client-error', 2],
    ['broken-internal-links', 4],
  ]);
  const entries = checkEntries(CHECKS, COUNTED);
  const SHOWN: CheckListFilters = { ...FILTERS, showPassed: true };

  it('by fix first takes the engine’s order, then lists the skipped and the passed checks', () => {
    const list = buildCheckList(CHECKS, entries, SHOWN, { sort: 'fixFirst', fixFirst: FIX_FIRST });
    expect(list.categories.map((part) => [part.title, ids(part.entries)])).toEqual([
      ['Found issues', ['broken-internal-links', 'internal-client-error', 'empty-anchor-text', 'title-too-long']],
      ['Skipped', ['orphan-page', 'future-check']],
      ['Passed', ['internal-server-error', 'title-duplicate']],
    ]);
    expect(ids(list.visible)).toEqual(list.categories.flatMap((part) => ids(part.entries)));
  });

  it('without the engine’s order, by the app’s: the most severe, then the most URLs', () => {
    expect(ids(sortChecks(entries, { sort: 'fixFirst' })).slice(0, 4)).toEqual([
      'internal-client-error',
      'broken-internal-links',
      'empty-anchor-text',
      'title-too-long',
    ]);
  });

  it('by count, by points and by severity', () => {
    const found = (sort: 'count' | 'points' | 'severity') =>
      ids(
        buildCheckList(CHECKS, entries, FILTERS, { sort, fixFirst: FIX_FIRST, points: POINTS }).categories[0]!.entries,
      );
    expect(found('count')).toEqual([
      'empty-anchor-text',
      'title-too-long',
      'internal-client-error',
      'broken-internal-links',
    ]);
    // Notices take no points: they come after, in fix-first order.
    expect(found('points')).toEqual([
      'broken-internal-links',
      'internal-client-error',
      'empty-anchor-text',
      'title-too-long',
    ]);
    const bySeverity = buildCheckList(CHECKS, entries, FILTERS, { sort: 'severity', fixFirst: FIX_FIRST });
    expect(bySeverity.categories.map((part) => [part.title, ids(part.entries)])).toEqual([
      ['Errors', ['broken-internal-links', 'internal-client-error']],
      ['Notices', ['empty-anchor-text', 'title-too-long']],
      ['Skipped', ['orphan-page', 'future-check']],
    ]);
  });

  it('by category is the list by category in catalog order, as before', () => {
    const list = buildCheckList(CHECKS, entries, FILTERS, { sort: 'category', fixFirst: FIX_FIRST });
    expect(list.categories.map((category) => category.title)).toEqual([
      'Response codes',
      'Links',
      'Titles',
      'Other checks',
    ]);
    expect(buildCheckList(CHECKS, entries, FILTERS)).toEqual(list);
  });

  it('keeps the filters: a severity, the text, and hidden checks out unless shown', () => {
    const hidden = new Set(['internal-client-error']);
    const withHidden = checkEntries(CHECKS, COUNTED, hidden);
    const errors = buildCheckList(
      CHECKS,
      withHidden,
      { ...FILTERS, severity: ['error'], hidden },
      { sort: 'fixFirst', fixFirst: FIX_FIRST },
    );
    expect(ids(errors.visible)).toEqual(['broken-internal-links']);
    expect(errors.hiddenCount).toBe(1);
    const shown = buildCheckList(
      CHECKS,
      withHidden,
      { ...FILTERS, severity: ['error'], hidden, showHidden: true },
      { sort: 'fixFirst', fixFirst: FIX_FIRST },
    );
    expect(ids(shown.visible)).toEqual(['broken-internal-links', 'internal-client-error']);
    const text = buildCheckList(CHECKS, entries, { ...FILTERS, q: 'title' }, { sort: 'count' });
    expect(ids(text.visible)).toEqual(['title-too-long']);
  });
});

describe('related checks', () => {
  const entries = checkEntries(CHECKS, COUNTED);
  const entry = (id: string) => entries.find((candidate) => candidate.check.id === id)!;
  const POINTS = new Map([['broken-internal-links', 4]]);

  it('says what a fix of a check also clears, with the points when it clears all of it', () => {
    const related = relatedChecks(entries, entry('internal-client-error'), POINTS);
    expect(
      related.clears.map((cleared) => [cleared.entry.check.id, cleared.pages, cleared.all, cleared.points]),
    ).toEqual([['broken-internal-links', 5, true, 4]]);
    expect(related.clearedBy).toEqual([]);
  });

  it('says which fix clears a check’s pages, from the other side', () => {
    const related = relatedChecks(entries, entry('broken-internal-links'), POINTS);
    expect(related.clears).toEqual([]);
    expect(related.clearedBy.map((by) => [by.entry.check.id, by.pages, by.all])).toEqual([
      ['internal-client-error', 5, true],
    ]);
  });

  it('claims no points for a part, and leaves out a check this catalog doesn’t have', () => {
    const partial = checkEntries(CHECKS, {
      ...COUNTED,
      counts: COUNTED.counts.map((count) =>
        count.checkId === 'internal-client-error'
          ? {
              ...count,
              clears: [
                { checkId: 'broken-internal-links', pages: 3, all: false },
                { checkId: 'no-such-check', pages: 2, all: true },
              ],
            }
          : count,
      ),
    });
    const related = relatedChecks(partial, partial[0]!, POINTS);
    expect(related.clears.map((cleared) => [cleared.entry.check.id, cleared.pages, cleared.points])).toEqual([
      ['broken-internal-links', 3, null],
    ]);
  });
});
