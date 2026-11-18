import { describe, expect, it } from 'vitest';
import type { Check } from '../api/types';
import {
  GARDEN_ANALYSIS,
  NOTICES_ANALYSIS,
  PERFECT_ANALYSIS,
  SIMPLE_CHECKS,
  SITE_WIDE_ANALYSIS,
  STOPPED_ANALYSIS,
} from '../test/simpleSidecar';
import {
  bandWord,
  coverageSentences,
  fixerWords,
  ifFixedLead,
  lineShareWords,
  pointsWords,
  shareWords,
  summarySentence,
} from './plainWords';
import { plainResults, siteWideLead } from './results';

const NONE = new Set<string>();
const check = (id: string) => SIMPLE_CHECKS.checks.find((candidate) => candidate.id === id)!;
const ids = (entries: readonly { check: Check }[]) => entries.map((entry) => entry.check.id);

describe('Simple’s results on the garden crawl', () => {
  const results = plainResults(SIMPLE_CHECKS, GARDEN_ANALYSIS, NONE);

  it('counts the garden’s problems: 2 serious, 4 worth fixing, 22 worth a look, 27 couldn’t run', () => {
    expect(results.counts).toEqual({ error: 2, warning: 4, notice: 22 });
    expect(results.skippedCount).toBe(27);
    expect(results.passed.length + results.counts.error + results.counts.warning + results.counts.notice).toBe(
      results.ran,
    );
  });

  it('lists five to fix first in the engine’s order, serious and worth fixing only, and says what fixing them gives', () => {
    expect(ids(results.fixFirst)).toEqual([
      'internal-client-error',
      'broken-internal-links',
      'image-too-large',
      'title-duplicate',
      'slow-response',
    ]);
    expect(results.ifFixed).toBe(99);
  });

  it('says nothing of the five fixed when a hidden check is among the engine’s five', () => {
    const hidden = plainResults(SIMPLE_CHECKS, GARDEN_ANALYSIS, new Set(['image-too-large']));
    // The next of the engine's order that's serious or worth fixing takes its place, so its five aren't these five.
    expect(ids(hidden.fixFirst)).toEqual([
      'internal-client-error',
      'broken-internal-links',
      'title-duplicate',
      'slow-response',
      'canonical-to-first-page',
    ]);
    expect(hidden.ifFixed).toBeNull();
    expect(hidden.counts.warning).toBe(3);
    expect(ids(hidden.hidden)).toEqual(['image-too-large']);
  });

  it('groups what couldn’t run by its reason, with the engine’s plain words', () => {
    expect(results.skipped).toHaveLength(3);
    expect(results.skipped.map((group) => [group.code, ids(group.entries)])).toEqual(
      expect.arrayContaining([
        ['site-versions-unchecked', ['duplicate-site-version']],
        ['no-sitemap', ['orphan-page', 'not-in-sitemap']],
        [
          'earlier-version',
          [
            'uncrawlable-links',
            'pagination-not-linked',
            'pagination-broken',
            'pagination-to-non-indexable',
            'pagination-sequence-error',
            'canonical-invalid-attribute',
            'sitemap-lastmod-invalid',
            'h1-image-only',
            'near-duplicate-content',
            'placeholder-text',
            'readability-low',
            'boilerplate-heavy',
            'image-missing-src',
            'image-no-srcset',
            'image-inline-large',
            'form-not-secure',
            'protocol-relative-links',
            'x-card-incomplete',
            'social-image-broken',
            'social-image-too-large',
            'social-image-too-small',
            'head-tags-in-body',
            'html-structure-errors',
            'mobile-alternate',
          ],
        ],
      ]),
    );
    expect(results.skipped.find((group) => group.code === 'no-sitemap')?.reason?.plainAction).toMatch(/Add a sitemap/);
  });

  it('names the categories with nothing found by their plain titles', () => {
    expect(results.fineCategories).toContain('Visibility in search');
    expect(results.fineCategories).not.toContain('Links');
  });

  it('builds its sentence, word and coverage from counts only', () => {
    const score = GARDEN_ANALYSIS.score!;
    expect(bandWord(score.band)).toBe('Good');
    expect(summarySentence({ band: score.band, partial: false, counts: results.counts, siteWideSerious: null })).toBe(
      'Your site is in good shape. There are 2 serious problems and 4 problems worth fixing.',
    );
    expect(coverageSentences(score.covers)).toEqual([
      'The score covers all 265 pages and 143 of 170 checks.',
      '27 checks couldn’t run.',
    ]);
    expect(ifFixedLead(5)).toBe('Fixing these five takes the score from');
  });

  it('says each count with what it is a share of: pages, addresses, or the whole site', () => {
    expect(shareWords(check('internal-client-error'), 9, results.wholes).text).toBe('9 of 4,521 addresses');
    expect(shareWords(check('broken-internal-links'), 5, results.wholes).text).toBe('5 of 265 pages');
    expect(shareWords(check('no-https'), 1, results.wholes).text).toBe('The whole site');
    expect(shareWords(check('sitemap-missing'), 1, results.wholes).text).toBe('Once, for the whole site');
    const lines = GARDEN_ANALYSIS.score!.lines;
    expect(lines.map(lineShareWords).slice(0, 3)).toEqual([
      '9 of 4,521 addresses: a few',
      '5 of 265 pages: some',
      '61 of 265 pages: many',
    ]);
    expect(pointsWords(lines[1], 'error').text).toBe('Takes 4 points off the score');
    expect(pointsWords(undefined, 'notice').text).toBe('Doesn’t change the score');
    expect(fixerWords('you')).toBe('Usually fixed by you, in your site’s editor');
  });
});

