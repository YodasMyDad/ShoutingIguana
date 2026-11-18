import { describe, expect, it } from 'vitest';
import { runningTitle } from './runningWords';

describe('the sidebar’s heading over the crawls running now', () => {
  it('in Simple, never “Checking now” while it only works out the results, or is paused (gap 29)', () => {
    expect(runningTitle(['running'], 'simple')).toBe('Checking now');
    expect(runningTitle(['analyzing'], 'simple')).toBe('Working out the results');
    expect(runningTitle(['paused'], 'simple')).toBe('Paused');
    expect(runningTitle(['stopping'], 'simple')).toBe('Stopping');
    expect(runningTitle(['paused', 'analyzing'], 'simple')).toBe('Checking now');
    // The fix loop: a recheck is a check of some of the site's pages, and each row says "Rechecking" itself.
    expect(runningTitle(['rechecking'], 'simple')).toBe('Checking now');
  });

  it('counts a recheck as rechecking in Advanced, never as crawling', () => {
    expect(runningTitle(['rechecking'], 'advanced')).toBe('1 rechecking');
    expect(runningTitle(['running', 'rechecking'], 'advanced')).toBe('1 crawling, 1 rechecking');
  });

  /** The closing audit: the sidebar said "1 crawling" for a paused crawl. */
  it('in Advanced, counts each by what it does: never crawling for one that’s paused or being analysed', () => {
    expect(runningTitle(['running'], 'advanced')).toBe('1 crawling');
    expect(runningTitle(['paused'], 'advanced')).toBe('1 paused');
    expect(runningTitle(['analyzing', 'running', 'paused', 'running'], 'advanced')).toBe(
      '2 crawling, 1 paused, 1 analysing',
    );
    expect(runningTitle(['running', 'stopping', 'paused', 'analyzing'], 'advanced')).toBe(
      '1 crawling, 1 paused, 1 stopping, 1 analysing',
    );
    expect(runningTitle(['stopping'], 'advanced')).toBe('1 stopping');
    // Live before its status has come: it was started, so it's crawling.
    expect(runningTitle([null], 'advanced')).toBe('1 crawling');
    expect(runningTitle([undefined], 'simple')).toBe('Checking now');
  });
});
