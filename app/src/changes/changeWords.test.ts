import { describe, expect, it } from 'vitest';
import type { CheckChange, CrawlComparison } from '../api/fixLoop';
import {
  ANALYSED_AGAIN_WORDS,
  changeDate,
  checkChangeWords,
  checkSplitWords,
  checksMovedLine,
  clearedIsFixed,
  clearedWords,
  copyChangeWords,
  cautionLine,
  differenceWords,
  fixedCountWords,
  fixedGroupTitle,
  fixedThenWords,
  groupChangeWords,
  homeChangeLine,
  homeScoreLine,
  issuesMovedLine,
  moveGlyph,
  moveSpoken,
  needsAnalysisWords,
  notCheckedWords,
  pointsThenWords,
  rowChangeWords,
  scoreWithMove,
  summaryChangeLine,
  wasWords,
  whenWords,
} from './changeWords';

const SINCE = '2026-11-03T09:12:00Z';
const NOW = new Date(2026, 10, 17, 15, 0).getTime();

const change = (patch: Partial<CheckChange>): CheckChange => ({
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

describe('the dates a change is said by', () => {
  it('says the previous crawl’s day short, with its year only when it isn’t this year’s', () => {
    expect(changeDate(SINCE, NOW)).toBe('3 Nov');
    expect(changeDate('2025-11-03T09:12:00Z', NOW)).toBe('3 Nov 2025');
    expect(changeDate(null, NOW)).toBe('');
    expect(changeDate('not a time', NOW)).toBe('');
  });

  it('says when a recheck ended: today, yesterday, or its day', () => {
    expect(whenWords(new Date(2026, 10, 17, 14, 2).toISOString(), NOW)).toBe('today at 14:02');
    expect(whenWords(new Date(2026, 10, 16, 9, 12).toISOString(), NOW)).toBe('yesterday at 09:12');
    expect(whenWords(new Date(2026, 10, 3, 9, 12).toISOString(), NOW)).toBe('on 3 Nov at 09:12');
  });
});

describe('the score’s change', () => {
  it('is text with a glyph, never colour alone, and says itself to a screen reader', () => {
    expect(moveGlyph(14)).toBe('▲14');
    expect(moveGlyph(-3)).toBe('▼3');
    expect(moveGlyph(0)).toBe('0');
    expect(moveSpoken(14)).toBe('up 14 points');
    expect(moveSpoken(-1)).toBe('down 1 point');
    expect(moveSpoken(0)).toBe('no change');
  });

  it('reads "▲14 since 3 Nov" beside Home’s ring, "so far" for a crawl that stopped, and nothing without a change', () => {
    expect(homeScoreLine(14, SINCE, false)).toBe('▲14 since 3 Nov');
    expect(homeScoreLine(-3, SINCE, false)).toBe('▼3 since 3 Nov');
    expect(homeScoreLine(0, SINCE, false)).toBe('Same as 3 Nov');
    expect(homeScoreLine(14, SINCE, true)).toBe('▲14 since 3 Nov so far');
    // A first crawl, or one scored another way: nothing at all, never "+0".
    expect(homeScoreLine(null, SINCE, false)).toBeNull();
  });

  it('reads "85 ▲14" in Advanced’s score cell, and the score alone without a change', () => {
    expect(scoreWithMove(85, 14)).toBe('85 ▲14');
    expect(scoreWithMove(85, null)).toBe('85');
    expect(scoreWithMove(85, 0)).toBe('85');
  });

  it('gives each line of the breakdown its points then', () => {
    expect(pointsThenWords(7, 4)).toBe('was −7');
    expect(pointsThenWords(0, 4)).toBe('none then');
    expect(pointsThenWords(4, 4)).toBeNull();
  });
});

describe('the counts that moved', () => {
  it('reads "3 fixed · 1 new since 3 Nov" on Home, and nothing when nothing moved', () => {
    expect(homeChangeLine({ fixed: 3, new: 1 }, SINCE)).toBe('3 fixed · 1 new since 3 Nov');
    expect(homeChangeLine({ fixed: 3, new: 0 }, SINCE)).toBe('3 fixed since 3 Nov');
    expect(homeChangeLine({ fixed: 0, new: 2 }, SINCE)).toBe('2 new since 3 Nov');
    expect(homeChangeLine({ fixed: 0, new: 0 }, SINCE)).toBeNull();
  });

  it('reads the Summary’s line with the score then and now', () => {
    expect(
      summaryChangeLine({ since: SINCE, score: { then: 71, now: 85 }, counts: { fixed: 3, new: 1 }, partial: false }),
    ).toBe('Since your last check on 3 Nov: score 71 → 85, 3 problems fixed, 1 new.');
    expect(summaryChangeLine({ since: SINCE, score: null, counts: { fixed: 1, new: 0 }, partial: false })).toBe(
      'Since your last check on 3 Nov: 1 problem fixed.',
    );
    expect(summaryChangeLine({ since: SINCE, score: null, counts: { fixed: 0, new: 2 }, partial: false })).toBe(
      'Since your last check on 3 Nov: 2 new problems.',
    );
    expect(
      summaryChangeLine({ since: SINCE, score: { then: 85, now: 85 }, counts: { fixed: 0, new: 0 }, partial: true }),
    ).toBe('Since your last check on 3 Nov: score 85 → 85, no problems fixed or new so far.');
    expect(ANALYSED_AGAIN_WORDS).toBe('Your last check was worked out again with today’s checks.');
  });

  it('reads Advanced’s lines of checks and issues by state, gone counted as fixed', () => {
    expect(checksMovedLine({ fixed: 5, new: 2, fewer: 6, more: 1, newCheck: 1, notCheckedNow: 0 })).toBe(
      '5 checks fixed · 2 new · 6 fewer · 1 more · 1 new check',
    );
    expect(checksMovedLine({ fixed: 0, new: 0, fewer: 0, more: 0, newCheck: 0, notCheckedNow: 0 })).toBeNull();
    expect(issuesMovedLine({ fixed: 38, gone: 3, notChecked: 3, new: 5, newUrl: 1 })).toBe(
      '41 issues fixed · 6 new · 3 not checked this time',
    );
  });
});

describe('a check’s change', () => {
  it('reads in Simple’s plain words', () => {
    expect(checkChangeWords(change({ state: 'fewer', then: 9, now: 4 }), SINCE, 'simple')).toBe('9 on 3 Nov');
    expect(checkChangeWords(change({ state: 'more', then: 4, now: 9 }), SINCE, 'simple')).toBe('4 on 3 Nov');
    expect(checkChangeWords(change({ state: 'same', then: 12, now: 12 }), SINCE, 'simple')).toBe('same as 3 Nov');
    expect(checkChangeWords(change({ state: 'new', then: 0, now: 1 }), SINCE, 'simple')).toBe('New since 3 Nov');
    expect(checkChangeWords(change({ state: 'fixed', then: 9, now: 0 }), SINCE, 'simple')).toBe('Fixed since 3 Nov');
    // Never "New since" for a check that didn't run last time.
    expect(checkChangeWords(change({ state: 'newCheck', then: null, now: 3 }), SINCE, 'simple')).toBe(
      'Not looked for last time',
    );
    // A check skipped now says its own "couldn't check" reason, as before.
    expect(checkChangeWords(change({ state: 'notCheckedNow', then: 3, now: null }), SINCE, 'simple')).toBeNull();
  });

  it('reads in Advanced’s terms', () => {
    expect(checkChangeWords(change({ state: 'fewer', then: 9, now: 4 }), SINCE, 'advanced')).toBe('−5');
    expect(checkChangeWords(change({ state: 'more', then: 4, now: 6 }), SINCE, 'advanced')).toBe('+2');
    expect(checkChangeWords(change({ state: 'same', then: 4, now: 4 }), SINCE, 'advanced')).toBe('0');
    expect(checkChangeWords(change({ state: 'new' }), SINCE, 'advanced')).toBe('New');
    expect(checkChangeWords(change({ state: 'fixed' }), SINCE, 'advanced')).toBe('Fixed');
    expect(checkChangeWords(change({ state: 'newCheck' }), SINCE, 'advanced')).toBe('New check');
    expect(checkChangeWords(change({ state: 'notCheckedNow' }), SINCE, 'advanced')).toBe('Not checked');
  });

  it('splits its rows in Advanced’s tooltip, and has none when counts only', () => {
    expect(checkSplitWords(change({ fixed: 3, gone: 2, notChecked: 2, new: 0 }))).toBe(
      '5 fixed, 0 new, 2 not checked this time',
    );
    expect(checkSplitWords(change({ fixed: null, gone: null, new: null, notChecked: null }))).toBeNull();
  });

  it('adds "was 9 on 3 Nov" to the problem page’s count, and "(was 9)" or "(new since 3 Nov)" to a copied list', () => {
    expect(wasWords(change({ state: 'fewer', then: 9 }), SINCE)).toBe('was 9 on 3 Nov');
    expect(wasWords(change({ state: 'new', then: 0 }), SINCE)).toBeNull();
    expect(copyChangeWords(change({ state: 'fewer', then: 9 }), SINCE)).toBe(' (was 9)');
    expect(copyChangeWords(change({ state: 'new' }), SINCE)).toBe(' (new since 3 Nov)');
    expect(copyChangeWords(change({ state: 'same' }), SINCE)).toBe('');
  });

  it('heads What to fix’s Fixed group, each check with its count then', () => {
    expect(fixedGroupTitle(SINCE, 3)).toBe('Fixed since your last check on 3 Nov (3)');
    // Counted in the unit of Simple's headline: problems, then the things worth a look ("6 problems to fix and 22 more
    // worth a look").
    expect(fixedCountWords(3, 1)).toBe('3 problems and 1 more worth a look');
    expect(fixedCountWords(1, 0)).toBe('1 problem');
    expect(fixedCountWords(0, 2)).toBe('2 worth a look');
    expect(fixedThenWords({ subject: 'url' }, 9, SINCE)).toBe('9 addresses on 3 Nov');
    expect(fixedThenWords({ subject: 'page' }, 14, SINCE)).toBe('14 pages on 3 Nov');
    expect(fixedThenWords({ subject: 'page' }, 1, SINCE)).toBe('1 page on 3 Nov');
    expect(fixedThenWords({ siteWide: true }, 1, SINCE)).toBe('whole site on 3 Nov');
  });
});

describe('causes and rows', () => {
  it('says a cause is still there or new in Simple, and its movement in Advanced', () => {
    expect(groupChangeWords({ state: 'fewer', then: 5 }, 3, SINCE, 'simple')).toBe('Still there since 3 Nov');
    expect(groupChangeWords({ state: 'new', then: 0 }, 3, SINCE, 'simple')).toBe('New since 3 Nov');
    expect(groupChangeWords({ state: 'fewer', then: 5 }, 2, SINCE, 'advanced')).toBe('−3');
    expect(groupChangeWords({ state: 'more', then: 2 }, 3, SINCE, 'advanced')).toBe('+1');
    expect(groupChangeWords({ state: 'same', then: 3 }, 3, SINCE, 'advanced')).toBe('0');
    expect(groupChangeWords({ state: 'new', then: 0 }, 3, SINCE, 'advanced')).toBe('New');
  });

  it('names a row’s change', () => {
    expect(rowChangeWords('still', SINCE, 'advanced')).toBe('Still');
    expect(rowChangeWords('newUrl', SINCE, 'advanced')).toBe('New URL');
    expect(rowChangeWords('notComparedThen', SINCE, 'advanced')).toBe('Not compared');
    expect(rowChangeWords('still', SINCE, 'simple')).toBe('Still there since 3 Nov');
    expect(rowChangeWords('newUrl', SINCE, 'simple')).toBe('New since 3 Nov');
  });

  it('never says a row that wasn’t checked is fixed', () => {
    expect(clearedWords('fixed', 'sources', 'simple')).toBe('Fixed');
    expect(clearedWords('gone', 'sources', 'simple')).toBe('No longer linked from any page');
    expect(clearedWords('gone', 'sources', 'advanced')).toBe('No longer linked');
    expect(clearedWords('gone', 'none', 'simple')).toBe('No longer found');
    expect(clearedWords('notChecked', 'sources', 'simple')).toBe('Not checked this time');
    expect(clearedIsFixed('fixed')).toBe(true);
    expect(clearedIsFixed('gone')).toBe(true);
    expect(clearedIsFixed('notChecked')).toBe(false);
    expect(notCheckedWords({ code: 'redirects', text: 'Now forwards to /new/' }, 'simple')).toBe(
      'It now forwards to another address.',
    );
    expect(notCheckedWords({ code: 'redirects', text: 'Now forwards to /new/' }, 'advanced')).toBe(
      'Now forwards to /new/',
    );
  });
});

describe('a comparison that isn’t like for like', () => {
  const compared = (patch: Partial<CrawlComparison>) =>
    ({ comparable: 'partial', differences: [], ...patch }) as Pick<CrawlComparison, 'comparable' | 'differences'>;

  it('says nothing when it is', () => {
    expect(cautionLine(compared({ comparable: 'full' }))).toBeNull();
  });

  it('says one caution line in Simple, for the difference that matters most', () => {
    expect(
      cautionLine(
        compared({
          comparable: 'countsOnly',
          differences: [
            { code: 'startOrigin', then: 'http://example.com/', now: 'https://example.com/' },
            { code: 'coverage', then: 265, now: 50 },
          ],
        }),
      ),
    ).toBe(
      'This check started at https://example.com/, the last at http://example.com/, so only the totals are compared.',
    );
    expect(cautionLine(compared({ differences: [{ code: 'coverage', then: 265, now: 50 }] }))).toBe(
      'Your last check covered more of the site (265 pages against 50 now), so some of it couldn’t be compared.',
    );
    expect(cautionLine(compared({ differences: [{ code: 'stoppedEarly', side: 'now' }] }))).toBe(
      'This check stopped before the end, so what it didn’t read couldn’t be compared.',
    );
    expect(cautionLine(compared({ differences: [{ code: 'stoppedEarly', side: 'both' }] }))).toBe(
      'Both checks stopped before the end, so what they didn’t read couldn’t be compared.',
    );
    expect(cautionLine(compared({ differences: [{ code: 'limits', field: 'maxUrls', then: null, now: 5000 }] }))).toBe(
      'This check and your last had different limits, so some of it couldn’t be compared.',
    );
    expect(
      cautionLine(compared({ comparable: 'countsOnly', differences: [{ code: 'rules', field: 'settings' }] })),
    ).toBe('Your last check was worked out with other checks, so only the totals are compared.');
  });

  it('lists each difference in Advanced’s terms', () => {
    expect(differenceWords({ code: 'limits', field: 'maxUrls', then: null, now: 5000 })).toBe(
      'Max URLs: none then, 5,000 now',
    );
    expect(differenceWords({ code: 'stoppedEarly', side: 'now' })).toBe('Stopped early: this crawl');
    expect(differenceWords({ code: 'coverage', then: 265, now: 50 })).toBe('Pages analysed: 265 then, 50 now');
    expect(differenceWords({ code: 'rules', field: 'settings' })).toBe('Analysed by other rules: Analysis settings');
    expect(differenceWords({ code: 'scope', field: 'includeSubdomains', then: false, now: true })).toBe(
      'Include subdomains: off then, on now',
    );
    expect(needsAnalysisWords(SINCE, ['engineChanged'])).toBe(
      'The 3 Nov crawl was analysed by an earlier version: analyse it again to compare like for like.',
    );
  });
});