describe('the Summary’s sentence in every state', () => {
  it('a perfect site: nothing to fix', () => {
    const results = plainResults(SIMPLE_CHECKS, PERFECT_ANALYSIS, NONE);
    expect(summarySentence({ band: 'veryGood', partial: false, counts: results.counts, siteWideSerious: null })).toBe(
      'Nothing to fix. Every check that ran found nothing wrong.',
    );
    expect(results.fixFirst).toEqual([]);
    expect(results.ifFixed).toBeNull();
  });

  it('nothing to fix with checks hidden: among the checks shown, and how many are hidden', () => {
    const counts = { error: 0, warning: 0, notice: 0 };
    expect(summarySentence({ band: 'good', partial: false, counts, siteWideSerious: null, hidden: 1 })).toBe(
      'Nothing to fix among the checks you show. 1 check is hidden.',
    );
    expect(
      summarySentence({
        band: 'good',
        partial: true,
        counts: { ...counts, notice: 2 },
        siteWideSerious: null,
        hidden: 3,
      }),
    ).toBe(
      'On the pages read so far, nothing needs fixing among the checks you show. 2 things are worth a look when you have a moment. 3 checks are hidden.',
    );
  });

  it('nothing but things worth a look', () => {
    const results = plainResults(SIMPLE_CHECKS, NOTICES_ANALYSIS, NONE);
    expect(summarySentence({ band: 'veryGood', partial: false, counts: results.counts, siteWideSerious: null })).toBe(
      'Nothing needs fixing. 3 things are worth a look when you have a moment.',
    );
  });

  it('one problem: is, not are', () => {
    expect(
      summarySentence({
        band: 'veryGood',
        partial: false,
        counts: { error: 0, warning: 1, notice: 6 },
        siteWideSerious: null,
      }),
    ).toBe('Your site is in very good shape. There is 1 problem worth fixing.');
  });

  it('stopped early: of the pages read so far, and a partial word', () => {
    const results = plainResults(SIMPLE_CHECKS, STOPPED_ANALYSIS, NONE);
    expect(summarySentence({ band: 'good', partial: true, counts: results.counts, siteWideSerious: null })).toBe(
      'On the pages read so far there are 2 serious problems and 4 problems worth fixing.',
    );
    expect(bandWord('good', true)).toBe('Good so far');
    expect(coverageSentences(STOPPED_ANALYSIS.score!.covers)[0]).toBe(
      'The score covers the 265 pages read before the check was stopped, and 143 of 170 checks.',
    );
  });

  it('a serious fault of the whole site leads, by its plain title', () => {
    const results = plainResults(SIMPLE_CHECKS, SITE_WIDE_ANALYSIS, NONE);
    expect(ids(results.fixFirst)).toEqual(['no-https', 'broken-internal-links']);
    expect(results.ifFixed).toBe(100);
    expect(
      summarySentence({ band: 'poor', partial: false, counts: results.counts, siteWideSerious: siteWideLead(results) }),
    ).toBe(`${check('no-https').plainTitle}. That holds everything else back. There are 2 serious problems.`);
  });

  it('a tiny site says it may have more than was found, and what robots.txt kept out', () => {
    const covers = { ...GARDEN_ANALYSIS.score!.covers, pages: 5, blockedByRobots: 1_080 };
    const sentences = coverageSentences(covers);
    expect(sentences).toContain(
      'Only 5 pages were found. If your site has more, its links may need JavaScript, which this version doesn’t run.',
    );
    expect(sentences.some((sentence) => sentence.includes('1,080 addresses, which weren’t read'))).toBe(true);
  });

  it('a check that ended before reading the whole site doesn’t say only a few pages were found', () => {
    const covers = { ...GARDEN_ANALYSIS.score!.covers, pages: 6, complete: false, reason: 'stopped' as const };
    expect(coverageSentences(covers).join(' ')).not.toMatch(/Only 6 pages were found/);
  });
});
