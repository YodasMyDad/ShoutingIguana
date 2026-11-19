import { describe, expect, it } from 'vitest';
import type { RecheckAnswers, RecheckPlan, RecheckResult } from '../api/fixLoop';
import {
  aboutWords,
  advancedEndedWords,
  advancedResultWords,
  recheckButtonWords,
  recheckIntroWords,
  recheckPlanLine,
  recheckProgressWords,
  recheckResultKind,
  recheckScoreWords,
  recheckToastTitle,
  rechecksCountWords,
  rechecksMetaWords,
  refusalWords,
  refusedAgainWords,
  simpleResultWords,
} from './recheckWords';

const plan = (patch: Partial<RecheckPlan> = {}): RecheckPlan => ({
  checkId: 'broken-internal-links',
  group: null,
  urlList: false,
  kinds: ['urls'],
  judged: 5,
  phase1: 8,
  phase2Max: 0,
  limit: 1_000,
  capped: false,
  notRechecked: 0,
  estimateMs: 21_000,
  suggestFullCheck: false,
  analysisFirst: false,
  countBefore: 5,
  secrets: { missingHeaders: [], proxyCredentials: false },
  refused: null,
  ...patch,
});

const result = (patch: Partial<RecheckResult> = {}): RecheckResult => ({
  fixed: 4,
  still: 1,
  notChecked: 0,
  notRechecked: 0,
  new: 0,
  scoreBefore: 85,
  scoreAfter: 88,
  ...patch,
});

describe('how long a recheck takes', () => {
  it('is rounded to what a person would say', () => {
    expect(aboutWords(4_000)).toBe('a few seconds');
    expect(aboutWords(21_000)).toBe('about 20 seconds');
    expect(aboutWords(52_000)).toBe('about 50 seconds');
    expect(aboutWords(70_000)).toBe('about a minute');
    expect(aboutWords(130_000)).toBe('about 2 minutes');
    expect(aboutWords(9 * 60_000 + 10_000)).toBe('about 9 minutes');
    expect(aboutWords(60 * 60_000)).toBe('about 1 hour');
    expect(aboutWords(80 * 60_000)).toBe('about 1 hour 20 minutes');
    expect(aboutWords(2 * 60 * 60_000 + 2 * 60_000)).toBe('about 2 hours');
  });
});

