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
    expect(recheckButtonWords(plan({ judged: 1 }), undefined, 'urls', 'advanced')).toBe('Recheck 1 selected URL');
    expect(
      recheckButtonWords(plan({ judged: 4_000, capped: true, notRechecked: 3_000 }), 'page', 'check', 'advanced'),
    ).toBe('Recheck 1,000 of 4,000 URLs');
  });

  // The shapes the garden furniture crawl's plans had on 19 Nov, where the button read "Recheck 1,000 of 240 URLs".
  describe('on the plans real checks gave', () => {
    // Image in an older format: 240 pages flagged, 1,442 images named. The cap of 1,000 fetches holds 150 of the pages
    // and the 850 images on them; the other 90 pages are past it.
    const manyImages = plan({
      checkId: 'image-legacy-format',
      judged: 240,
      phase1: 1_000,
      capped: true,
      notRechecked: 90,
      estimateMs: 496_000,
    });
    // Images without width and height: 225 pages, 4,151 images named: 59 pages and their 940 images fit.
    const moreImages = plan({
      checkId: 'image-missing-dimensions',
      judged: 225,
      phase1: 999,
      capped: true,
      notRechecked: 166,
      estimateMs: 496_000,
    });
    // One URL flagged, one page linking to it.
    const oneUrl = plan({ checkId: 'url-parameters', judged: 1, phase1: 1, phase2Max: 1, estimateMs: 4_000 });
    // Eight 404s, four pages linking to them.
    const eight = plan({ checkId: 'internal-client-error', judged: 8, phase1: 8, phase2Max: 4, estimateMs: 6_000 });
    // robots.txt doesn't name the sitemap: nothing of the home page is fetched, only the two files.
    const files = plan({
      checkId: 'robots-txt-no-sitemap',
      kinds: ['robotsTxt', 'sitemaps'],
      judged: 1,
      phase1: 1,
      estimateMs: 1_000,
    });

    it('counts the check’s own URLs on the button, never the fetches: 150 of 240, not 1,000 of 240', () => {
      expect(recheckButtonWords(manyImages, 'page', 'check', 'advanced')).toBe('Recheck 150 of 240 URLs');
      expect(recheckButtonWords(moreImages, 'page', 'check', 'advanced')).toBe('Recheck 59 of 225 URLs');
      expect(recheckButtonWords(manyImages, 'page', 'check', 'simple')).toBe('Recheck the 150 most important pages');
      expect(recheckButtonWords(moreImages, 'page', 'check', 'simple')).toBe('Recheck the 59 most important pages');
      expect(recheckButtonWords(oneUrl, 'url', 'check', 'advanced')).toBe('Recheck 1 URL');
      expect(recheckButtonWords(oneUrl, 'url', 'check', 'simple')).toBe('Recheck this address');
    });

    it('names the files on the button when the recheck reads only robots.txt or the sitemaps', () => {
      expect(recheckButtonWords(files, 'page', 'check', 'advanced')).toBe('Recheck robots.txt and the sitemaps');
      expect(recheckButtonWords(files, 'page', 'check', 'simple')).toBe('Recheck the rules file and the sitemaps');
      expect(recheckButtonWords(plan({ kinds: ['sitemaps'], judged: 1 }), 'page', 'check', 'advanced')).toBe(
        'Recheck the sitemaps',
      );
      expect(recheckButtonWords(plan({ kinds: ['siteVersions'], judged: 1 }), 'page', 'check', 'simple')).toBe(
        'Recheck the site’s other versions',
      );
    });

    it('says under Advanced’s button what is fetched, each number in its own unit, and what the cap leaves', () => {
      expect(recheckPlanLine(manyImages)).toBe(
        'Fetches 150 URLs again, and the 850 URLs they refer to (about 8 minutes). The other 90 of the 240 are past the cap of 1,000 fetches. Other URLs stay as crawled.',
      );
      expect(recheckPlanLine(moreImages)).toBe(
        'Fetches 59 URLs again, and the 940 URLs they refer to (about 8 minutes). The other 166 of the 225 are past the cap of 1,000 fetches. Other URLs stay as crawled.',
      );
      expect(recheckPlanLine(oneUrl)).toBe(
        'Fetches 1 URL again, then up to 1 page linking to it if it still fails (a few seconds). Other URLs stay as crawled.',
      );
      expect(recheckPlanLine(eight)).toBe(
        'Fetches 8 URLs again, then up to 4 pages linking to those that still fail (a few seconds). Other URLs stay as crawled.',
      );
      expect(recheckPlanLine(plan({ judged: 1, phase1: 4 }))).toBe(
        'Fetches 1 URL again, and the 3 URLs it refers to (about 20 seconds). Other URLs stay as crawled.',
      );
      expect(recheckPlanLine(plan({ judged: 3, phase1: 3, estimateMs: 4_000 }))).toBe(
        'Fetches 3 URLs again (a few seconds). Other URLs stay as crawled.',
      );
      expect(recheckPlanLine(files)).toBe(
        'Reads robots.txt and the sitemaps again (a few seconds). Every URL stays as crawled.',
      );
      // Pages and the sitemaps: "Indexable page not in a sitemap".
      expect(recheckPlanLine(plan({ kinds: ['urls', 'sitemaps'], judged: 18, phase1: 18, estimateMs: 30_000 }))).toBe(
        'Fetches 18 URLs again, and reads the sitemaps again (about 30 seconds). Other URLs stay as crawled.',
      );
      // More pages link to the flagged URLs than the cap has room for: none of the flagged URLs is left out.
      expect(
        recheckPlanLine(plan({ judged: 600, phase1: 600, phase2Max: 400, capped: true, estimateMs: 496_000 })),
      ).toBe(
        'Fetches 600 URLs again, then up to 400 pages linking to those that still fail (about 8 minutes). The cap of 1,000 fetches leaves out some of the pages linking to them. Other URLs stay as crawled.',
      );
    });

    it('says in Simple’s card how many pages, of how many, and why not all: 150 + 90 = 240', () => {
      const intro = (
        which: RecheckPlan,
        references: 'targets' | 'sources' | 'none',
        subject: 'page' | 'url' = 'page',
      ) => recheckIntroWords({ plan: which, subject, references, crawledAt: '2026-11-17T09:12:00Z', scope: 'check' });
      expect(intro(manyImages, 'targets')).toEqual([
        'Recheck the 150 most important pages to see whether it’s gone. It reads only them and the addresses they link to: about 8 minutes. Other pages stay as the check found them on 17 Nov.',
        'It rechecks the 150 most important of the 240: a recheck reads at most 1,000 addresses, and the 850 these pages link to count too. The other 90 stay as the check found them.',
      ]);
      expect(intro(moreImages, 'targets')[1]).toBe(
        'It rechecks the 59 most important of the 225: a recheck reads at most 1,000 addresses, and the 940 these pages link to count too. The other 166 stay as the check found them.',
      );
      expect(intro(oneUrl, 'sources', 'url')).toEqual([
        'Recheck this address to see whether it’s gone. It reads only it and the pages that link to it: a few seconds. Other pages stay as the check found them on 17 Nov.',
      ]);
      expect(intro(files, 'none')).toEqual([
        'Recheck to see whether it’s gone. It reads only the site’s rules file (robots.txt) and its sitemaps again: a few seconds. Every page stays as the check found it on 17 Nov.',
      ]);
      expect(intro(plan({ kinds: ['urls', 'sitemaps'], judged: 18, phase1: 18, estimateMs: 30_000 }), 'none')[0]).toBe(
        'Recheck these 18 pages to see whether it’s gone. It reads only them, and the site’s sitemaps again: about 30 seconds. Other pages stay as the check found them on 17 Nov.',
      );
      expect(
        intro(plan({ judged: 1_001, phase1: 1_000, capped: true, notRechecked: 1, estimateMs: 496_000 }), 'none')[1],
      ).toBe(
        'It rechecks the 1,000 most important of the 1,001: a recheck reads at most 1,000 addresses. The other one stays as the check found it.',
      );
    });
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
      'It rechecks the 1,000 most important of the 4,000: a recheck reads at most 1,000 addresses. The other 3,000 stay as the check found them.',
      'That’s most of the site: checking the whole site again takes about as long, and checks everything.',
    ]);
    // Five pages, and the three URLs their issues name.
    expect(recheckPlanLine(plan())).toBe(
      'Fetches 5 URLs again, and the 3 URLs they refer to (about 20 seconds). Other URLs stay as crawled.',
    );
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
    expect(recheckProgressWords({ done: 0, total: 1, phase: 1 }, 'advanced')).toBe('Rechecking: 0 of 1 URL fetched');
    expect(recheckProgressWords({ done: 0, total: 1, phase: 1 }, 'simple')).toBe('Rechecking… 0 of 1 address read');
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

  it('says "both" and "neither" of two', () => {
    const words = (patch: Partial<RecheckResult>) =>
      simpleResultWords({ status: 'completed', judged: 2, result: result(patch), subject: 'page' })[0];
    expect(words({ fixed: 2, still: 0 })).toBe('Both fixed.');
    expect(words({ fixed: 0, still: 2 })).toBe(
      'Neither is fixed yet. If you changed it in the last few minutes, your site may be showing a saved copy (a cache): try again in a few minutes.',
    );
    expect(words({ fixed: 0, still: 0, notChecked: 2 })).toBe('Neither could be rechecked.');
    expect(recheckToastTitle('completed', 2, result({ fixed: 2, still: 0 }))).toBe('Recheck done: both fixed');
  });

  // The garden crawl's third recheck: it failed, judged nothing, and the analysis after it moved the score.
  it('says of a recheck that failed only that it has no result: no counts, no score', () => {
    const failed = result({ fixed: 0, still: 0, notChecked: 0, notRechecked: 1, scoreBefore: 83, scoreAfter: null });
    expect(simpleResultWords({ status: 'failed', judged: 1, result: failed, subject: 'page' })).toEqual([
      'The recheck didn’t finish, so it has no result. Try again.',
    ]);
    expect(
      simpleResultWords({ status: 'failed', judged: 8, result: result({ fixed: 3, still: 1 }), subject: 'page' }),
    ).toEqual(['The recheck didn’t finish, so it has no result. Try again.']);
    expect(recheckScoreWords(failed)).toBeNull();
    expect(
      advancedResultWords({
        endedAt: new Date(2026, 10, 18, 6, 33).toISOString(),
        status: 'failed',
        judged: 1,
        result: failed,
      }),
    ).toBe('Failed 18 Nov 06:33: it didn’t finish, so it has no result.');
    expect(recheckToastTitle('failed', 1, failed)).toBe('Recheck couldn’t finish');
  });

  it('says a crawl’s rechecks in a line', () => {
    expect(rechecksMetaWords({ judged: 5, endedAt: '2026-11-17T14:02:41Z' })).toBe('rechecked 5 on 17 Nov');
    expect(rechecksCountWords(3, '2026-11-17T14:02:41Z')).toBe('Rechecked 3 times, last on 17 Nov');
    expect(rechecksCountWords(1, '2026-11-17T14:02:41Z')).toBe('Rechecked once, on 17 Nov');
  });
});
