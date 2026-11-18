import { describe, expect, it } from 'vitest';
import { severitiesOf } from '../../changes/changes';
import { gardenComparison } from '../../test/fixLoopSidecar';
import { SIMPLE_CHECKS } from '../../test/simpleSidecar';
import { comparisonFigures, figureWords } from './comparisonFigures';

const severityOf = severitiesOf(SIMPLE_CHECKS.checks);

describe('the comparison card’s figures (Advanced)', () => {
  const comparison = gardenComparison();

  it('counts the checks with issues by severity then and now, and how the checks moved', () => {
    const figures = comparisonFigures(comparison, new Set(), severityOf);
    const counted = comparison.checks;
    const thenErrors = counted.filter(
      (check) => (check.then ?? 0) > 0 && (check.severityThen ?? check.severity) === 'error',
    ).length;
    const nowErrors = counted.filter(
      (check) => (check.now ?? 0) > 0 && (check.severity ?? check.severityThen) === 'error',
    ).length;
    expect(figures.severities[0]).toEqual({ severity: 'error', then: thenErrors, now: nowErrors });
    expect(figures.severities.map((figure) => figure.severity)).toEqual(['error', 'warning', 'notice']);
    expect(figures.checks.fixed).toBe(counted.filter((check) => check.state === 'fixed').length);
    expect(figures.score).toEqual({
      then: comparison.score?.then.value,
      now: comparison.score?.now.value,
      change: comparison.score?.change,
      partial: false,
    });
  });

  it('leaves hidden checks out of every figure, the issues’ split included', () => {
    const fixedCheck = comparison.checks.find((check) => check.state === 'fixed');
    expect(fixedCheck).toBeDefined();
    const all = comparisonFigures(comparison, new Set(), severityOf);
    const hidden = comparisonFigures(comparison, new Set([fixedCheck?.checkId ?? '']), severityOf);
    expect(hidden.checks.fixed).toBe(all.checks.fixed - 1);
    expect(hidden.issues?.fixed).toBe((all.issues?.fixed ?? 0) - (fixedCheck?.fixed ?? 0));
  });

  it('has no issues’ split when counts only: no URL could be matched', () => {
    const countsOnly = gardenComparison({ comparable: 'countsOnly' });
    expect(comparisonFigures(countsOnly, new Set(), severityOf).issues).toBeNull();
  });

  it('says a figure then and now, its move for the eye and in words for a screen reader', () => {
    expect(figureWords('Errors', 3, 2)).toEqual({
      shown: '3 → 2',
      glyph: '▼1',
      spoken: 'Errors: 3 then, 2 now, down 1',
    });
    expect(figureWords('Notices', 13, 13)).toEqual({
      shown: '13 → 13',
      glyph: null,
      spoken: 'Notices: 13 then, 13 now, no change',
    });
    expect(figureWords('Score', 71, 85, 14)).toEqual({
      shown: '71 → 85',
      glyph: '▲14',
      spoken: 'Score: 71 then, 85 now, up 14',
    });
    // Scored another way: the two, and no change.
    expect(figureWords('Score', 71, 85, null)).toEqual({
      shown: '71 → 85',
      glyph: null,
      spoken: 'Score: 71 then, 85 now',
    });
  });
});