describe('the recheck’s button', () => {
  it('follows the check’s subject and scope in Simple', () => {
    expect(recheckButtonWords(plan(), 'page', 'check', 'simple')).toBe('Recheck these 5 pages');
    expect(recheckButtonWords(plan({ judged: 9 }), 'url', 'check', 'simple')).toBe('Recheck these 9 addresses');
    expect(recheckButtonWords(plan({ judged: 1 }), 'page', 'check', 'simple')).toBe('Recheck this page');
    expect(recheckButtonWords(plan({ judged: 264 }), 'page', 'group', 'simple')).toBe('Recheck this one (264 pages)');
    expect(
      recheckButtonWords(plan({ judged: 4_000, capped: true, notRechecked: 3_000 }), 'page', 'check', 'simple'),
    ).toBe('Recheck the 1,000 most important pages');
  });

  it('counts URLs in Advanced', () => {
    expect(recheckButtonWords(plan(), 'page', 'check', 'advanced')).toBe('Recheck 5 URLs');
    expect(recheckButtonWords(plan({ judged: 1 }), 'page', 'check', 'advanced')).toBe('Recheck 1 URL');
    expect(recheckButtonWords(plan({ judged: 264 }), 'page', 'group', 'advanced')).toBe(
      'Recheck this group (264 URLs)',
    );
    expect(recheckButtonWords(plan({ judged: 6 }), undefined, 'urls', 'advanced')).toBe('Recheck 6 selected URLs');
    expect(recheckButtonWords(plan({ judged: 4_000, capped: true }), 'page', 'check', 'advanced')).toBe(
      'Recheck 1,000 of 4,000 URLs',
    );
  });

  it('says in the card what it reads and how long, and that the rest stays as it was', () => {
    expect(
      recheckIntroWords({
        plan: plan(),
        subject: 'page',
        references: 'targets',
        crawledAt: '2026-11-03T09:12:00Z',
        scope: 'check',
      }),
    ).toEqual([
      'Recheck these 5 pages to see whether it’s gone. It reads only them and the addresses they link to: about 20 seconds. Other pages stay as the check found them on 3 Nov.',
    ]);
    expect(
      recheckIntroWords({
        plan: plan({ judged: 9, estimateMs: 130_000 }),
        subject: 'url',
        references: 'sources',
        crawledAt: '2026-11-03T09:12:00Z',
        scope: 'check',
      })[0],
    ).toBe(
      'Recheck these 9 addresses to see whether it’s gone. It reads only them and the pages that link to them: about 2 minutes. Other pages stay as the check found them on 3 Nov.',
    );
    expect(
      recheckIntroWords({
        plan: plan({
          judged: 4_000,
          capped: true,
          notRechecked: 3_000,
          estimateMs: 9 * 60_000,
          suggestFullCheck: true,
        }),
        subject: 'page',
        references: 'none',
        crawledAt: '2026-11-03T09:12:00Z',
        scope: 'check',
      }),
    ).toEqual([
      'Recheck the 1,000 most important pages to see whether it’s gone. It reads only them: about 9 minutes. Other pages stay as the check found them on 3 Nov.',
      'It rechecks the 1,000 most important of 4,000. The other 3,000 stay as the check found them.',
      'That’s most of the site: checking the whole site again takes about as long, and checks everything.',
    ]);
    expect(recheckPlanLine(plan())).toBe('Fetches 8 URLs again (about 20 seconds). Other URLs stay as crawled.');
  });

  it('says why it can’t run, in each mode’s words', () => {
    const refused = {
      type: 'site-busy',
      title: 'A check of this site is running. Its results will show what’s fixed.',
    };
    expect(refusalWords(refused, 'simple')).toBe(
      'A check of this site is running. Its results will show what’s fixed.',
    );
    expect(
      refusalWords({ type: 'recheck-not-latest', title: 'Recheck works on the site’s latest check.' }, 'simple'),
    ).toBe('This is an earlier check. Open the latest check of this site to recheck it.');
    expect(
      refusalWords({ type: 'recheck-not-latest', title: 'Recheck works on the site’s latest check.' }, 'advanced'),
    ).toBe('Recheck works on the site’s latest check.');
    expect(refusalWords({ type: 'recheck-unsupported', title: 'x' }, 'simple')).toBe(
      'This one needs the whole site read again to see a fix: check the site again.',
    );
  });
});

describe('a recheck running', () => {
  it('counts what it has read, then works out the results', () => {
    expect(recheckProgressWords({ done: 3, total: 8, phase: 1 }, 'simple')).toBe('Rechecking… 3 of 8 addresses read');
    expect(recheckProgressWords(null, 'simple')).toBe('Rechecking…');
    expect(recheckProgressWords({ done: 3, total: 8, phase: 1 }, 'simple', true)).toBe('Working out the results…');
    expect(recheckProgressWords({ done: 3, total: 8, phase: 2 }, 'advanced')).toBe(
      'Rechecking: 3 of 8 URLs fetched (the pages linking to them)',
    );
    expect(recheckProgressWords({ done: 3, total: 8, phase: 1 }, 'advanced', true)).toBe('Analysing…');
  });
});

