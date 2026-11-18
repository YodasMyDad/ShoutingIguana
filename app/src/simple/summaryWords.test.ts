import { describe, expect, it } from 'vitest';
import { platformLines, summarySubtitle } from './summaryWords';

const item = {
  startedAt: '2026-10-31T14:13:00Z',
  finishedAt: '2026-10-31T14:51:00Z',
  crawlingMs: 2_245_000,
  pageCount: 265,
};

// The closing audit's gap 14: the head had no time for a check an older engine made, and a stopped check read as done.
describe('the Summary’s head', () => {
  it('says when, the pages read and how long it read for, as summary.html', () => {
    expect(summarySubtitle(item, 'completed', 265)).toBe('Checked on 31 October 2026. 265 pages read in 37 min 25 s.');
  });

  it('a check stopped early: when it stopped, after how long, and the pages read so far, as summary-stopped.html', () => {
    expect(summarySubtitle({ ...item, crawlingMs: 400_000 }, 'stopped', 312)).toBe(
      'Stopped on 31 October 2026 after 6 min 40 s. 312 pages read so far.',
    );
    expect(summarySubtitle({ ...item, crawlingMs: 400_000 }, 'interrupted', 1)).toBe(
      'Stopped on 31 October 2026 after 6 min 40 s. 1 page read so far.',
    );
  });

  it('a check with no time spent crawling on record: its start and end’s span, said as such, under a day only', () => {
    expect(summarySubtitle({ ...item, crawlingMs: null }, 'completed', 265)).toBe(
      'Checked on 31 October 2026. 265 pages read in 38 min, start to finish.',
    );
    expect(summarySubtitle({ ...item, crawlingMs: null, finishedAt: '2026-11-02T09:00:00Z' }, 'completed', 265)).toBe(
      'Checked on 2 November 2026. 265 pages read.',
    );
  });

  it('no page read: the date alone', () => {
    expect(summarySubtitle({ ...item, pageCount: 0 }, 'completed', 0)).toBe('Checked on 31 October 2026.');
  });
});

// The Shopify lane, phase B: one quiet line for what the platform sets, apart from the problems to fix.
describe('what the site’s platform sets, in a line', () => {
  it('counts it by who sets it, and says there’s nothing to change', () => {
    expect(platformLines(['Shopify', 'Shopify'])).toEqual(['2 set by Shopify: nothing for you to change.']);
    expect(platformLines(['Shopify'])).toEqual(['1 set by Shopify: nothing for you to change.']);
    expect(platformLines([])).toEqual([]);
  });
});
