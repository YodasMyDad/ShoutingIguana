import { describe, expect, it } from 'vitest';
import { problemsToFix, simpleFinishedWords, simpleStoppedWords } from './finishedWords';

const site = 'gardenfurnituresets.co.uk';

describe('the words of a check’s end, in Simple', () => {
  it('says the design’s sentence for the garden crawl', () => {
    expect(
      simpleFinishedWords({
        site,
        crawled: true,
        score: { value: 85, band: 'good', partial: false },
        problems: 6,
        pageCount: 265,
      }),
    ).toEqual({ title: 'gardenfurnituresets.co.uk is checked', description: 'Score 85: 6 problems to fix' });
  });

  it('has its own words for every case', () => {
    const score = { value: 100, band: 'veryGood' as const, partial: false };
    expect(simpleFinishedWords({ site, crawled: true, score, problems: 0, pageCount: 3 }).description).toBe(
      'Score 100: nothing to fix',
    );
    expect(
      simpleFinishedWords({
        site,
        crawled: true,
        score: { ...score, value: 71, partial: true },
        problems: 1,
        pageCount: 3,
      }).description,
    ).toBe('Score 71 so far: 1 problem to fix');
    expect(simpleFinishedWords({ site, crawled: false, score, problems: null, pageCount: 3 })).toEqual({
      title: 'The results for gardenfurnituresets.co.uk are updated',
      description: 'Score 100',
    });
    expect(simpleFinishedWords({ site, crawled: true, score: null, problems: 0, pageCount: 0 }).description).toBe(
      'No pages could be read, so there’s no score.',
    );
    expect(simpleFinishedWords({ site, crawled: true, score: undefined, problems: 4, pageCount: 9 }).description).toBe(
      '4 problems to fix.',
    );
    expect(simpleFinishedWords({ site, crawled: true, score: null, problems: null, pageCount: 9 }).description).toBe(
      'The results are ready.',
    );
    expect(problemsToFix(1_234)).toBe('1,234 problems to fix');
  });

  it('says a stopped check with what it checked', () => {
    expect(simpleStoppedWords(site, 1)).toEqual({
      title: 'The check of gardenfurnituresets.co.uk was stopped',
      description: '1 address checked. You can continue it later.',
    });
  });
});
