import { describe, expect, it } from 'vitest';
import { hiddenChecksOf, hiddenScopeOf, withHiddenScope } from './hiddenChecks';
import { DEFAULT_SETTINGS, readSettings, type Settings } from './schema';

const crawl = '0123456789abcdef';
const other = 'fedcba9876543210';
const settings = (patch: Partial<Settings>): Settings => ({ ...DEFAULT_SETTINGS, ...patch });

describe('checks hidden in one crawl or in every crawl', () => {
  it('hides in a crawl beside what every crawl hides, and nowhere else', () => {
    const state = settings({ hiddenChecks: ['a'], hiddenChecksByCrawl: { [crawl]: ['b', 'a'] } });
    expect(hiddenChecksOf(state, crawl)).toEqual(['a', 'b']);
    expect(hiddenChecksOf(state, other)).toEqual(['a']);
    expect(hiddenChecksOf(state, undefined)).toEqual(['a']);
    expect(hiddenScopeOf(state, crawl, 'a')).toBe('all');
    expect(hiddenScopeOf(state, crawl, 'b')).toBe('crawl');
    expect(hiddenScopeOf(state, other, 'b')).toBeNull();
  });

  it('moves a check between the scopes, and drops a crawl with nothing hidden', () => {
    let state = settings({});
    state = { ...state, ...withHiddenScope(state, crawl, 'b', 'crawl') };
    expect(state.hiddenChecksByCrawl).toEqual({ [crawl]: ['b'] });
    state = { ...state, ...withHiddenScope(state, crawl, 'b', 'all') };
    expect(state).toMatchObject({ hiddenChecks: ['b'], hiddenChecksByCrawl: {} });
    state = { ...state, ...withHiddenScope(state, crawl, 'b', null) };
    expect(state).toMatchObject({ hiddenChecks: [], hiddenChecksByCrawl: {} });
  });

  it('reads back only crawls’ ids and checks’ ids', () => {
    expect(
      readSettings({
        hiddenChecksByCrawl: { [crawl]: ['title-missing', 'Bad Id', 3], nope: ['h1-missing'], [other]: [] },
      }).hiddenChecksByCrawl,
    ).toEqual({ [crawl]: ['title-missing'] });
    expect(readSettings({ hiddenChecksByCrawl: [] }).hiddenChecksByCrawl).toEqual({});
  });
});
