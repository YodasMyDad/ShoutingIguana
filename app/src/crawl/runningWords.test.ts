import { describe, expect, it } from 'vitest';
import { runningTitle } from './runningWords';

describe('the sidebar’s heading over the crawls running now', () => {
  it('in Simple, never “Checking now” while it only works out the results, or is paused (gap 29)', () => {
    expect(runningTitle(['running'], 'simple')).toBe('Checking now');
    expect(runningTitle(['analyzing'], 'simple')).toBe('Working out the results');
    expect(runningTitle(['paused'], 'simple')).toBe('Paused');
    expect(runningTitle(['paused', 'analyzing'], 'simple')).toBe('Checking now');
  });

  it('in Advanced, counts each by what it does: a paused crawl isn’t “crawling”', () => {
    expect(runningTitle(['running'], 'advanced')).toBe('1 crawling');
    expect(runningTitle(['paused'], 'advanced')).toBe('1 paused');
    expect(runningTitle(['running', 'stopping', 'paused', 'analyzing'], 'advanced')).toBe(
      '2 crawling, 1 paused, 1 analysing',
    );
  });
});
