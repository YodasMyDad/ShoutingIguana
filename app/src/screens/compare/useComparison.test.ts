import { describe, expect, it } from 'vitest';
import { gardenComparison } from '../../test/fixLoopSidecar';
import type { Comparing } from './comparing';
import { advancedStage } from './useComparison';

const PREVIOUS: Comparing = {
  kind: 'previous',
  id: 'a1b2c3d4e5f60718',
  startedAt: '2026-10-17T14:13:30Z',
  later: false,
};

describe('Advanced’s comparison, at a glance', () => {
  it('shows nothing while the crawl is live here, nor with nothing to compare', () => {
    const answer = { comparison: gardenComparison(), error: null };
    expect(advancedStage(PREVIOUS, 'running', answer)).toEqual({ kind: 'live' });
    expect(advancedStage(PREVIOUS, 'rechecking', answer)).toEqual({ kind: 'live' });
    expect(advancedStage({ kind: 'none', why: 'first' }, 'completed', answer)).toEqual({ kind: 'none' });
    expect(advancedStage({ kind: 'none', why: 'off' }, 'completed', answer)).toEqual({ kind: 'none' });
    expect(advancedStage(PREVIOUS, 'completed', { comparison: null, error: null })).toEqual({ kind: 'none' });
  });

  it('is loading until the sidecar answers, and says a failed read', () => {
    expect(advancedStage(PREVIOUS, 'completed', { comparison: undefined, error: null })).toEqual({ kind: 'loading' });
    const error = new Error('That crawl can’t be compared.');
    expect(advancedStage(PREVIOUS, 'completed', { comparison: undefined, error })).toEqual({ kind: 'failed', error });
  });

  it('asks for the other crawl to be analysed again when the analyses differ, else shows the comparison', () => {
    const stale = gardenComparison({ previousNeedsAnalysis: ['engineChanged'], comparable: 'countsOnly' });
    expect(advancedStage(PREVIOUS, 'completed', { comparison: stale, error: null })).toEqual({
      kind: 'needsAnalysis',
      comparison: stale,
    });
    const fine = gardenComparison();
    expect(advancedStage(PREVIOUS, 'stopped', { comparison: fine, error: null })).toEqual({
      kind: 'shown',
      comparison: fine,
    });
  });
});
