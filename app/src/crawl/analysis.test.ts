import { describe, expect, it } from 'vitest';
import { analysisPhaseWords, analysisProgressText, analysisStep, outOfDateWords } from './analysis';

describe('an analysis in words', () => {
  it('names each phase, with its step, and says how far it has got in what it counts', () => {
    expect(analysisPhaseWords('pageRules')).toBe('Checking each page');
    expect(analysisStep('pageRules')).toBe('Step 2 of 5');
    expect(analysisProgressText({ phase: 'pageRules', done: 1_204, total: 4_521 })).toBe('1,204 of 4,521 pages');
    expect(analysisProgressText({ phase: 'siteRules', done: 3, total: 9 })).toBe('3 of 9 checks');
    expect(analysisProgressText({ phase: 'reading', done: 1, total: 1 })).toBe('1 of 1 URL');
  });

  it('says nothing it can’t: no count for the last phase, and a plain word for a phase it doesn’t know', () => {
    expect(analysisProgressText({ phase: 'writing', done: 0, total: 1 })).toBeNull();
    expect(analysisPhaseWords('somethingNew')).toBe('Analysing');
    expect(analysisStep('somethingNew')).toBeNull();
    expect(analysisProgressText({ phase: 'somethingNew', done: 1, total: 2 })).toBeNull();
  });

  it('says why an analysis is out of date', () => {
    expect(outOfDateWords(['crawlChanged'])).toBe('The crawl was continued after it was analysed.');
    expect(outOfDateWords(['crawlChanged', 'settingsChanged'])).toContain('settings changed');
    expect(outOfDateWords([])).toBe('The crawl changed after it was analysed.');
  });
});