describe('what a recheck found', () => {
  it('reads "4 of 5 fixed. 1 still has it"', () => {
    expect(recheckResultKind(result(), 5)).toBe('someFixed');
    expect(simpleResultWords({ status: 'completed', judged: 5, result: result(), subject: 'page' })).toEqual([
      '4 of 5 fixed. 1 still has it.',
    ]);
    expect(
      simpleResultWords({
        status: 'completed',
        judged: 5,
        result: result({ fixed: 3, still: 2 }),
        subject: 'page',
        listed: true,
      }),
    ).toEqual(['3 of 5 fixed. 2 still have it:']);
  });

  it('reads "All 5 fixed", and that the problem is gone only when the check found nothing after it', () => {
    const all = result({ fixed: 5, still: 0 });
    expect(recheckResultKind(all, 5)).toBe('allFixed');
    expect(simpleResultWords({ status: 'completed', judged: 5, result: all, subject: 'page', cleared: true })).toEqual([
      'All 5 fixed. This problem is gone from the list.',
    ]);
    expect(simpleResultWords({ status: 'completed', judged: 5, result: all, subject: 'page' })).toEqual([
      'All 5 fixed.',
    ]);
    expect(
      simpleResultWords({ status: 'completed', judged: 1, result: result({ fixed: 1, still: 0 }), subject: 'page' }),
    ).toEqual(['Fixed.']);
  });

  it('reads "None of the 5 is fixed yet", with the cache hint', () => {
    const none = result({ fixed: 0, still: 5 });
    expect(recheckResultKind(none, 5)).toBe('noneFixed');
    expect(simpleResultWords({ status: 'completed', judged: 5, result: none, subject: 'page' })).toEqual([
      'None of the 5 is fixed yet. If you changed it in the last few minutes, your site may be showing a saved copy (a cache): try again in a few minutes.',
    ]);
  });

  it('never counts what couldn’t be rechecked as fixed', () => {
    expect(
      simpleResultWords({
        status: 'completed',
        judged: 5,
        result: result({ fixed: 3, still: 0, notChecked: 2 }),
        subject: 'page',
        notCheckedWhy: 'the site didn’t answer',
      }),
    ).toEqual([
      '3 of 5 fixed.',
      '2 couldn’t be rechecked: the site didn’t answer.',
      'They stay on the list until they can be checked again.',
    ]);
    const unread = result({ fixed: 0, still: 0, notChecked: 5 });
    expect(recheckResultKind(unread, 5)).toBe('noneChecked');
    expect(simpleResultWords({ status: 'completed', judged: 5, result: unread, subject: 'page' })).toEqual([
      'None of the 5 could be rechecked.',
      'They stay on the list until they can be checked again.',
    ]);
    expect(
      simpleResultWords({
        status: 'completed',
        judged: 1,
        result: result({ fixed: 0, still: 0, notChecked: 1 }),
        subject: 'page',
      }),
    ).toEqual(['It couldn’t be rechecked.', 'It stays on the list until it can be checked again.']);
    expect(
      simpleResultWords({
        status: 'stopped',
        judged: 5,
        result: result({ fixed: 1, still: 0, notChecked: 1, notRechecked: 3 }),
        subject: 'page',
      }),
    ).toEqual([
      'Stopped: 2 of 5 rechecked. 1 fixed.',
      '1 couldn’t be rechecked.',
      'It stays on the list until it can be checked again.',
    ]);
  });

  it('says why the site refused them again, in plain words', () => {
    const answers = (patch: Partial<RecheckAnswers>): RecheckAnswers => ({
      read: 0,
      rateLimited: 0,
      blocked: 0,
      robotsTxt: 0,
      noAnswer: 0,
      notAsked: 0,
      refused: 0,
      ...patch,
    });
    expect(refusedAgainWords(null)).toBeNull();
    expect(refusedAgainWords(answers({ read: 5 }))).toBeNull();
    expect(refusedAgainWords(answers({ read: 3, rateLimited: 2, refused: 2 }))).toBe(
      'the site asked us to slow down and turned them away again',
    );
    expect(refusedAgainWords(answers({ read: 4, blocked: 1, refused: 1 }))).toBe(
      'the site’s security turned it away again',
    );
    expect(refusedAgainWords(answers({ robotsTxt: 2, refused: 2 }))).toBe('the site’s robots.txt doesn’t allow them');
    expect(refusedAgainWords(answers({ robotsTxt: 1, noAnswer: 1, refused: 2 }))).toBe(
      'the site turned them away again, or didn’t answer',
    );
    expect(
      simpleResultWords({
        status: 'completed',
        judged: 5,
        result: result({ fixed: 3, still: 0, notChecked: 2 }),
        subject: 'page',
        notCheckedWhy: refusedAgainWords(answers({ read: 3, rateLimited: 2, refused: 2 })),
      }),
    ).toEqual([
      '3 of 5 fixed.',
      '2 couldn’t be rechecked: the site asked us to slow down and turned them away again.',
      'They stay on the list until they can be checked again.',
    ]);
    // One colon to a sentence: the reason never adds a second.
    const refused = simpleResultWords({
      status: 'completed',
      judged: 7,
      result: result({ fixed: 0, still: 0, notChecked: 7, new: 0 }),
      subject: 'page',
      notCheckedWhy: refusedAgainWords(answers({ rateLimited: 6, notAsked: 1, refused: 6 })),
    });
    expect(refused).toEqual([
      'None of the 7 could be rechecked: the site asked us to slow down and turned them away again.',
      'They stay on the list until they can be checked again.',
    ]);
    for (const line of refused) expect(line.split(':').length).toBeLessThanOrEqual(2);
  });

  it('reads a stopped recheck as what it got through', () => {
    expect(
      simpleResultWords({
        status: 'stopped',
        judged: 5,
        result: result({ fixed: 2, still: 1, notRechecked: 2 }),
        subject: 'page',
      }),
    ).toEqual(['Stopped: 3 of 5 rechecked. 2 fixed, 1 still has it.']);
    expect(
      simpleResultWords({
        status: 'interrupted',
        judged: 5,
        result: result({ fixed: 2, still: 0, notRechecked: 3 }),
        subject: 'page',
      }),
    ).toEqual(['Cut short: 2 of 5 rechecked. 2 fixed.']);
  });

  it('adds the pages that have it now and didn’t before', () => {
    expect(simpleResultWords({ status: 'completed', judged: 5, result: result({ new: 1 }), subject: 'page' })).toEqual([
      '4 of 5 fixed. 1 still has it.',
      'And 1 page has it now that didn’t before.',
    ]);
  });

  it('says the score before and after, and when', () => {
    expect(recheckScoreWords(result())).toBe('Score 85 → 88.');
    expect(recheckScoreWords(result({ scoreBefore: null }))).toBeNull();
  });

  it('reads in Advanced’s banner, with the URLs still flagged', () => {
    expect(
      advancedResultWords({
        endedAt: new Date(2026, 10, 17, 14, 2).toISOString(),
        status: 'completed',
        judged: 5,
        result: result(),
        stillPaths: ['/sofas/'],
      }),
    ).toBe('Rechecked 17 Nov 14:02: 4 of 5 fixed, 1 still (/sofas/), 0 new. Score 85 → 88');
    expect(
      advancedResultWords({
        endedAt: null,
        status: 'completed',
        judged: 7,
        result: result({ fixed: 0, still: 0, notChecked: 2, notRechecked: 4, new: 0, scoreBefore: 97, scoreAfter: 97 }),
        answers: { read: 1, rateLimited: 2, blocked: 0, robotsTxt: 0, noAnswer: 0, notAsked: 4, refused: 2 },
      }),
    ).toBe(
      'Rechecked: 0 of 7 fixed, 0 still, 2 not checked (2 refused again: 429), 4 not rechecked, 0 new. Score 97 → 97',
    );
  });

  it('says in Advanced’s banner why one ended without a result: it didn’t run, or it failed and kept nothing', () => {
    expect(advancedEndedWords(null, 'Recheck works on the site’s latest check.')).toBe(
      'The recheck didn’t run: Recheck works on the site’s latest check.',
    );
    expect(advancedEndedWords('failed', 'The recheck failed: the disk is full')).toBe(
      'The recheck failed, so nothing it fetched was kept: the disk is full',
    );
    expect(advancedEndedWords('failed', null)).toBe('The recheck failed, so nothing it fetched was kept.');
  });

  it('titles the toast when it ends', () => {
    expect(recheckToastTitle('completed', 5, result())).toBe('Recheck done: 4 of 5 fixed');
    expect(recheckToastTitle('completed', 5, result({ fixed: 5, still: 0 }))).toBe('Recheck done: all 5 fixed');
    expect(recheckToastTitle('stopped', 5, result({ fixed: 2 }))).toBe('Recheck stopped: 2 of 5 fixed');
  });

  it('says a crawl’s rechecks in a line', () => {
    expect(rechecksMetaWords({ judged: 5, endedAt: '2026-11-17T14:02:41Z' })).toBe('rechecked 5 on 17 Nov');
    expect(rechecksCountWords(3, '2026-11-17T14:02:41Z')).toBe('Rechecked 3 times, last on 17 Nov');
    expect(rechecksCountWords(1, '2026-11-17T14:02:41Z')).toBe('Rechecked once, on 17 Nov');
  });
});
